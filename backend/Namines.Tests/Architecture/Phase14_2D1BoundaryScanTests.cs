using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Namines.API.Controllers;
using Namines.Gateway.Application;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Architecture;

/// <summary>
/// Denetim m-1 ve M-1'in kaçırdığı sınır aşımları: SQL metninde başka modülün tablosu, başka modülün tablosunu
/// eşleyen varlık, kapıdan geçmeyen Gateway veri düzlemi ucu. Kaynak metni değil derlenmiş IL taranır:
/// interpolasyonlu, ham ya da birleştirilmiş SQL'in parçaları da <c>ldstr</c> olarak durur.
/// </summary>
public sealed class Phase14_2D1BoundaryScanTests
{
    private const string ModelOnly = "Host=localhost;Database=model-only;Username=u;Password=p";

    private static readonly (string Module, Assembly Assembly)[] Owners =
    {
        ("Studio", typeof(Namines.Studio.Infrastructure.Persistence.StudioDbContext).Assembly),
        ("Hub", typeof(Namines.Hub.Infrastructure.Persistence.HubDbContext).Assembly),
        ("Gateway", typeof(Namines.Gateway.Infrastructure.Persistence.GatewayDbContext).Assembly),
        ("Ops", typeof(Namines.Ops.Infrastructure.Persistence.OpsDbContext).Assembly),
        // D-2: gerçek modül oldular; kendi tablolarını başka modülün SQL metni anamaz (denetim m-2).
        ("Billing", typeof(Namines.Billing.Infrastructure.Persistence.BillingDbContext).Assembly),
        ("Governance", typeof(Namines.Governance.Infrastructure.Persistence.GovernanceDbContext).Assembly),
        ("Identity", typeof(Namines.Identity.Infrastructure.Persistence.AuthDbContext).Assembly),
        // Denetim R2-m6: bu dört modülün tabloları da korunur; başka modülün SQL metni onları anarsa test düşer.
        // Bağlamları internal: derleme adla, bağlam tanımlayıcıdan kurulur.
        ("Vault", Assembly.Load("Namines.Vault")),
        ("Ground", Assembly.Load("Namines.Ground")),
        ("Flow", Assembly.Load("Namines.Flow")),
        ("Desk", Assembly.Load("Namines.Desk")),
    };

    private static DbContext OwnerContext(string module) => module switch
    {
        "Studio" => new Namines.Studio.Infrastructure.Persistence.StudioDbContext(new DbContextOptionsBuilder<Namines.Studio.Infrastructure.Persistence.StudioDbContext>().UseNpgsql(ModelOnly).Options),
        "Hub" => new Namines.Hub.Infrastructure.Persistence.HubDbContext(new DbContextOptionsBuilder<Namines.Hub.Infrastructure.Persistence.HubDbContext>().UseNpgsql(ModelOnly).Options),
        "Gateway" => new Namines.Gateway.Infrastructure.Persistence.GatewayDbContext(new DbContextOptionsBuilder<Namines.Gateway.Infrastructure.Persistence.GatewayDbContext>().UseNpgsql(ModelOnly).Options),
        "Ops" => new Namines.Ops.Infrastructure.Persistence.OpsDbContext(new DbContextOptionsBuilder<Namines.Ops.Infrastructure.Persistence.OpsDbContext>().UseNpgsql(ModelOnly).Options),
        // Billing/Governance/Identity bağlamları internal; tanımlayıcıdan kurulur (Identity'nin migration adı "Auth").
        _ => Phase14_2D1ModuleArchitectureTests.AllDescriptors().Single(d => d.Module == (module == "Identity" ? "Auth" : module)).CreateContext(ModelOnly),
    };

