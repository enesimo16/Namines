using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Core.Security;

namespace Namines.Infrastructure.Security;

/// <summary>
/// <see cref="IDbHostAccessPolicy"/> — üretimde daima sıkı, yalnızca geliştirmede
/// ve YALNIZCA açıkça istendiğinde gevşer.
///
/// ÇİFT KAPI (ikisi de sağlanmadan gevşemez):
///   1) Ortam Development olacak (<see cref="IHostEnvironment.IsDevelopment"/>)
///   2) `Security:AllowPrivateDbHosts` = true olacak
///
/// Neden iki kapı: tek başına config bayrağı, prod'a yanlışlıkla kopyalanan bir
/// appsettings/env değişkeniyle SSRF korumasını kapatabilirdi. Tek başına ortam
/// kontrolü ise geliştiricinin haberi olmadan korumayı düşürürdü. İkisi birden
/// aranınca kaza ile açılması pratikte imkânsız hale geliyor.
///
/// Gevşeme aktifken başlangıçta UYARI loglanır — sessizce açık kalmasın.
/// </summary>
public sealed class DbHostAccessPolicy : IDbHostAccessPolicy
{
    private readonly bool _allowPrivateHosts;

    /// <summary>
    /// Operatörün açıkça izin verdiği hedefler. BOŞSA kapı yok (yalnızca
    /// özel/ayrılmış adres kontrolü çalışır); DOLUYSA yalnızca bu listedekilere
    /// bağlanılabilir.
    ///
    /// <b>DNS rebinding'in gerçek cevabı bu.</b> <see cref="SsrfGuard"/> host'u
    /// doğrularken DNS'i çözüyor, sürücü ise bağlanırken YENİDEN çözüyor — arada
    /// cevap değişebilir (TOCTOU). Kod tarafında bu pencereyi kapatmak, TLS
    /// sertifika doğrulamasını bozmadan mümkün değil: sürücülere "şu IP'ye
    /// bağlan ama sertifikayı şu ad için doğrula" demenin taşınabilir bir yolu yok.
    ///
    /// Allowlist bu yarışı <b>anlamsız</b> kılıyor: saldırganın alan adı listede
    /// olmadığı için hangi IP'ye çözüldüğü fark etmiyor.
    ///
    /// Kabul edilen biçimler:
    /// <list type="bullet">
    /// <item><c>db.ornek.com</c> — tam eşleşme</item>
    /// <item><c>*.ornek.com</c> — alt alan adları (kökün kendisi DAHİL)</item>
    /// <item><c>203.0.113.5</c> — IP değişmezi</item>
    /// </list>
    /// </summary>
    private readonly string[] _allowedHosts;

    public DbHostAccessPolicy(IHostEnvironment environment, IConfiguration configuration, ILogger<DbHostAccessPolicy> logger)
    {
        var configured = configuration.GetValue<bool>("Security:AllowPrivateDbHosts");
        _allowPrivateHosts = environment.IsDevelopment() && configured;

        _allowedHosts = ParseAllowedHosts(configuration["Security:DbEgress:AllowedHosts"]);

        if (_allowedHosts.Length > 0)
        {
            logger.LogInformation(
                "Veritabani egress allowlist AKTIF ({Count} girdi). Yalnizca bu hedeflere baglanilabilir.",
                _allowedHosts.Length);
        }
        else
        {
            // Sessiz kalmiyor: allowlist yoksa DNS rebinding penceresi acik
            // demektir ve bunu bilen biri karar vermeli.
            logger.LogInformation(
                "Veritabani egress allowlist TANIMLI DEGIL (Security:DbEgress:AllowedHosts). " +
                "Ozel/ayrilmis adresler yine reddediliyor, ama herhangi bir PUBLIC hedefe " +
                "baglanilabilir ve DNS rebinding penceresi kapali degil.");
        }

        if (configured && !environment.IsDevelopment())
        {
            // Prod'da bayrak açık bırakılmış — YOK SAYILDI, ama sessiz kalma.
            logger.LogWarning(
                "Security:AllowPrivateDbHosts=true ancak ortam '{Environment}' — YOK SAYILDI. " +
                "Özel/ayrılmış adreslere bağlanma yalnızca Development'ta gevşetilebilir.",
                environment.EnvironmentName);
        }
        else if (_allowPrivateHosts)
        {
            logger.LogWarning(
                "SSRF koruması GEVŞETİLDİ (Development + Security:AllowPrivateDbHosts). " +
                "localhost/özel ağ adreslerine veritabanı bağlantısına izin veriliyor. " +
                "Bu ayar production'da etkisizdir.");
        }
    }

    public bool IsHostAllowed(string? host, out string denyReason)
    {
        denyReason = string.Empty;

        if (string.IsNullOrWhiteSpace(host))
        {
            denyReason = "Connection target host could not be determined.";
            return false;
        }

        // Allowlist ÖNCE: listede olmayan bir hedef, public bir adres olsa da
        // reddedilir. Sıra önemli — sonra kontrol edilseydi, özel adres
        // kontrolünü geçen her host allowlist'i atlardı.
        if (_allowedHosts.Length > 0 && !IsAllowlisted(host))
        {
            denyReason = $"Connection target '{host}' is not in the configured database egress allowlist.";
            return false;
        }

        if (SsrfGuard.IsHostSafe(host)) return true;

        if (_allowPrivateHosts) return true;

        denyReason = $"Connection target '{host}' is not allowed (private/reserved address).";
        return false;
    }

    private bool IsAllowlisted(string host) =>
        _allowedHosts.Any(entry => Matches(entry, host));

    /// <summary>
    /// <c>*.ornek.com</c> kökün KENDİSİNİ de kapsıyor (<c>ornek.com</c>).
    /// Aksi hâlde operatör aynı alan adı için iki satır yazmak zorunda kalır ve
    /// birini unutmak sessiz bir kesinti üretir.
    /// </summary>
    internal static bool Matches(string entry, string host)
    {
        if (entry.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = entry[1..];                    // ".ornek.com"
            var root = entry[2..];                      // "ornek.com"

            return host.Equals(root, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return host.Equals(entry, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Virgül, noktalı virgül ve satır sonuyla ayrılabilir.</summary>
    internal static string[] ParseAllowedHosts(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                 .Select(x => x.Trim())
                 .Where(x => x.Length > 0)
                 .ToArray();
}
