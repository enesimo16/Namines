using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.API.Controllers;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

// SQLite in-memory bağlantı deseni — AuthControllerAutomationTests.cs /
// AutomationExecutorTests.cs ile aynı: EF InMemory sağlayıcısı bu projede
// referanslı değil, ve CloudProject -> ApplicationUser FK'sı ZORUNLU
// (AuthDbContext.OnModelCreating), bu yüzden SQLite kullanılıyor ve her
// testte önce minimal bir kullanıcı satırı ekleniyor.
public sealed class AutomationControllerTests : IAsyncLifetime
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

    private async Task<CloudProject> SeedProjectAsync(AuthDbContext db, string ownerId, string projectId = "proj-1")
    {
        db.Users.Add(new ApplicationUser { Id = ownerId, UserName = ownerId });
        var project = new CloudProject
        {
            Id = projectId, Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = ownerId,
        };
        db.CloudProjects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    /// <summary>
    /// Test ucu dışındaki her uç yürütücüye hiç dokunmuyor; bu sahte, yalnızca
    /// hangi kuralın çalıştırıldığını kaydediyor ve gerçek bir webhook/AI
    /// çağrısı yapmıyor.
    /// </summary>
    private sealed class RecordingExecutor : IAutomationExecutor
    {
        public List<(string RuleId, bool IsTest)> Runs { get; } = new();

        public Task RunAsync(string projectId, SchemaDiffResult diff, DatabaseSchema oldSchema, DatabaseSchema newSchema, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RunRuleAsync(AutomationRule rule, string userId, DatabaseSchema schema, DatabaseType engine, bool isTest, CancellationToken ct)
        {
            Runs.Add((rule.Id, isTest));
            return Task.CompletedTask;
        }
    }

    private static AutomationController NewController(AuthDbContext db, string userId, IAutomationExecutor? executor = null)
    {
        var controller = new AutomationController(db, executor ?? new RecordingExecutor())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth")),
                },
            },
        };
        return controller;
    }

    [Fact]
    public async Task GetRules_proje_sahibi_olmayan_kullanici_icin_bos_liste_donuyor()
    {
        // DAVRANIŞ DEĞİŞTİ (404 -> boş liste) ve bu bilinçli.
        //
        // Bilgi saklama korunuyor: saldırgan boş listeye bakarak projenin var
        // olup boş mu olduğunu, kendisine ait olmadığını mı, yoksa hiç var
        // olmadığını mı ayırt edemiyor — 404'te de edemiyordu.
        //
        // Kazanılan şey: projeler istemcide üretiliyor ve sunucuya ancak
        // kaydedilince yazılıyor. Yeni üretilen her şemada canvas, sunucunun
        // bilmediği bir kimlikle bu ucu çağırıp her açılışta iki 404 alıyordu.
        await using var db = NewContext();
        await SeedProjectAsync(db, ownerId: "owner-1");

        var controller = NewController(db, userId: "intruder-1");

        var result = await controller.GetRules("proj-1", default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>());
    }

    [Fact]
    public async Task GetRules_baskasinin_kurallari_bos_listede_sizmiyor()
    {
        // Boş liste "her şeyi göster" demek DEĞİL: sahibin gerçek kuralları
        // varken bile saldırgan hiçbirini görmemeli.
        await using var db = NewContext();
        await SeedProjectAsync(db, ownerId: "owner-1");
        db.AutomationRules.Add(new Namines.Core.Models.AutomationRule
        {
            ProjectId = "proj-1",
            ScopeTableId = "t_users",
            TriggerType = "TableAdded",
            Actions = { new Namines.Core.Models.AutomationAction { ActionType = "Toast" } },
        });
        await db.SaveChangesAsync();

        var controller = NewController(db, userId: "intruder-1");

        var ok = Assert.IsType<OkObjectResult>(await controller.GetRules("proj-1", default));
        Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value).Cast<object>());

        // Sahibi ise kendi kuralını görüyor — boş liste bir susturma değil.
        var owner = NewController(db, userId: "owner-1");
        var ownerOk = Assert.IsType<OkObjectResult>(await owner.GetRules("proj-1", default));
        Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(ownerOk.Value).Cast<object>());
    }

    [Fact]
    public async Task CreateRule_gecerli_istek_icin_Ok_donuyor_ve_DB_de_bulunuyor()
    {
        await using var db = NewContext();
        await SeedProjectAsync(db, ownerId: "owner-1");

        var controller = NewController(db, userId: "owner-1");
        var request = new CreateAutomationRuleRequest
        {
            ProjectId = "proj-1",
            ScopeTableId = null,
            TriggerType = "TableAdded",
            Actions = { new AutomationActionDto { ActionType = "Toast" } },
        };

        var result = await controller.CreateRule(request, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var created = Assert.IsType<AutomationRule>(ok.Value);
        Assert.Equal("proj-1", created.ProjectId);

        await using var verifyDb = NewContext();
        var persisted = await verifyDb.AutomationRules.SingleOrDefaultAsync(r => r.Id == created.Id);
        Assert.NotNull(persisted);
        Assert.Equal("TableAdded", persisted!.TriggerType);
    }

    private async Task<AutomationRule> SeedRuleAsync(AuthDbContext db, string projectId)
    {
        var rule = new AutomationRule
        {
            ProjectId = projectId,
            TriggerType = "TableDeleted",
            Actions = { new AutomationAction { SortOrder = 0, ActionType = "Toast" } },
        };
        db.AutomationRules.Add(rule);
        await db.SaveChangesAsync();
        return rule;
    }

    [Fact]
    public async Task UpdateRule_degisikligi_kalici_yaziyor()
    {
        // C2: PUT ucu olmadan istemcideki her düzenleme yereldeydi ve sunucudaki
        // kayıt sonsuza dek ActionType="Toast" kalıyordu.
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);

        var controller = NewController(db, userId: "owner-1");
        var result = await controller.UpdateRule(rule.Id, new UpdateAutomationRuleRequest
        {
            TriggerType = "ColumnAdded",
            ConditionsJson = "[{\"field\":\"columnName\",\"op\":\"endsWith\",\"value\":\"_id\"}]",
            Actions =
            {
                new AutomationActionDto { ActionType = "Toast" },
                new AutomationActionDto { ActionType = "Webhook", ActionConfigJson = "{\"url\":\"https://example.test/hook\"}" },
            },
            Enabled = false,
        }, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<AutomationRule>(ok.Value);
        Assert.Equal(2, returned.Actions.Count);

        await using var verifyDb = NewContext();
        var persisted = await verifyDb.AutomationRules.Include(r => r.Actions).SingleAsync(r => r.Id == rule.Id);
        Assert.Equal("ColumnAdded", persisted.TriggerType);
        Assert.Contains("endsWith", persisted.ConditionsJson);
        Assert.False(persisted.Enabled);

        // Sıra, istek gövdesindeki diziden alınıyor — zincirin anlamının parçası.
        var ordered = persisted.Actions.OrderBy(a => a.SortOrder).ToList();
        Assert.Equal("Toast", ordered[0].ActionType);
        Assert.Equal("Webhook", ordered[1].ActionType);
        Assert.Equal("{\"url\":\"https://example.test/hook\"}", ordered[1].ActionConfigJson);

        // Eski adımlar toptan değiştiriliyor, yerinde eşleştirilmiyor: güncelleme
        // sonrası DB'de yalnızca yeni iki adım kalmalı, eskisi sızmamalı.
        Assert.Equal(2, await verifyDb.AutomationActions.CountAsync(a => a.RuleId == rule.Id));
    }

    [Fact]
    public async Task UpdateRule_proje_sahibi_olmayan_kullanici_icin_NotFound_donuyor_ve_yazmiyor()
    {
        // DeleteRule ile AYNI sahiplik kuralı: yabancı 404 alır, kayıt değişmez.
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);

        var controller = NewController(db, userId: "intruder-1");
        var result = await controller.UpdateRule(rule.Id, new UpdateAutomationRuleRequest
        {
            TriggerType = "ColumnAdded",
            Actions = { new AutomationActionDto { ActionType = "Webhook", ActionConfigJson = "{\"url\":\"https://evil.test\"}" } },
            Enabled = true,
        }, default);

        Assert.IsType<NotFoundResult>(result);

        await using var verifyDb = NewContext();
        var persisted = await verifyDb.AutomationRules.Include(r => r.Actions).SingleAsync(r => r.Id == rule.Id);
        Assert.Equal("Toast", Assert.Single(persisted.Actions).ActionType);
        Assert.Equal("TableDeleted", persisted.TriggerType);
    }

    [Fact]
    public async Task UpdateRule_olmayan_kural_icin_NotFound_donuyor()
    {
        await using var db = NewContext();
        await SeedProjectAsync(db, ownerId: "owner-1");

        var controller = NewController(db, userId: "owner-1");
        var result = await controller.UpdateRule("yok-boyle-bir-id", new UpdateAutomationRuleRequest(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteRule_var_olan_kurali_kaldiriyor()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = new AutomationRule
        {
            ProjectId = project.Id,
            TriggerType = "TableAdded",
            Actions = { new AutomationAction { SortOrder = 0, ActionType = "Toast" } },
        };
        db.AutomationRules.Add(rule);
        await db.SaveChangesAsync();

        var controller = NewController(db, userId: "owner-1");

        var result = await controller.DeleteRule(rule.Id, default);

        Assert.IsType<OkObjectResult>(result);

        await using var verifyDb = NewContext();
        var persisted = await verifyDb.AutomationRules.SingleOrDefaultAsync(r => r.Id == rule.Id);
        Assert.Null(persisted);
    }

    // ── Çalışma geçmişi (Faz 5) ─────────────────────────────────────────────

    private async Task SeedRunAsync(AuthDbContext db, string ruleId, string status, DateTime at, bool isTest = false)
    {
        db.AutomationRunLogs.Add(new AutomationRunLog
        {
            RuleId = ruleId, ActionType = "Webhook", Status = status, TriggeredAt = at, IsTest = isTest,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetRuns_en_yeniden_eskiye_donuyor()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);

        var now = DateTime.UtcNow;
        await SeedRunAsync(db, rule.Id, "Failed", now.AddMinutes(-5));
        await SeedRunAsync(db, rule.Id, "Success", now);

        var controller = NewController(db, userId: "owner-1");
        var ok = Assert.IsType<OkObjectResult>(await controller.GetRuns(rule.Id, 20, default));
        var runs = Assert.IsAssignableFrom<List<AutomationRunLog>>(ok.Value);

        Assert.Equal(new[] { "Success", "Failed" }, runs.Select(r => r.Status));
    }

    [Fact]
    public async Task GetRuns_baskasinin_kuralinda_NotFound_donuyor()
    {
        // Diğer uçlarla AYNI desen: varlığı sızdırmamak için 403 değil 404.
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);
        await SeedRunAsync(db, rule.Id, "Success", DateTime.UtcNow);

        var controller = NewController(db, userId: "intruder-1");

        Assert.IsType<NotFoundResult>(await controller.GetRuns(rule.Id, 20, default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task GetRuns_gecersiz_limit_bos_liste_degil_varsayilan_kullaniyor(int limit)
    {
        // `Take(0)` sessizce boş liste döner ve kullanıcı geçmişi yokmuş sanır.
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);
        await SeedRunAsync(db, rule.Id, "Success", DateTime.UtcNow);

        var controller = NewController(db, userId: "owner-1");
        var ok = Assert.IsType<OkObjectResult>(await controller.GetRuns(rule.Id, limit, default));

        Assert.Single(Assert.IsAssignableFrom<List<AutomationRunLog>>(ok.Value));
    }

    [Fact]
    public async Task GetRuns_asiri_buyuk_limit_kelepceleniyor()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);
        for (var i = 0; i < 120; i++)
            await SeedRunAsync(db, rule.Id, "Success", DateTime.UtcNow.AddSeconds(-i));

        var controller = NewController(db, userId: "owner-1");
        var ok = Assert.IsType<OkObjectResult>(await controller.GetRuns(rule.Id, 10_000, default));

        Assert.Equal(100, Assert.IsAssignableFrom<List<AutomationRunLog>>(ok.Value).Count);
    }

    // ── "Şimdi test et" (Faz 5) ─────────────────────────────────────────────

    [Fact]
    public async Task TestRule_kurali_test_isaretiyle_calistiriyor()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);

        var executor = new RecordingExecutor();
        var controller = NewController(db, userId: "owner-1", executor);

        Assert.IsType<OkObjectResult>(await controller.TestRule(rule.Id, default));

        var run = Assert.Single(executor.Runs);
        Assert.Equal(rule.Id, run.RuleId);
        Assert.True(run.IsTest);
    }

    [Fact]
    public async Task TestRule_baskasinin_kuralini_CALISTIRMIYOR()
    {
        // Yabancı bir kullanıcı bu uçla başkasının webhook'unu ateşleyebilseydi
        // "test" düğmesi bir tetikleme silahına dönerdi.
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);

        var executor = new RecordingExecutor();
        var controller = NewController(db, userId: "intruder-1", executor);

        Assert.IsType<NotFoundResult>(await controller.TestRule(rule.Id, default));
        Assert.Empty(executor.Runs);
    }

    [Fact]
    public async Task TestRule_hiz_siniri_asilinca_429_donuyor_ve_calistirmiyor()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);

        // Pencere içinde sınır kadar test çalışması zaten var.
        for (var i = 0; i < 5; i++)
            await SeedRunAsync(db, rule.Id, "Success", DateTime.UtcNow.AddSeconds(-i), isTest: true);

        var executor = new RecordingExecutor();
        var controller = NewController(db, userId: "owner-1", executor);

        var result = Assert.IsType<ObjectResult>(await controller.TestRule(rule.Id, default));
        Assert.Equal(429, result.StatusCode);
        Assert.Empty(executor.Runs);
    }

    [Fact]
    public async Task TestRule_eski_test_calismalari_hiz_sinirini_doldurmuyor()
    {
        // Pencere dışındaki kayıtlar sayılsaydı, bir kez test eden kullanıcı
        // o kuralı bir daha hiç test edemezdi.
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);

        for (var i = 0; i < 10; i++)
            await SeedRunAsync(db, rule.Id, "Success", DateTime.UtcNow.AddHours(-2), isTest: true);

        var executor = new RecordingExecutor();
        var controller = NewController(db, userId: "owner-1", executor);

        Assert.IsType<OkObjectResult>(await controller.TestRule(rule.Id, default));
        Assert.Single(executor.Runs);
    }

    [Fact]
    public async Task TestRule_gercek_calismalari_hiz_sinirina_saymiyor()
    {
        // Sık tetiklenen bir kural, elle test edilemez hâle gelmemeli.
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = await SeedRuleAsync(db, project.Id);

        for (var i = 0; i < 20; i++)
            await SeedRunAsync(db, rule.Id, "Success", DateTime.UtcNow.AddSeconds(-i), isTest: false);

        var executor = new RecordingExecutor();
        var controller = NewController(db, userId: "owner-1", executor);

        Assert.IsType<OkObjectResult>(await controller.TestRule(rule.Id, default));
        Assert.Single(executor.Runs);
    }
}
