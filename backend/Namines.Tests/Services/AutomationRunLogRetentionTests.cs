using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

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

    private static async Task<AutomationRule> SeedRuleAsync(AuthDbContext db, string ruleId = "r1")
    {
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });
            db.CloudProjects.Add(new CloudProject
            {
                Id = "p1", Name = "p", DbType = "PostgreSQL",
                SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u1",
            });
            await db.SaveChangesAsync();
        }

        var rule = new AutomationRule { Id = ruleId, ProjectId = "p1", TriggerType = "TableDeleted" };
        db.AutomationRules.Add(rule);
        await db.SaveChangesAsync();
        return rule;
    }

    private static void AddRuns(AuthDbContext db, string ruleId, int count, DateTime at)
    {
        for (var i = 0; i < count; i++)
        {
            db.AutomationRunLogs.Add(new AutomationRunLog
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
        await SeedRuleAsync(db);
        AddRuns(db, "r1", 3, DateTime.UtcNow.AddDays(-40));
        AddRuns(db, "r1", 2, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var deleted = await AutomationRunLogRetentionService.PurgeAsync(db, retentionDays: 30, maxPerRule: 0, CancellationToken.None);

        Assert.Equal(3, deleted);
        Assert.Equal(2, await db.AutomationRunLogs.CountAsync());
    }

    [Fact]
    public async Task Kural_basina_sayi_siniri_uygulaniyor_ve_EN_YENILER_kaliyor()
    {
        // Yanlış uçtan kesmek, teşhis için bakılan tek şeyi (son çalışmalar)
        // silerdi.
        await using var db = NewContext();
        await SeedRuleAsync(db);
        var now = DateTime.UtcNow;
        AddRuns(db, "r1", 10, now);
        await db.SaveChangesAsync();

        await AutomationRunLogRetentionService.PurgeAsync(db, retentionDays: 0, maxPerRule: 4, CancellationToken.None);

        var remaining = await db.AutomationRunLogs.OrderByDescending(l => l.TriggeredAt).ToListAsync();
        Assert.Equal(4, remaining.Count);
        // AddRuns en yeniyi `now` ile yazıyor, sonrakiler saniye saniye geriye.
        Assert.All(remaining, r => Assert.True(r.TriggeredAt > now.AddSeconds(-4)));
    }

    [Fact]
    public async Task Sinirin_altindaki_kural_hic_dokunulmadan_kaliyor()
    {
        await using var db = NewContext();
        await SeedRuleAsync(db);
        AddRuns(db, "r1", 3, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var deleted = await AutomationRunLogRetentionService.PurgeAsync(db, retentionDays: 30, maxPerRule: 100, CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(3, await db.AutomationRunLogs.CountAsync());
    }

    [Fact]
    public async Task Bir_kuralin_gurultusu_digerinin_kayitlarini_silmiyor()
    {
        await using var db = NewContext();
        await SeedRuleAsync(db, "r1");
        await SeedRuleAsync(db, "r2");
        AddRuns(db, "r1", 10, DateTime.UtcNow);
        AddRuns(db, "r2", 2, DateTime.UtcNow);
        await db.SaveChangesAsync();

        await AutomationRunLogRetentionService.PurgeAsync(db, retentionDays: 0, maxPerRule: 4, CancellationToken.None);

        Assert.Equal(4, await db.AutomationRunLogs.CountAsync(l => l.RuleId == "r1"));
        Assert.Equal(2, await db.AutomationRunLogs.CountAsync(l => l.RuleId == "r2"));
    }

    [Fact]
    public async Task Sinirlar_sifir_verilince_temizlik_yapilmiyor()
    {
        // Yapılandırmayla tamamen kapatılabilmeli.
        await using var db = NewContext();
        await SeedRuleAsync(db);
        AddRuns(db, "r1", 5, DateTime.UtcNow.AddYears(-1));
        await db.SaveChangesAsync();

        var deleted = await AutomationRunLogRetentionService.PurgeAsync(db, retentionDays: 0, maxPerRule: 0, CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(5, await db.AutomationRunLogs.CountAsync());
    }
}
