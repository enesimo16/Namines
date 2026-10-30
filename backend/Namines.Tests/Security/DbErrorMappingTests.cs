using System.Data.Common;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Middleware;
using Xunit;

namespace Namines.Tests.Security;

/// <summary>
/// Veritabanı kısıt hatalarının ExceptionMiddleware'de 500 yerine anlamlı 4xx'e
/// çevrilmesi ve yanıtın kısıt/tablo adı sızdırmaması (DB-01/DB-10 uygulama tarafı).
/// </summary>
public sealed class DbErrorMappingTests
{
    private sealed class FakeDbException(string sqlState, string message) : DbException(message)
    {
        public override string? SqlState => sqlState;
    }

    private sealed class Env : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Namines.API";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static async Task<(int Status, JsonElement Body, string Raw)> RunAsync(Exception ex)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(_ => throw ex, NullLogger<ExceptionMiddleware>.Instance, new Env());
        await middleware.InvokeAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var raw = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    [Fact]
    public async Task DbUpdateConcurrencyException_409_CONCURRENCY_CONFLICT()
    {
        var (status, body, _) = await RunAsync(new DbUpdateConcurrencyException("xmin of CloudProjects"));
        Assert.Equal(409, status);
        Assert.Equal("CONCURRENCY_CONFLICT", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("23503", 409, "REFERENCE_CONFLICT")]
    [InlineData("23505", 409, "UNIQUE_CONFLICT")]
    [InlineData("23514", 400, "VALIDATION_FAILED")]
    public async Task Kisit_ihlali_dogru_koda_cevrilir_ve_kisit_adi_sizmaz(string sqlState, int expectedStatus, string expectedCode)
    {
        var inner = new FakeDbException(sqlState, "violates constraint \"FK_VaultBackups_CloudProjects_ProjectId\" on table \"VaultBackups\"");
        var (status, body, raw) = await RunAsync(new DbUpdateException("save failed", inner));

        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedCode, body.GetProperty("code").GetString());
        Assert.DoesNotContain("FK_VaultBackups", raw);
        Assert.DoesNotContain("VaultBackups", raw);
    }

    [Fact]
    public async Task Taninmayan_DbUpdateException_500_kalir()
    {
        var (status, _, _) = await RunAsync(new DbUpdateException("x", new FakeDbException("08006", "conn")));
        Assert.Equal(500, status);
    }
}
