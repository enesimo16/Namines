using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// Denetim kaydına BAŞKASININ proje kimliğini yazdırmak — docs/last-review H-5.
///
/// <b>Bulunan açık:</b> bu uçlar bağlantı dizesini çağırandan alıyor, yani çağıran
/// projeye ait bir şey yaptığını hiç kanıtlamak zorunda değil — ama yazdıkları
/// denetim satırı istekteki <c>ProjectId</c> ile etiketleniyordu. Herhangi bir
/// kullanıcı kendi veritabanına zararsız bir betik çalıştırıp satırı BAŞKASININ
/// projesine yazdırabiliyordu: kurbanın Logs/Analytics ekranı, saldırgan kontrollü
/// ve "yıkıcı anahtar kelime içeriyor" işaretli sahte kayıtlarla doluyordu.
/// Denetim kaydı, sahibinin güvenemediği bir kayda dönüşüyordu.
/// </summary>
public sealed class AuditProjectIdSpoofingTests : IAsyncLifetime
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

    /// <summary>Kurbanın projesi + ilgisiz bir saldırgan hesabı.</summary>
    private static async Task SeedAsync(AuthDbContext db)
    {
        db.Users.Add(new ApplicationUser { Id = "victim", UserName = "victim" });
        db.Users.Add(new ApplicationUser { Id = "attacker", UserName = "attacker" });

        db.CloudProjects.Add(new CloudProject
        {
            Id = "victim-proj", Name = "victim", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "victim",
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Hiç çalıştırmayan, hep başarılı diyen sahte yürütücü.</summary>
    private sealed class NoopExecutor : IDatabaseExecutor
    {
        public Task<ExecutionResult> ExecuteScriptAsync(
            string connectionString, string ddlScript, DatabaseType dbType,
            System.Threading.CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExecutionResult(true, null, 1));

        public Task<bool> TestConnectionAsync(
            string connectionString, DatabaseType dbType,
            System.Threading.CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private static DatabaseExecutorController NewController(AuthDbContext db, string userId) =>
        new(new NoopExecutor(), db, NullLogger<DatabaseExecutorController>.Instance)
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

    [Fact]
    public async Task Yabanci_kullanici_KURBANIN_projesine_denetim_satiri_YAZDIRAMIYOR()
    {
        await using var db = NewContext();
        await SeedAsync(db);

        var controller = NewController(db, "attacker");
        var result = await controller.ExecuteScript(
            new ExecutorRequest
            {
                ConnectionString = "Host=attacker-db;Database=x;Username=u;Password=p",
                Script = "DROP TABLE everything;",
                DbType = DatabaseType.PostgreSQL,
                ProjectId = "victim-proj",
            },
            default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);

        // Kurbanın kaydında HİÇBİR satır olmamalı.
        await using var verify = NewContext();
        Assert.Empty(await verify.SqlExecutionAudits
            .Where(a => a.ProjectId == "victim-proj").ToListAsync());
    }

    [Fact]
    public async Task Kendi_projesine_yazmak_CALISMAYA_DEVAM_EDIYOR()
    {
        // Düzeltme meşru kullanımı kırmamalı.
        await using var db = NewContext();
        await SeedAsync(db);

        var controller = NewController(db, "victim");
        var result = await controller.ExecuteScript(
            new ExecutorRequest
            {
                ConnectionString = "Host=my-db;Database=x;Username=u;Password=p",
                Script = "CREATE TABLE t (x INT);",
                DbType = DatabaseType.PostgreSQL,
                ProjectId = "victim-proj",
            },
            default);

        Assert.IsType<OkObjectResult>(result);

        await using var verify = NewContext();
        var entry = Assert.Single(await verify.SqlExecutionAudits.ToListAsync());
        Assert.Equal("victim-proj", entry.ProjectId);
        Assert.Equal("victim", entry.UserId);
    }

    [Fact]
    public async Task ProjectId_HIC_verilmediyse_calisma_yine_kaydediliyor()
    {
        // Studio'nun canvas çağrıları proje bilgisi taşımıyor; onları reddetmek
        // düzeltmeyi bir kırılmaya çevirirdi. Kayıt yine yazılıyor, projesiz.
        await using var db = NewContext();
        await SeedAsync(db);

        var controller = NewController(db, "attacker");
        var result = await controller.ExecuteScript(
            new ExecutorRequest
            {
                ConnectionString = "Host=my-db;Database=x;Username=u;Password=p",
                Script = "CREATE TABLE t (x INT);",
                DbType = DatabaseType.PostgreSQL,
                ProjectId = null,
            },
            default);

        Assert.IsType<OkObjectResult>(result);

        await using var verify = NewContext();
        var entry = Assert.Single(await verify.SqlExecutionAudits.ToListAsync());
        Assert.Null(entry.ProjectId);
    }

    [Fact]
    public async Task Var_olmayan_proje_kimligi_de_REDDEDILIYOR()
    {
        // "Kayıt yok" durumunu izin saymak fail-open olurdu.
        await using var db = NewContext();
        await SeedAsync(db);

        var controller = NewController(db, "attacker");
        var result = await controller.ExecuteScript(
            new ExecutorRequest
            {
                ConnectionString = "Host=my-db;Database=x;Username=u;Password=p",
                Script = "SELECT 1;",
                DbType = DatabaseType.PostgreSQL,
                ProjectId = "does-not-exist",
            },
            default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }
}
