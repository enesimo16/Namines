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
            linter: new Namines.Infrastructure.LinterService(),
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
            linter: new Namines.Infrastructure.LinterService(),
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

    // ── Şablon ve başlık güvenliği (Faz 6) ──────────────────────────────────

    private async Task<StubHttpClientFactory> RunWebhookAsync(
        string configJson, DatabaseSchema schema, string addedTableName = "orders")
    {
        var (db, project) = await SeedAsync(new AutomationRule
        {
            ScopeTableId = null, TriggerType = "TableAdded", Enabled = true,
            Actions = { new AutomationAction { ActionType = "Webhook", ActionConfigJson = configJson } },
        });

        var http = new StubHttpClientFactory(HttpStatusCode.OK);
        var executor = new AutomationExecutor(
            db, new StubQuota(AiQuotaDecision.Allowed),
            groqDba: null!, aiService: null!,
            httpClientFactory: http,
            linter: new Namines.Infrastructure.LinterService(),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationExecutor>.Instance);

        // Şablon tablo adını DIFF'ten alıyor (eşleşmeyi sağlayan bağlam),
        // şemadan değil — test de o yüzden diff'teki adı kontrol ediyor.
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
        // Bu alan eklenmeden önce kurulmuş webhook'ların alıcıları eski şekli
        // bekliyor; boş gövde onu bozmamalı.
        var http = await RunWebhookAsync("""{"url":"https://example.com/hook"}""", SchemaWith("orders"));

        Assert.NotNull(http.LastBody);
        Assert.Contains("\"trigger\":\"TableAdded\"", http.LastBody);
    }

    [Fact]
    public async Task Baslik_degerindeki_satir_sonlari_atiliyor()
    {
        // Değer şablondan geliyor ve şablon TABLO ADINI dolduruyor. Adında
        // CR/LF olan bir tablo, hedef sunucuya giden isteğe başlık enjekte
        // etmenin yolu olurdu.
        // Şablon tablo adını DIFF'ten alıyor, şemadan değil — zehirli ad
        // ikisine de konuyor ki yol gerçekten test edilsin.
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

        // `Headers.Contains("Bad Name")` GEÇERSİZ ad için istisna fırlatıyor;
        // bu yüzden koleksiyon doğrudan taranıyor.
        Assert.NotNull(http.LastRequest);
        Assert.DoesNotContain(http.LastRequest!.Headers, h => h.Key == "Bad Name");
        Assert.Equal("y", Assert.Single(http.LastRequest.Headers.GetValues("X-Good")));
    }

    [Fact]
    public async Task Slack_mesaji_text_alanina_konuyor()
    {
        var (db, project) = await SeedAsync(new AutomationRule
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

        var http = new StubHttpClientFactory(HttpStatusCode.OK);
        var executor = new AutomationExecutor(
            db, new StubQuota(AiQuotaDecision.Allowed),
            groqDba: null!, aiService: null!,
            httpClientFactory: http,
            linter: new Namines.Infrastructure.LinterService(),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationExecutor>.Instance);

        await executor.RunAsync(
            project.Id, new SchemaDiffResult { AddedTables = { "orders" } },
            new DatabaseSchema(), SchemaWith("orders"), CancellationToken.None);

        Assert.Equal("{\"text\":\"orders eklendi\"}", http.LastBody);
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
    private readonly StubHandler _handler;

    public StubHttpClientFactory(HttpStatusCode status)
    {
        _status = status;
        _handler = new StubHandler(_status);
    }

    /// <summary>Yakalanan son istek — başlık/gövde doğrulaması için.</summary>
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
            // Gövde, istek atıldıktan SONRA okunamaz (stream tüketilir), bu
            // yüzden burada yakalanıyor.
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status);
        }
    }
}
