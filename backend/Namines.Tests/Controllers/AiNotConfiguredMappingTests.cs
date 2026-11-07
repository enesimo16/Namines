using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.API.Middleware;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// Denetim R2-m3: controller'ın kendi catch'i AI yapılandırılmamış hatasını mesajıyla 500'e çeviriyordu; merkezi
/// ara katman aynı istisnayı 503 + AI_NOT_CONFIGURED'a eşliyor. İstisna artık ara katmana ulaşıyor, diğer hatalar
/// eskisi gibi uçta kalıyor.
/// </summary>
public sealed class AiNotConfiguredMappingTests
{
    private sealed class Env : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Namines.API";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Production";
    }

    private sealed class ThrowingMigrations(Exception error) : IMigrationService
    {
        public Task<DatabaseSchema> ParseDbContextAsync(string dbContextCode, DatabaseType dbType) => throw error;
        public Task<SchemaDiffResult> CalculateDiffAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType engine = DatabaseType.PostgreSQL) => throw error;
        public Task<MigrationResult> GenerateMigrationAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType dbType) => throw error;
    }

    private sealed class ThrowingAi(Exception error) : IAIService
    {
        public Task<DatabaseSchema> GenerateSchemaAsync(GenerateRequest request) => throw error;
        public Task<DatabaseSchema> ReviseSchemaAsync(ReviseRequest request) => throw error;
        public Task<string> GenerateMockDataAsync(DatabaseSchema schema) => throw error;
        public Task<string> GenerateProjectSummaryAsync(DatabaseSchema schema, string projectName) => throw error;
        public Task<string> GenerateStreamlitAppAsync(DatabaseSchema schema, DatabaseType dbType) => throw error;
        public Task<DatabaseSchema> AnalyzeImageAsync(byte[] imageBytes, string mimeType) => throw error;
        public Task<string> ExplainImpactAsync(ImpactReport impact) => throw error;
    }

    private static MigrationController Migration(Exception error) =>
        new(new ThrowingMigrations(error), NullLogger<MigrationController>.Instance);

    private static ReverseEngineerController Reverse(Exception error) =>
        new(new ThrowingAi(error), NullLogger<ReverseEngineerController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static IFormFile Png()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "image", "board.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
    }

    private static readonly MigrationGenerateRequest Generate = new() { OldSchema = new DatabaseSchema(), NewSchema = new DatabaseSchema(), DbType = DatabaseType.PostgreSQL };

    /// <summary>Uç, üretimdeki gibi merkezi ara katmanın arkasında çalıştırılır.</summary>
    private static async Task<(int Status, JsonElement Body, string Raw)> ThroughMiddlewareAsync(Func<Task<IActionResult>> action)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        IActionResult? result = null;
        var middleware = new ExceptionMiddleware(async _ => result = await action(), NullLogger<ExceptionMiddleware>.Instance, new Env());
        await middleware.InvokeAsync(context);
        if (result is ObjectResult obj)
        {
            var raw = JsonSerializer.Serialize(obj.Value);
            return (obj.StatusCode ?? 200, JsonDocument.Parse(raw).RootElement, raw);
        }
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, JsonDocument.Parse(text).RootElement, text);
    }

    public static IEnumerable<object[]> MigrationActions() => new[]
    {
        new object[] { "generate" }, new object[] { "diff" }, new object[] { "parse" },
    };

    private static Func<Task<IActionResult>> Call(MigrationController c, string action) => action switch
    {
        "generate" => () => c.GenerateMigration(Generate),
        "diff" => () => c.CalculateDiff(new SchemaDiffRequest { OldSchema = new DatabaseSchema(), NewSchema = new DatabaseSchema() }),
        _ => () => c.ParseDbContext(new ParseDbContextRequest { DbContextCode = "class A {}", DbType = DatabaseType.PostgreSQL }),
    };

    [Theory]
    [MemberData(nameof(MigrationActions))]
    public async Task Migration_ai_not_configured_reaches_the_middleware_as_503(string action)
    {
        var (status, body, raw) = await ThroughMiddlewareAsync(Call(Migration(new AiNotConfiguredException("Groq")), action));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("AI_NOT_CONFIGURED", body.GetProperty("code").GetString());
        Assert.DoesNotContain("failed:", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Migration_other_failures_keep_their_endpoint_response()
    {
        var (status, _, raw) = await ThroughMiddlewareAsync(Call(Migration(new InvalidOperationException("bad input")), "generate"));
        Assert.Equal(500, status);
        Assert.Contains("Migration code generation failed", raw);
    }

    [Fact]
    public async Task Reverse_engineer_ai_not_configured_reaches_the_middleware_as_503()
    {
        var (status, body, raw) = await ThroughMiddlewareAsync(() => Reverse(new AiNotConfiguredException("Gemini")).AnalyzeImage(Png()));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        Assert.Equal("AI_NOT_CONFIGURED", body.GetProperty("code").GetString());
        Assert.DoesNotContain("Whiteboard analysis failed", raw);

        var (other, _, otherRaw) = await ThroughMiddlewareAsync(() => Reverse(new InvalidOperationException("x")).AnalyzeImage(Png()));
        Assert.Equal(500, other);
        Assert.Contains("Whiteboard analysis failed", otherRaw);
    }

    [Theory]
    [InlineData("generate")]
    public async Task Migration_500_body_never_carries_the_exception_text(string action)
    {
        var (status, _, raw) = await ThroughMiddlewareAsync(Call(Migration(new InvalidOperationException("SECRET-DRIVER-DETAIL host=10.0.0.5")), action));
        Assert.Equal(500, status);
        Assert.DoesNotContain("SECRET-DRIVER-DETAIL", raw);
        Assert.DoesNotContain("10.0.0.5", raw);
    }

    [Fact]
    public async Task Reverse_engineer_500_body_never_carries_the_exception_text()
    {
        var (status, _, raw) = await ThroughMiddlewareAsync(() => Reverse(new InvalidOperationException("SECRET-PROVIDER-DETAIL")).AnalyzeImage(Png()));
        Assert.Equal(500, status);
        Assert.DoesNotContain("SECRET-PROVIDER-DETAIL", raw);
    }
}
