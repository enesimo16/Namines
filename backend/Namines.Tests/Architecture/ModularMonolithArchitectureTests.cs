using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.BuildingBlocks.DependencyInjection;
using Namines.BuildingBlocks.Modules;
using Namines.BuildingBlocks.Persistence;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Desk.Abstractions;
using Namines.Desk.DependencyInjection;
using Namines.Desk.Infrastructure.Persistence;
using Namines.Flow.Abstractions;
using Namines.Flow.DependencyInjection;
using Namines.Flow.Infrastructure.Persistence;
using Namines.Ground.Abstractions;
using Namines.Ground.DependencyInjection;
using Namines.Ground.Infrastructure.Persistence;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Security;
using Namines.Infrastructure.Services;
using Namines.Vault.Abstractions;
using Namines.Vault.DependencyInjection;
using Namines.Vault.Infrastructure.Persistence;

namespace Namines.Tests.Architecture;

/// <summary>
/// Modüler monolit sınırlarını, katmanlı mimari kurallarını, tenant önbellek
/// anahtarlamasını ve paylaşılan transaction/migration altyapısını doğrulayan
/// mimari ve birim testleri.
/// </summary>
public sealed class ModularMonolithArchitectureTests
{
    private static readonly Assembly BuildingBlocksAssembly = typeof(ITenantProjectAuthority).Assembly;
    private static readonly Assembly VaultAssembly = typeof(Namines.Vault.Configurations.VaultModelConfiguration).Assembly;
    private static readonly Assembly GroundAssembly = typeof(Namines.Ground.Configurations.GroundModelConfiguration).Assembly;
    private static readonly Assembly FlowAssembly = typeof(Namines.Flow.Configurations.FlowModelConfiguration).Assembly;
    private static readonly Assembly DeskAssembly = typeof(Namines.Desk.Configurations.DeskModelConfiguration).Assembly;

    [Fact]
    public void BuildingBlocks_And_Modules_MustNotReference_Infrastructure_Or_Api()
    {
        var assembliesToCheck = new[]
        {
            BuildingBlocksAssembly,
            VaultAssembly,
            GroundAssembly,
            FlowAssembly,
            DeskAssembly
        };

        var forbiddenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Namines.Infrastructure",
            "Namines.API"
        };

