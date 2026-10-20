using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Namines.Infrastructure.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Namines.API.Extensions;

/// <summary>
/// OpenTelemetry ve W3C Dağıtık İzleme (Distributed Tracing) & Prometheus Metrik kurulumu (new-phase/21-OBSERVABILITY.md).
///
/// Satıcı-bağımsız kalması bilinçli: enstrümantasyon W3C ve OTel standartlarıyla konuşuyor, arkasında
/// Grafana, Jaeger veya Prometheus olabilir.
///
/// Tracing katmanı ASP.NET Core, HttpClient, ModuleEventBus ve Polly Resilience boru hatlarını W3C Traceparent
/// formatında span'lar ile birbirine bağlar ve her yanıta 'X-Trace-Id' başlığı iliştirir.
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
            })
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    .AddSource(NaminesActivitySource.SourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Prometheus scrape ucu ve sağlık kontrolleri trace ağacını kirletmesin
                        options.Filter = httpContext =>
                            !httpContext.Request.Path.StartsWithSegments("/metrics") &&
                            !httpContext.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation();
            });

        // 21 §8: PostHog Ürün Analitiği ve Feature Flags İstemcisi (Kota dostu, Fail-open)
        services.Configure<PostHogOptions>(configuration.GetSection(PostHogOptions.SectionName));
        services.AddHttpClient(PostHogClient.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PostHogOptions>>().Value;
            var baseHost = string.IsNullOrWhiteSpace(options.Host) ? "https://eu.i.posthog.com" : options.Host.TrimEnd('/');
            client.BaseAddress = new Uri(baseHost);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        });
        services.AddSingleton<Namines.Core.Interfaces.IPostHogService, PostHogClient>();

        return services;
    }

    /// <summary>
    /// Prometheus scrape ucunu ve W3C X-Trace-Id yanıt başlığı ara yazılımını bağlar.
    ///
    /// <b>Kimlik doğrulaması YOK ama bu bilinçli bir taviz değil:</b> metrik uçları
    /// genellikle iç ağdan kazınır ve Prometheus'un kimlik doğrulama desteği
    /// sınırlıdır. Uç, altyapı seviyesinde (ingress/firewall) korunmalıdır —
    /// dışarıya açık bırakılırsa istek hacmi ve hata oranları görünür olur.
    /// </summary>
    public static WebApplication UseNaminesObservability(this WebApplication app)
    {
        // Her HTTP yanıtına W3C TraceId'sini iliştirerek ön yüz ve arka yüz log korelasyonunu sağlar
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
                context.Response.Headers.TryAdd("X-Trace-Id", traceId);
                return Task.CompletedTask;
            });

            await next();
        });

        app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }
}
