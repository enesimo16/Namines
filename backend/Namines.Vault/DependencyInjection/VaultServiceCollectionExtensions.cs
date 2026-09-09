using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Namines.Vault.Abstractions;
using Namines.Vault.Providers;
using Namines.Vault.Storage;

namespace Namines.Vault.DependencyInjection;

/// <summary>
/// Vault'un kayıt noktası.
///
/// <b>Tek giriş:</b> API katmanı Vault'un iç tiplerini tek tek tanımak zorunda
/// kalmasın; sağlayıcı ya da depo değiştiğinde değişen tek yer burası olsun.
/// </summary>
public static class VaultServiceCollectionExtensions
{
    public static IServiceCollection AddNaminesVault(this IServiceCollection services)
    {
        // Singleton: DockerClient ve türetilmiş şifreleme anahtarı istek başına
        // yeniden kurulmaya değmez; ikisi de durumsuz ve iş parçacığı güvenli.
        services.AddSingleton<BackupCipher>();

        // Depo seçimi YAPILANDIRMADAN, koddan değil: nesne deposu tanımlıysa o
        // kullanılıyor, yoksa sunucu diski.
        //
        // <b>Sessiz bir varsayılana düşmüyor:</b> hangisinin kullanıldığı
        // Description üzerinden arayüzde yazıyor (Vault ekranı "Yedekler burada
        // saklanıyor: …" diyor). v1'in disk seçeneği kabul edilebilir ama
        // kullanıcı bunu bilmeden yedeğe güvenmemeli.
        services.AddSingleton<IBackupStore>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();

            return string.IsNullOrWhiteSpace(configuration["Vault:S3:Bucket"])
                ? new FileSystemBackupStore(configuration)
                : new S3BackupStore(configuration);
        });
        // Motor başına bir sağlayıcı. Liste burada; VaultService desteklenen
        // motorları bu kayıtlardan TÜRETİYOR, ayrıca bir yerde yazmıyor.
        services.AddSingleton<IBackupProvider, PostgresBackupProvider>();

        // MySQL ve MariaDB aynı sınıfın iki kaydı: araçlar ve protokol aynı,
        // yalnızca imaj ve motor adı farklı. İmajların AYRI olması bilinçli —
        // MySQL 8 istemcisiyle MariaDB'ye dump almak kimlik doğrulama ve
        // sürüm-özel deyimler yüzünden sessizce bozuk çıktı verebiliyor.
        services.AddSingleton<IBackupProvider>(sp => new MySqlFamilyBackupProvider(
            engine: "MySQL",
            image: sp.GetRequiredService<IConfiguration>()["Vault:MySqlImage"] ?? "mysql:8.0",
            hasColumnStatisticsFlag: true,
            logger: sp.GetRequiredService<ILogger<MySqlFamilyBackupProvider>>()));

        services.AddSingleton<IBackupProvider>(sp => new MySqlFamilyBackupProvider(
            engine: "MariaDB",
            image: sp.GetRequiredService<IConfiguration>()["Vault:MariaDbImage"] ?? "mariadb:10.6",
            // MariaDB istemcisi --column-statistics bayrağını tanımıyor.
            hasColumnStatisticsFlag: false,
            logger: sp.GetRequiredService<ILogger<MySqlFamilyBackupProvider>>()));

        return services;
    }
}
