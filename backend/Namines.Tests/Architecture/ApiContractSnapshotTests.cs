using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace Namines.Tests.Architecture;

/// <summary>
/// API sözleşmesi taşıma boyunca değişmemeli: rota, HTTP fiili, yetki, hız sınırı, parametre ve istek
/// gövdesi alanları. Taban dosya 14.2C koduyla üretildi (kod taşınmadan önce); fark çıkarsa bilinçli karar
/// olarak dosya güncellenir. Yanıt şekilleri için ayrı HTTP karşılaştırması raporda.
/// </summary>
public sealed class ApiContractSnapshotTests
{
    private static string BackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("Namines.sln").Any()) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string SnapshotPath => Path.Combine(BackendRoot(), "Namines.Tests", "Snapshots", "api-contract.txt");

    [Fact]
    public void Routes_and_request_shapes_match_the_pre_move_baseline()
    {
        var current = Generate();
        var regenerate = Environment.GetEnvironmentVariable("NAMINES_WRITE_API_SNAPSHOT");
        if (!string.IsNullOrEmpty(regenerate))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(regenerate)!);
            File.WriteAllText(regenerate, current);
            return;
        }

        var expected = File.ReadAllText(SnapshotPath).Replace("\r\n", "\n");
        if (expected == current) return;

        var expectedLines = expected.Split('\n');
        var currentLines = current.Split('\n');
        var removed = expectedLines.Except(currentLines).Take(20).ToList();
        var added = currentLines.Except(expectedLines).Take(20).ToList();
        Assert.Fail("API sözleşmesi değişti.\nKALKAN:\n" + string.Join("\n", removed) + "\nEKLENEN:\n" + string.Join("\n", added));
    }

    private static string Generate()
    {
        var assembly = typeof(Namines.API.Controllers.HubController).Assembly;
        var lines = new List<string>();
        var dtos = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var controller in assembly.GetTypes()
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var classRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            var classAuth = AuthOf(controller);
            var classLimit = controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName ?? "-";

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName)
                         .OrderBy(m => m.Name, StringComparer.Ordinal)
                         .ThenBy(m => m.ToString(), StringComparer.Ordinal))
            {
                var verbs = action.GetCustomAttributes().OfType<HttpMethodAttribute>().ToList();
                if (verbs.Count == 0) continue;

                foreach (var verb in verbs)
                {
                    var parameters = action.GetParameters()
                        .Where(p => p.ParameterType != typeof(CancellationToken))
                        .Select(p =>
                        {
                            CollectDto(p.ParameterType, dtos);
                            return $"{p.Name}:{TypeName(p.ParameterType)}:{SourceOf(p)}";
                        });
                    var methodAuth = AuthOf(action);
                    var limit = action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName ?? classLimit;
                    var size = action.GetCustomAttribute<RequestSizeLimitAttribute>() is not null ? " size-limited" : string.Empty;

                    lines.Add($"{string.Join("|", verb.HttpMethods)} {Combine(classRoute, verb.Template, controller)} " +
                              $"[{controller.Name}.{action.Name}] auth={methodAuth ?? classAuth} limit={limit}{size} ({string.Join(", ", parameters)})");
                }
            }
        }

        var sb = new StringBuilder();
        foreach (var line in lines.OrderBy(l => l, StringComparer.Ordinal)) sb.Append(line).Append('\n');
        sb.Append("--- request bodies ---\n");
        foreach (var (name, shape) in dtos) sb.Append(name).Append(' ').Append(shape).Append('\n');
        return sb.ToString();
    }

    private static string Combine(string classRoute, string? template, Type controller)
    {
        var name = controller.Name.EndsWith("Controller", StringComparison.Ordinal) ? controller.Name[..^"Controller".Length] : controller.Name;
        var root = classRoute.Replace("[controller]", name, StringComparison.Ordinal);
        if (string.IsNullOrEmpty(template)) return "/" + root;
        return template.StartsWith('/') ? template : $"/{root}/{template}";
    }

    private static string? AuthOf(MemberInfo member)
    {
        if (member.GetCustomAttribute<AllowAnonymousAttribute>() is not null) return "anonymous";
        return member.GetCustomAttribute<AuthorizeAttribute>() is not null ? "authorize" : null;
    }

    private static string SourceOf(ParameterInfo p)
    {
        if (p.GetCustomAttribute<FromBodyAttribute>() is not null) return "body";
        if (p.GetCustomAttribute<FromQueryAttribute>() is not null) return "query";
        if (p.GetCustomAttribute<FromHeaderAttribute>() is not null) return "header";
        if (p.GetCustomAttribute<FromFormAttribute>() is not null) return "form";
        return "route";
    }

    private static string TypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return TypeName(underlying) + "?";
        if (type.IsGenericType)
            return type.Name.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
        return type.Name;
    }

    private static void CollectDto(Type type, SortedDictionary<string, string> dtos)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsGenericType)
        {
            foreach (var argument in underlying.GetGenericArguments()) CollectDto(argument, dtos);
            return;
        }
        if (underlying.IsArray) { CollectDto(underlying.GetElementType()!, dtos); return; }
        if (underlying.IsEnum)
        {
            dtos[underlying.Name] = "enum{" + string.Join(",", Enum.GetNames(underlying).Select(n => n + "=" + Convert.ToInt64(Enum.Parse(underlying, n)))) + "}";
            return;
        }
        if (!underlying.IsClass || underlying == typeof(string)) return;
        if (underlying.Namespace?.StartsWith("Namines", StringComparison.Ordinal) != true) return;
        if (dtos.ContainsKey(underlying.Name)) return;

        dtos[underlying.Name] = string.Empty;
        var props = underlying.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.Name != "EqualityContract")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
        dtos[underlying.Name] = "{" + string.Join(", ", props.Select(p => $"{p.Name}:{TypeName(p.PropertyType)}")) + "}";
        foreach (var prop in props) CollectDto(prop.PropertyType, dtos);
    }
}
