namespace Namines.Infrastructure.Observability;

/// <summary>
/// PostHog ürün analitiği ve özellik bayrakları için konfigürasyon seçenekleri (new-phase/21-OBSERVABILITY.md §8).
/// </summary>
public sealed class PostHogOptions
{
    public const string SectionName = "PostHog";

    /// <summary>
    /// PostHog Proje API Anahtarı (Project API Key).
    /// Tanımlanmadığında istemci pasif moda geçer (no-op).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// PostHog Ingest sunucu adresi. Varsayılan: AB bulutu ("https://eu.i.posthog.com").
    /// </summary>
    public string Host { get; set; } = "https://eu.i.posthog.com";

    /// <summary>
    /// Servisin aktif olup olmadığını belirten anahtar.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// HTTP istek zaman aşımı (saniye cinsinden). Analitik çağrıları ana istek akışını
    /// yavaşlatmamalıdır (varsayılan: 3 saniye).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 3;
}
