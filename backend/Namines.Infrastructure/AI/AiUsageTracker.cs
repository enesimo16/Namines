using System.Threading;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.AI;

/// <summary>
/// <see cref="IAiUsageTracker"/>'ın istek kapsamlı (scoped) uygulaması.
///
/// <b>Thread-safe:</b> şema hattı bir istekte birden çok sağlayıcı çağrısı
/// yapıyor (draft → inspect → repair) ve bunlar ileride paralelleşebilir.
/// <c>Interlocked</c> kullanmak, iki turun birbirinin artışını ezip harcamayı
/// eksik göstermesini engelliyor — eksik ölçüm, tam da düzeltmeye çalıştığımız
/// hatanın ta kendisi olurdu.
/// </summary>
public sealed class AiUsageTracker : IAiUsageTracker
{
    private int _total;
    private int _measurements;

    public void Record(int totalTokens)
    {
        if (totalTokens <= 0) return;
        Interlocked.Add(ref _total, totalTokens);
        Interlocked.Increment(ref _measurements);
    }

    public int TotalTokens => Volatile.Read(ref _total);

    public bool HasMeasurement => Volatile.Read(ref _measurements) > 0;
}
