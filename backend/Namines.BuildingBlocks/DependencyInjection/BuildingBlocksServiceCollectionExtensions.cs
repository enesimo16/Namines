using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Namines.BuildingBlocks.Persistence;

namespace Namines.BuildingBlocks.DependencyInjection;

/// <summary>
/// Modüler monolit paylaşılan altyapısı: yalnızca paylaşılan işlem yöneticisi. Modüllerin kendi bağlamları ve
/// servisleri kendi <c>Add*Module</c> uzantılarında; burada hiçbir modül tanınmaz.
/// </summary>
public static class BuildingBlocksServiceCollectionExtensions
{
    public static IServiceCollection AddNaminesBuildingBlocks(this IServiceCollection services)
    {
        services.TryAddScoped<ISharedTransactionManager, SharedTransactionManager>();
        return services;
    }
}
