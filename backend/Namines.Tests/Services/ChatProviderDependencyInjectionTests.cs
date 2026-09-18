using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.API.Extensions;
using Namines.Core.Interfaces;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// ZİNCİR TESTİ: sağlayıcı seçimi ÜRETİMDEKİ kayıt yolundan gerçekten çıkıyor mu.
///
/// <b>Neden derleme yetmiyor:</b> bu kod tabanında bir arayüz (IAiQuotaReserver)
/// hiçbir yerde kayıtlı olmadan kullanıma girdi. Her katmanın birim testi
/// yeşildi, derleme temizdi, ama uygulama açılışta düşüyordu. Yeni bir arayüz
/// (<see cref="IChatCompletionProvider"/>) eklerken aynı boşluğu açık bırakmamak
/// için burada konteyner gerçekten kuruluyor ve servis gerçekten çözümleniyor.
///
/// <b>Yapılandırma seçiminin de buradan sınanması bilinçli:</b> fabrikanın kendi
/// birim testi doğru sağlayıcıyı ÜRETTİĞİNİ kanıtlıyor, ama o sağlayıcının
/// <c>GroqAIService</c>'e gerçekten ULAŞTIĞINI kanıtlamıyor. İkisinin arası —
/// yani DI kaydının doğru bağlanması — bu testin kapsadığı yer.
/// </summary>
public sealed class ChatProviderDependencyInjectionTests
{
    private static ServiceProvider BuildProductionLikeProvider(
        SqliteConnection connection, params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection));

        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in settings) dict[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddNaminesServices(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void IChatCompletionProvider_uretim_kayitlariyla_cozumlenebiliyor()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var provider = BuildProductionLikeProvider(connection);
        using var scope = provider.CreateScope();

        var chatProvider = scope.ServiceProvider.GetRequiredService<IChatCompletionProvider>();

        // Varsayılan Groq: yapılandırmada hiçbir şey söylenmediğinde bugün
        // çalışan yol değişmemeli.
        Assert.Equal("groq", chatProvider.Name);
    }

    [Fact]
    public void GroqAIService_uretim_kayitlariyla_cozumlenebiliyor()
    {
        // Kurucusu HttpClient, IConfiguration, IHttpContextAccessor, IMemoryCache,
        // ILogger ve artık IChatCompletionProvider istiyor. Sonuncusu typed-client
        // kaydıyla birlikte çözümlenemezse uygulama açılışta düşer.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var provider = BuildProductionLikeProvider(connection);
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GroqAIService>());
    }

    [Fact]
    public void Yapilandirmayla_secilen_saglayici_konteynerden_gercekten_cikiyor()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var provider = BuildProductionLikeProvider(
            connection, ("Ai:Provider", "deepseek"), ("DeepSeek:ApiKey", "sk-test"));
        using var scope = provider.CreateScope();

        var chatProvider = scope.ServiceProvider.GetRequiredService<IChatCompletionProvider>();

        Assert.Equal("deepseek", chatProvider.Name);
        Assert.IsType<DeepSeekChatCompletionProvider>(chatProvider);
    }

    [Fact]
    public void Tanimsiz_saglayici_adi_uygulamayi_dusurmuyor()
    {
        // Bir yazım hatasının uygulamayı hiç açılmaz yapması, yapılandırma
        // hatasının bedelini orantısız kılardı.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var provider = BuildProductionLikeProvider(connection, ("Ai:Provider", "gpt4all"));
        using var scope = provider.CreateScope();

        Assert.Equal("groq", scope.ServiceProvider.GetRequiredService<IChatCompletionProvider>().Name);
    }
}
