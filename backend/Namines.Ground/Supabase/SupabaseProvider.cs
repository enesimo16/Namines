using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Namines.Ground.Abstractions;

namespace Namines.Ground.Supabase;

/// <summary>
/// Supabase üzerinde yönetilen PostgreSQL açan sağlayıcı (F-10 / B-45).
///
/// <b>Stratejik gerekçe — Ground'la yarışmak değil, bağlanmak:</b> Rakip
/// analizi (<c>16-competitor-analysis.md</c>) Supabase'in Free planının bile
/// Ground'un v1 limitlerinin üstünde olduğunu ve arkasında yıllarca altyapı
/// yatırımı bulunduğunu ölçtü. Doğru hamle, kullanıcıyı platformundan
/// taşımaya çalışmak değil, <b>platformunun üstüne yönetişim koymak</b>:
/// şema tasarımı, değişiklik onayı, denetim kaydı, yedek.
///
/// <b>CANLI DOĞRULANMADI ve bunu kendisi söylüyor</b>
/// (<c>IsLiveVerified: false</c>). Bu oturumda Supabase erişim jetonu yoktu;
/// uçlar belgeden alındı ama gerçek bir kaynağa karşı denenmedi. Deponun
/// kuralı (<c>00-GENEL-BAKIS.md</c> §9): yazılıp canlı doğrulanmayan bir
/// sağlayıcı "destekleniyor" sayılmaz. Bayrak arayüzde gösteriliyor, yani
/// kullanıcı verisini kanıtlanmamış bir yola koyarken bunu bilerek yapar.
/// </summary>
public sealed class SupabaseProvider : IDatabaseProvider
{
    private readonly ISupabaseClient _client;
    private readonly ILogger<SupabaseProvider> _logger;

    public SupabaseProvider(ISupabaseClient client, ILogger<SupabaseProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string Name => "Supabase";

    public ProviderCapabilities Capabilities => new(
        // Supabase'in dal (branch) özelliği var ama Ground onu KULLANMIYOR;
        // "var" demek olmayan bir düğme vaat etmek olurdu.
        SupportsBranching: false,
        SupportsRegionChoice: true,
        // Jeton olmadığı için gerçek bir projeye karşı denenemedi.
        IsLiveVerified: false,
        ResponsibilityNote:
            "The database is created on Supabase and operated by Supabase. Supabase's own " +
            "service level and availability apply. This provider has not been verified " +
            "against a live Supabase account yet.");

    public Task<string?> ProbeAsync(CancellationToken ct) => _client.ProbeAsync(ct);

    public async Task<ProvisionedDatabase> CreateAsync(ProvisionSpec spec, CancellationToken ct)
    {
        // Ad Supabase panelinde görünür: oraya bakan biri kaynağın HANGİ
        // projeye ait olduğunu görebilmeli.
        var name = BuildProjectName(spec);

        var project = await _client.CreateProjectAsync(name, spec.Region, ct);

        try
        {
            return new ProvisionedDatabase(
                ProviderProjectId: project.ProjectRef,
                // Supabase'de dal/alt kaynak kullanılmıyor (bkz. Capabilities).
                ProviderBranchId: null,
                Region: project.Region,
                ConnectionString: project.ConnectionString);
        }
        catch (Exception ex)
        {
            // Proje AÇILDI ama kullanılabilir hâle getirilemedi. Temizlenmezse
            // kimsenin bilmediği ama FATURALANAN bir kaynak kalır.
            _logger.LogError(ex,
                "Ground: Supabase projesi {ProjectRef} olusturuldu ama kullanilamadi; temizleniyor.",
                project.ProjectRef);

            await TryDeleteAsync(project.ProjectRef);
            throw;
        }
    }

    public async Task DeleteAsync(ProvisionedDatabase database, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(database.ProviderProjectId)) return;
        await _client.DeleteProjectAsync(database.ProviderProjectId, ct);
    }

    /// <summary>
    /// Ölçümler. <b>Supabase Management API'si depolama/bağlantı sayısını bu
    /// yoldan vermiyor</b>, o yüzden ikisi de <c>null</c>: "bilinmiyor".
    ///
    /// Sıfır yazmak "ölçüldü ve sıfır çıktı" demek olurdu — Ground'un kendi
    /// kuralı (<c>00-GENEL-BAKIS.md</c> §9 madde 4) tahmin edilen sayıyı
    /// yasaklıyor, çünkü boyut uyarısı o sayının üstüne kuruluyor.
    /// </summary>
    public Task<DatabaseMetrics> GetMetricsAsync(ProvisionedDatabase database, CancellationToken ct) =>
        Task.FromResult(new DatabaseMetrics(null, null));

    private async Task TryDeleteAsync(string projectRef)
    {
        try
        {
            await _client.DeleteProjectAsync(projectRef, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Temizlik hatası ASIL hatayı bastırmamalı; kalan kaynak loglanıyor
            // ki operatör onu elle silebilsin.
            _logger.LogError(ex,
                "Ground: yarim kalan Supabase projesi {ProjectRef} SILINEMEDI, elle silinmeli.",
                projectRef);
        }
    }

    /// <summary>
    /// Supabase panelinde görünecek proje adı.
    ///
    /// <c>NeonProvider.BuildProjectName</c> ile aynı desen ve aynı gerekçe:
    /// ad okunabilirlik, kimlik kesinlik sağlıyor — iki Namines projesi aynı
    /// ada sahip olabilir.
    /// </summary>
    internal static string BuildProjectName(ProvisionSpec spec)
    {
        var clean = new string(
            (spec.ProjectName ?? string.Empty)
                .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
                .ToArray()).Trim('-');

        var label = string.IsNullOrEmpty(clean) ? "project" : clean;
        if (label.Length > 30) label = label[..30];

        return $"namines-{label}-{spec.ProjectId}";
    }
}
