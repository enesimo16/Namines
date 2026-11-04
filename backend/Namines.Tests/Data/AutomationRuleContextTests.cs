using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Flow.Infrastructure.Persistence;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Data;

/// <summary>
/// Namines Flow (Bölüm 3) — <see cref="AutomationRule"/>/<see cref="AutomationRunLog"/>
/// modellerinin <see cref="FlowDbContext"/> üzerinde kayıt, geri okuma ve cascade
/// delete davranışını doğrular.
/// </summary>
public sealed class AutomationRuleContextTests : IAsyncLifetime
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

    private static async Task<CloudProject> SeedProjectAsync(AuthDbContext db)
    {
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
        await using var flowDb = NewFlowContext();
        var project = await SeedProjectAsync(db);

        flowDb.AutomationRules.Add(new AutomationRule
        {
            ProjectId = project.Id,
            ScopeTableId = "t1",
            TriggerType = "TableDeleted",
            Actions =
            {
                new AutomationAction
                {
                    SortOrder = 0,
                    ActionType = "Webhook",
                    ActionConfigJson = "{\"url\":\"https://example.com/hook\"}",
                },
            },
        });
        await flowDb.SaveChangesAsync();

        var saved = await flowDb.AutomationRules.Include(r => r.Actions).SingleAsync();
        Assert.Equal("TableDeleted", saved.TriggerType);
        Assert.True(saved.Enabled);
        Assert.Equal("Webhook", Assert.Single(saved.Actions).ActionType);
    }

    [Fact]
    public async Task Kural_silinince_aksiyonlari_da_siliniyor()
    {
        await using var db = NewContext();
        await using var flowDb = NewFlowContext();
        var project = await SeedProjectAsync(db);

        var rule = new AutomationRule
        {
            ProjectId = project.Id,
            TriggerType = "TableAdded",
            Actions =
            {
                new AutomationAction { SortOrder = 0, ActionType = "Toast" },
                new AutomationAction { SortOrder = 1, ActionType = "Webhook" },
            },
        };
        flowDb.AutomationRules.Add(rule);
        await flowDb.SaveChangesAsync();
        Assert.Equal(2, await flowDb.AutomationActions.CountAsync());

        flowDb.AutomationRules.Remove(rule);
        await flowDb.SaveChangesAsync();

        Assert.Empty(await flowDb.AutomationActions.ToListAsync());
    }

    [Fact]
    public async Task Proje_silinince_kurallari_ve_loglari_da_siliniyor()
    {
        await using var db = NewContext();
        await using var flowDb = NewFlowContext();
        var project = await SeedProjectAsync(db);

        var rule = new AutomationRule { ProjectId = project.Id, TriggerType = "TableAdded" };
        flowDb.AutomationRules.Add(rule);
        await flowDb.SaveChangesAsync();

        flowDb.AutomationRunLogs.Add(new AutomationRunLog { RuleId = rule.Id, Status = "Success" });
        await flowDb.SaveChangesAsync();

        db.CloudProjects.Remove(project);
        await db.SaveChangesAsync();

        await using var verifyFlow = NewFlowContext();
        Assert.Empty(await verifyFlow.AutomationRules.ToListAsync());
        Assert.Empty(await verifyFlow.AutomationRunLogs.ToListAsync());
    }
}
