using System;
using System.Collections.Generic;
using Namines.Vault.Abstractions;
using Namines.Vault.Storage;

namespace Namines.Vault
{
    /// <summary>
    /// Namines Vault modülü facade implementasyonu.
    /// </summary>
    public sealed class VaultModule : IVaultModule
    {
        public IBackupStore Store { get; }
        public BackupCipher Cipher { get; }
        public IEnumerable<IBackupProvider> Providers { get; }

        public VaultModule(IBackupStore store, BackupCipher cipher, IEnumerable<IBackupProvider> providers)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            Providers = providers ?? throw new ArgumentNullException(nameof(providers));
        }
    }
}
