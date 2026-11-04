using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Namines.Vault.Abstractions;
using Namines.Vault.Application;
using Namines.Vault.Storage;

namespace Namines.Vault
{
    /// <summary>
    /// Namines Vault modülü facade implementasyonu.
    /// </summary>
    public sealed class VaultModule : IVaultModule
    {
        private readonly IServiceProvider? _serviceProvider;
        private IVaultApplicationService? _backups;

        public IBackupStore Store { get; }
        public BackupCipher Cipher { get; }
        public IEnumerable<IBackupProvider> Providers { get; }
        public IVaultApplicationService Backups =>
            _backups ??= _serviceProvider!.GetRequiredService<IVaultApplicationService>();
        public IVaultJobQueue JobQueue { get; }

        [ActivatorUtilitiesConstructor]
        public VaultModule(
            IBackupStore store,
            BackupCipher cipher,
            IEnumerable<IBackupProvider> providers,
            IVaultJobQueue jobQueue,
            IServiceProvider serviceProvider)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            Providers = providers ?? throw new ArgumentNullException(nameof(providers));
            JobQueue = jobQueue ?? throw new ArgumentNullException(nameof(jobQueue));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public VaultModule(
            IBackupStore store,
            BackupCipher cipher,
            IEnumerable<IBackupProvider> providers,
            IVaultApplicationService backups,
            IVaultJobQueue jobQueue)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            Providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _backups = backups ?? throw new ArgumentNullException(nameof(backups));
            JobQueue = jobQueue ?? throw new ArgumentNullException(nameof(jobQueue));
        }
    }
}
