using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Namines.Vault.Application;

/// <summary>Kuyruğa alınmış bir yedekleme işi.</summary>
/// <param name="BackupId">Zaten <c>Running</c> durumuyla oluşturulmuş kaydın kimliği.</param>
/// <param name="ProjectId">Yedeği alınacak proje.</param>
public sealed record VaultBackupJob(string BackupId, string ProjectId);

/// <summary>
/// Vault yedekleme işlerinin modül içi kuyruk sözleşmesi (PERF-003 / B-35).
/// </summary>
public interface IVaultJobQueue
{
    bool TryEnqueue(VaultBackupJob job);
    ValueTask<VaultBackupJob> DequeueAsync(CancellationToken ct);
    int PendingCount { get; }
}

public sealed class VaultJobQueue : IVaultJobQueue
{
    private const int Capacity = 32;

    private readonly Channel<VaultBackupJob> _channel =
        Channel.CreateBounded<VaultBackupJob>(new BoundedChannelOptions(Capacity)
        {
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
