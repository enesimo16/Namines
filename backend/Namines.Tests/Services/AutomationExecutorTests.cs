using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

// SQLite in-memory bağlantı deseni — diğer *ContextTests.cs (bkz.
// AutomationRuleContextTests.cs) ile aynı: EF InMemory sağlayıcısı bu
// projede referanslı değil, ve CloudProject -> ApplicationUser FK'sı
// ZORUNLU (AuthDbContext.OnModelCreating), bu yüzden SQLite kullanılıyor
// ve her testte önce minimal bir kullanıcı satırı ekleniyor.
public sealed class AutomationExecutorTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AuthDbContext NewContext() => new(_options);

    private async Task<(AuthDbContext Db, CloudProject Project)> SeedAsync(params AutomationRule[] rules)
    {
        var db = NewContext();
        db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "user-1" });
        var project = new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "user-1",
        };
        db.CloudProjects.Add(project);
        foreach (var r in rules) { r.ProjectId = project.Id; db.AutomationRules.Add(r); }
        await db.SaveChangesAsync();
        return (db, project);
    }

    [Fact]
    public async Task Webhook_kurali_guvenli_olmayan_URL_icin_atlaniyor_ve_loglaniyor()
    {
        var (db, project) = await SeedAsync(new AutomationRule
        {
            ScopeTableId = null, TriggerType = "TableAdded", Enabled = true,
            Actions = { new AutomationAction { ActionType = "Webhook", ActionConfigJson = "{\"url\":\"http://127.0.0.1/hook\"}" } },
        });

        var executor = new AutomationExecutor(
            db, new StubQuota(AiQuotaDecision.Allowed),
            groqDba: null!, aiService: null!,
            httpClientFactory: new StubHttpClientFactory(HttpStatusCode.OK),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationExecutor>.Instance);

        var diff = new SchemaDiffResult { AddedTables = { "orders" } };
        var schema = new DatabaseSchema { Tables = { new SchemaTable { Id = "t1", Name = "orders" } } };

        await executor.RunAsync(project.Id, diff, new DatabaseSchema(), schema, CancellationToken.None);

        var log = await db.AutomationRunLogs.SingleAsync();
        Assert.Equal("Skipped", log.Status);
    }

    [Fact]
    public async Task Bir_kuralin_basarisizligi_digerini_engellemiyor()
    {
        var (db, project) = await SeedAsync(
            new AutomationRule { ScopeTableId = null, TriggerType = "TableAdded", Enabled = true, Actions = { new AutomationAction { ActionType = "Webhook", ActionConfigJson = "{\"url\":\"https://example.com/hook\"}" } } },
            new AutomationRule { ScopeTableId = null, TriggerType = "TableAdded", Enabled = true, Actions = { new AutomationAction { ActionType = "Toast" } } });

        var executor = new AutomationExecutor(
            db, new StubQuota(AiQuotaDecision.Allowed),
            groqDba: null!, aiService: null!,
            httpClientFactory: new StubHttpClientFactory(HttpStatusCode.InternalServerError),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationExecutor>.Instance);

        var diff = new SchemaDiffResult { AddedTables = { "orders" } };
        var schema = new DatabaseSchema { Tables = { new SchemaTable { Id = "t1", Name = "orders" } } };

        // Patlamamalı — Toast sunucuda hiç işlenmediği için tek log satırı
        // (Webhook) beklenir, Toast için log YAZILMAZ.
        await executor.RunAsync(project.Id, diff, new DatabaseSchema(), schema, CancellationToken.None);

        var logs = await db.AutomationRunLogs.ToListAsync();
        Assert.Single(logs);
        Assert.Equal("Failed", logs[0].Status);
    }
}

internal sealed class StubQuota : IAiQuotaReserver
{
    private readonly AiQuotaDecision _decision;
    public StubQuota(AiQuotaDecision decision) => _decision = decision;
    public Task<AiQuotaDecision> TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct = default)
        => Task.FromResult(_decision);
}

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpStatusCode _status;
    public StubHttpClientFactory(HttpStatusCode status) => _status = status;
    public HttpClient CreateClient(string name) =>
        new(new StubHandler(_status)) { Timeout = System.TimeSpan.FromSeconds(5) };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public StubHandler(HttpStatusCode status) => _status = status;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_status));
    }
}
