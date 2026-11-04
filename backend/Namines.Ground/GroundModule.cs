using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Namines.Ground.Abstractions;
using Namines.Ground.Application;

namespace Namines.Ground
{
    /// <summary>
    /// Namines Ground modülü facade implementasyonu.
    /// </summary>
    public sealed class GroundModule : IGroundModule
    {
        private readonly IServiceProvider? _serviceProvider;
        private IGroundApplicationService? _databases;

        public IEnumerable<IDatabaseProvider> Providers { get; }
        public IGroundApplicationService Databases =>
            _databases ??= _serviceProvider!.GetRequiredService<IGroundApplicationService>();

        [ActivatorUtilitiesConstructor]
        public GroundModule(
            IEnumerable<IDatabaseProvider> providers,
            IServiceProvider serviceProvider)
        {
            Providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public GroundModule(
            IEnumerable<IDatabaseProvider> providers,
            IGroundApplicationService databases)
        {
            Providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _databases = databases ?? throw new ArgumentNullException(nameof(databases));
        }

        public IDatabaseProvider? GetProvider(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName)) return null;

            return Providers.FirstOrDefault(p =>
                string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
