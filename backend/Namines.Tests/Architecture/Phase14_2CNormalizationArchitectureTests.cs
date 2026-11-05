using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Hub.Domain;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Architecture;

/// <summary>
/// Faz 14.2C'nin geri gelmemesi gerekenleri: CSV özellikleri, CSV sorgulayan kod, ikinci etiket
/// sistemi, CORS ile yetkilendirmenin karışması.
/// </summary>
public sealed class Phase14_2CNormalizationArchitectureTests
{
    private static string BackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("Namines.sln").Any()) dir = dir.Parent;
        return dir!.FullName;
    }

    private static IEnumerable<(string Name, string Text)> ProductionSources()
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(BackendRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{sep}bin{sep}") && !p.Contains($"{sep}obj{sep}") && !p.Contains($"{sep}Migrations{sep}")
                        && !p.Contains($"Namines.Tests"))
            .Select(p => (Path.GetFileName(p), File.ReadAllText(p)));
    }

    [Theory]
    [InlineData(typeof(GatewayApiKey), "AllowedOrigins")]
    [InlineData(typeof(GatewayApiKey), "AllowedIps")]
    [InlineData(typeof(GatewayTablePermission), "MaskedColumns")]
    [InlineData(typeof(RepoIssue), "Labels")]
    [InlineData(typeof(CloudProject), "Tags")]
    public void Eski_metin_ozelligi_varlikta_yok(Type entity, string property) =>
        Assert.Null(entity.GetProperty(property, BindingFlags.Public | BindingFlags.Instance));

    [Theory]
    [InlineData("GatewayAccessService.cs")]
    [InlineData("GatewayKeyRestrictions.cs")]
    [InlineData("HubRepositoryService.cs")]
    [InlineData("GatewayController.cs")]
    [InlineData("HubController.cs")]
    public void Istek_yolunda_virgullu_metin_ayristirilmiyor(string file)
    {
        var text = ProductionSources().Single(s => s.Name == file).Text;
        Assert.DoesNotContain(".Split(','", text);
        Assert.DoesNotContain("SplitCsv(", text);
        Assert.DoesNotContain("Labels.Contains", text);
    }

    [Fact]
    public void Allowlist_ayristirici_yalnizca_yazma_ucunda_cagriliyor()
    {
        var callers = ProductionSources()
            .Where(s => s.Name != "GatewayAllowlists.cs" &&
                        (s.Text.Contains("GatewayAllowlistRules.ParseOrigins") || s.Text.Contains("GatewayAllowlistRules.ParseIps") ||
                         s.Text.Contains("GatewayAllowlistRules.ParseMaskedColumns")))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "GatewayKeyService.cs" }, callers);
    }

    [Fact]
    public void Etiket_sistemleri_ayri_ve_tek()
    {
        // Proje etiketi tek tabloda (14.2B); issue etiketi ona bağlanmıyor, ikinci bir proje etiketi de yok.
        Assert.DoesNotContain(typeof(RepoIssueLabel).GetProperties(), p => p.PropertyType == typeof(ProjectTag));
        var tagEntities = typeof(ProjectTag).Assembly.GetTypes()
            .Where(t => t.IsClass && t.GetProperty("ProjectId") != null && t.Name.Contains("Tag", StringComparison.Ordinal))
            .Select(t => t.Name)
            .ToList();
        Assert.Equal(new[] { "ProjectTag" }, tagEntities);
    }

    [Fact]
    public void Cors_ile_anahtar_yetkilendirmesi_karismiyor()
    {
        var sources = ProductionSources().ToList();
        var program = sources.Single(s => s.Name == "Program.cs" && s.Text.Contains("AddCors")).Text;
        Assert.DoesNotContain("GatewayApiKey", program);
        Assert.DoesNotContain("OriginRules", program);

        var restrictions = sources.Single(s => s.Name == "GatewayKeyRestrictions.cs").Text;
        Assert.DoesNotContain("Cors", restrictions);
    }

    [Fact]
    public void Her_eslenen_cocuk_tablo_dogru_semada()
    {
        using var gateway = new GatewayDbContext(new DbContextOptionsBuilder<GatewayDbContext>().UseNpgsql("Host=localhost;Database=model-only").Options);
        using var hub = new HubDbContext(new DbContextOptionsBuilder<HubDbContext>().UseNpgsql("Host=localhost;Database=model-only").Options);

        (Type, string, string)[] expected =
        {
            (typeof(GatewayApiKeyAllowedOrigin), "gateway", "api_key_allowed_origins"),
            (typeof(GatewayApiKeyAllowedIp), "gateway", "api_key_allowed_ips"),
            (typeof(GatewayTableMaskedColumn), "gateway", "table_masked_columns"),
            (typeof(IssueLabel), "hub", "issue_labels"),
            (typeof(RepoIssueLabel), "hub", "repo_issue_labels"),
        };

        foreach (var (type, schema, table) in expected)
        {
            var entity = (gateway.Model.FindEntityType(type) ?? hub.Model.FindEntityType(type))!;
            Assert.Equal(schema, entity.GetSchema());
            Assert.Equal(table, entity.GetTableName());
            // Silme zinciri: çocuk satır sahipsiz kalamaz.
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior));
        }
    }
}
