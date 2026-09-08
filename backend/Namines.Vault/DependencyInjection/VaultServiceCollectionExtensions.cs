using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IBackupStore, FileSystemBackupStore>();
        services.AddSingleton<IBackupProvider, PostgresBackupProvider>();

        return services;
    }
}
