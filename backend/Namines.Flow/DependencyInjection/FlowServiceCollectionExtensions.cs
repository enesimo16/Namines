using Microsoft.Extensions.DependencyInjection;
using Namines.Flow.Abstractions;
using Namines.Flow.Services;

namespace Namines.Flow.DependencyInjection
{
    /// <summary>
    /// Namines Flow modülünü DI konteynerine kaydeden uzantılar.
    /// </summary>
    public static class FlowServiceCollectionExtensions
    {
        public static IServiceCollection AddFlowModule(this IServiceCollection services)
        {
            services.AddSingleton<IFlowRuleMatcher, FlowRuleMatcher>();
            services.AddSingleton<IFlowModule, FlowModule>();

            return services;
        }
    }
}
