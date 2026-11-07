using Namines.Billing.DependencyInjection;
using Namines.Governance.DependencyInjection;
using Namines.Identity.DependencyInjection;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Identity.Application;
using Namines.Identity.Infrastructure.Security;
using Namines.Gateway.DependencyInjection;
using Namines.Hub.DependencyInjection;
using Namines.Studio.DependencyInjection;
using Namines.Ops.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.BuildingBlocks.DependencyInjection;
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
using Namines.Tests.TestSupport;

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
        await context.EnsureModulesCreatedAsync();

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
        context.Studio().Projects.Add(project);
        await context.SaveChangesAsync();

        var authority = TestModules.For(context).Authority(protector);

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
    public void All_Eleven_Modules_Register_DbContexts_And_Services()
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
        services.AddOpsModule();
        services.AddBillingModule(registerWorkers: false);
        services.AddGovernanceModule();
        services.AddIdentityModule("Host=localhost;Database=model-only;Username=u;Password=p", _ => { });
        services.AddStudioModule();
        services.AddHubModule();
        services.AddGatewayModule();
        services.AddNaminesVault(registerWorkers: false);
        services.AddNaminesGround(registerWorkers: false);
        services.AddFlowModule(registerWorkers: false);
        services.AddDeskModule();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        // Tüm 11 modül cephesi (facade) ve izole DbContext'leri çözümlenebilmeli ve PostgreSQL şemaları eşleşmeli.
        var identityDb = scope.ServiceProvider.GetRequiredService<Namines.Identity.Infrastructure.Persistence.AuthDbContext>();
        var studioDb = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
        var billingDb = scope.ServiceProvider.GetRequiredService<Namines.Billing.Infrastructure.Persistence.BillingDbContext>();
        var governanceDb = scope.ServiceProvider.GetRequiredService<Namines.Governance.Infrastructure.Persistence.GovernanceDbContext>();
        var ground = scope.ServiceProvider.GetRequiredService<IGroundModule>();
        var vault = scope.ServiceProvider.GetRequiredService<IVaultModule>();
        var flow = scope.ServiceProvider.GetRequiredService<IFlowModule>();
        var desk = scope.ServiceProvider.GetRequiredService<IDeskModule>();
        var hubDb = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var gatewayDb = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var opsDb = scope.ServiceProvider.GetRequiredService<OpsDbContext>();

        Assert.Null(identityDb.Model.GetDefaultSchema()); // Identity tabloları public'te (tarihsel yer)
        Assert.Equal("studio", studioDb.Model.GetDefaultSchema());
        Assert.Equal("billing", billingDb.Model.GetDefaultSchema());
        Assert.Equal("governance", governanceDb.Model.GetDefaultSchema());
        Assert.Equal("hub", hubDb.Model.GetDefaultSchema());
        Assert.Equal("gateway", gatewayDb.Model.GetDefaultSchema());
        Assert.Equal("ops", opsDb.Model.GetDefaultSchema());

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

    // ── Faz 14.2B ────────────────────────────────────────────────────────────

    [Fact]
    public void CloudProject_YalnizcaCekirdekAlanlariTasir()
    {
        // Yansımayla izin listesi: taşınan bir alanın geri eklenmesi burada patlar.
        var allowed = new HashSet<string>
        {
            "Id", "Name", "DbType", "SchemaJson", "NodePositionsJson", "UserId",
            "OrganizationId", "RowVersion", "CreatedAt", "UpdatedAt", "AutoApproveSafeChanges",
        };
        var actual = typeof(CloudProject).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet();
        Assert.Equal(allowed, actual);
    }

    [Fact]
    public void TasinanTablolar_kendi_modul_baglaminda_ve_semasinda()
    {
        using var studio = new StudioDbContext(new DbContextOptionsBuilder<StudioDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        using var hub = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        Assert.Equal("studio", studio.Model.FindEntityType(typeof(CloudProject))!.GetSchema());
        Assert.Equal("projects", studio.Model.FindEntityType(typeof(CloudProject))!.GetTableName());
        Assert.Equal("studio", studio.Model.FindEntityType(typeof(ProjectConnection))!.GetSchema());
        Assert.Equal("studio", studio.Model.FindEntityType(typeof(ProjectShare))!.GetSchema());
        Assert.Equal("hub", hub.Model.FindEntityType(typeof(ProjectRepository))!.GetSchema());
        Assert.Equal("hub", hub.Model.FindEntityType(typeof(ProjectTag))!.GetSchema());
    }

    private static string BackendRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("Namines.sln").Any()) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Namines.sln bulunamadı.");
    }

    private static IEnumerable<(string Path, string Text)> ProductionSources()
    {
        foreach (var project in new[] { "Namines.API", "Namines.Infrastructure", "Namines.BuildingBlocks", "Namines.Core", "Namines.Studio", "Namines.Hub", "Namines.Gateway", "Namines.Ops", "Namines.Vault", "Namines.Ground", "Namines.Flow", "Namines.Desk", "Namines.Mcp", "Namines.Cli" })
        {
            var root = System.IO.Path.Combine(BackendRoot(), project);
            foreach (var file in System.IO.Directory.EnumerateFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
            {
                if (file.Contains(System.IO.Path.DirectorySeparatorChar + "obj" + System.IO.Path.DirectorySeparatorChar) ||
                    file.Contains(System.IO.Path.DirectorySeparatorChar + "bin" + System.IO.Path.DirectorySeparatorChar) ||
                    file.Contains(System.IO.Path.DirectorySeparatorChar + "Migrations" + System.IO.Path.DirectorySeparatorChar))
                    continue;
                yield return (file, System.IO.File.ReadAllText(file));
            }
        }
    }

    [Fact]
    public void BaglantiSirriTekKapidanCozulur_VeTasinanTablolaraModulDisindanDokunulmaz()
    {
        // Proje bağlantısını çözen yalnızca secret provider; rotasyon aracı bilinçli istisna (anahtar
        // değiştirmek için çözmek zorunda, sonucu kimseye vermez). Protector'ın kendisi ve kullanıcı
        // sırları (EncryptedUserStore) proje bağlantısı değil.
        var allowedUnprotect = new[]
        {
            "TenantProjectAuthorityService.cs", "ConnectionSecretRotation.cs",
            "AesGcmConnectionSecretProtector.cs", "IConnectionSecretProtector.cs", "EncryptedUserStore.cs",
        };
        var moduleRoots = new[] { "Namines.Studio", "Namines.Hub", "Namines.Gateway", "Namines.Ops" };
        var allowedModuleTables = new[] { "AuthDbContext.cs" };

        var violations = new List<string>();
        foreach (var (path, text) in ProductionSources())
        {
            var name = System.IO.Path.GetFileName(path);
            if (!allowedUnprotect.Contains(name) && text.Contains(".Unprotect("))
                violations.Add("Unprotect: " + name);

            var inModule = moduleRoots.Any(m => path.Contains(System.IO.Path.DirectorySeparatorChar + m + System.IO.Path.DirectorySeparatorChar));
            if (!inModule && !allowedModuleTables.Contains(name) &&
                (text.Contains(".ProjectConnections") || text.Contains(".ProjectShares") || text.Contains("Set<ProjectConnection>") ||
                 text.Contains("Set<ProjectShare>") || text.Contains("Set<ProjectRepository>") || text.Contains("Set<ProjectTag>") ||
                 text.Contains("Hub.Repositories") || text.Contains(".ProjectTags")))
                violations.Add("Tablo erişimi: " + name);

            // Eski tablo adı SQL metni olarak yaşamamalı.
            if (text.Contains("\"CloudProjects\"") || text.Contains("FROM CloudProjects"))
                violations.Add("Eski tablo adı: " + name);
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void TaslananServisler_ZorunluBagimlilik_LegacyDalYok()
    {
        // İsteğe bağlı parametre, "null ise eski yola düş" dalını canlı tutuyordu.
        foreach (var type in new[] { typeof(ShareController), typeof(GatewayController), typeof(AuthController), typeof(ProjectDeletionService), typeof(AccountDeletionService), typeof(TenantProjectAuthorityService) })
        {
            foreach (var ctor in type.GetConstructors())
            {
                foreach (var parameter in ctor.GetParameters())
                {
                    if (parameter.ParameterType == typeof(Namines.BuildingBlocks.Application.IStudioApplicationService) ||
                        parameter.ParameterType == typeof(IHubApplicationService) ||
                        parameter.ParameterType == typeof(IProjectConnectionSecretProvider) ||
                        parameter.ParameterType == typeof(StudioDbContext))
                    {
                        Assert.False(parameter.HasDefaultValue, $"{type.Name}.{parameter.Name} zorunlu olmalı");
                    }
                }
            }
        }
    }

    [Fact]
    public void Istek_govdeleri_sayac_sahip_ve_sir_alani_tasimaz()
    {
        // Mass assignment: istemci yıldız/fork sayacını, sahibi, fork kaynağını ya da şifreli metni
        // gövdeyle yazamaz; bu alanlar yalnızca sunucu tarafından değişir.
        var forbidden = new[] { "StarCount", "ForkCount", "OwnerUserId", "UserId", "ForkedFromProjectId", "EncryptedConnectionString", "ShareToken", "ShareTokenHash", "AllowDeskSql" };
        foreach (var type in new[] { typeof(UpdateRepoSettingsRequest), typeof(ForkRepoRequest), typeof(SyncProjectDto) })
        {
            var names = type.GetProperties().Select(p => p.Name).ToList();
            Assert.Empty(names.Intersect(forbidden));
        }
    }

    [Fact]
    public void Explore_ve_paylasim_olusturma_kendi_hiz_sinirinda()
    {
        Assert.Equal("explore-public", typeof(HubController).GetMethod(nameof(HubController.Explore))!
            .GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()!.PolicyName);
        // Fork/ayar/silmenin "sensitive" kovasını tüketmemeli.
        Assert.Equal("share-create", typeof(ShareController).GetMethod(nameof(ShareController.CreateShareLink))!
            .GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()!.PolicyName);
    }

    [Fact]
    public void Share_anonim_uclari_hiz_sinirina_bagli()
    {
        foreach (var action in new[] { nameof(ShareController.GetSharedSchema), nameof(ShareController.GetDbaBadge), nameof(ShareController.OgImage), nameof(ShareController.Meta) })
        {
            var attribute = typeof(ShareController).GetMethod(action)!
                .GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal("share-public", attribute!.PolicyName);
        }
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
        // 28: PendingSubscriptionCancellations, kullanıcı silmeyle aynı işlemde yazılmak zorunda (D-2'de Billing'e taşınır).
        Assert.Equal(14, declaredDbSetProperties.Count);
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
