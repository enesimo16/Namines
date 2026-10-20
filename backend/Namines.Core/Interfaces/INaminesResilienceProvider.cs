using System;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Core.Interfaces;

/// <summary>
/// Harici servisleri ve ağır işlemleri (AI sağlayıcıları, Cloud DB API'leri, Docker daemon)
/// Polly v8 dayanıklılık (resilience) hatları arkasında yürüten merkezi sağlayıcı arayüzü.
/// </summary>
public interface INaminesResilienceProvider
{
    /// <summary>
    /// Belirtilen resilience pipeline'ı üzerinden asenkron bir eylem yürütür.
    /// </summary>
    Task ExecuteAsync(
        string pipelineName,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Belirtilen resilience pipeline'ı üzerinden asenkron ve sonuç döndüren bir eylem yürütür.
    /// </summary>
    Task<T> ExecuteAsync<T>(
        string pipelineName,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);
}
