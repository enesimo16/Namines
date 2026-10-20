using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Core.Interfaces;

/// <summary>
/// Sunucu-taraflı PostHog ürün analitiği ve özellik bayrakları (feature flags) servisi.
/// (docs/new-phase/21-OBSERVABILITY.md §8)
///
/// <b>Kota Koruma Prensibi:</b>
/// PostHog ücretsiz katmanında 1.000.000 aylık etkinlik sınırı vardır.
/// Bu servis yalnızca sunucu tarafında gerçekleşen ve kritik iş değerine sahip olaylar
/// (veritabanı sağlama, migrasyon uygulama, kota aşımı, şema üretimi) için kullanılır.
///
/// API anahtarı yapılandırılmamışsa sessizce no-op (fail-open) çalışır, hiçbir iş mantığını
/// engellemez veya istisna fırlatmaz.
/// </summary>
public interface IPostHogService
{
    /// <summary>
    /// Servisin geçerli bir API anahtarı ve etkinleştirilmiş konfigürasyona sahip olup olmadığını belirtir.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Belirtilen kullanıcı veya oturum için kanonik bir analitik olayı yakalar.
    /// </summary>
    Task CaptureAsync(
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kullanıcı kimlik özelliklerini (rol, plan, e-posta) günceller.
    /// </summary>
    Task IdentifyAsync(
        string distinctId,
        Dictionary<string, object>? properties = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PostHog Feature Flag değerini sorgular.
    /// </summary>
    Task<bool> IsFeatureFlagEnabledAsync(
        string distinctId,
        string flagKey,
        bool defaultValue = false,
        CancellationToken cancellationToken = default);
}
