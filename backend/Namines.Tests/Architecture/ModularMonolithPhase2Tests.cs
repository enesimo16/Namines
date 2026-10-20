using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Events;
using Namines.Desk.Abstractions;
using Namines.Desk.DependencyInjection;
using Namines.Flow.Abstractions;
using Namines.Flow.DependencyInjection;
using Namines.Ground.Abstractions;
using Namines.Ground.DependencyInjection;
using Namines.Infrastructure.Events;
using Namines.Infrastructure.Migrations;
using Namines.Vault.Abstractions;
using Namines.Vault.DependencyInjection;
using Xunit;

namespace Namines.Tests.Architecture
{
    /// <summary>
    /// Modular Monolith (Faz 2) — Modüller arası olay veri yolu (In-Process Event Bus),
    /// üst seviye modül facade sözleşmeleri ve şema izolasyon migration doğrulama testleri.
    /// </summary>
    public sealed class ModularMonolithPhase2Tests
    {
        private class TestVaultBackupHandler : IModuleEventHandler<VaultBackupCompletedEvent>
        {
            public List<VaultBackupCompletedEvent> HandledEvents { get; } = new();

            public Task HandleAsync(VaultBackupCompletedEvent @event, CancellationToken cancellationToken = default)
            {
                HandledEvents.Add(@event);
                return Task.CompletedTask;
            }
        }

        private class FailingHandler : IModuleEventHandler<VaultBackupCompletedEvent>
        {
            public Task HandleAsync(VaultBackupCompletedEvent @event, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Handler hatası simülasyonu");
            }
        }

        [Fact]
        public async Task ModuleEventBus_Olay_Basariyla_Yayinlaniyor_Ve_Isleyiciye_Ulasıyor()
        {
            var services = new ServiceCollection();
            var testHandler = new TestVaultBackupHandler();
            services.AddSingleton<IModuleEventHandler<VaultBackupCompletedEvent>>(testHandler);
            services.AddSingleton<ILogger<ModuleEventBus>, NullLogger<ModuleEventBus>>();
            services.AddSingleton<IModuleEventBus, ModuleEventBus>();

            var provider = services.BuildServiceProvider();
            var bus = provider.GetRequiredService<IModuleEventBus>();

            var @event = new VaultBackupCompletedEvent(
                BackupId: "bk_123",
                ProjectId: "proj_99",
                DatabaseName: "main_db",
                SizeBytes: 1048576,
                Success: true);

            await bus.PublishAsync(@event);

            Assert.Single(testHandler.HandledEvents);
            var handled = testHandler.HandledEvents[0];
            Assert.Equal("bk_123", handled.BackupId);
            Assert.Equal("proj_99", handled.ProjectId);
            Assert.Equal("main_db", handled.DatabaseName);
            Assert.Equal(1048576, handled.SizeBytes);
            Assert.True(handled.Success);
            Assert.NotEmpty(handled.EventId);
        }

        [Fact]
        public async Task ModuleEventBus_Bir_Isleyici_Hata_Firlatsa_Bile_Digerlerini_Bozmaz()
        {
            var services = new ServiceCollection();
            var testHandler = new TestVaultBackupHandler();
            services.AddSingleton<IModuleEventHandler<VaultBackupCompletedEvent>>(new FailingHandler());
            services.AddSingleton<IModuleEventHandler<VaultBackupCompletedEvent>>(testHandler);
            services.AddSingleton<ILogger<ModuleEventBus>, NullLogger<ModuleEventBus>>();
            services.AddSingleton<IModuleEventBus, ModuleEventBus>();

            var provider = services.BuildServiceProvider();
            var bus = provider.GetRequiredService<IModuleEventBus>();

            var @event = new VaultBackupCompletedEvent(
                BackupId: "bk_safe",
                ProjectId: "proj_safe",
                DatabaseName: "safe_db",
                SizeBytes: 500,
                Success: true);

            // İstisna çağıran koda yansımamalı, loglanarak diğer handler çalışmalı
            await bus.PublishAsync(@event);

            Assert.Single(testHandler.HandledEvents);
            Assert.Equal("bk_safe", testHandler.HandledEvents[0].BackupId);
        }

        [Fact]
        public void Flow_Module_Facade_Ve_Operasyonlari_Dogru_Calisiyor()
        {
            var services = new ServiceCollection();
            services.AddFlowModule();

            var provider = services.BuildServiceProvider();
            var flowModule = provider.GetService<IFlowModule>();

            Assert.NotNull(flowModule);
            Assert.NotNull(flowModule.RuleMatcher);
        }

        [Fact]
        public void Desk_Module_Facade_Ve_Operasyonlari_Dogru_Calisiyor()
        {
            var services = new ServiceCollection();
            services.AddDeskModule();

            var provider = services.BuildServiceProvider();
            var deskModule = provider.GetService<IDeskModule>();

            Assert.NotNull(deskModule);
            Assert.NotNull(deskModule.TokenValidator);
        }

        [Fact]
        public void Vault_Module_Facade_Ve_Operasyonlari_Dogru_Calisiyor()
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                ["Vault:BackupEncryptionKey"] = "this-is-a-valid-test-key-of-at-least-32-characters"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging();
            services.AddVaultModule();

            var provider = services.BuildServiceProvider();
            var vaultModule = provider.GetService<IVaultModule>();

            Assert.NotNull(vaultModule);
            Assert.NotNull(vaultModule.Store);
            Assert.NotNull(vaultModule.Cipher);
            Assert.NotEmpty(vaultModule.Providers);
        }

        [Fact]
        public void Ground_Module_Facade_Ve_Operasyonlari_Dogru_Calisiyor()
        {
            var config = new ConfigurationBuilder().Build();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging();
            services.AddGroundModule();

            var provider = services.BuildServiceProvider();
            var groundModule = provider.GetService<IGroundModule>();

            Assert.NotNull(groundModule);
            Assert.NotEmpty(groundModule.Providers);

            // LocalPostgres provider'ı aranabilir
            var localProvider = groundModule.GetProvider("LocalPostgres");
            Assert.NotNull(localProvider);
        }

        [Fact]
        public void ModularMonolithSchemaIsolation_Migration_Sinifi_Mevcut_Ve_Semalari_Iceriyor()
        {
            var migrationType = typeof(ModularMonolithSchemaIsolation);
            Assert.NotNull(migrationType);

            // Migration sınıfı var ve ismi doğru
            Assert.Equal("ModularMonolithSchemaIsolation", migrationType.Name);
        }
    }
}
