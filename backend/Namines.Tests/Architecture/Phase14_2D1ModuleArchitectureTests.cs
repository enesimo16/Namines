using Namines.Billing.Domain;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Identity.Application;
using Namines.Identity.Infrastructure.Security;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Namines.BuildingBlocks.Persistence;
using Namines.Infrastructure.Data;
using Xunit;
using Namines.Identity.Domain;

namespace Namines.Tests.Architecture;

/// <summary>
/// Faz 14.2D-1 sınırları: bağlam yerine sözleşme. Kaynak metni değil, derlenmiş derlemeler ve csproj grafiği
/// taranır; yansıma ile bağlam alma ya da ana kapsamdan çözümleme de kaçamaz. Mutation kontrolü raporda.
/// </summary>
public sealed class Phase14_2D1ModuleArchitectureTests
{
    // Namines.AI: AI sağlayıcıları/ölçüm modülü; DbContext'i yok ama aynı derleme sınırına tabi.
    private static readonly string[] Modules = { "Namines.Studio", "Namines.Hub", "Namines.Gateway", "Namines.Ops", "Namines.Billing", "Namines.Governance", "Namines.Identity", "Namines.Vault", "Namines.Ground", "Namines.Flow", "Namines.Desk", "Namines.AI" };

    /// <summary>
    /// AuthDbContext enjekte etmesine izin verilen controller'lar ve dokundukları D-2 verisi (14.2D-1 denetim
    /// sonrası sayıldı). Studio/Hub/Gateway/Ops verisi ve proje yaşam döngüsü bu controller'larda yok
    /// (<see cref="Controllers_do_not_write_studio_projects_directly"/>). Her satır D-2'de kalkmak üzere burada.
    /// </summary>
    private static readonly HashSet<string> D2ControllersAllowedAuthDbContext = new(StringComparer.Ordinal);

