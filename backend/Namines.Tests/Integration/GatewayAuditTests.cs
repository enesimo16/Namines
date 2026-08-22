using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Integration;

/// <summary>
/// Gateway denetim kaydı (07 §5).
///
/// <b>Kayıt Gateway'de tutuluyor, üretilen panelde değil — ve bu bir güvenlik
/// kararıdır.</b> Panel müşterinin kendi sunucusunda çalışan, kaynağı ona ait bir
/// uygulama; oradaki bir kaydı silmek ya da hiç yazmamak tamamen mümkün. Denetim
/// kaydının değeri, kaydı tutanın kaydı yapanla aynı taraf OLMAMASINDAN gelir.
/// </summary>
[Collection("Docker")]
public class GatewayAuditTests : IAsyncLifetime
{
    // GatewayApiKeyTests ile aynı desen: GERÇEK PostgreSQL. Bellek içi sağlayıcı
    // sıralama, tip ve kısıt davranışını tam uygulamaz — "en yeniden eskiye"
    // gibi bir iddia orada test edilmiş GÖRÜNÜP gerçekte doğrulanmamış olurdu.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();

        _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options;

        await using var context = new AuthDbContext(_options);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => DockerAvailable.Value ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

    private AuthDbContext Context() => new(_options);

    private static GatewayApiKey Key() => new()
    {
        Id = "k1", ProjectId = "p1", Name = "console", Prefix = "nmn_abc123",
        KeyHash = "hash", CreatedByUserId = "u1",
    };

    [RequiresDockerFact]
    public async Task A_write_is_recorded_with_who_what_and_which_row()
    {
        await using var context = Context();

        await context.RecordAsync(Key(), null, GatewayWriteKind.Update, "users", "42",
            new[] { "email", "note" }, affectedRows: 1, succeeded: true);

        var entry = await context.GatewayAuditEntries.SingleAsync();

        Assert.Equal("p1", entry.ProjectId);
        Assert.Equal("nmn_abc123", entry.ApiKeyPrefix);
        Assert.Equal(GatewayWriteKind.Update, entry.Kind);
        Assert.Equal("users", entry.TableName);
        Assert.Equal("42", entry.RowKey);
        Assert.Equal("email,note", entry.Columns);
        Assert.True(entry.Succeeded);
    }

    [RequiresDockerFact]
    public async Task A_refused_write_is_recorded_too()
    {
        // Yalnızca başarılı yazmaları kaydetmek, denetim kaydını "ne oldu"nun
        // değil "ne işe yaradı"nın listesi yapar; reddedilen bir yazma girişimi
        // çoğu zaman başarılı olandan daha ilgi çekicidir.
        await using var context = Context();

        await context.RecordAsync(Key(), null, GatewayWriteKind.Delete, "users", "7",
            null, affectedRows: 0, succeeded: false);

        var entry = await context.GatewayAuditEntries.SingleAsync();

        Assert.False(entry.Succeeded);
        Assert.Equal(0, entry.AffectedRows);
    }

    [RequiresDockerFact]
    public async Task Written_values_are_never_stored()
    {
        // Yazılan içerik müşterinin verisi — çoğu zaman kişisel veri — ve onu
        // bizim veritabanımıza kopyalamak, tek bir denetim özelliği uğruna yeni
        // bir sızıntı yüzeyi açmak olurdu.
        await using var context = Context();

        await context.RecordAsync(Key(), null, GatewayWriteKind.Create, "users", null,
            new[] { "email" }, affectedRows: 1, succeeded: true);

        var entry = await context.GatewayAuditEntries.SingleAsync();

        Assert.Equal("email", entry.Columns);
        // Modelde değeri tutacak bir alan olmadığını da kilitliyoruz: sonradan
        // "kolaylık olsun" diye eklenmesi, bu kararı sessizce geri alırdı.
        Assert.Null(typeof(GatewayAuditEntry).GetProperty("Values"));
        Assert.Null(typeof(GatewayAuditEntry).GetProperty("NewValues"));
    }

    [RequiresDockerFact]
    public async Task A_session_write_has_a_user_instead_of_a_key()
    {
        await using var context = Context();

        await context.RecordAsync(null, "user-9", GatewayWriteKind.Sql, null, null, null, 3, true);

        var entry = await context.GatewayAuditEntries.SingleAsync();

        Assert.Equal("user-9", entry.ActorUserId);
        Assert.Null(entry.ApiKeyId);
        Assert.Equal("session", entry.ProjectId);
    }

    [RequiresDockerFact]
    public async Task The_trail_is_newest_first_and_capped()
    {
        // Sınırsız bir sorgu, kayıt büyüdükçe yavaşlar ve tek bir istekle
        // sunucuyu meşgul eder.
        await using var context = Context();

        for (var i = 0; i < 10; i++)
        {
            context.GatewayAuditEntries.Add(new GatewayAuditEntry
            {
                ProjectId = "p1", Kind = GatewayWriteKind.Create, TableName = "t" + i,
                CreatedAt = DateTime.UtcNow.AddMinutes(i),
            });
        }
        await context.SaveChangesAsync();

        var trail = await context.AuditTrailAsync("p1", take: 3);

        Assert.Equal(3, trail.Count);
        Assert.Equal("t9", trail[0].TableName);
        Assert.Equal("t7", trail[2].TableName);
    }

    [RequiresDockerFact]
    public async Task An_absurd_page_size_is_clamped_rather_than_honoured()
    {
        await using var context = Context();
        for (var i = 0; i < 3; i++)
            context.GatewayAuditEntries.Add(new GatewayAuditEntry { ProjectId = "p1" });
        await context.SaveChangesAsync();

        Assert.Equal(3, (await context.AuditTrailAsync("p1", take: 100_000)).Count);
        Assert.Single(await context.AuditTrailAsync("p1", take: 0));
    }

    [RequiresDockerFact]
    public async Task One_project_cannot_see_another_projects_trail()
    {
        await using var context = Context();
        context.GatewayAuditEntries.Add(new GatewayAuditEntry { ProjectId = "p1", TableName = "mine" });
        context.GatewayAuditEntries.Add(new GatewayAuditEntry { ProjectId = "p2", TableName = "theirs" });
        await context.SaveChangesAsync();

        var trail = await context.AuditTrailAsync("p1", 100);

        Assert.Single(trail);
        Assert.Equal("mine", trail[0].TableName);
    }
}
