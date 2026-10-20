using Microsoft.Extensions.DependencyInjection;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.Resilience;

/// <summary>
/// Polly v8 tabanlı dayanıklılık servislerinin DI kaydı.
/// </summary>
public static class ResilienceServiceCollectionExtensions
{
    public static IServiceCollection AddNaminesResilience(this IServiceCollection services)
    {
        services.AddSingleton<INaminesResilienceProvider, NaminesResilienceProvider>();
        return services;
    }
}
