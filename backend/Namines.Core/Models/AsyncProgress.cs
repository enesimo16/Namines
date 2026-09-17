namespace Namines.Core.Models;

/// <summary>
/// <see cref="IProgress{T}"/>'in asenkron bir geri çağırmayı senkron
/// arayüzün arkasına sarması.
///
/// <b>Neden gerekli:</b> standart <see cref="Progress{T}"/> raporları
/// yakaladığı <c>SynchronizationContext</c>'e (varsa) kuyruklar ve ne zaman
/// işleneceğini garanti etmez — ASP.NET Core'da context olmadığı için sıra
/// belirsizleşir. Üretim ekranına akan adımların **sırayla ve tamamlanmadan
/// bir sonrakine geçmeden** yazılması gerekiyor (bkz.
/// second-phase/04-LOADING-EKRANI.md), aksi hâlde istemci adımları karışık
/// sırada görebilir. Bu sınıf her raporu senkron olarak bekliyor.
///
/// <b>Sıralılık, ÇAĞIRANIN THREAD'İ ile sınırlı değil.</b> Bu sınıf tek bir
/// çağırandan sırayla gelen raporları senkron bekletiyordu; ama parçalı
/// (chunked) şema üretimi <c>Task.WhenAll</c> ile BİRDEN ÇOK arka plan
/// görevinden AYNI ANDA <see cref="Report"/> çağırabiliyor — bu durumda
/// "her rapor sırayla ve tamamlanmadan bir sonrakine geçmeden" sözü, her
/// görev kendi thread'inde ayrı ayrı doğru olsa da, GÖREVLER ARASI yanlış
/// olur: iki thread aynı anda <c>_callback</c>'in içine (ör. SSE
/// yanıtına yazan kod) girebilir. Bir <see cref="System.Threading.SemaphoreSlim"/>
/// bu sınıfın kendi belgelediği sıralılık sözünü, çağıranın sayısından
/// bağımsız olarak garanti ediyor.
/// </summary>
public sealed class AsyncProgress<T> : IProgress<T>
{
    private readonly Func<T, Task> _callback;
    private readonly System.Threading.SemaphoreSlim _gate = new(1, 1);

    public AsyncProgress(Func<T, Task> callback) => _callback = callback;

    public void Report(T value)
    {
        _gate.Wait();
        try
        {
            _callback(value).GetAwaiter().GetResult();
        }
        finally
        {
            _gate.Release();
        }
    }
}
