using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
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

    private static AutomationController NewController(AuthDbContext db, string userId)
    {
        var controller = new AutomationController(db)
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
    public async Task GetRules_proje_sahibi_olmayan_kullanici_icin_NotFound_donuyor()
    {
        await using var db = NewContext();
        await SeedProjectAsync(db, ownerId: "owner-1");

        var controller = NewController(db, userId: "intruder-1");

        var result = await controller.GetRules("proj-1", default);

        Assert.IsType<NotFoundResult>(result);
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
            ActionType = "Toast",
            ActionConfigJson = "{}",
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

    [Fact]
    public async Task DeleteRule_var_olan_kurali_kaldiriyor()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db, ownerId: "owner-1");
        var rule = new AutomationRule
        {
            ProjectId = project.Id,
            TriggerType = "TableAdded",
            ActionType = "Toast",
            ActionConfigJson = "{}",
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
}
