using System.Collections.Generic;
using Namines.Vault.Storage;

namespace Namines.Vault.Abstractions
{
    /// <summary>
    /// Namines Vault modülünün dış dünyaya sunduğu üst seviye modül sözleşmesi (Modular Monolith Facade).
    /// </summary>
    public interface IVaultModule
    {
        IBackupStore Store { get; }
        BackupCipher Cipher { get; }
        IEnumerable<IBackupProvider> Providers { get; }
    }
}
