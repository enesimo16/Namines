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

    public DbHostAccessPolicy(IHostEnvironment environment, IConfiguration configuration, ILogger<DbHostAccessPolicy> logger)
    {
        var configured = configuration.GetValue<bool>("Security:AllowPrivateDbHosts");
        _allowPrivateHosts = environment.IsDevelopment() && configured;

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

        if (SsrfGuard.IsHostSafe(host)) return true;

        if (_allowPrivateHosts) return true;

        denyReason = $"Connection target '{host}' is not allowed (private/reserved address).";
        return false;
    }
}
