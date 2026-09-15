using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Data;

/// <summary>
/// Namines Flow (Bölüm 3) — <see cref="AutomationRule"/>/<see cref="AutomationRunLog"/>
/// modellerinin <see cref="AuthDbContext"/> üzerinde kayıt, geri okuma ve cascade
/// delete davranışını doğrular. Diğer *ContextTests.cs (bkz. ExportPermissionTests.cs)
/// ile aynı desen: gerçek FK zorlaması için SQLite in-memory bağlantı.
/// </summary>
public sealed class AutomationRuleContextTests : IAsyncLifetime
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

    private static async Task<CloudProject> SeedProjectAsync(AuthDbContext db)
    {
        // CloudProject -> ApplicationUser FK'sı ZORUNLU (bkz. AuthDbContext.OnModelCreating);
        // SQLite bunu uyguluyor, önce asgari bir kullanıcı satırı lazım.
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1" });

        var project = new CloudProject
        {
            Id = Guid.NewGuid().ToString(),
            Name = "p",
            DbType = "PostgreSQL",
            SchemaJson = "{}",
            NodePositionsJson = "{}",
            UserId = "u1",
        };
        db.CloudProjects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    [Fact]
    public async Task Kural_kaydedilip_geri_okunabiliyor()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db);

        db.AutomationRules.Add(new AutomationRule
        {
            ProjectId = project.Id,
            ScopeTableId = "t1",
            TriggerType = "TableDeleted",
            ActionType = "Webhook",
            ActionConfigJson = "{\"url\":\"https://example.com/hook\"}",
        });
        await db.SaveChangesAsync();

        var saved = await db.AutomationRules.SingleAsync();
        Assert.Equal("TableDeleted", saved.TriggerType);
        Assert.True(saved.Enabled);
    }

    [Fact]
    public async Task Proje_silinince_kurallari_ve_loglari_da_siliniyor()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db);

        var rule = new AutomationRule { ProjectId = project.Id, TriggerType = "TableAdded", ActionType = "Toast" };
        db.AutomationRules.Add(rule);
        await db.SaveChangesAsync();

        db.AutomationRunLogs.Add(new AutomationRunLog { RuleId = rule.Id, Status = "Success" });
        await db.SaveChangesAsync();

        db.CloudProjects.Remove(project);
        await db.SaveChangesAsync();

        Assert.Empty(db.AutomationRules);
        Assert.Empty(db.AutomationRunLogs);
    }
}
