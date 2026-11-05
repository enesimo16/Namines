using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>Saklama temizliği: tek büyük DELETE hem kilit tutar hem WAL şişirir, parti parti silinir.</summary>
public static class BatchedDelete
{
    public const int DefaultBatchSize = 5000;

    public static async Task<int> RunAsync<T>(
        IQueryable<T> expired,
        Expression<Func<T, object?>> orderKey,
        TimeSpan pause,
        CancellationToken ct,
        int batchSize = DefaultBatchSize)
        where T : class
    {
        var total = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var n = await expired.OrderBy(orderKey).Take(batchSize).ExecuteDeleteAsync(ct);
            total += n;
            if (n < batchSize) return total;
            if (pause > TimeSpan.Zero) await Task.Delay(pause, ct);
        }
    }
}
