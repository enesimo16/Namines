using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Namines.Core.Models;
using Namines.Flow.Application;

namespace Namines.Infrastructure.Services;

/// <summary>Kuyruğa alınmış bir otomasyon-tetikleme işi.</summary>
public sealed record AutomationJob(
    string ProjectId, SchemaDiffResult Diff, DatabaseSchema OldSchema, DatabaseSchema NewSchema);

/// <summary>
/// Namines Flow'un sunucu tarafı aksiyon kuyruğu (Bölüm 3).
/// Üretimde <see cref="IFlowJobQueue"/> sözleşmesine devreder;
/// doğrudan örneklenen birim testleri için kendi kanalını da destekler.
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

    private readonly IFlowJobQueue? _flowQueue;
    private readonly Channel<AutomationJob>? _fallbackChannel;
    private int _pending;

    public AutomationJobQueue()
    {
        _fallbackChannel = Channel.CreateBounded<AutomationJob>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    [ActivatorUtilitiesConstructor]
    public AutomationJobQueue(IFlowJobQueue flowQueue)
    {
        _flowQueue = flowQueue ?? throw new ArgumentNullException(nameof(flowQueue));
    }

    public int PendingCount => _flowQueue is not null
        ? _flowQueue.PendingCount
        : Volatile.Read(ref _pending);

    public bool TryEnqueue(AutomationJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (_flowQueue is not null)
        {
            return _flowQueue.TryEnqueue(new FlowAutomationJob(
                job.ProjectId, job.Diff, job.OldSchema, job.NewSchema));
        }

        if (!_fallbackChannel!.Writer.TryWrite(job)) return false;
        Interlocked.Increment(ref _pending);
        return true;
    }

    public async ValueTask<AutomationJob> DequeueAsync(CancellationToken ct)
    {
        if (_flowQueue is not null)
        {
            var flowJob = await _flowQueue.DequeueAsync(ct);
            return new AutomationJob(flowJob.ProjectId, flowJob.Diff, flowJob.OldSchema, flowJob.NewSchema);
        }

        var job = await _fallbackChannel!.Reader.ReadAsync(ct);
        Interlocked.Decrement(ref _pending);
        return job;
    }
}
