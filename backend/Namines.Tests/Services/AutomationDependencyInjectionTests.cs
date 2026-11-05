using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.API.Extensions;
using Namines.Flow.Abstractions;
using Namines.Flow.Application;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// ZİNCİR TESTİ (C1/I3): her katmanın kendi birim testi geçiyordu ama
/// Flow modülünün uygulama servisi (<see cref="IFlowApplicationService"/> / <see cref="IFlowModule"/>)
/// üretim kayıtlarıyla (<see cref="ServiceCollectionExtensions.AddNaminesServices"/>)
/// eksiksiz çözümlenebilmeli.
/// </summary>
public sealed class AutomationDependencyInjectionTests
{
    private static ServiceProvider BuildProductionLikeProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection));
        services.AddDbContext<OpsDbContext>(options => options.UseSqlite(connection));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:ConnectionEncryptionKey"] = "test-encryption-key-that-is-at-least-32-bytes-long!"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeHostEnvironment());
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddNaminesServices(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
    }

    [Fact]
    public void IFlowApplicationService_uretim_kayitlariyla_cozumlenebiliyor()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var provider = BuildProductionLikeProvider(connection);
        using var scope = provider.CreateScope();

        var flowApp = scope.ServiceProvider.GetRequiredService<IFlowApplicationService>();
        var flowModule = scope.ServiceProvider.GetRequiredService<IFlowModule>();

        Assert.NotNull(flowApp);
        Assert.IsType<FlowApplicationService>(flowApp);
        Assert.Same(flowApp, flowModule.Application);
    }

    [Fact]
    public void IAiQuotaReserver_uretim_kayitlariyla_cozumlenebiliyor()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var provider = BuildProductionLikeProvider(connection);
        using var scope = provider.CreateScope();

        var quota = scope.ServiceProvider.GetRequiredService<IAiQuotaReserver>();

        Assert.IsType<AiQuotaService>(quota);
        Assert.Same(scope.ServiceProvider.GetRequiredService<AiQuotaService>(), quota);
    }

    private sealed class FakeHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Namines.Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
