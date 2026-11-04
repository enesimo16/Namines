using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Namines.Core.Models;

namespace Namines.Flow.Application;

/// <summary>Kuyruğa alınmış bir otomasyon-tetikleme işi.</summary>
public sealed record FlowAutomationJob(
    string ProjectId,
    SchemaDiffResult Diff,
    DatabaseSchema OldSchema,
    DatabaseSchema NewSchema);

/// <summary>
/// Namines Flow'un modül içi sunucu tarafı aksiyon kuyruğu.
/// </summary>
public interface IFlowJobQueue
{
    bool TryEnqueue(FlowAutomationJob job);
    ValueTask<FlowAutomationJob> DequeueAsync(CancellationToken ct);
    int PendingCount { get; }
}

public sealed class FlowJobQueue : IFlowJobQueue
{
    private const int Capacity = 64;

    private readonly Channel<FlowAutomationJob> _channel =
        Channel.CreateBounded<FlowAutomationJob>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    private int _pending;

    public int PendingCount => Volatile.Read(ref _pending);

    public bool TryEnqueue(FlowAutomationJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!_channel.Writer.TryWrite(job)) return false;
        Interlocked.Increment(ref _pending);
        return true;
    }

    public async ValueTask<FlowAutomationJob> DequeueAsync(CancellationToken ct)
    {
        var job = await _channel.Reader.ReadAsync(ct);
        Interlocked.Decrement(ref _pending);
        return job;
    }
}