        foreach (var assembly in assembliesToCheck)
        {
            var referencedNames = assembly.GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .ToList();

            foreach (var forbidden in forbiddenReferences)
            {
                Assert.DoesNotContain(
                    referencedNames,
                    r => string.Equals(r, forbidden, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void Modules_MustNotReference_OtherModuleImplementationAssemblies()
    {
        var moduleAssemblies = new[]
        {
            VaultAssembly,
            GroundAssembly,
            FlowAssembly,
            DeskAssembly
        };

        var moduleNames = moduleAssemblies
            .Select(a => a.GetName().Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in moduleAssemblies)
        {
            var ownName = assembly.GetName().Name!;
            var crossModuleRefs = assembly.GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(name => !string.Equals(name, ownName, StringComparison.OrdinalIgnoreCase) &&
                               moduleNames.Contains(name))
                .ToList();

            Assert.Empty(crossModuleRefs);
        }
    }

    [Fact]
    public void Domain_Namespaces_MustNotDependOn_Infrastructure_Or_Presentation_Namespaces()
    {
        var moduleAssemblies = new[]
        {
            VaultAssembly,
            GroundAssembly,
            FlowAssembly,
            DeskAssembly
        };

        foreach (var assembly in moduleAssemblies)
        {
            var domainTypes = assembly.GetTypes()
                .Where(t => t.Namespace is not null &&
                            (t.Namespace.EndsWith(".Domain", StringComparison.Ordinal) ||
                             t.Namespace.Contains(".Domain.", StringComparison.Ordinal)))
                .ToList();

            foreach (var domainType in domainTypes)
            {
                var referencedTypes = GetDirectlyReferencedTypes(domainType);
                foreach (var refType in referencedTypes)
                {
                    var ns = refType.Namespace ?? string.Empty;
                    Assert.False(
                        ns.Contains(".Infrastructure", StringComparison.Ordinal) ||
                        ns.Contains(".Presentation", StringComparison.Ordinal),
                        $"Domain type '{domainType.FullName}' must not depend on '{refType.FullName}'.");
                }
            }
        }
    }

    [Fact]
    public void TenantCacheKey_Produces_Deterministic_And_TenantIsolated_Keys()
    {
        var keyOrgA = TenantCacheKey.ForOrganization("org-alpha", "ai_dba", "hash123");
        var keyOrgB = TenantCacheKey.ForOrganization("org-beta", "ai_dba", "hash123");

        Assert.NotEqual(keyOrgA, keyOrgB);
        Assert.Equal(keyOrgA, TenantCacheKey.ForOrganization("org-alpha", "ai_dba", "hash123"));

        // ':' enjeksiyonunu önlemeli
        var injected = TenantCacheKey.ForUser("u1:admin", "area", "key");
        Assert.Equal("nmn:u:u1_admin:area:key", injected);
    }

    [Fact]
    public async Task SemanticCacheService_Isolates_Tenants_And_Detects_Index_And_Constraint_Changes()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new SemanticCacheService(memoryCache);

        var baseSchema = new DatabaseSchema
        {
            Name = "shop",
            Tables =
            [
                new SchemaTable
                {
                    Id = "t1",
                    Name = "orders",
                    Columns =
                    [
                        new SchemaColumn { Id = "c1", Name = "id", Type = "uuid", IsPK = true },
                        new SchemaColumn { Id = "c2", Name = "total", Type = "numeric", Length = 10, Scale = 2, IsNullable = false }
                    ]
                }
            ]
        };

        await service.SetAsync("ai_dba", baseSchema, null, "response-tenant-a", TimeSpan.FromMinutes(5), tenantScope: "tenant-a");

        // Farklı tenant aynı şema ile sorguladığında tenant-a'nın önbelleğini GÖRMEMELİ (SEC-01).
        var crossTenantHit = await service.TryGetAsync("ai_dba", baseSchema, null, tenantScope: "tenant-b");
        Assert.Null(crossTenantHit);

        var sameTenantHit = await service.TryGetAsync("ai_dba", baseSchema, null, tenantScope: "tenant-a");
        Assert.Equal("response-tenant-a", sameTenantHit);

        // İndeks eklendiğinde şema özeti değişmeli ve eski önbellek geçersiz olmalı.
        var schemaWithIndex = new DatabaseSchema
        {
            Name = "shop",
            Tables =
            [
                new SchemaTable
                {
                    Id = "t1",
                    Name = "orders",
                    Columns =
                    [
                        new SchemaColumn { Id = "c1", Name = "id", Type = "uuid", IsPK = true },
                        new SchemaColumn { Id = "c2", Name = "total", Type = "numeric", Length = 10, Scale = 2, IsNullable = false }
                    ],
                    Indexes =
                    [
                        new SchemaIndex
                        {
                            Name = "ix_orders_total",
                            Columns = [new SchemaIndexColumn { ColumnId = "c2", Descending = false }],
                            IsUnique = false
                        }
                    ]
                }
            ]
        };

        var afterIndexAdded = await service.TryGetAsync("ai_dba", schemaWithIndex, null, tenantScope: "tenant-a");
        Assert.Null(afterIndexAdded);
    }

    [Fact]
    public async Task ModuleMigrationOrchestrator_Executes_Migrators_In_Order()
    {
        var executionOrder = new List<string>();
        var migrators = new IModuleDbContextMigrator[]
        {
            new FakeMigrator("vault", 60, executionOrder),
            new FakeMigrator("identity", 10, executionOrder),
            new FakeMigrator("studio", 20, executionOrder)
        };

        var orchestrator = new ModuleMigrationOrchestrator(
            migrators,
            NullLogger<ModuleMigrationOrchestrator>.Instance);

        await orchestrator.MigrateAllAsync();

        Assert.Equal(["identity", "studio", "vault"], executionOrder);
    }

    [Fact]
    public async Task SharedTransactionManager_Commits_And_RollsBack_Across_Multiple_DbContexts()
    {
        await using var sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await sqliteConnection.OpenAsync();

        var optionsA = new DbContextOptionsBuilder<TestContextA>()
            .UseSqlite(sqliteConnection)
            .Options;
        var optionsB = new DbContextOptionsBuilder<TestContextB>()
            .UseSqlite(sqliteConnection)
            .Options;

        await using var ctxA = new TestContextA(optionsA);
        await using var ctxB = new TestContextB(optionsB);
        await ctxA.Database.EnsureCreatedAsync();

        await using (var createCmd = sqliteConnection.CreateCommand())
        {
            createCmd.CommandText = "CREATE TABLE IF NOT EXISTS ItemsB (Id TEXT PRIMARY KEY, Label TEXT NOT NULL);";
            await createCmd.ExecuteNonQueryAsync();
        }

        var manager = new SharedTransactionManager();

        // Rollback senaryosu: ikinci context'ten sonra hata fırlatılırsa ilk context'in yazımı da geri alınmalı.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await manager.ExecuteInTransactionAsync(
                ctxA,
                new DbContext[] { ctxB },
                async ct =>
                {
                    ctxA.ItemsA.Add(new ItemA { Id = "a-1", Name = "First" });
                    await ctxA.SaveChangesAsync(ct);

                    ctxB.ItemsB.Add(new ItemB { Id = "b-1", Label = "Second" });
                    await ctxB.SaveChangesAsync(ct);

                    throw new InvalidOperationException("Simulated failure across modules.");
                });
        });

        ctxA.ChangeTracker.Clear();
        ctxB.ChangeTracker.Clear();
        Assert.Equal(0, await ctxA.ItemsA.CountAsync());
        Assert.Equal(0, await ctxB.ItemsB.CountAsync());

        // Commit senaryosu: hata olmadığında her iki context'in yazımı kalıcı olmalı.
        await manager.ExecuteInTransactionAsync(
            ctxA,
            new DbContext[] { ctxB },
            async ct =>
            {
                ctxA.ItemsA.Add(new ItemA { Id = "a-2", Name = "Committed A" });
                await ctxA.SaveChangesAsync(ct);

                ctxB.ItemsB.Add(new ItemB { Id = "b-2", Label = "Committed B" });
                await ctxB.SaveChangesAsync(ct);
            });

        Assert.Equal(1, await ctxA.ItemsA.CountAsync());
        Assert.Equal(1, await ctxB.ItemsB.CountAsync());
        Assert.Null(ctxA.Database.CurrentTransaction);
        Assert.Null(ctxB.Database.CurrentTransaction);

        // Farklı başlangıç bağlantısına sahip ikincil context senaryosu: işlem sonunda orijinal bağlantı geri yüklenmeli.
        await using var separateConnC = new SqliteConnection("DataSource=:memory:");
        await separateConnC.OpenAsync();
        var optionsC = new DbContextOptionsBuilder<TestContextB>()
            .UseSqlite(separateConnC)
            .Options;
        await using var ctxC = new TestContextB(optionsC);

        await manager.ExecuteInTransactionAsync(
            ctxA,
            new DbContext[] { ctxC },
            async ct =>
            {
                Assert.Same(sqliteConnection, ctxC.Database.GetDbConnection());
                ctxC.ItemsB.Add(new ItemB { Id = "b-3", Label = "Via Shared Conn" });
                await ctxC.SaveChangesAsync(ct);
            });

        Assert.Same(separateConnC, ctxC.Database.GetDbConnection());
        Assert.Null(ctxC.Database.CurrentTransaction);
        Assert.Equal(2, await ctxB.ItemsB.CountAsync());
    }

