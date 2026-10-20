using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Ground.Abstractions;

namespace Namines.Ground
{
    /// <summary>
    /// Namines Ground modülü facade implementasyonu.
    /// </summary>
    public sealed class GroundModule : IGroundModule
    {
        public IEnumerable<IDatabaseProvider> Providers { get; }

        public GroundModule(IEnumerable<IDatabaseProvider> providers)
        {
            Providers = providers ?? throw new ArgumentNullException(nameof(providers));
        }

        public IDatabaseProvider? GetProvider(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName)) return null;

            return Providers.FirstOrDefault(p =>
                string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
