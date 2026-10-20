using System.Collections.Generic;

namespace Namines.Ground.Abstractions
{
    /// <summary>
    /// Namines Ground modülünün dış dünyaya sunduğu üst seviye modül sözleşmesi (Modular Monolith Facade).
    /// </summary>
    public interface IGroundModule
    {
        IEnumerable<IDatabaseProvider> Providers { get; }
        IDatabaseProvider? GetProvider(string providerName);
    }
}
