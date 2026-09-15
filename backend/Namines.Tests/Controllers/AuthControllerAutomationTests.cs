using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Controllers;

// SQLite in-memory bağlantı deseni — AutomationExecutorTests.cs ile aynı: EF
// InMemory sağlayıcısı bu projede referanslı değil, ve CloudProject ->
// ApplicationUser FK'sı ZORUNLU (AuthDbContext.OnModelCreating), bu yüzden
// SQLite kullanılıyor. UserManager burada bir sahte (mock) DEĞİL — gerçek
// UserStore<ApplicationUser> üzerinden, aynı AuthDbContext'e karşı çalışıyor;
// bu depoda UserManager'ı sahtelemenin (Moq vb.) hiçbir emsali yok, en az
// varsayım gerektiren yol gerçek Identity store'unu SQLite'a bağlamak.
public sealed class AuthControllerAutomationTests : IAsyncLifetime
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

    private static UserManager<ApplicationUser> NewUserManager(AuthDbContext db)
    {
        var store = new UserStore<ApplicationUser>(db);
        return new UserManager<ApplicationUser>(
            store,
            optionsAccessor: null!,
            passwordHasher: new PasswordHasher<ApplicationUser>(),
            userValidators: new List<IUserValidator<ApplicationUser>>(),
            passwordValidators: new List<IPasswordValidator<ApplicationUser>>(),
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: null!,
            logger: NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static AuthController NewController(
        AuthDbContext db, UserManager<ApplicationUser> userManager, IAutomationJobQueue queue, string userId)
    {
        var controller = new AuthController(
            userManager,
            db,
            new ConfigurationBuilder().Build(),
            // CalculateDiffAsync yalnızca DDL üretmez, GroqAIService'e hiç
            // dokunmaz — bu yüzden burada null geçmek güvenli (bkz.
            // MigrationService.CalculateDiffAsync).
            new MigrationService(null!),
            queue,
            NullLogger<AuthController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth")),
            },
        };
        return controller;
    }

    /// <summary>Her zaman false dönen sahte kuyruk — dolu kuyruk senaryosunu simüle eder.</summary>
    private sealed class AlwaysFullQueue : IAutomationJobQueue
    {
        public bool TryEnqueue(AutomationJob job) => false;
        public ValueTask<AutomationJob> DequeueAsync(CancellationToken ct) => throw new NotSupportedException();
        public int PendingCount => 0;
    }

    /// <summary>Kuyruğa atılan işleri kaydeden sahte kuyruk.</summary>
    private sealed class RecordingQueue : IAutomationJobQueue
    {
        public List<AutomationJob> Jobs { get; } = new();
        public bool TryEnqueue(AutomationJob job) { Jobs.Add(job); return true; }
        public ValueTask<AutomationJob> DequeueAsync(CancellationToken ct) => throw new NotSupportedException();
        public int PendingCount => Jobs.Count;
    }

    private async Task SeedUserAsync(string userId)
    {
        await using var seedDb = NewContext();
        using var seedUserManager = NewUserManager(seedDb);
        var user = new ApplicationUser { Id = userId, UserName = userId, Email = $"{userId}@example.com" };
        Assert.True((await seedUserManager.CreateAsync(user)).Succeeded);
    }

    private static List<SyncProjectDto> SyncPayload(string schemaJson) => new()
    {
        new()
        {
            Id = "proj-1",
            Name = "Proje",
            DbType = "PostgreSQL",
            SchemaJson = schemaJson,
            NodePositionsJson = "{}",
        },
    };

    private const string EmptySchema = "{\"tables\":[]}";
    private const string OneTableSchema = "{\"tables\":[{\"id\":\"t1\",\"name\":\"orders\",\"columns\":[]}]}";

    [Fact]
    public async Task Otomasyon_kurali_olmayan_proje_icin_diff_hic_hesaplanmiyor()
    {
        // I1: diff+enqueue döngüsü ESKİDEN her projede koşulsuz çalışıyordu;
        // kullanıcıların çoğunun hiç kuralı yok.
        const string userId = "user-norules";
        await SeedUserAsync(userId);

        await using var db = NewContext();
        using var userManager = NewUserManager(db);
        var queue = new RecordingQueue();
        var controller = NewController(db, userManager, queue, userId);

        Assert.IsType<OkObjectResult>(await controller.SyncProjects(SyncPayload(EmptySchema)));
        Assert.IsType<OkObjectResult>(await controller.SyncProjects(SyncPayload(OneTableSchema)));

        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public async Task Acik_kurali_olan_proje_icin_diff_hala_kuyruga_atiliyor()
    {
        const string userId = "user-withrules";
        await SeedUserAsync(userId);

        await using var db = NewContext();
        using var userManager = NewUserManager(db);
        var queue = new RecordingQueue();
        var controller = NewController(db, userManager, queue, userId);

        // İlk sync projeyi oluşturur (diff dalı yalnızca GÜNCELLEMEDE çalışır).
        Assert.IsType<OkObjectResult>(await controller.SyncProjects(SyncPayload(EmptySchema)));

        await using (var ruleDb = NewContext())
        {
            ruleDb.AutomationRules.Add(new Namines.Core.Models.AutomationRule
            {
                ProjectId = "proj-1",
                ScopeTableId = null,
                TriggerType = "TableAdded",
                ActionType = "Webhook",
                ActionConfigJson = "{}",
                Enabled = true,
            });
            await ruleDb.SaveChangesAsync();
        }

        Assert.IsType<OkObjectResult>(await controller.SyncProjects(SyncPayload(OneTableSchema)));

        var job = Assert.Single(queue.Jobs);
        Assert.Equal("proj-1", job.ProjectId);
    }

    [Fact]
    public async Task Kapali_kurali_olan_proje_icin_diff_atlaniyor()
    {
        const string userId = "user-disabledrule";
        await SeedUserAsync(userId);

        await using var db = NewContext();
        using var userManager = NewUserManager(db);
        var queue = new RecordingQueue();
        var controller = NewController(db, userManager, queue, userId);

        Assert.IsType<OkObjectResult>(await controller.SyncProjects(SyncPayload(EmptySchema)));

        await using (var ruleDb = NewContext())
        {
            ruleDb.AutomationRules.Add(new Namines.Core.Models.AutomationRule
            {
                ProjectId = "proj-1",
                TriggerType = "TableAdded",
                ActionType = "Webhook",
                ActionConfigJson = "{}",
                Enabled = false,
            });
            await ruleDb.SaveChangesAsync();
        }

        Assert.IsType<OkObjectResult>(await controller.SyncProjects(SyncPayload(OneTableSchema)));

        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public async Task Kuyruk_dolu_olsa_bile_SyncProjects_basariyla_donuyor()
    {
        const string userId = "user-1";

        // Arrange: kullanıcı + bir proje kaydet.
        await using (var seedDb = NewContext())
        {
            using var seedUserManager = NewUserManager(seedDb);
            var user = new ApplicationUser { Id = userId, UserName = "user-1", Email = "user-1@example.com" };
            var createResult = await seedUserManager.CreateAsync(user);
            Assert.True(createResult.Succeeded);
        }

        await using var db = NewContext();
        using var userManager = NewUserManager(db);
        var controller = NewController(db, userManager, new AlwaysFullQueue(), userId);

        var firstSync = new List<SyncProjectDto>
        {
            new()
            {
                Id = "proj-1",
                Name = "Proje",
                DbType = "PostgreSQL",
                SchemaJson = "{\"tables\":[]}",
                NodePositionsJson = "{}",
            },
        };

        var firstResult = await controller.SyncProjects(firstSync);
        Assert.IsType<OkObjectResult>(firstResult);

        // I1'den SONRA: diff yalnızca AÇIK kuralı olan projeler için
        // hesaplanıyor, bu yüzden bu test "dolu kuyruk" dalına girebilmek için
        // artık bir kural SEEDLEMEK ZORUNDA — yoksa sessizce hiçbir şeyi
        // sınamayan bir teste dönüşürdü.
        await using (var ruleDb = NewContext())
        {
            ruleDb.AutomationRules.Add(new Namines.Core.Models.AutomationRule
            {
                ProjectId = "proj-1",
                TriggerType = "TableAdded",
                ActionType = "Webhook",
                ActionConfigJson = "{}",
                Enabled = true,
            });
            await ruleDb.SaveChangesAsync();
        }

        // Act: aynı projeyi değiştirip tekrar sync çağır — bu, existing != null
        // dalını (diff hesaplama + kuyruğa atma) tetikler. Kuyruk her zaman
        // dolu (AlwaysFullQueue), ama bu senkron isteğin başarısını
        // ETKİLEMEMELİ.
        var secondSync = new List<SyncProjectDto>
        {
            new()
            {
                Id = "proj-1",
                Name = "Proje",
                DbType = "PostgreSQL",
                SchemaJson = "{\"tables\":[{\"id\":\"t1\",\"name\":\"orders\",\"columns\":[]}]}",
                NodePositionsJson = "{}",
            },
        };

        var secondResult = await controller.SyncProjects(secondSync);

        // Assert: yanıt yine 200 OK / "Sync successful." — kuyruk dolu olması
        // proje kaydetmeyi düşürmüyor.
        var ok = Assert.IsType<OkObjectResult>(secondResult);
        var message = ok.Value?.GetType().GetProperty("Message")?.GetValue(ok.Value) as string;
        Assert.Equal("Sync successful.", message);
    }
}
