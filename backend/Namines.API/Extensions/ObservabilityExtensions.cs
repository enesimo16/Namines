using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Namines.Infrastructure.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Namines.API.Extensions;

/// <summary>
/// OpenTelemetry kurulumu (new-phase/21-OBSERVABILITY.md §1, §3, §4).
///
/// Satıcı-bağımsız kalması bilinçli: enstrümantasyon OTLP ile konuşuyor, arkasında
/// Grafana da olabilir başka bir şey de. Kod hiçbir sağlayıcıya bağlanmıyor.
///
/// <b>Şu an yalnızca METRİK var, iz (trace) yok.</b> İz toplamanın tek anlamı onu
/// bir yere göndermek; OTLP dışa aktarıcısının denenen tüm sürümleri bilinen bir
/// güvenlik açığı bildiriyor (NU1902) ve projede henüz bir collector yok. İz
/// enstrümantasyonunu dışa aktarıcısız kaydetmek, hiçbir yere gitmeyen veri için
/// CPU harcamak olurdu. Collector kurulduğunda ikisi birlikte eklenir.
/// </summary>
public static class ObservabilityExtensions
{
    public static IServiceCollection AddNaminesObservability(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var resource = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: "namines-api",
                serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString() ?? "0.0.0")
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("deployment.environment", environment.EnvironmentName),
            });

        services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resource)
                    // RED metrikleri (21 §3) çerçeveden hazır geliyor; elle yazmak
                    // aynı seriyi ikinci kez, farklı etiketlerle üretmek olurdu.
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(NaminesMetrics.MeterName)
                    // Prometheus scrape ucu: collector olmadan da metrik okunabilsin.
                    .AddPrometheusExporter();
            });

        return services;
    }

    /// <summary>
    /// Prometheus scrape ucunu bağlar.
    ///
    /// <b>Kimlik doğrulaması YOK ama bu bilinçli bir taviz değil:</b> metrik uçları
    /// genellikle iç ağdan kazınır ve Prometheus'un kimlik doğrulama desteği
    /// sınırlıdır. Uç, altyapı seviyesinde (ingress/firewall) korunmalıdır —
    /// dışarıya açık bırakılırsa istek hacmi ve hata oranları görünür olur.
    /// Bu, dağıtım dokümanında belirtilmelidir.
    /// </summary>
    public static WebApplication UseNaminesObservability(this WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }
}
