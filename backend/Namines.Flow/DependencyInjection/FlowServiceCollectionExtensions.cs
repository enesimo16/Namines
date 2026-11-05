using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Namines.Flow.Abstractions;
using Namines.Flow.Application;
using Namines.Flow.Infrastructure.Persistence;
using Namines.Flow.Infrastructure.Workers;
using Namines.Flow.Services;

namespace Namines.Flow.DependencyInjection;

/// <summary>
/// Namines Flow modülünü DI konteynerine kaydeden uzantılar.
/// </summary>
public static class FlowServiceCollectionExtensions
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=postgres";

    public static IServiceCollection AddFlowModule(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDbContext = null,
        bool registerWorkers = true)
    {
        if (configureDbContext is not null)
        {
            services.AddDbContext<FlowDbContext>(configureDbContext);
        }
        else
        {
            services.AddDbContext<FlowDbContext>((sp, options) =>
            {
                var config = sp.GetService<IConfiguration>();
                var connStr = config?.GetConnectionString("DefaultConnection") ?? FallbackConnectionString;
                options.UseNpgsql(
                    connStr,
                    npgsql => npgsql.MigrationsHistoryTable(
                        FlowDbContext.MigrationsHistoryTableName,
                        FlowDbContext.SchemaName));
            });
        }

        services.AddSingleton(Namines.BuildingBlocks.Persistence.NonOwnerContext.Descriptor<Namines.Flow.Infrastructure.Persistence.FlowDbContext>("Flow", "flow", "Tablolar kendi şemasında; DDL AuthDbContext zincirinde kalıyor. Bu fazda tablo taşınmadı, bağlam yalnızca okur/yazar."));

        services.TryAddSingleton<IFlowRuleMatcher, FlowRuleMatcher>();
        services.TryAddSingleton<IFlowJobQueue, FlowJobQueue>();
        services.TryAddScoped<IFlowApplicationService, FlowApplicationService>();
        services.TryAddScoped<IFlowModule>(sp => new FlowModule(
            sp.GetRequiredService<IFlowRuleMatcher>(),
            sp.GetRequiredService<IFlowJobQueue>(),
            sp));

        if (registerWorkers)
        {
            services.AddHostedService<FlowExecutorWorker>();
            services.AddHostedService<FlowRunLogRetentionService>();
        }

        return services;
    }
}