    [Fact]
    public async Task TenantProjectAuthorityService_Enforces_RoleHierarchy_And_SecretIsolation()
    {
        await using var sqlite = new SqliteConnection("DataSource=:memory:");
        await sqlite.OpenAsync();

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite(sqlite)
            .Options;

        await using var context = new AuthDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:ConnectionEncryptionKey"] = "0123456789abcdef0123456789abcdef-strong-key"
            })
            .Build();
        var protector = new AesGcmConnectionSecretProtector(config);

        var owner = new ApplicationUser { Id = "user-owner", UserName = "owner@test.com", Email = "owner@test.com" };
        var viewer = new ApplicationUser { Id = "user-viewer", UserName = "viewer@test.com", Email = "viewer@test.com" };
        var outsider = new ApplicationUser { Id = "user-outsider", UserName = "out@test.com", Email = "out@test.com" };
        context.Users.AddRange(owner, viewer, outsider);

        var org = new Organization { Id = "org-1", Name = "Acme", CreatedByUserId = owner.Id };
        context.Organizations.Add(org);
        context.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = org.Id, UserId = owner.Id, Role = OrgRole.Owner },
            new OrganizationMember { OrganizationId = org.Id, UserId = viewer.Id, Role = OrgRole.Viewer });

        var project = new CloudProject
        {
            Id = "proj-1",
            Name = "Orders DB",
            DbType = "PostgreSQL",
            SchemaJson = "{}",
            NodePositionsJson = "{}",
            UserId = owner.Id,
            OrganizationId = org.Id
        };
        context.CloudProjects.Add(project);
        await context.SaveChangesAsync();

        var authority = new TenantProjectAuthorityService(context, protector);

        // Outsider erişemez
        Assert.False(await authority.CanViewAsync(project.Id, outsider.Id));

        // Viewer okuyabilir ama düzenleyemez
        Assert.True(await authority.CanViewAsync(project.Id, viewer.Id));
        Assert.False(await authority.CanEditAsync(project.Id, viewer.Id));

        // Owner düzenleyebilir ve yönetebilir
        Assert.True(await authority.IsOwnerAsync(project.Id, owner.Id));
        var snapshot = await authority.GetProjectSnapshotAsync(project.Id);
        Assert.NotNull(snapshot);
        Assert.Equal(org.Id, snapshot!.OrganizationId);

        // Bağlantı sırrı şifreli yazılıp çözülebilmeli
        Assert.True(await authority.SetConnectionAsync(
            project.Id,
            "Host=db.internal;Database=orders;Username=app;Password=secret",
            "PostgreSQL"));

        var resolved = await authority.ResolveAsync(project.Id);
        Assert.NotNull(resolved);
        Assert.Equal("PostgreSQL", resolved!.ConnectionDbType);
        Assert.Contains("Password=secret", resolved.ConnectionString);

        Assert.True(await authority.ClearConnectionAsync(project.Id));
        Assert.Null(await authority.ResolveAsync(project.Id));
    }

    [Fact]
    public void All_Eleven_Modules_Register_DbContexts_Facades_And_Ordered_Migrators()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vault:BackupEncryptionKey"] = "0123456789abcdef0123456789abcdef-vault-test-key"
            })
            .Build());

        services.AddNaminesBuildingBlocks();
        services.AddNaminesVault(registerWorkers: false);
        services.AddNaminesGround(registerWorkers: false);
        services.AddFlowModule(registerWorkers: false);
        services.AddDeskModule();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var orchestrator = scope.ServiceProvider.GetRequiredService<ModuleMigrationOrchestrator>();
        Assert.Equal(11, orchestrator.Migrators.Count);

        var expectedOrder = new (int Order, string ModuleName, string SchemaName)[]
        {
            (10, "Identity", "identity"),
            (20, "Studio", "studio"),
            (30, "Billing", "billing"),
            (40, "Governance", "governance"),
            (50, "Ground", "ground"),
            (60, "Vault", "namines_vault"),
            (70, "Flow", "flow"),
            (80, "Desk", "desk"),
            (90, "Hub", "hub"),
            (100, "Gateway", "gateway"),
            (110, "Ops", "ops")
        };

        for (int i = 0; i < expectedOrder.Length; i++)
        {
            Assert.Equal(expectedOrder[i].Order, orchestrator.Migrators[i].Order);
            Assert.Equal(expectedOrder[i].ModuleName, orchestrator.Migrators[i].ModuleName);
            Assert.Equal(expectedOrder[i].SchemaName, orchestrator.Migrators[i].SchemaName);
        }

        // Tüm 11 modül cephesi (facade) ve izole DbContext'leri çözümlenebilmeli ve PostgreSQL şemaları eşleşmeli.
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityModule>();
        var studio = scope.ServiceProvider.GetRequiredService<IStudioModule>();
        var billing = scope.ServiceProvider.GetRequiredService<IBillingModule>();
        var governance = scope.ServiceProvider.GetRequiredService<IGovernanceModule>();
        var ground = scope.ServiceProvider.GetRequiredService<IGroundModule>();
        var vault = scope.ServiceProvider.GetRequiredService<IVaultModule>();
        var flow = scope.ServiceProvider.GetRequiredService<IFlowModule>();
        var desk = scope.ServiceProvider.GetRequiredService<IDeskModule>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubModule>();
        var gateway = scope.ServiceProvider.GetRequiredService<IGatewayModule>();
        var ops = scope.ServiceProvider.GetRequiredService<IOpsModule>();

        Assert.Equal("identity", identity.DbContext.Model.GetDefaultSchema());
        Assert.Equal("studio", studio.DbContext.Model.GetDefaultSchema());
        Assert.Equal("billing", billing.DbContext.Model.GetDefaultSchema());
        Assert.Equal("governance", governance.DbContext.Model.GetDefaultSchema());
        Assert.Equal("hub", hub.DbContext.Model.GetDefaultSchema());
        Assert.Equal("gateway", gateway.DbContext.Model.GetDefaultSchema());
        Assert.Equal("ops", ops.DbContext.Model.GetDefaultSchema());

        var groundDb = scope.ServiceProvider.GetRequiredService<GroundDbContext>();
        var vaultDb = scope.ServiceProvider.GetRequiredService<VaultDbContext>();
        var flowDb = scope.ServiceProvider.GetRequiredService<FlowDbContext>();
        var deskDb = scope.ServiceProvider.GetRequiredService<DeskDbContext>();

        Assert.Equal("ground", groundDb.Model.GetDefaultSchema());
        Assert.Equal("namines_vault", vaultDb.Model.GetDefaultSchema());
        Assert.Equal("flow", flowDb.Model.GetDefaultSchema());
        Assert.Equal("desk", deskDb.Model.GetDefaultSchema());
        Assert.NotNull(ground);
        Assert.NotNull(vault);
        Assert.NotNull(flow);
        Assert.NotNull(desk);
    }

    [Fact]
    public void ThreeNf_Entities_And_Migration_Scripts_Enforce_Transactional_Verification_And_TokenHashing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaminesBuildingBlocks();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var studioDb = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
        var hubDb = scope.ServiceProvider.GetRequiredService<HubDbContext>();

        var connEntity = studioDb.Model.FindEntityType(typeof(ProjectConnection));
        var gitEntity = studioDb.Model.FindEntityType(typeof(ProjectGitRepository));
        var shareEntity = studioDb.Model.FindEntityType(typeof(SchemaShareLink));
        var pubEntity = hubDb.Model.FindEntityType(typeof(ProjectPublication));

        Assert.NotNull(connEntity);
        Assert.Equal("project_connections", connEntity!.GetTableName());
        Assert.Equal("studio", connEntity.GetSchema());

        Assert.NotNull(gitEntity);
        Assert.Equal("project_git_repositories", gitEntity!.GetTableName());
        Assert.Equal("studio", gitEntity.GetSchema());

        Assert.NotNull(shareEntity);
        Assert.Equal("schema_share_links", shareEntity!.GetTableName());
        Assert.Equal("studio", shareEntity.GetSchema());

        Assert.NotNull(pubEntity);
        Assert.Equal("project_publications", pubEntity!.GetTableName());
        Assert.Equal("hub", pubEntity.GetSchema());

        var hash = SchemaShareLink.ComputeTokenHash("secret-share-token-xyz");
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain("secret-share-token-xyz", hash, StringComparison.OrdinalIgnoreCase);

        var activeLink = new SchemaShareLink
        {
            BranchId = "b1",
            TokenHash = hash,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        Assert.True(activeLink.IsUsable(DateTime.UtcNow));

        activeLink.RevokedAt = DateTime.UtcNow;
        Assert.False(activeLink.IsUsable(DateTime.UtcNow));

        var expandSql = ThreeNfAndSchemaNormalizationSql.BuildExpandSchemasAndTablesSql();
        foreach (var schema in ThreeNfAndSchemaNormalizationSql.AllModuleSchemas)
        {
            Assert.Contains($"CREATE SCHEMA IF NOT EXISTS {schema};", expandSql);
        }
        Assert.Contains("studio.project_connections", expandSql);
        Assert.Contains("studio.project_git_repositories", expandSql);
        Assert.Contains("hub.project_publications", expandSql);
        Assert.Contains("studio.schema_share_links", expandSql);

        var backfillSql = ThreeNfAndSchemaNormalizationSql.BuildBackfillAndVerify3NfSql();
        Assert.Contains("sha256", backfillSql);
        Assert.Contains("DO $$", backfillSql);
        Assert.Contains("RAISE EXCEPTION '3NF Migration verification failed for studio.project_connections", backfillSql);
        Assert.Contains("RAISE EXCEPTION '3NF Migration verification failed for studio.project_git_repositories", backfillSql);
        Assert.Contains("RAISE EXCEPTION '3NF Migration verification failed for hub.project_publications", backfillSql);
        Assert.Contains("RAISE EXCEPTION '3NF Migration verification failed for studio.schema_share_links", backfillSql);
    }

    [Fact]
    public void Isolated_Module_Controllers_MustNot_Inject_AuthDbContext()
    {
        var isolatedControllers = new[]
        {
            typeof(VaultController),
            typeof(GroundController),
            typeof(AutomationController)
        };

        foreach (var controllerType in isolatedControllers)
        {
            foreach (var ctor in controllerType.GetConstructors())
            {
                var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
                Assert.DoesNotContain(typeof(AuthDbContext), paramTypes);
                Assert.Contains(typeof(ITenantProjectAuthority), paramTypes);
            }
        }
    }

    private static IEnumerable<Type> GetDirectlyReferencedTypes(Type type)
    {
        var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
            yield return field.FieldType;

        foreach (var prop in type.GetProperties(flags))
            yield return prop.PropertyType;

        foreach (var method in type.GetMethods(flags))
        {
            yield return method.ReturnType;
            foreach (var param in method.GetParameters())
                yield return param.ParameterType;
        }
    }

    private sealed class FakeMigrator : IModuleDbContextMigrator
    {
        private readonly List<string> _log;

        public FakeMigrator(string moduleName, int order, List<string> log)
        {
            ModuleName = moduleName;
            SchemaName = moduleName;
            Order = order;
            _log = log;
        }

        public string ModuleName { get; }
        public string SchemaName { get; }
        public int Order { get; }

        public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task MigrateAsync(CancellationToken ct = default)
        {
            _log.Add(ModuleName);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void AuthDbContext_MustNotExpose_DbSetsFor_ExtractedModules()
    {
        var authDbContextType = typeof(AuthDbContext);
        var declaredDbSetProperties = authDbContextType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var forbiddenDbSets = new[]
        {
            "AutomationRules",
            "AutomationActions",
            "AutomationRunLogs",
            "DeskHandoffTokens",
            "SqlQueryHistory",
            "SavedQueries",
            "VaultBackups",
            "VaultRestores",
            "VaultSchedules",
            "GroundDatabases"
        };

        foreach (var forbidden in forbiddenDbSets)
        {
            Assert.DoesNotContain(forbidden, declaredDbSetProperties);
        }

        // 37 - 10 = 27 DbSets declared directly on AuthDbContext for Phase 14.1
        Assert.Equal(27, declaredDbSetProperties.Count);
    }

    [Fact]
    public void Infrastructure_MustNotContain_RetiredLegacyServices()
    {
        var infraAssembly = typeof(AuthDbContext).Assembly;

        var retiredFullTypeNames = new[]
        {
            "Namines.Infrastructure.Services.VaultService",
            "Namines.Infrastructure.Services.VaultBackupWorker",
            "Namines.Infrastructure.Services.VaultScheduleBackgroundService",
            "Namines.Infrastructure.Services.VaultJobQueue",
            "Namines.Infrastructure.Services.GroundService",
            "Namines.Infrastructure.Services.GroundPurgeBackgroundService",
            "Namines.Infrastructure.Services.AutomationExecutor",
            "Namines.Infrastructure.Services.AutomationExecutorWorker",
            "Namines.Infrastructure.Services.AutomationRunLogRetentionService",
            "Namines.Infrastructure.Data.DeskHandoff"
        };

        foreach (var fullTypeName in retiredFullTypeNames)
        {
            var type = infraAssembly.GetType(fullTypeName, throwOnError: false);
            Assert.Null(type);
        }
    }

    private sealed class ItemA
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ItemB
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    private sealed class TestContextA : DbContext
    {
        public TestContextA(DbContextOptions<TestContextA> options) : base(options) { }
        public DbSet<ItemA> ItemsA => Set<ItemA>();
    }

    private sealed class TestContextB : DbContext
    {
        public TestContextB(DbContextOptions<TestContextB> options) : base(options) { }
        public DbSet<ItemB> ItemsB => Set<ItemB>();
    }
}
