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
/// </summary>
public sealed class AsyncProgress<T> : IProgress<T>
{
    private readonly Func<T, Task> _callback;

    public AsyncProgress(Func<T, Task> callback) => _callback = callback;

    public void Report(T value) => _callback(value).GetAwaiter().GetResult();
}
