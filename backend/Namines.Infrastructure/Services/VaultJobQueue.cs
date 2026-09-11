using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Namines.Infrastructure.Services;

/// <summary>Kuyruğa alınmış bir yedekleme işi.</summary>
/// <param name="BackupId">Zaten <c>Running</c> durumuyla oluşturulmuş kaydın kimliği.</param>
/// <param name="ProjectId">Yedeği alınacak proje.</param>
public sealed record VaultBackupJob(string BackupId, string ProjectId);

/// <summary>
/// Yedekleme işlerinin kuyruğu (PERF-003 / B-35).
///
/// <b>Neden bir kuyruk, <c>Task.Run</c> değil:</b> İstek kapsamındaki
/// <c>DbContext</c>, yanıt döndüğünde atılır. <c>Task.Run</c> ile başlatılan
/// bir iş o context'i kullanmaya devam ederse <c>ObjectDisposedException</c>
/// alır — ve bunu yalnızca yavaş yedeklerde, yani üretimde görürsünüz.
/// Kuyruğu tüketen <see cref="VaultBackupWorker"/> KENDİ kapsamını açıyor.
///
/// <b>Neden sınırlı kapasite:</b> Sınırsız bir kuyruk, arka arkaya gelen
/// isteklerde belleği ve Docker'ı doyurur. Kapasite dolduğunda uç 503 dönüp
/// "şu an meşgul" diyor — sessizce sıraya sokup dakikalar sonra başlamaktan
/// dürüst.
/// </summary>
public interface IVaultJobQueue
{
    /// <summary>Kuyruğa ekler. Kuyruk doluysa <c>false</c> döner (beklemez).</summary>
    bool TryEnqueue(VaultBackupJob job);

    /// <summary>İşçi için: sıradaki işi bekler.</summary>
    ValueTask<VaultBackupJob> DequeueAsync(CancellationToken ct);

    /// <summary>Bekleyen iş sayısı — sağlık/teşhis için.</summary>
    int PendingCount { get; }
}

public sealed class VaultJobQueue : IVaultJobQueue
{
    /// <summary>
    /// Eşzamanlı bekleyen iş sınırı.
    ///
    /// Yedekleme Docker konteyneri açıyor ve disk yazıyor; kuyruğun uzun olması
    /// bir kazanç değil, yalnızca daha uzun bir yalan olur ("kabul edildi" deyip
    /// 20 dakika hiç başlamamak).
    /// </summary>
    private const int Capacity = 32;

    private readonly Channel<VaultBackupJob> _channel =
        Channel.CreateBounded<VaultBackupJob>(new BoundedChannelOptions(Capacity)
        {
            // `Wait` modunda `TryWrite` kuyruk doluyken BEKLEMEZ, `false` doner --
            // denetleyici hemen 503 dönebilsin diye gereken tam da bu.
            //
            // `DropWrite` DEGIL, ve bu bir hataydi: o modda `TryWrite` gelen isi
            // ATIP `true` donuyor. Yani dolu kuyruk yedekleri SESSIZCE yutuyordu
            // -- kayit `Running` kaliyor, kimse calistirmiyor, arayuz sonsuza
            // kadar "suruyor" gosteriyordu. `VaultJobQueueTests` bunu yakaladi.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    private int _pending;

    public int PendingCount => Volatile.Read(ref _pending);

    public bool TryEnqueue(VaultBackupJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!_channel.Writer.TryWrite(job)) return false;

        Interlocked.Increment(ref _pending);
        return true;
    }

    public async ValueTask<VaultBackupJob> DequeueAsync(CancellationToken ct)
    {
        var job = await _channel.Reader.ReadAsync(ct);
        Interlocked.Decrement(ref _pending);
        return job;
    }
}
