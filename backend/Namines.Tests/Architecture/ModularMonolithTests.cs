using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Namines.Core.Analysis;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Desk.Abstractions;
using Namines.Desk.Configurations;
using Namines.Desk.DependencyInjection;
using Namines.Desk.Services;
using Namines.Flow.Abstractions;
using Namines.Flow.Configurations;
using Namines.Flow.DependencyInjection;
using Namines.Flow.Services;
using Namines.Ground.Configurations;
using Namines.Ground.DependencyInjection;
using Namines.Vault.Configurations;
using Namines.Vault.DependencyInjection;
using Xunit;

namespace Namines.Tests.Architecture
{
    /// <summary>
    /// Modular Monolith Mimari Doğrulama Testleri (Faz 1)
    /// Flow, Desk, Vault ve Ground modüllerinin DI kayıtlarını, izole şema konfigürasyonlarını
    /// ve temel domain servislerini doğrular.
    /// </summary>
    public sealed class ModularMonolithTests
    {
        [Fact]
        public void Flow_Module_DependencyInjection_Kayitlari_Dogru_Cozumleniyor()
        {
            var services = new ServiceCollection();
            services.AddFlowModule();

            var provider = services.BuildServiceProvider();
            var matcher = provider.GetService<IFlowRuleMatcher>();

            Assert.NotNull(matcher);
            Assert.IsType<FlowRuleMatcher>(matcher);
        }

        [Fact]
        public void Desk_Module_DependencyInjection_Kayitlari_Dogru_Cozumleniyor()
        {
            var services = new ServiceCollection();
            services.AddDeskModule();

            var provider = services.BuildServiceProvider();
            var validator = provider.GetService<IDeskHandoffTokenValidator>();

            Assert.NotNull(validator);
            Assert.IsType<DeskHandoffTokenValidator>(validator);
        }

        [Fact]
        public void Vault_Ve_Ground_Module_Alias_Kayitlari_Basariyla_Calisiyor()
        {
            var services = new ServiceCollection();
            services.AddVaultModule();
            services.AddGroundModule();

            var provider = services.BuildServiceProvider();
            Assert.NotNull(provider);
        }

        [Fact]
        public void Tum_Moduller_Relational_Semalarini_Izole_Olarak_Tanimliyor()
        {
            var builder = new ModelBuilder();

            // Tüm modüllerin model yapılandırmalarını kendi şemalarıyla uygula
            FlowModelConfiguration.Configure(builder, "flow");
            VaultModelConfiguration.Configure(builder, "vault");
            GroundModelConfiguration.Configure(builder, "ground");
            DeskModelConfiguration.Configure(builder, "desk");

            // Flow tabloları "flow" şemasında olmalı
            var flowRuleEntity = builder.Model.FindEntityType(typeof(AutomationRule));
            Assert.NotNull(flowRuleEntity);
            Assert.Equal("flow", flowRuleEntity.GetSchema());
            Assert.Equal("automation_rules", flowRuleEntity.GetTableName());

            var flowActionEntity = builder.Model.FindEntityType(typeof(AutomationAction));
            Assert.NotNull(flowActionEntity);
            Assert.Equal("flow", flowActionEntity.GetSchema());

            var flowLogEntity = builder.Model.FindEntityType(typeof(AutomationRunLog));
            Assert.NotNull(flowLogEntity);
            Assert.Equal("flow", flowLogEntity.GetSchema());

            // Vault tabloları "vault" şemasında olmalı
            var vaultBackupEntity = builder.Model.FindEntityType(typeof(VaultBackup));
            Assert.NotNull(vaultBackupEntity);
            Assert.Equal("vault", vaultBackupEntity.GetSchema());
            Assert.Equal("vault_backups", vaultBackupEntity.GetTableName());

            var vaultRestoreEntity = builder.Model.FindEntityType(typeof(VaultRestore));
            Assert.NotNull(vaultRestoreEntity);
            Assert.Equal("vault", vaultRestoreEntity.GetSchema());

            // Ground tabloları "ground" şemasında olmalı
            var groundDbEntity = builder.Model.FindEntityType(typeof(GroundDatabase));
            Assert.NotNull(groundDbEntity);
            Assert.Equal("ground", groundDbEntity.GetSchema());
            Assert.Equal("ground_databases", groundDbEntity.GetTableName());

            // Desk tabloları "desk" şemasında olmalı
            var deskTokenEntity = builder.Model.FindEntityType(typeof(DeskHandoffToken));
            Assert.NotNull(deskTokenEntity);
            Assert.Equal("desk", deskTokenEntity.GetSchema());
            Assert.Equal("desk_handoff_tokens", deskTokenEntity.GetTableName());

            var sqlHistoryEntity = builder.Model.FindEntityType(typeof(SqlQueryHistoryEntry));
            Assert.NotNull(sqlHistoryEntity);
            Assert.Equal("desk", sqlHistoryEntity.GetSchema());

            var savedQueryEntity = builder.Model.FindEntityType(typeof(SavedQuery));
            Assert.NotNull(savedQueryEntity);
            Assert.Equal("desk", savedQueryEntity.GetSchema());
        }

