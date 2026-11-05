using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Flow.Application;
using Namines.Flow.Infrastructure.Persistence;
using Namines.Infrastructure.Data;
using Xunit;
using Namines.Tests.TestSupport;

namespace Namines.Tests.Services;

/// <summary>
/// Çalışma kayıtları hiçbir şey tarafından silinmiyordu. Bu testler iki sınırın
/// da uygulandığını ve YENİ kayıtların korunduğunu garanti ediyor — fazla
/// agresif bir temizlik, teşhis için bakılan tek şeyi silerdi.
/// </summary>
public sealed class AutomationRunLogRetentionTests : IAsyncLifetime
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
        await db.EnsureModulesCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AuthDbContext NewContext() => new(_options);
    private FlowDbContext NewFlowContext() => new(_flowOptions);

    private sealed class PassthroughProtector : IConnectionSecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string ciphertext) => ciphertext;
    }

    private FlowApplicationService NewService(AuthDbContext db, FlowDbContext flowDb) =>
        new(
            flowDb,
            TestModules.For(db).Authority(new PassthroughProtector()),
            new StubQuota(allowed: true),
            dbaAnalyzer: null,
            aiService: null,
            httpClientFactory: new StubHttpClientFactory(HttpStatusCode.OK),
            linter: new Namines.Infrastructure.LinterService(),
            logger: NullLogger<FlowApplicationService>.Instance);

    private static async Task<AutomationRule> SeedRuleAsync(AuthDbContext db, FlowDbContext flowDb, string ruleId = "r1")
    {
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
            db.Studio().Projects.Add(new CloudProject
            {
                Id = "p1", Name = "p", DbType = "PostgreSQL",
                SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u1",
            });
            await db.SaveChangesAsync();
        }

        var rule = new AutomationRule { Id = ruleId, ProjectId = "p1", TriggerType = "TableDeleted" };
        flowDb.AutomationRules.Add(rule);
        await flowDb.SaveChangesAsync();
        return rule;
    }

    private static void AddRuns(FlowDbContext flowDb, string ruleId, int count, DateTime at)
    {
        for (var i = 0; i < count; i++)
        {
            flowDb.AutomationRunLogs.Add(new AutomationRunLog
            {
                RuleId = ruleId, ActionType = "Webhook", Status = "Success",
                TriggeredAt = at.AddSeconds(-i),
            });
        }
    }

    [Fact]
    public async Task Yas_sinirini_asan_kayitlar_siliniyor()
    {
        await using var db = NewContext();
        await using var flowDb = NewFlowContext();
        await SeedRuleAsync(db, flowDb);
        AddRuns(flowDb, "r1", 3, DateTime.UtcNow.AddDays(-40));
        AddRuns(flowDb, "r1", 2, DateTime.UtcNow);
        await flowDb.SaveChangesAsync();

        var deleted = await NewService(db, flowDb).PurgeRunLogsAsync(retentionDays: 30, maxPerRule: 0, CancellationToken.None);

        Assert.Equal(3, deleted);
        Assert.Equal(2, await flowDb.AutomationRunLogs.CountAsync());
    }

    [Fact]
    public async Task Kural_basina_sayi_siniri_uygulaniyor_ve_EN_YENILER_kaliyor()
    {
        await using var db = NewContext();
        await using var flowDb = NewFlowContext();
        await SeedRuleAsync(db, flowDb);
        var now = DateTime.UtcNow;
        AddRuns(flowDb, "r1", 10, now);
        await flowDb.SaveChangesAsync();

        await NewService(db, flowDb).PurgeRunLogsAsync(retentionDays: 0, maxPerRule: 4, CancellationToken.None);

        var remaining = await flowDb.AutomationRunLogs.OrderByDescending(l => l.TriggeredAt).ToListAsync();
        Assert.Equal(4, remaining.Count);
        Assert.All(remaining, r => Assert.True(r.TriggeredAt > now.AddSeconds(-4)));
    }

    [Fact]
    public async Task Sinirin_altindaki_kural_hic_dokunulmadan_kaliyor()
    {
        await using var db = NewContext();
        await using var flowDb = NewFlowContext();
        await SeedRuleAsync(db, flowDb);
        AddRuns(flowDb, "r1", 3, DateTime.UtcNow);
        await flowDb.SaveChangesAsync();

        var deleted = await NewService(db, flowDb).PurgeRunLogsAsync(retentionDays: 30, maxPerRule: 100, CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(3, await flowDb.AutomationRunLogs.CountAsync());
    }

    [Fact]
    public async Task Bir_kuralin_gurultusu_digerinin_kayitlarini_silmiyor()
    {
        await using var db = NewContext();
        await using var flowDb = NewFlowContext();
        await SeedRuleAsync(db, flowDb, "r1");
        await SeedRuleAsync(db, flowDb, "r2");
        AddRuns(flowDb, "r1", 10, DateTime.UtcNow);
        AddRuns(flowDb, "r2", 2, DateTime.UtcNow);
        await flowDb.SaveChangesAsync();

        await NewService(db, flowDb).PurgeRunLogsAsync(retentionDays: 0, maxPerRule: 4, CancellationToken.None);

        Assert.Equal(4, await flowDb.AutomationRunLogs.CountAsync(l => l.RuleId == "r1"));
        Assert.Equal(2, await flowDb.AutomationRunLogs.CountAsync(l => l.RuleId == "r2"));
    }

    [Fact]
    public async Task Sinirlar_sifir_verilince_temizlik_yapilmiyor()
    {
        await using var db = NewContext();
        await using var flowDb = NewFlowContext();
        await SeedRuleAsync(db, flowDb);
        AddRuns(flowDb, "r1", 5, DateTime.UtcNow.AddYears(-1));
        await flowDb.SaveChangesAsync();

        var deleted = await NewService(db, flowDb).PurgeRunLogsAsync(retentionDays: 0, maxPerRule: 0, CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(5, await flowDb.AutomationRunLogs.CountAsync());
    }
}
