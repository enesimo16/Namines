using Microsoft.Extensions.DependencyInjection;
using Namines.Desk.Abstractions;
using Namines.Desk.Services;

namespace Namines.Desk.DependencyInjection
{
    /// <summary>
    /// Namines Desk modülünü DI konteynerine kaydeden uzantılar.
    /// </summary>
    public static class DeskServiceCollectionExtensions
    {
        public static IServiceCollection AddDeskModule(this IServiceCollection services)
        {
            services.AddSingleton<IDeskHandoffTokenValidator, DeskHandoffTokenValidator>();
            services.AddSingleton<IDeskModule, DeskModule>();

            return services;
        }
    }
}
