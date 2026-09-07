using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Integration;

/// <summary>
/// Namines Desk — Analytics (D7, 07-ANALYTICS.md).
///
/// <b>Bu testlerin varlık sebebi:</b> zaman-kovası (bucket) hesaplaması ham SQL
/// ile yapılıyor (Npgsql EF Core 8'de <c>date_trunc</c>'ın bir LINQ çevirisi
/// yok) — ham SQL'in derlenmesi onun DOĞRU gruplaması demek değil. Kabul
/// kriteri 2 ("toplamlar psql sayımıyla birebir") burada gerçek PostgreSQL'e
/// karşı doğrulanıyor, bellek içi sağlayıcıyla değil.
/// </summary>
[Collection("Docker")]
public class GatewayAnalyticsTests : IAsyncLifetime
{
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

    [RequiresDockerFact]
    public async Task Buckets_and_totals_match_a_hand_counted_seed()
    {
        await using var context = Context();

        var day1 = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

        // Bilinçli, elle sayılabilir bir dağılım: gün 1'de 2 Create + 1 Update
        // (biri BAŞARISIZ), gün 2'de 1 Delete. Başka bir projenin (p2) kaydı da
        // eklenir — sızmadığını doğrulamak için.
        context.GatewayAuditEntries.AddRange(
            new GatewayAuditEntry { ProjectId = "p1", Kind = GatewayWriteKind.Create, TableName = "customers", AffectedRows = 1, Succeeded = true, CreatedAt = day1 },
            new GatewayAuditEntry { ProjectId = "p1", Kind = GatewayWriteKind.Create, TableName = "customers", AffectedRows = 1, Succeeded = true, CreatedAt = day1.AddHours(1), ActorUserId = "u1" },
            new GatewayAuditEntry { ProjectId = "p1", Kind = GatewayWriteKind.Update, TableName = "vehicles", AffectedRows = 0, Succeeded = false, CreatedAt = day1.AddHours(2), ApiKeyPrefix = "nmn_abc" },
            new GatewayAuditEntry { ProjectId = "p1", Kind = GatewayWriteKind.Delete, TableName = "customers", AffectedRows = 1, Succeeded = true, CreatedAt = day2 },
            new GatewayAuditEntry { ProjectId = "p2", Kind = GatewayWriteKind.Create, TableName = "other", AffectedRows = 1, Succeeded = true, CreatedAt = day1 });
        await context.SaveChangesAsync();

        var result = await context.AnalyticsAsync("p1", day1.AddDays(-1), day2.AddDays(1), "day");

        Assert.Equal(4, result.TotalWrites); // p2 hariç
        Assert.Equal(0.75, result.SuccessRate, precision: 2); // 3/4 başarılı
        Assert.Equal(3, result.TotalAffectedRows);
        Assert.Equal(1, result.HumanCount); // yalnızca ActorUserId dolu olan
        Assert.Equal(1, result.ApplicationCount); // yalnızca ApiKeyPrefix dolu olan

        Assert.Equal(2, result.Buckets.Count); // gün 1 ve gün 2
        var bucket1 = result.Buckets.Single(b => b.BucketStart.Date == day1.Date);
        Assert.Equal(2, bucket1.Create);
        Assert.Equal(1, bucket1.Update);
        Assert.Equal(0, bucket1.Delete);
        var bucket2 = result.Buckets.Single(b => b.BucketStart.Date == day2.Date);
        Assert.Equal(1, bucket2.Delete);

        var topCustomers = result.TopTables.Single(t => t.TableName == "customers");
        Assert.Equal(3, topCustomers.Count); // 2 create + 1 delete
    }

    [RequiresDockerFact]
    public async Task Hour_bucket_splits_the_same_day_into_separate_buckets()
    {
        await using var context = Context();
        var baseTime = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

        context.GatewayAuditEntries.AddRange(
            new GatewayAuditEntry { ProjectId = "p1", Kind = GatewayWriteKind.Create, AffectedRows = 1, Succeeded = true, CreatedAt = baseTime },
            new GatewayAuditEntry { ProjectId = "p1", Kind = GatewayWriteKind.Create, AffectedRows = 1, Succeeded = true, CreatedAt = baseTime.AddHours(3) });
        await context.SaveChangesAsync();

        var result = await context.AnalyticsAsync("p1", baseTime.AddHours(-1), baseTime.AddHours(4), "hour");

        Assert.Equal(2, result.Buckets.Count);
    }

    [RequiresDockerFact]
    public async Task Empty_range_reports_zero_not_an_error()
    {
        await using var context = Context();
        var result = await context.AnalyticsAsync("p1", DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, "day");

        Assert.Equal(0, result.TotalWrites);
        Assert.Equal(0.0, result.SuccessRate);
        Assert.Empty(result.Buckets);
        Assert.Empty(result.TopTables);
    }
}
