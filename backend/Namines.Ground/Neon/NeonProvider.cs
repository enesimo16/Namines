using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Namines.Ground.Abstractions;
using Npgsql;

namespace Namines.Ground.Neon;

/// <summary>
/// Neon üzerinde yönetilen PostgreSQL açan sağlayıcı.
///
/// <b>CANLI KANITLANMADI.</b> Depoda Neon API anahtarı yok
/// (<c>02-V1-KARARLARI.md</c> §1), dolayısıyla bu sağlayıcı gerçek bir
/// kaynağa karşı hiç çalıştırılmadı. İş mantığı sahte bir
/// <see cref="INeonClient"/> ile test edildi, ama <c>00-GENEL-BAKIS.md</c> §9
/// madde 2'nin kuralı gereği ("kanıt = çalışan komut + görülen çıktı")
/// <b>"çalışıyor" sayılmıyor</b> — ve bu, <see cref="Capabilities"/>
/// üzerinden arayüze kadar taşınıyor.
/// </summary>
public sealed class NeonProvider : IDatabaseProvider
{
    private readonly INeonClient _client;
    private readonly ILogger<NeonProvider> _logger;

    public NeonProvider(INeonClient client, ILogger<NeonProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string Name => "Neon";

    public ProviderCapabilities Capabilities => new(
        // Neon'un copy-on-write dal özelliği var ama Ground onu v1'de
        // KULLANMIYOR; "var" demek olmayan bir düğme vaat etmek olurdu.
        SupportsBranching: false,
        SupportsRegionChoice: true,
        IsLiveVerified: false,
        ResponsibilityNote:
            "The database is created on Neon and operated by Neon. " +
            "WARNING: this provider has not yet been exercised against a live Neon " +
            "account — verify it on your own account before trusting it with " +
            "production data.");

    public Task<string?> ProbeAsync(CancellationToken ct) => _client.ProbeAsync(ct);

    public async Task<ProvisionedDatabase> CreateAsync(ProvisionSpec spec, CancellationToken ct)
    {
        // Ad Neon panelinde görünür: oraya bakan biri kaynağın HANGİ projeye
        // ait olduğunu görebilmeli, yoksa kimin ne olduğu bilinmeyen bir
        // kaynak yığınına dönerdi.
        var name = BuildProjectName(spec);

        var project = await _client.CreateProjectAsync(name, spec.Region, ct);

        try
        {
            return new ProvisionedDatabase(
                ProviderProjectId: project.ProjectId,
                ProviderBranchId: project.BranchId,
                Region: project.RegionId,
                // Neon URI biçiminde döner; kod tabanının geri kalanı (Desk,
                // Vault, Gateway) anahtar=değer biçimi bekliyor.
                ConnectionString: ToKeyValueConnectionString(project.ConnectionUri));
        }
        catch (Exception ex)
        {
            // Proje AÇILDI ama biz onu kullanılabilir hâle getiremedik.
            // Temizlemezsek kimsenin bilmediği ama FATURALANAN bir kaynak kalır.
            _logger.LogError(ex,
                "Ground: Neon projesi {ProjectId} olusturuldu ama kullanilamadi; temizleniyor.",
                project.ProjectId);

            await TryDeleteAsync(project.ProjectId);
            throw;
        }
    }

    public async Task DeleteAsync(ProvisionedDatabase database, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(database.ProviderProjectId)) return;
        await _client.DeleteProjectAsync(database.ProviderProjectId, ct);
    }

    public async Task<DatabaseMetrics> GetMetricsAsync(ProvisionedDatabase database, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(database.ProviderProjectId))
            return new DatabaseMetrics(null, null);

        var usage = await _client.GetUsageAsync(database.ProviderProjectId, ct);

        // Eşzamanlı bağlantı sayısı Neon'un bu ucundan gelmiyor; null
        // "bilinmiyor" demek. Sıfır yazmak "ölçüldü ve sıfır çıktı" olurdu.
        return new DatabaseMetrics(usage.StorageBytes, null);
    }

    private async Task TryDeleteAsync(string projectId)
    {
        try
        {
            await _client.DeleteProjectAsync(projectId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Temizlik hatası ASIL hatayı bastırmamalı; kalan kaynak loglanıyor
            // ki operatör onu elle silebilsin.
            _logger.LogError(ex,
                "Ground: yarim kalan Neon projesi {ProjectId} SILINEMEDI, elle silinmeli.",
                projectId);
        }
    }

    /// <summary>
    /// Neon panelinde görünecek proje adı.
    ///
    /// Proje adı ve kimliği birlikte: ad okunabilirlik, kimlik ise kesinlik
    /// sağlıyor — iki Namines projesi aynı ada sahip olabilir.
    /// </summary>
    internal static string BuildProjectName(ProvisionSpec spec)
    {
        var clean = new string(
            (spec.ProjectName ?? string.Empty)
                .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
                .ToArray()).Trim('-');

        var label = string.IsNullOrEmpty(clean) ? "project" : clean;
        // Neon proje adı sınırı 64; kısaltma kimliği DEĞİL adı kırpıyor ki
        // kimlik tam kalsın (kaynağı bulmanın kesin yolu o).
        if (label.Length > 30) label = label[..30];

        return $"namines-{label}-{spec.ProjectId}";
    }

    /// <summary>
    /// <c>postgresql://kullanici:parola@host/db?sslmode=require</c> biçimini
    /// Npgsql'in anahtar=değer biçimine çevirir.
    ///
    /// <b>Neden gerekli:</b> Namines'in geri kalanı (Desk, Vault, Gateway,
    /// SSRF kontrolü) bağlantıyı anahtar=değer olarak ayrıştırıyor. URI'yi
    /// olduğu gibi saklamak, o yolların hepsinde host'u çıkaramamak demekti —
    /// yani SSRF kontrolünün sessizce atlanması.
    /// </summary>
    internal static string ToKeyValueConnectionString(string connectionUri)
    {
        if (string.IsNullOrWhiteSpace(connectionUri))
            throw new ArgumentException("Bağlantı adresi boş.", nameof(connectionUri));

        // Zaten anahtar=değer biçimindeyse dokunma.
        if (!connectionUri.Contains("://", StringComparison.Ordinal)) return connectionUri;

        var uri = new Uri(connectionUri);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            // Neon TLS zorunlu tutuyor; bunu düşürmek bağlantıyı ağda açık
            // hâle getirirdi.
            SslMode = SslMode.Require,
        };

        return builder.ConnectionString;
    }
}