    private static string BackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("Namines.sln").Any()) dir = dir.Parent;
        return dir!.FullName;
    }

    private static Type[] LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null).ToArray()!; }
    }

    private static IEnumerable<Type> Controllers() =>
        typeof(Namines.API.Controllers.HubController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    private static bool IsDbContext(Type t) => typeof(DbContext).IsAssignableFrom(t);

    /// <summary>Alan, kurucu parametresi ve özellik tipleri: "özel alanda sakladım" da yakalanır.</summary>
    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var found = new List<Type>();
        // Testcontainers Docker.DotNet'in başka sürümünü getirir: üretimde yüklenen bir üye burada TypeLoadException
        // atabilir. Bütün tipi atlamak boşluk olurdu; yalnızca o üye atlanır, gövdesi IL taramasından geçmeye devam eder.
        void Add(Func<IEnumerable<Type>> read)
        {
            try { found.AddRange(read()); }
            catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException) { }
        }
        foreach (var field in type.GetFields(all)) Add(() => new[] { field.FieldType });
        foreach (var property in type.GetProperties(all)) Add(() => new[] { property.PropertyType });
        foreach (var ctor in type.GetConstructors(all)) Add(() => ctor.GetParameters().Select(p => p.ParameterType).ToArray());
        foreach (var method in type.GetMethods(all | BindingFlags.DeclaredOnly))
        {
            Add(() => new[] { method.ReturnType });
            Add(() => method.GetParameters().Select(p => p.ParameterType).ToArray());
        }
        return found;
    }

    /// <summary>
    /// IL içindeki tip başvuruları: <c>GetService(typeof(AuthDbContext))</c> gibi yansıma yolları alan
    /// tiplerinde görünmez, ama metot gövdesinde token olarak durur.
    /// </summary>
    internal static IEnumerable<Type> TypesUsedInIl(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
        // Derleyicinin ürettiği async durum makineleri ve lambda kapanışları iç içe tiplerdedir.
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            foreach (var used in TypesUsedInIl(nested))
                yield return used;

        foreach (var method in methods)
        {
            byte[]? il;
            try { il = method.GetMethodBody()?.GetILAsByteArray(); }
            catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException) { continue; }
            if (il is null) continue;

            for (var i = 0; i + 4 < il.Length; i++)
            {
                // 0xD0 ldtoken, 0x28 call, 0x6F callvirt, 0x73 newobj, 0x7B/0x7D ldfld/stfld + 4 baytlık token.
                if (il[i] is not (0xD0 or 0x28 or 0x6F or 0x73 or 0x7B or 0x7D)) continue;
                var token = BitConverter.ToInt32(il, i + 1);
                MemberInfo? member;
                try
                {
                    member = method.Module.ResolveMember(token,
                        type.IsGenericType ? type.GetGenericArguments() : null,
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                }
                catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or TypeLoadException or MissingMemberException or FileNotFoundException)
                {
                    continue; // hizası kaymış bayt ya da test ortamında yüklenemeyen derleme
                }

                if (member is Type t) yield return t;
                else if (member?.DeclaringType is { } declaring) yield return declaring;
                if (member is MethodInfo { IsGenericMethod: true } generic)
                    foreach (var argument in generic.GetGenericArguments()) yield return argument;
            }
        }
    }

    // ── Controller'lar ──────────────────────────────────────────────────────

    [Fact]
    public void Controllers_do_not_use_any_DbContext()
    {
        var violations = new List<string>();
        foreach (var controller in Controllers())
        {
            var used = ReferencedTypes(controller).Concat(TypesUsedInIl(controller)).Where(IsDbContext).Distinct().ToList();
            foreach (var context in used)
            {
                violations.Add($"{controller.Name} -> {context.Name}");
            }
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void The_controller_allowlist_is_empty_after_14_2D_2()
    {
        Assert.Empty(D2ControllersAllowedAuthDbContext);
    }

    [Theory]
    [InlineData("BranchController")]
    [InlineData("HubController")]
    [InlineData("CrossDatabaseController")]
    [InlineData("ShareController")]
    [InlineData("GatewayController")]
    [InlineData("GatewayKeyController")]
    [InlineData("FeedbackController")]
    [InlineData("FeaturesController")]
    [InlineData("DatabaseExecutorController")]
    [InlineData("LaunchController")]
    [InlineData("AdminController")]
    [InlineData("AuthController")]
    [InlineData("ChangeRequestController")]
    [InlineData("TeamController")]
    [InlineData("ProjectMemberController")]
    [InlineData("StripeWebhookController")]
    [InlineData("QuotaController")]
    [InlineData("UsageController")]
    [InlineData("PolicyController")]
    [InlineData("CoderAIController")]
    [InlineData("DocumentationController")]
    [InlineData("SmartSeedController")]
    public void Module_controllers_are_not_on_the_allowlist(string controller)
    {
        Assert.DoesNotContain(controller, D2ControllersAllowedAuthDbContext);
        Assert.Contains(Controllers(), c => c.Name == controller);
    }

    // ── AuthDbContext ───────────────────────────────────────────────────────

    [Fact]
    public void AuthDbContext_model_contains_only_identity_entity_types()
    {
        using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql("Host=localhost;Database=model-only").Options);
        var allowed = new HashSet<string>
        {
            "ApplicationUser", "Organization", "OrganizationMember", "TeamInvite",
            "IdentityRole", "IdentityRoleClaim<string>", "IdentityUserClaim<string>", "IdentityUserLogin<string>",
            "IdentityUserRole<string>", "IdentityUserToken<string>",
        };
        var foreign = db.Model.GetEntityTypes().Select(e => e.ClrType.Name).Where(n => !allowed.Contains(n) && !n.StartsWith("Identity", StringComparison.Ordinal)).ToList();
        Assert.Empty(foreign);

        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToHashSet();
        foreach (var moved in new[] { "projects", "Branches", "SchemaVersions", "CrossDatabaseRelations", "repositories", "RepoIssues", "RepoStars", "GatewayApiKeys", "GatewayAuditEntries", "Feedbacks", "SqlExecutionAudits", "FeatureFlags", "AiToolRoutes",
                                      "UsageEvents", "UserAIQuotas", "UserBillingSettings", "GlobalAiUsages", "OrgAiUsages", "StripeWebhookEvents", "PendingSubscriptionCancellations",
                                      "ChangeRequests", "ChangeRequestApprovals", "ChangeRequestAuditLogs", "UserAIPolicies",
                                      "vault_backups", "ground_databases", "automation_rules", "sql_query_history" })
            Assert.DoesNotContain(moved, tables);

        // Stripe alanları kullanıcı satırında değil.
        var userColumns = db.Model.FindEntityType(typeof(Namines.Core.Models.Auth.ApplicationUser))!.GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(userColumns, c => c is "StripeCustomerId" or "StripeSubscriptionId" or "SubscriptionStatus" or "PlanCode" or "CurrentPeriodEnd");
    }

    [Fact]
    public void Only_the_identity_assembly_references_AuthDbContext()
    {
        var identity = typeof(AuthDbContext).Assembly;
        var offenders = new List<string>();
        var production = new[]
        {
            typeof(Namines.API.Controllers.HubController).Assembly, typeof(Namines.Infrastructure.Data.CrossModuleTransaction).Assembly,
            typeof(ModuleMigrationOrchestrator).Assembly, typeof(Namines.Core.Models.Auth.ApplicationUser).Assembly,
        }.Concat(ModuleAssemblies().Where(a => a != identity));
        foreach (var assembly in production)
            foreach (var type in LoadableTypes(assembly))
                if (ReferencedTypes(type).Concat(TypesUsedInIl(type)).Any(t => t == typeof(AuthDbContext) || t.IsAssignableTo(typeof(AuthDbContext))))
                    offenders.Add(type.FullName!);
        Assert.Empty(offenders);
    }

    // ── Bağımlılık grafiği ──────────────────────────────────────────────────

    private static IEnumerable<string> ProjectReferences(string project)
    {
        var path = Path.Combine(BackendRoot(), project, project + ".csproj");
        return XDocument.Load(path).Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')));
    }

    [Theory]
    [InlineData("Namines.Studio")]
    [InlineData("Namines.Hub")]
    [InlineData("Namines.Gateway")]
    [InlineData("Namines.Ops")]
    [InlineData("Namines.Billing")]
    [InlineData("Namines.Governance")]
    [InlineData("Namines.Identity")]
    [InlineData("Namines.AI")]
    public void Module_project_references_only_Core_and_BuildingBlocks(string project) =>
        Assert.Equal(new[] { "Namines.BuildingBlocks", "Namines.Core" }, ProjectReferences(project).OrderBy(x => x, StringComparer.Ordinal));

    private static IEnumerable<Assembly> ModuleAssemblies() => Modules.Select(Assembly.Load);

    [Fact]
    public void Compiled_module_assemblies_do_not_reference_infrastructure_api_or_each_other()
    {
        var forbiddenCommon = new[] { "Namines.Infrastructure", "Namines.API", "Namines.Vault", "Namines.Ground", "Namines.Flow", "Namines.Desk" };
        foreach (var assembly in ModuleAssemblies())
        {
            var references = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToHashSet();
            foreach (var forbidden in forbiddenCommon.Concat(Modules).Where(m => m != assembly.GetName().Name))
                Assert.DoesNotContain(forbidden, references);
        }
    }

    [Fact]
    public void BuildingBlocks_does_not_know_any_module()
    {
        var references = typeof(ModuleMigrationOrchestrator).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.Empty(references.Intersect(Modules.Concat(new[] { "Namines.Infrastructure", "Namines.API" })));
    }

    [Theory]
    [InlineData(typeof(StudioDbContext))]
    [InlineData(typeof(HubDbContext))]
    [InlineData(typeof(GatewayDbContext))]
    [InlineData(typeof(OpsDbContext))]
    [InlineData(typeof(Namines.Billing.Infrastructure.Persistence.BillingDbContext))]
    [InlineData(typeof(Namines.Governance.Infrastructure.Persistence.GovernanceDbContext))]
    [InlineData(typeof(AuthDbContext))]
    [InlineData(typeof(Namines.Vault.Infrastructure.Persistence.VaultDbContext))]
    [InlineData(typeof(Namines.Ground.Infrastructure.Persistence.GroundDbContext))]
    [InlineData(typeof(Namines.Flow.Infrastructure.Persistence.FlowDbContext))]
    [InlineData(typeof(Namines.Desk.Infrastructure.Persistence.DeskDbContext))]
    public void Module_DbContext_is_internal_so_the_compiler_blocks_outside_use(Type context)
    {
        Assert.False(context.IsPublic);
        Assert.Equal(context.Namespace, context.Assembly.GetName().Name + ".Infrastructure.Persistence");
    }

    [Fact]
    public void Infrastructure_and_API_cannot_see_module_contexts_at_compile_time()
    {
        // InternalsVisibleTo yalnızca test projelerine: üretim derlemesi eklenirse sınır delinir.
        foreach (var assembly in ModuleAssemblies())
        {
            var friends = assembly.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToList();
            Assert.All(friends, f => Assert.StartsWith("Namines.Tests", f));
        }
    }

    [Fact]
    public void Application_layer_has_no_dependency_on_infrastructure_or_a_concrete_context()
    {
        // Yalnızca public yüzey değil: alan, kurucu ve metot gövdesi (IL) de taranır, internal servis de kapsanır.
        var violations = new List<string>();
        foreach (var assembly in ModuleAssemblies())
        {
            foreach (var type in LoadableTypes(assembly).Where(t => t.Namespace?.EndsWith(".Application", StringComparison.Ordinal) == true))
            {
                // İmza düzeyi (alan, kurucu, metot imzası) HİÇ muaf değil: tasarım-zamanı fabrika da burada yasak.
                foreach (var used in ReferencedTypes(type).Distinct())
                    if (IsDbContext(used) || used.Namespace?.StartsWith("Namines.", StringComparison.Ordinal) == true && used.Namespace.Contains(".Infrastructure", StringComparison.Ordinal))
                        violations.Add($"{type.FullName} -> {used.FullName}");
                // IL düzeyi: yalnız tasarım-zamanı fabrikaları muaf. Fabrikalar kodda hiç çağrılmaz (yalnız dotnet-ef bulur);
                // IL tarayıcısı hizası kaymış baytları tesadufen onlara çözebiliyor. Muafiyet bu tek türle sınırlı ve
                // imza düzeyindeki yasak yukarıda aynen duruyor.
                foreach (var used in TypesUsedInIl(type).Distinct())
                {
                    if (used.GetInterfaces().Any(i => i.Name.StartsWith("IDesignTimeDbContextFactory", StringComparison.Ordinal))) continue;
                    if (IsDbContext(used) || used.Namespace?.StartsWith("Namines.", StringComparison.Ordinal) == true && used.Namespace.Contains(".Infrastructure", StringComparison.Ordinal))
                        violations.Add($"{type.FullName} -> {used.FullName}");
                }
            }
        }
        Assert.True(violations.Count == 0, string.Join(", ", violations));
    }

    [Fact]
    public void Domain_namespaces_do_not_depend_on_persistence_or_ef()
    {
        foreach (var assembly in ModuleAssemblies())
        {
            foreach (var type in LoadableTypes(assembly).Where(t => t.Namespace?.EndsWith(".Domain", StringComparison.Ordinal) == true))
            {
                var referenced = ReferencedTypes(type).Concat(TypesUsedInIl(type)).ToList();
                Assert.DoesNotContain(referenced, IsDbContext);
                Assert.DoesNotContain(referenced, t => t.Namespace?.Contains(".Infrastructure", StringComparison.Ordinal) == true);
                Assert.DoesNotContain(referenced, t => t.Assembly.GetName().Name == "Microsoft.EntityFrameworkCore");
                // Öznitelikler tür başvurusu olarak görünmez (ör. [Microsoft.EntityFrameworkCore.Index]); ayrıca taranır.
                Assert.Empty(PersistenceAttributesOn(type));
            }
        }
    }

    /// <summary>Türün, üyelerinin ve parametrelerinin EF Core / Npgsql / Identity depolama özniteliği (DataAnnotations serbest).</summary>
    private static IEnumerable<string> PersistenceAttributesOn(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static bool Persistence(CustomAttributeData a) =>
            a.AttributeType.Assembly.GetName().Name is { } n && (n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || n.StartsWith("Npgsql", StringComparison.Ordinal));

        var carriers = new List<(string Name, IList<CustomAttributeData> Data)> { (type.FullName!, type.GetCustomAttributesData()) };
        carriers.AddRange(type.GetProperties(all).Select(p => ($"{type.FullName}.{p.Name}", p.GetCustomAttributesData())));
        carriers.AddRange(type.GetFields(all).Select(f => ($"{type.FullName}.{f.Name}", f.GetCustomAttributesData())));
        carriers.AddRange(type.GetMethods(all).Select(m => ($"{type.FullName}.{m.Name}", m.GetCustomAttributesData())));
        foreach (var (name, data) in carriers)
            foreach (var attribute in data.Where(Persistence))
                yield return $"{name} [{attribute.AttributeType.FullName}]";
    }

    [Fact]
    public void Modules_keep_their_layers()
    {
        foreach (var assembly in ModuleAssemblies())
        {
            var name = assembly.GetName().Name!;
            var namespaces = LoadableTypes(assembly).Select(t => t.Namespace).Distinct().ToHashSet();
            // 14.2D-2 kabul edilen borç 1 kapandı: Vault/Ground/Desk varlıkları ve TeamInvite kendi modüllerinin Domain'inde.
            // ApplicationUser/Organization paylaşılan çekirdekte kalır (ASP.NET Identity ve Studio/Vault/Ground/Desk gezinti
            // özellikleri; modüller Identity'ye başvuramaz). Tek istisna Flow: AutomationRule'u Core'daki saf kural
            // değerlendiricisi (AutomationRuleMatcher) ve modülsüz çalışan MCP aracı da kullanıyor; gerçekten paylaşılan kavram.
            if (name is not "Namines.Flow") Assert.Contains(name + ".Domain", namespaces);
            Assert.Contains(name + ".Application", namespaces);
            // AI modülü durumsuz: kendi tablosu yok (kota defteri Billing'de, politika Governance'ta), dış adaptörleri
            // (LLM sağlayıcıları) Infrastructure.Providers'ta. Kalıcılık katmanı uydurmak yerine adaptör katmanı aranır.
            Assert.Contains(name + (name == "Namines.AI" ? ".Infrastructure.Providers" : ".Infrastructure.Persistence"), namespaces);
            Assert.Contains(name + ".DependencyInjection", namespaces);
        }
    }

    [Fact]
    public void Facades_are_gone_from_BuildingBlocks()
    {
        var names = LoadableTypes(typeof(ModuleMigrationOrchestrator).Assembly).Select(t => t.Name).ToHashSet();
        foreach (var gone in new[] { "StudioDbContext", "HubDbContext", "GatewayDbContext", "OpsDbContext", "IStudioModule", "IHubModule", "IGatewayModule", "IOpsModule", "StudioApplicationService", "HubApplicationService" })
            Assert.DoesNotContain(gone, names);
    }

    // ── Sadece sözleşmeden: Hub, Gateway, Ops kuralları ─────────────────────

    [Fact]
    public void Gateway_never_touches_the_secret_protector()
    {
        var assembly = typeof(Namines.Gateway.Domain.GatewayApiKey).Assembly;
        foreach (var type in LoadableTypes(assembly))
        {
            var used = ReferencedTypes(type).Concat(TypesUsedInIl(type));
            Assert.DoesNotContain(used, t => t == typeof(Namines.Core.Security.IConnectionSecretProtector));
        }
    }

    [Fact]
    public void Secrets_are_decrypted_only_in_Studio_outside_the_rotation_tool_and_protector()
    {
        var unprotect = typeof(Namines.Core.Security.IConnectionSecretProtector).GetMethods();
        var allowed = new HashSet<string> { "TenantProjectAuthorityService", "ConnectionSecretReencryptor", "Program" }; // Program: yalnızca --reencrypt-secrets aracını kurar, sır çözmez.
        var users = new List<string>();
        foreach (var assembly in ModuleAssemblies().Append(typeof(AuthDbContext).Assembly).Append(typeof(Namines.API.Controllers.HubController).Assembly))
        {
            foreach (var type in LoadableTypes(assembly))
            {
                var usesProtector = ReferencedTypes(type).Concat(TypesUsedInIl(type)).Any(t => t == typeof(Namines.Core.Security.IConnectionSecretProtector));
                if (!usesProtector) continue;
                var root = type;
                while (root.DeclaringType is not null) root = root.DeclaringType;
                if (allowed.Contains(root.Name)) continue;
                users.Add(root.FullName!);
            }
        }
        // Infrastructure'daki kullanıcı sırrı deposu proje bağlantısı değil; bilinçli istisna.
        var offenders = users.Where(u => !u.EndsWith("EncryptedUserStore", StringComparison.Ordinal) && !u.EndsWith("ServiceCollectionExtensions", StringComparison.Ordinal)).ToList();
        Assert.Empty(offenders);
    }

    // ── Migration sahipliği ─────────────────────────────────────────────────

    internal static IReadOnlyList<ModuleMigrationDescriptor> AllDescriptors()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Namines.BuildingBlocks.DependencyInjection.BuildingBlocksServiceCollectionExtensions.AddNaminesBuildingBlocks(services);
        Namines.Studio.DependencyInjection.StudioServiceCollectionExtensions.AddStudioModule(services);
        Namines.Hub.DependencyInjection.HubServiceCollectionExtensions.AddHubModule(services);
        Namines.Gateway.DependencyInjection.GatewayServiceCollectionExtensions.AddGatewayModule(services);
        Namines.Ops.DependencyInjection.OpsServiceCollectionExtensions.AddOpsModule(services);
        Namines.Billing.DependencyInjection.BillingServiceCollectionExtensions.AddBillingModule(services, registerWorkers: false);
        Namines.Governance.DependencyInjection.GovernanceServiceCollectionExtensions.AddGovernanceModule(services);
        Namines.Vault.DependencyInjection.VaultServiceCollectionExtensions.AddNaminesVault(services, registerWorkers: false);
        Namines.Ground.DependencyInjection.GroundServiceCollectionExtensions.AddNaminesGround(services, registerWorkers: false);
        Namines.Flow.DependencyInjection.FlowServiceCollectionExtensions.AddFlowModule(services, registerWorkers: false);
        Namines.Desk.DependencyInjection.DeskServiceCollectionExtensions.AddDeskModule(services);
        return services.Where(d => d.ServiceType == typeof(ModuleMigrationDescriptor))
            .Select(d => (ModuleMigrationDescriptor)d.ImplementationInstance!)
            .Append(Namines.Identity.DependencyInjection.IdentityServiceCollectionExtensions.MigrationDescriptor)
            .ToList();
    }

    private const string ModelOnly = "Host=localhost;Database=model-only;Username=u;Password=p";

    [Fact]
    public void Every_DbContext_in_the_solution_has_a_declared_migration_status()
    {
        var declared = AllDescriptors().Select(d => { using var ctx = d.CreateContext(ModelOnly); return ctx.GetType(); }).ToHashSet();

        var assemblies = new[]
        {
            typeof(AuthDbContext).Assembly, typeof(ModuleMigrationOrchestrator).Assembly,
            typeof(StudioDbContext).Assembly, typeof(HubDbContext).Assembly, typeof(GatewayDbContext).Assembly, typeof(OpsDbContext).Assembly,
            typeof(Namines.Vault.Infrastructure.Persistence.VaultDbContext).Assembly, typeof(Namines.Ground.Infrastructure.Persistence.GroundDbContext).Assembly,
            typeof(Namines.Flow.Infrastructure.Persistence.FlowDbContext).Assembly, typeof(Namines.Desk.Infrastructure.Persistence.DeskDbContext).Assembly,
        };
        var all = assemblies.SelectMany(LoadableTypes).Where(t => IsDbContext(t) && !t.IsAbstract).ToList();
        Assert.Empty(all.Where(t => !declared.Contains(t)).Select(t => t.Name));
    }

    [Fact]
    public void Chain_order_is_auth_first_then_modules_and_owners_have_their_own_history_schema()
    {
        var chain = ModuleMigrationOrchestrator.Chain(AllDescriptors());
        Assert.Equal(new[] { "Auth", "Studio", "Billing", "Governance", "Hub", "Gateway", "Ops", "Vault", "Ground", "Flow", "Desk" }, chain.Select(d => d.Module));

        foreach (var module in chain.Where(d => d.Module != "Auth"))
        {
            using var ctx = module.CreateContext(ModelOnly);
            var script = ctx.GetService<Microsoft.EntityFrameworkCore.Migrations.IHistoryRepository>().GetCreateIfNotExistsScript();
            Assert.Contains($"{module.Schema}.\"__EFMigrationsHistory\"", script);
        }
    }

    [Fact]
    public void Every_context_has_no_pending_model_changes()
    {
        var dirty = new List<string>();
        foreach (var module in AllDescriptors())
        {
            using var ctx = module.CreateContext(ModelOnly);
            if (ctx.Database.HasPendingModelChanges()) dirty.Add(module.Module);
        }
        Assert.Empty(dirty);
    }

    [Fact]
    public void No_context_is_left_without_a_migration_chain()
    {
        // 14.2D-2 sonu: AuthDbContext zincirinde DDL'i kalan, sahibi olmayan bağlam yok.
        Assert.DoesNotContain(AllDescriptors(), d => d.Ownership == ModuleMigrationOwnership.DdlInAuthChain);
    }

    [Fact]
    public void Owner_contexts_have_migrations_in_their_own_assembly()
    {
        foreach (var module in ModuleMigrationOrchestrator.Chain(AllDescriptors()))
        {
            using var ctx = module.CreateContext(ModelOnly);
            var migrations = ctx.GetService<IMigrationsAssembly>();
            Assert.NotEmpty(migrations.Migrations);
            Assert.Equal(ctx.GetType().Assembly, migrations.Assembly);
        }
    }

    [Fact]
    public void Controllers_do_not_write_studio_projects_directly()
    {
        // Denetim M-2: proje yazımı ve org taşıması aktörlü yaşam döngüsü servisinden geçer; yetkisiz sözleşme
        // metotları yalnızca yetkiyi kendisi yapmış modül içi akışlar (hesap/proje silme, Hub) içindir.
        var writes = typeof(IStudioProjects).GetMethods()
            .Where(m => m.Name is not ("GetAsync" or "GetHeaderAsync" or "GetManyAsync" or "GetHeadersAsync" or "ExistsAsync")
                        && !m.Name.StartsWith("List", StringComparison.Ordinal))
            .ToHashSet();
        var violations = new List<string>();
        foreach (var controller in Controllers())
        {
            foreach (var method in AllMethods(controller))
            {
                foreach (var called in CalledMethods(method))
                    if (writes.Contains(called)) violations.Add($"{controller.Name}.{method.Name} -> {called.Name}");
            }
        }
        Assert.Empty(violations);
    }

    private static IEnumerable<MethodBase> AllMethods(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var m in type.GetMethods(all)) yield return m;
        foreach (var c in type.GetConstructors(all)) yield return c;
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            foreach (var m in AllMethods(nested)) yield return m;
    }

    /// <summary>Gövdedeki call/callvirt/ldftn hedefleri.</summary>
    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        byte[]? il;
        try { il = method.GetMethodBody()?.GetILAsByteArray(); }
        catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException) { yield break; }
        if (il is null) yield break;
        for (var i = 0; i + 4 < il.Length; i++)
        {
            var isCall = il[i] is 0x28 or 0x6F or 0x73 || (il[i] == 0xFE && i + 5 < il.Length && il[i + 1] is 0x06 or 0x07);
            if (!isCall) continue;
            var at = il[i] == 0xFE ? i + 2 : i + 1;
            MethodBase? target;
            try
            {
                target = method.Module.ResolveMethod(BitConverter.ToInt32(il, at),
                    method.DeclaringType is { IsGenericType: true } d ? d.GetGenericArguments() : null,
                    method.IsGenericMethod ? method.GetGenericArguments() : null);
            }
            catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or TypeLoadException or MissingMemberException or FileNotFoundException)
            {
                continue;
            }
            if (target is not null) yield return target;
        }
    }

    [Fact]
    public void Project_read_model_writes_have_a_single_door()
    {
        // BL-5: ad/motor kopyası Hub'da. Yazım yolu Studio'nun sözleşmesi; bağlam internal olduğu için dışarıdan
        // SetProperty yazılamaz. Modül içinde de Name/DbType yazan tek metot SaveAsync; ham SQL UPDATE yok.
        var sources = Directory.EnumerateFiles(Path.Combine(BackendRoot(), "Namines.Studio"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !p.Contains("Migrations"));
        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("SetProperty(p => p.Name", text);
            Assert.DoesNotContain("SetProperty(p => p.DbType", text);
            Assert.DoesNotContain("UPDATE studio.projects", text, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var project in new[] { "Namines.API", "Namines.Infrastructure", "Namines.Hub", "Namines.Gateway", "Namines.Ops", "Namines.Vault", "Namines.Ground", "Namines.Flow", "Namines.Desk", "Namines.Mcp" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(BackendRoot(), project), "*.cs", SearchOption.AllDirectories)
                         .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !p.Contains("Migrations")))
                Assert.DoesNotContain("studio.projects", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
        }

        // ProjectWrite üreten her üretim sınıfı okuma modelini de yazıyor.
        var writers = new Dictionary<string, string[]>
        {
            ["ProjectLifecycleService.cs"] = new[] { "_readModel.WriteAsync" },
            ["HubRepositoryService.cs"] = new[] { "ApplySettingsAsync", "CreateRepositoryAsync" },
            ["DevSandboxSeeder.cs"] = new[] { "EnsureRepositoryAsync" },
            ["HubBlueprintSeeder.cs"] = new[] { "CreateRepositoryAsync" },
        };
        var actual = new HashSet<string>();
        foreach (var project in new[] { "Namines.API", "Namines.Infrastructure", "Namines.Hub", "Namines.Gateway", "Namines.Ops", "Namines.Studio", "Namines.Vault", "Namines.Ground", "Namines.Flow", "Namines.Desk" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(BackendRoot(), project), "*.cs", SearchOption.AllDirectories)
                         .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
            {
                if (File.ReadAllText(file).Contains("new ProjectWrite(", StringComparison.Ordinal)) actual.Add(Path.GetFileName(file));
            }
        }
        Assert.Equal(writers.Keys.OrderBy(x => x, StringComparer.Ordinal), actual.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var (file, required) in writers)
        {
            var path = Directory.EnumerateFiles(BackendRoot(), file, SearchOption.AllDirectories).First(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
            var text = File.ReadAllText(path);
            foreach (var call in required) Assert.Contains(call, text);
        }
    }
}