    /// <summary>Modülün sahip olduğu (şema, tablo) çiftleri; kaynak modelin kendisi, elle liste değil.</summary>
    private static IReadOnlyList<(string Schema, string Table)> TablesOf(string module)
    {
        using var ctx = OwnerContext(module);
        return ctx.Model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null)
            .Select(e => (e.GetSchema() ?? "public", e.GetTableName()!))
            .Distinct()
            .ToList();
    }

    private static Regex TablePattern(string schema, string table)
    {
        var s = Regex.Escape(schema);
        var t = Regex.Escape(table);
        // Nitelikli ad (tırnaklı ya da değil); büyük harfli adlar tırnaksız yazılamaz, tek başına tırnaklı ad da
        // search_path'e yaslanan sorgudur.
        var quotedBare = table.Any(char.IsUpper) ? $"|\"{t}\"" : string.Empty;
        return new Regex($"(?<![A-Za-z0-9_]){s}\\.(\"{t}\"|{t}(?![A-Za-z0-9_])){quotedBare}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Bir derlemedeki tüm metot gövdelerinin dize sabitleri, migration ad alanları hariç.</summary>
    internal static IEnumerable<(string Where, string Text)> StringLiterals(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            if (type.Namespace?.Contains(".Migrations", StringComparison.Ordinal) == true) continue;
            IEnumerable<MethodBase> methods;
            try { methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)).ToList(); }
            catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException) { continue; }

            foreach (var method in methods)
            {
                byte[]? il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException) { continue; }
                if (il is null) continue;
                for (var i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x72 || il[i + 4] != 0x70) continue;
                    string? text;
                    try { text = method.Module.ResolveString(BitConverter.ToInt32(il, i + 1)); }
                    catch (ArgumentException) { continue; }
                    yield return ($"{type.FullName}.{method.Name}", text);
                }
            }
        }
    }

    public static IEnumerable<object[]> ScannedAssemblies() => new[]
    {
        "Namines.API", "Namines.Infrastructure", "Namines.BuildingBlocks", "Namines.Studio", "Namines.Hub", "Namines.Gateway",
        "Namines.Ops", "Namines.Vault", "Namines.Ground", "Namines.Flow", "Namines.Desk", "Namines.Billing", "Namines.Governance", "Namines.Identity",
        "Namines.AI",
    }.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(ScannedAssemblies))]
    public void Sql_text_never_names_another_modules_table(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var own = Owners.FirstOrDefault(o => o.Assembly == assembly).Module;
        var patterns = Owners.Where(o => o.Module != own)
            .SelectMany(o => TablesOf(o.Module).Select(t => (Owner: o.Module, Name: $"{t.Schema}.{t.Table}", Regex: TablePattern(t.Schema, t.Table))))
            .ToList();
        Assert.NotEmpty(patterns);

        var violations = StringLiterals(assembly)
            .SelectMany(s => patterns.Where(p => p.Regex.IsMatch(s.Text)).Select(p => $"{s.Where} -> {p.Name} ({p.Owner})"))
            .Distinct()
            .ToList();
        Assert.Empty(violations);
    }

    /// <summary>
    /// Bütün LLM/ses sağlayıcı trafiği AI modülünden geçer: kota uzlaştırması (gerçek token ölçümü) ve kimlik doğrulama
    /// yalnızca orada. VoiceController Groq'a doğrudan gidiyordu; bir sonraki doğrudan çağrı burada düşer.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScannedAssemblies))]
    public void Ai_provider_hosts_live_only_in_the_AI_module(string assemblyName)
    {
        if (assemblyName == "Namines.AI") return;
        var hosts = new[] { "api.groq.com", "api.deepseek.com", "generativelanguage.googleapis.com", "api.openai.com" };
        var violations = StringLiterals(Assembly.Load(assemblyName))
            .Where(s => hosts.Any(h => s.Text.Contains(h, StringComparison.OrdinalIgnoreCase)))
            .Select(s => $"{s.Where}: {s.Text}")
            .Distinct()
            .ToList();
        Assert.Empty(violations);
    }

    [Fact]
    public void A_table_owned_by_a_module_is_mapped_by_no_other_context()
    {
        // ExcludeFromMigrations ile başka modülün tablosunu eşlemek DDL'siz ama gerçek bir okuma/yazma kapısıdır.
        var owned = Owners.SelectMany(o => TablesOf(o.Module).Select(t => (t.Schema, t.Table, o.Module))).ToList();
        var violations = new List<string>();
        foreach (var descriptor in Phase14_2D1ModuleArchitectureTests.AllDescriptors())
        {
            using var ctx = descriptor.CreateContext(ModelOnly);
            foreach (var entity in ctx.Model.GetEntityTypes())
            {
                var table = entity.GetTableName();
                if (table is null) continue;
                var schema = entity.GetSchema() ?? "public";
                var owner = owned.FirstOrDefault(o => string.Equals(o.Schema, schema, StringComparison.Ordinal) && string.Equals(o.Table, table, StringComparison.Ordinal));
                if (owner.Module is not null && owner.Module != (descriptor.Module == "Auth" ? "Identity" : descriptor.Module))
                    violations.Add($"{descriptor.Module}: {entity.ClrType.Name} -> {schema}.{table} ({owner.Module})");
            }
        }
        Assert.Empty(violations);
    }

    /// <summary>Anahtarsız, yalnız oturumla çalışan uçlar: veri düzlemi kapısı onlara uygulanmaz.</summary>
    private static readonly HashSet<string> SessionOnlyGatewayActions = new(StringComparer.Ordinal)
    {
        "DeskSql", "DeskSqlHistory", "ClearDeskSqlHistory", "SavedQueries", "SaveQuery", "DeleteSavedQuery",
    };

    [Fact]
    public void Every_gateway_data_plane_action_goes_through_the_gate()
    {
        // Yalnız karar veren metotlar: maske ve izin listesi okumak yetki kararı değildir.
        var gateMethods = typeof(IGatewayRequestGate).GetMethods().Where(m => m.Name.StartsWith("Authorize", StringComparison.Ordinal) || m.Name == nameof(IGatewayRequestGate.ResolveMetadataKeyAsync)).ToHashSet();
        var controller = typeof(GatewayController);
        var actions = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();
        Assert.NotEmpty(actions);

        var missing = actions
            .Where(a => !SessionOnlyGatewayActions.Contains(a.Name))
            .Where(a => !Reaches(a, controller, gateMethods, new HashSet<MethodBase>()))
            .Select(a => a.Name)
            .ToList();
        Assert.Empty(missing);

        // Liste yalnızca küçülür: artık var olmayan ya da kapıya ulaşan uç listeden çıkmalı.
        var stale = SessionOnlyGatewayActions.Where(n => actions.All(a => a.Name != n)).ToList();
        Assert.Empty(stale);
    }

    [Fact]
    public void Gateway_controller_does_not_decide_access_itself()
    {
        // Karar parçaları modülde internal; access arayüzü de controller'a girmiyor.
        Assert.False(typeof(Namines.Gateway.Domain.GatewayApiKey).Assembly.GetType("Namines.Gateway.Domain.GatewayKeyRestrictions")!.IsPublic);
        var fields = typeof(GatewayController).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(f => f.FieldType).ToList();
        Assert.DoesNotContain(typeof(IGatewayAccess), fields);
        Assert.Contains(typeof(IGatewayRequestGate), fields);
    }

    public static IEnumerable<object[]> NonGatewayAssemblies() => new[]
    {
        "Namines.API", "Namines.Infrastructure", "Namines.BuildingBlocks", "Namines.Studio", "Namines.Hub",
        "Namines.Ops", "Namines.Vault", "Namines.Ground", "Namines.Flow", "Namines.Desk",
    }.Select(n => new object[] { n });

    /// <summary>
    /// Denetim R2-m2: kapı taraması yalnız GatewayController'a bakıyordu; başka bir uçta anahtarı kapısız doğrulamak
    /// geçiyordu. Parçalar Gateway dışında internal (derleyici engeller); bu tarama yeniden public yapılmalarına ve
    /// yansımayla çağrılmalarına karşı ikinci kat. Gateway modülü kendi içinde serbest, kapı orada.
    /// </summary>
    [Theory]
    [MemberData(nameof(NonGatewayAssemblies))]
    public void Only_the_gate_composes_gateway_access_checks(string assemblyName)
    {
        var gateway = typeof(IGatewayRequestGate).Assembly;
        var parts = new[]
        {
            gateway.GetType("Namines.Gateway.Application.IGatewayAccess")!,
            gateway.GetType("Namines.Gateway.Domain.GatewayKeyRestrictions")!,
        };
        foreach (var part in parts) Assert.False(part.IsPublic, part.Name + " must stay internal to the Gateway module.");
        var limiterAcquire = typeof(GatewayRateLimiter).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "TryAcquireAsync").ToHashSet<MethodBase>();
        Assert.All(limiterAcquire.Where(m => m.GetParameters().Length == 1), m => Assert.False(m.IsPublic));

        var assembly = Assembly.Load(assemblyName);
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x is not null).ToArray()!; }

        var violations = new List<string>();
        foreach (var type in types)
        {
            foreach (var method in AllBodies(type))
            {
                foreach (var called in CalledMethods(method))
                {
                    var declaring = called.DeclaringType;
                    if (declaring is null) continue;
                    if (parts.Contains(declaring) || limiterAcquire.Contains(called))
                        violations.Add($"{type.FullName}.{method.Name} -> {declaring.Name}.{called.Name}");
                }
            }
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void Gateway_controller_leaves_owner_checks_to_the_gate()
    {
        // Denetim R2-m1: Desk SQL'in Owner kuralı controller'da ikinci kopya olarak duruyordu.
        var getRole = typeof(Namines.BuildingBlocks.Security.ITenantProjectAuthority).GetMethod(nameof(Namines.BuildingBlocks.Security.ITenantProjectAuthority.GetRoleAsync))!;
        var calls = AllBodies(typeof(GatewayController)).SelectMany(CalledMethods).Where(m => m == getRole).ToList();
        Assert.Empty(calls);
    }

    private static IEnumerable<MethodBase> AllBodies(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        IEnumerable<MethodBase> own;
        try { own = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)).ToList(); }
        catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException) { yield break; }
        foreach (var m in own) yield return m;
        Type[] nested;
        try { nested = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic); }
        catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException) { yield break; }
        foreach (var n in nested)
            foreach (var m in AllBodies(n)) yield return m;
    }

    private static bool Reaches(MethodBase method, Type owner, HashSet<MethodInfo> targets, HashSet<MethodBase> seen)
    {
        if (!seen.Add(method)) return false;
        foreach (var body in Bodies(method))
        {
            foreach (var called in CalledMethods(body))
            {
                if (called is MethodInfo mi && targets.Contains(mi)) return true;
                var declaring = called.DeclaringType;
                while (declaring?.DeclaringType is not null) declaring = declaring.DeclaringType;
                if (declaring == owner && Reaches(called, owner, targets, seen)) return true;
            }
        }
        return false;
    }

    /// <summary>Async metodun gövdesi derleyicinin durum makinesinde; lambda kapanışları iç içe tiplerde.</summary>
    private static IEnumerable<MethodBase> Bodies(MethodBase method)
    {
        yield return method;
        if (method.GetCustomAttribute<AsyncStateMachineAttribute>() is { } sm)
        {
            var moveNext = sm.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (moveNext is not null) yield return moveNext;
        }
    }

    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        byte[]? il;
        try { il = method.GetMethodBody()?.GetILAsByteArray(); }
        catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException) { yield break; }
        if (il is null) yield break;
        for (var i = 0; i + 4 < il.Length; i++)
        {
            int at;
            if (il[i] is 0x28 or 0x6F or 0x73) at = i + 1;
            else if (il[i] == 0xFE && i + 5 < il.Length && il[i + 1] is 0x06 or 0x07) at = i + 2;
            else continue;
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
            if (target is null) continue;
            yield return target;
            // Lambda ve yerel fonksiyon gövdeleri: aynı controller'ın iç içe tipinde.
            if (target.DeclaringType?.IsNested == true && target.DeclaringType.GetCustomAttribute<CompilerGeneratedAttribute>() is not null)
                foreach (var nested in Bodies(target)) if (nested != target) yield return nested;
        }
    }
}
