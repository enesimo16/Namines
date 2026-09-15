using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Namines.Core.Models;

namespace Namines.Infrastructure.Services;

/// <summary>Kuyruğa alınmış bir otomasyon-tetikleme işi.</summary>
public sealed record AutomationJob(
    string ProjectId, SchemaDiffResult Diff, DatabaseSchema OldSchema, DatabaseSchema NewSchema);

/// <summary>
/// Namines Flow'un sunucu tarafı aksiyon kuyruğu (Bölüm 3).
///
/// <see cref="VaultJobQueue"/> ile aynı gerekçeler (istek kapsamlı DbContext,
/// Task.Run yerine kuyruk) — TEK FARK: kuyruk dolduğunda çağıran BEKLEMEDEN
/// false alır ve bunu SESSİZCE loglar, isteği 503'e ÇEVİRMEZ. Otomasyon
/// tetikleme, senkron isteğin (proje kaydetme) yan etkisidir — asıl iş kuyruk
/// yüzünden asla başarısız görünmemeli.
/// </summary>
public interface IAutomationJobQueue
{
    bool TryEnqueue(AutomationJob job);
    ValueTask<AutomationJob> DequeueAsync(CancellationToken ct);
    int PendingCount { get; }
}

public sealed class AutomationJobQueue : IAutomationJobQueue
{
    private const int Capacity = 64;

    private readonly Channel<AutomationJob> _channel =
        Channel.CreateBounded<AutomationJob>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    private int _pending;

    public int PendingCount => Volatile.Read(ref _pending);

    public bool TryEnqueue(AutomationJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!_channel.Writer.TryWrite(job)) return false;
        Interlocked.Increment(ref _pending);
        return true;
    }

    public async ValueTask<AutomationJob> DequeueAsync(CancellationToken ct)
    {
        var job = await _channel.Reader.ReadAsync(ct);
        Interlocked.Decrement(ref _pending);
        return job;
    }
}
