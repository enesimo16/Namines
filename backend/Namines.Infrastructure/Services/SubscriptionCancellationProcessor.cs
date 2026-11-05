using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Abonelik iptali outbox'ını işler. Satır, kullanıcı silmeyle aynı işlemde yazılır; bu yüzden süreç
/// commit ile Stripe arasında ölse de iptal kaybolmaz.
///
/// Sahiplenme kısa bir işlemdir (SKIP LOCKED + sonraki deneme zamanını ileri al): Stripe çağrısı
/// sürerken satır kilidi ve bağlantı tutulmaz. Ölen işçinin satırı süre dolunca yeniden alınır; iptal
/// idempotent (<c>resource_missing</c> "tamam" sayılır), yani çift çağrı zarar vermez.
/// </summary>
public sealed class SubscriptionCancellationProcessor
{
    /// <summary>Bu denemeden sonra her başarısızlık ERROR loglar ve Admin'de "takılı" görünür.</summary>
    public const int AlertAfterAttempts = 8;

    /// <summary>İstemci bağlantıyı kesse de ilk deneme bitebilsin diye istekten bağımsız üst sınır.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);

    private readonly AuthDbContext _db;
    private readonly ISubscriptionCanceller? _canceller;
    private readonly ILogger _logger;

    public SubscriptionCancellationProcessor(AuthDbContext db, ISubscriptionCanceller? canceller, ILogger logger)
    {
        _db = db;
        _canceller = canceller;
        _logger = logger;
    }

    /// <summary>Art arda başarısızlıkta 2, 4, 8... dakika, en fazla 6 saat.</summary>
    public static TimeSpan BackoffFor(int attempts)
    {
        var minutes = Math.Pow(2, Math.Clamp(attempts, 1, 20));
        return TimeSpan.FromMinutes(Math.Min(minutes, MaxBackoff.TotalMinutes));
    }

    /// <summary>Belirli satırı hemen dener (hesap silmenin commit sonrası ilk denemesi).</summary>
    public async Task<bool> AttemptAsync(string id, DateTime utcNow)
    {
        var claimed = await ClaimAsync(id, utcNow, CancellationToken.None);
        return claimed is not null && await RunAsync(claimed);
    }

    /// <summary>Vadesi gelen satırları işler; denenen satır sayısını döndürür.</summary>
    public async Task<int> ProcessDueAsync(DateTime utcNow, int batchSize, CancellationToken ct)
    {
        var processed = 0;
        while (processed < batchSize && !ct.IsCancellationRequested)
        {
            var claimed = await ClaimAsync(null, utcNow, ct);
            if (claimed is null) break;
            await RunAsync(claimed);
            processed++;
        }
        return processed;
    }

    private Task<PendingSubscriptionCancellation?> ClaimAsync(string? id, DateTime utcNow, CancellationToken ct)
    {
        // SKIP LOCKED yalnızca PostgreSQL'de; SQLite testlerinde eşzamanlı işçi yok.
        var lockClause = _db.Database.IsNpgsql() ? " FOR UPDATE SKIP LOCKED" : string.Empty;
        const string open = """SELECT * FROM "PendingSubscriptionCancellations" WHERE "CompletedAt" IS NULL""";

        var strategy = _db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var rows = id is null
                ? await _db.PendingSubscriptionCancellations
                    .FromSqlRaw(open + """ AND "NextAttemptAt" <= {0} ORDER BY "NextAttemptAt" LIMIT 1""" + lockClause, utcNow)
                    .ToListAsync(ct)
                : await _db.PendingSubscriptionCancellations
                    .FromSqlRaw(open + """ AND "Id" = {0} LIMIT 1""" + lockClause, id)
                    .ToListAsync(ct);

            var row = rows.FirstOrDefault();
            if (row is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            row.Attempts++;
            row.LastAttemptAt = utcNow;
            row.NextAttemptAt = utcNow + BackoffFor(row.Attempts);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return row;
        });
    }

    private async Task<bool> RunAsync(PendingSubscriptionCancellation row)
    {
        var canceled = false;
        if (_canceller is not null)
        {
            // İstek token'ı bilerek kullanılmıyor: istemci kopunca para kesilmeye devam etmemeli.
            using var timeout = new CancellationTokenSource(AttemptTimeout);
            try
            {
                canceled = await _canceller.CancelAsync(row.SubscriptionId, timeout.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stripe iptal denemesi basarisiz (kayit {Id}, deneme {Attempts}).", row.Id, row.Attempts);
            }
        }

        if (canceled)
        {
            var now = DateTime.UtcNow;
            await _db.PendingSubscriptionCancellations
                .Where(c => c.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CompletedAt, (DateTime?)now), CancellationToken.None);
            _db.ChangeTracker.Clear();
            return true;
        }

        if (row.Attempts >= AlertAfterAttempts)
        {
            _logger.LogError(
                "Stripe aboneligi {Attempts} denemedir iptal edilemedi (kayit {Id}, abonelik {Sub}); musteri ucretlendirilmeye devam edebilir, elle bakin.",
                row.Attempts, row.Id, row.SubscriptionId);
        }
        return false;
    }
}

/// <summary>Outbox'ı dakikada bir tarar; ilk denemesi başarısız olan ya da süreci ölen iptalleri tamamlar.</summary>
public sealed class SubscriptionCancellationWorker : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionCancellationWorker> _logger;

    public SubscriptionCancellationWorker(IServiceScopeFactory scopeFactory, ILogger<SubscriptionCancellationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                using var scope = _scopeFactory.CreateScope();
                await new SubscriptionCancellationProcessor(
                        scope.ServiceProvider.GetRequiredService<AuthDbContext>(),
                        scope.ServiceProvider.GetService<ISubscriptionCanceller>(),
                        _logger)
                    .ProcessDueAsync(DateTime.UtcNow, BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Bir tur patlasa da satırlar açık kalır, sonraki tur yeniden dener.
                _logger.LogError(ex, "Abonelik iptal kuyrugu islenemedi.");
            }
        }
    }
}
