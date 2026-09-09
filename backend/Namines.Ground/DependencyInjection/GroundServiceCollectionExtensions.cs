using Microsoft.Extensions.DependencyInjection;
using Namines.Ground.Abstractions;
using Namines.Ground.Providers;

namespace Namines.Ground.DependencyInjection;

/// <summary>
/// Ground'un kayıt noktası.
///
/// <b>Sağlayıcılar bir KOLEKSİYON olarak kaydediliyor</b> (tek bir
/// <see cref="IDatabaseProvider"/> değil): birden çok sağlayıcı aynı anda
/// kullanılabilir olmalı ve kullanıcı hangisini istediğini seçebilmeli.
/// Tek kayıt olsaydı sağlayıcı değiştirmek yeniden dağıtım gerektirirdi.
/// </summary>
public static class GroundServiceCollectionExtensions
{
    public static IServiceCollection AddNaminesGround(this IServiceCollection services)
    {
        // Singleton: sağlayıcılar durumsuz — yapılandırmayı okuyup her çağrıda
        // kendi bağlantısını açıyorlar.
        services.AddSingleton<IDatabaseProvider, LocalPostgresProvider>();

        return services;
    }
}
