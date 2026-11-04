using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Flow.Application;
using Namines.Flow.Infrastructure.Persistence;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

public sealed class AutomationExecutorTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;
    private DbContextOptions<FlowDbContext> _flowOptions = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;
        _flowOptions = new DbContextOptionsBuilder<FlowDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AuthDbContext NewContext() => new(_options);
    private FlowDbContext NewFlowContext() => new(_flowOptions);

    private sealed class PassthroughProtector : IConnectionSecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private FlowApplicationService NewFlowService(
        AuthDbContext db,
        FlowDbContext flowDb,
        HttpStatusCode status,
        out StubHttpClientFactory http)
    {
        http = new StubHttpClientFactory(status);
        var authority = new TenantProjectAuthorityService(db, new PassthroughProtector());
        return new FlowApplicationService(
            flowDb,
            authority,
            new StubQuota(allowed: true),
            dbaAnalyzer: null,
            aiService: null,
            httpClientFactory: http,
            linter: new Namines.Infrastructure.LinterService(),
            logger: NullLogger<FlowApplicationService>.Instance);
    }

    private async Task<(AuthDbContext Db, FlowDbContext FlowDb, CloudProject Project)> SeedAsync(params AutomationRule[] rules)
    {
        var db = NewContext();
        var flowDb = NewFlowContext();
        db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "user-1" });
        var project = new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "user-1",
        };
        db.CloudProjects.Add(project);
        await db.SaveChangesAsync();

        foreach (var r in rules)
        {
            r.ProjectId = project.Id;
            flowDb.AutomationRules.Add(r);
        }
        await flowDb.SaveChangesAsync();
        return (db, flowDb, project);
    }

    [Fact]
    public async Task Webhook_kurali_guvenli_olmayan_URL_icin_atlaniyor_ve_loglaniyor()
    {
        var (db, flowDb, project) = await SeedAsync(new AutomationRule
        {
            ScopeTableId = null, TriggerType = "TableAdded", Enabled = true,
            Actions = { new AutomationAction { ActionType = "Webhook", ActionConfigJson = "{\"url\":\"http://127.0.0.1/hook\"}" } },
        });

        var executor = NewFlowService(db, flowDb, HttpStatusCode.OK, out _);

        var diff = new SchemaDiffResult { AddedTables = { "orders" } };
        var schema = new DatabaseSchema { Tables = { new SchemaTable { Id = "t1", Name = "orders" } } };

        await executor.RunAsync(project.Id, diff, new DatabaseSchema(), schema, CancellationToken.None);

        var log = await flowDb.AutomationRunLogs.SingleAsync();
        Assert.Equal("Skipped", log.Status);
    }

    [Fact]
    public async Task Bir_kuralin_basarisizligi_digerini_engellemiyor()
    {
        var (db, flowDb, project) = await SeedAsync(
            new AutomationRule { ScopeTableId = null, TriggerType = "TableAdded", Enabled = true, Actions = { new AutomationAction { ActionType = "Webhook", ActionConfigJson = "{\"url\":\"https://example.com/hook\"}" } } },
            new AutomationRule { ScopeTableId = null, TriggerType = "TableAdded", Enabled = true, Actions = { new AutomationAction { ActionType = "Toast" } } });

        var executor = NewFlowService(db, flowDb, HttpStatusCode.InternalServerError, out _);

        var diff = new SchemaDiffResult { AddedTables = { "orders" } };
        var schema = new DatabaseSchema { Tables = { new SchemaTable { Id = "t1", Name = "orders" } } };

        await executor.RunAsync(project.Id, diff, new DatabaseSchema(), schema, CancellationToken.None);

        var logs = await flowDb.AutomationRunLogs.ToListAsync();
        Assert.Single(logs);
        Assert.Equal("Failed", logs[0].Status);
    }

    // ── Şablon ve başlık güvenliği (Faz 6) ──────────────────────────────────

    private async Task<StubHttpClientFactory> RunWebhookAsync(
        string configJson, DatabaseSchema schema, string addedTableName = "orders")
    {
        var (db, flowDb, project) = await SeedAsync(new AutomationRule
        {
            ScopeTableId = null, TriggerType = "TableAdded", Enabled = true,
            Actions = { new AutomationAction { ActionType = "Webhook", ActionConfigJson = configJson } },
        });

        var executor = NewFlowService(db, flowDb, HttpStatusCode.OK, out var http);

        var diff = new SchemaDiffResult { AddedTables = { addedTableName } };
        await executor.RunAsync(project.Id, diff, new DatabaseSchema(), schema, CancellationToken.None);
        return http;
    }

    private static DatabaseSchema SchemaWith(string tableName) =>
        new() { Tables = { new SchemaTable { Id = "t1", Name = tableName } } };

    [Fact]
    public async Task Govde_sablonu_tetiklemenin_degerleriyle_dolduruluyor()
    {
        var http = await RunWebhookAsync(
            """{"url":"https://example.com/hook","body":"{\"t\":\"{{tableName}}\",\"e\":\"{{trigger}}\"}"}""",
            SchemaWith("orders"));

        Assert.Equal("{\"t\":\"orders\",\"e\":\"TableAdded\"}", http.LastBody);
    }

    [Fact]
    public async Task Govde_bos_birakilirsa_ESKI_varsayilan_gonderiliyor()
    {
        var http = await RunWebhookAsync("""{"url":"https://example.com/hook"}""", SchemaWith("orders"));

        Assert.NotNull(http.LastBody);
        Assert.Contains("\"trigger\":\"TableAdded\"", http.LastBody);
    }

    [Fact]
    public async Task Baslik_degerindeki_satir_sonlari_atiliyor()
    {
        const string poisoned = "orders\r\nX-Injected: 1";
        var http = await RunWebhookAsync(
            """{"url":"https://example.com/hook","headers":{"X-Table":"{{tableName}}"}}""",
            SchemaWith(poisoned), addedTableName: poisoned);

        var value = Assert.Single(http.LastRequest!.Headers.GetValues("X-Table"));
        Assert.Equal("ordersX-Injected: 1", value);
        Assert.False(http.LastRequest.Headers.Contains("X-Injected"));
    }

    [Fact]
    public async Task Gecersiz_baslik_adi_atlaniyor_ama_istek_gonderiliyor()
    {
        var http = await RunWebhookAsync(
            """{"url":"https://example.com/hook","headers":{"Bad Name":"x","X-Good":"y"}}""",
            SchemaWith("orders"));

        Assert.NotNull(http.LastRequest);
        Assert.DoesNotContain(http.LastRequest!.Headers, h => h.Key == "Bad Name");
        Assert.Equal("y", Assert.Single(http.LastRequest.Headers.GetValues("X-Good")));
    }

    [Fact]
    public async Task Yonlendirme_TAKIP_EDILMIYOR_ve_sebebi_yaziliyor()
    {
        var (db, flowDb, project) = await SeedAsync(new AutomationRule
        {
            ScopeTableId = null, TriggerType = "TableAdded", Enabled = true,
            Actions = { new AutomationAction { ActionType = "Webhook", ActionConfigJson = """{"url":"https://example.com/hook"}""" } },
        });

        var executor = NewFlowService(db, flowDb, HttpStatusCode.Redirect, out _);

        await executor.RunAsync(
            project.Id, new SchemaDiffResult { AddedTables = { "orders" } },
            new DatabaseSchema(), SchemaWith("orders"), CancellationToken.None);

        var log = await flowDb.AutomationRunLogs.SingleAsync();
        Assert.Equal("Skipped", log.Status);
        Assert.Contains("Redirects are not followed", log.ErrorMessage);
    }

    [Fact]
    public async Task Slack_mesaji_text_alanina_konuyor()
    {
        var (db, flowDb, project) = await SeedAsync(new AutomationRule
        {
            ScopeTableId = null, TriggerType = "TableAdded", Enabled = true,
            Actions =
            {
                new AutomationAction
                {
                    ActionType = "Slack",
                    ActionConfigJson = """{"url":"https://example.com/hook","message":"{{tableName}} eklendi"}""",
                },
            },
        });

        var executor = NewFlowService(db, flowDb, HttpStatusCode.OK, out var http);

        await executor.RunAsync(
            project.Id, new SchemaDiffResult { AddedTables = { "orders" } },
            new DatabaseSchema(), SchemaWith("orders"), CancellationToken.None);

        Assert.Equal("{\"text\":\"orders eklendi\"}", http.LastBody);
    }
}

internal sealed class StubQuota : IFlowAiQuotaGate
{
    private readonly bool _allowed;
    public StubQuota(bool allowed) => _allowed = allowed;
    public Task<bool> TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct = default)
        => Task.FromResult(_allowed);
}

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpStatusCode _status;
    private readonly StubHandler _handler;

    public StubHttpClientFactory(HttpStatusCode status)
    {
        _status = status;
        _handler = new StubHandler(_status);
    }

    public HttpRequestMessage? LastRequest => _handler.LastRequest;
    public string? LastBody => _handler.LastBody;

    public HttpClient CreateClient(string name) =>
        new(_handler, disposeHandler: false) { Timeout = System.TimeSpan.FromSeconds(5) };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(HttpStatusCode status) => _status = status;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status);
        }
    }
}
