using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Namines.Ground.Abstractions;
using Namines.Ground.Neon;
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

        // HttpClient fabrikadan: her çağrıda yeni bir HttpClient soket
        // tükenmesine, tek bir statik örnek ise DNS değişikliklerini
        // görmemeye yol açar.
        services.AddHttpClient<INeonClient, NeonClient>();

        // NeonProvider, INeonClient'ı fabrikadan alınan istemciyle çözüyor.
        // Anahtar tanımlı olmasa da KAYDEDILIYOR: sağlayıcı listesinde
        // görünüp "yapılandırılmamış" diyebilmesi, hiç görünmemesinden iyi —
        // kullanıcı seçeneğin var olduğunu ve neyin eksik olduğunu görsün.
        services.AddSingleton<IDatabaseProvider>(sp => new NeonProvider(
            sp.GetRequiredService<INeonClient>(),
            sp.GetRequiredService<ILogger<NeonProvider>>()));

        return services;
    }
}