        [Fact]
        public void DeskHandoffTokenValidator_Dogrulama_Kurallari_Dogru_Calisiyor()
        {
            var validator = new DeskHandoffTokenValidator();

            // Hash token deterministik ve boş olmamalı
            var hash1 = validator.HashToken("token_123");
            var hash2 = validator.HashToken("token_123");
            Assert.NotEmpty(hash1);
            Assert.Equal(hash1, hash2);

            // Süresi geçmiş token IsExpired = true
            var expiredToken = new DeskHandoffToken
            {
                TokenHash = "hash123",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                UsedAt = null
            };
            Assert.True(validator.IsExpired(expiredToken));

            // Kullanılmış (UsedAt dolu) token IsExpired = true
            var usedToken = new DeskHandoffToken
            {
                TokenHash = "hash456",
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                UsedAt = DateTime.UtcNow.AddMinutes(-1)
            };
            Assert.True(validator.IsExpired(usedToken));

            // Geçerli henüz kullanılmamış token IsExpired = false
            var validToken = new DeskHandoffToken
            {
                TokenHash = "hash789",
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                UsedAt = null
            };
            Assert.False(validator.IsExpired(validToken));
        }

        [Fact]
        public void FlowRuleMatcher_Tetikleyici_Eslesmelerini_Dogru_Degerlendiriyor()
        {
            var matcher = new FlowRuleMatcher();

            var oldSchema = new DatabaseSchema
            {
                Tables = new List<SchemaTable>()
            };

            var newTable = new SchemaTable
            {
                Id = "tbl_orders_id",
                Name = "orders",
                Columns = new List<SchemaColumn>()
            };

            var newSchema = new DatabaseSchema
            {
                Tables = new List<SchemaTable> { newTable }
            };

            var diff = new SchemaDiffResult
            {
                AddedTables = new List<string> { "orders" },
                RemovedTables = new List<string>()
            };

            var rules = new List<AutomationRule>
            {
                new AutomationRule
                {
                    Id = "rule_1",
                    ProjectId = "proj_1",
                    Enabled = true,
                    TriggerType = "TableAdded",
                    ScopeTableId = "tbl_orders_id"
                },
                new AutomationRule
                {
                    Id = "rule_disabled",
                    ProjectId = "proj_1",
                    Enabled = false,
                    TriggerType = "TableAdded",
                    ScopeTableId = "tbl_orders_id"
                }
            };

            var matches = matcher.Match(diff, oldSchema, newSchema, rules);

            // Yalnızca etkin kural eşleşmeli
            Assert.Single(matches);
            Assert.Equal("rule_1", matches[0].Rule.Id);
            Assert.Equal("orders", matches[0].Context.TableName);
        }
    }
}
