using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.API.Extensions;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// ZİNCİR TESTİ (C1/I3): her katmanın kendi birim testi geçiyordu ama
/// <see cref="IAutomationExecutor"/> ÇÖZÜMLENEMİYORDU — ctor'undaki
/// <see cref="IAiQuotaReserver"/> hiçbir yerde kayıtlı değildi (yalnızca somut
/// <c>AiQuotaService</c> kayıtlıydı). Sonuç: Development'ta açılışta
/// ValidateOnBuild patlaması, Production'da her otomasyon işinin işçinin
/// catch'inde sessizce ölmesi.
///
/// Burada ÜRETİMDEKİ kayıt yolu (<see cref="ServiceCollectionExtensions.AddNaminesServices"/>)
/// aynen çalıştırılıp konteyner gerçekten kuruluyor; taklit/atlatma yok.
/// </summary>
public sealed class AutomationDependencyInjectionTests
{
    private static ServiceProvider BuildProductionLikeProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Program.cs'in AddNaminesServices'ten AYRI yaptığı tek şey
        // (satır ~150); burada SQLite ile karşılığı veriliyor.
        services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Host (WebApplicationBuilder) IConfiguration'ı kendisi kaydeder;
        // burada ham bir ServiceCollection kullanıldığı için elle veriliyor.
        services.AddSingleton<IConfiguration>(configuration);

        // Aynı şekilde ASP.NET Core'un kendi altyapı kaydı (Program.cs:
        // AddHttpContextAccessor) — GroqAIService buna bağımlı.
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddNaminesServices(configuration);

        // ValidateScopes: scoped bir servisin kök kapsamdan çözümlenmesini de
        // hata sayar — AutomationExecutor scoped ve DbContext'e bağımlı.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
    }

    [Fact]
    public void IAutomationExecutor_uretim_kayitlariyla_cozumlenebiliyor()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var provider = BuildProductionLikeProvider(connection);
        using var scope = provider.CreateScope();

        var executor = scope.ServiceProvider.GetRequiredService<IAutomationExecutor>();

        Assert.NotNull(executor);
        Assert.IsType<AutomationExecutor>(executor);
    }

    [Fact]
    public void IAiQuotaReserver_uretim_kayitlariyla_cozumlenebiliyor()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var provider = BuildProductionLikeProvider(connection);
        using var scope = provider.CreateScope();

        var quota = scope.ServiceProvider.GetRequiredService<IAiQuotaReserver>();

        // Devredici fabrika: arayüz kaydı YENİ bir nesne kurmuyor, kayıtlı
        // somut AiQuotaService'i çözümlüyor (IAIService ile aynı desen).
        Assert.IsType<AiQuotaService>(quota);
        Assert.Same(scope.ServiceProvider.GetRequiredService<AiQuotaService>(), quota);
    }
}
