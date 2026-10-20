using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Namines.Core.Exceptions;
using Namines.Core.Interfaces;
using Namines.Infrastructure.Observability;

namespace Namines.Infrastructure.Resilience;

/// <summary>
/// Polly v8 boru hatlarını yöneten ve dış çağrıları kurumsal koruma altında yürüten servis.
/// BrokenCircuitException durumunu kurumsal CircuitBrokenException'a dönüştürür.
/// OpenTelemetry W3C distributed tracing ve Prometheus metrikleri ile donatılmıştır.
/// </summary>
public class NaminesResilienceProvider : INaminesResilienceProvider
{
    private readonly ConcurrentDictionary<string, ResiliencePipeline> _pipelines = new();
    private readonly ILoggerFactory _loggerFactory;

    public NaminesResilienceProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public ResiliencePipeline GetPipeline(string pipelineName)
    {
        return _pipelines.GetOrAdd(pipelineName, name =>
        {
            var logger = _loggerFactory.CreateLogger($"Namines.Resilience.{name}");
            return name switch
            {
                NaminesResiliencePipelineNames.AiPipeline => NaminesResiliencePipelines.CreateAiPipeline(logger),
                NaminesResiliencePipelineNames.CloudDbPipeline => NaminesResiliencePipelines.CreateCloudDbPipeline(logger),
                NaminesResiliencePipelineNames.DockerSocketPipeline => NaminesResiliencePipelines.CreateDockerSocketPipeline(logger),
                _ => NaminesResiliencePipelines.CreateExternalHttpPipeline(logger)
            };
        });
    }

    public async Task ExecuteAsync(
        string pipelineName,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        var pipeline = GetPipeline(pipelineName);
        using var activity = NaminesActivitySource.StartResilienceExecution(pipelineName);
        var sw = Stopwatch.StartNew();

        try
        {
            await pipeline.ExecuteAsync(async ct => await action(ct), cancellationToken);
            NaminesMetrics.ResilienceExecuted(pipelineName, "success", sw.Elapsed);
        }
        catch (BrokenCircuitException ex)
        {
            NaminesMetrics.ResilienceExecuted(pipelineName, "circuit_broken", sw.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit Breaker Open");

            throw new CircuitBrokenException(
                pipelineName,
                $"'{pipelineName}' hedefi aşırı hata oranı nedeniyle geçici olarak devre dışı bırakıldı (Circuit Breaker açık).",
                TimeSpan.FromSeconds(30),
                ex);
        }
        catch (TimeoutRejectedException)
        {
            NaminesMetrics.ResilienceExecuted(pipelineName, "timeout", sw.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Error, "Timeout");
            throw;
        }
        catch (Exception ex)
        {
            NaminesMetrics.ResilienceExecuted(pipelineName, "error", sw.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    public async Task<T> ExecuteAsync<T>(
        string pipelineName,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        var pipeline = GetPipeline(pipelineName);
        using var activity = NaminesActivitySource.StartResilienceExecution(pipelineName);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await pipeline.ExecuteAsync(async ct => await action(ct), cancellationToken);
            NaminesMetrics.ResilienceExecuted(pipelineName, "success", sw.Elapsed);
            return result;
        }
        catch (BrokenCircuitException ex)
        {
            NaminesMetrics.ResilienceExecuted(pipelineName, "circuit_broken", sw.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Error, "Circuit Breaker Open");

            throw new CircuitBrokenException(
                pipelineName,
                $"'{pipelineName}' hedefi aşırı hata oranı nedeniyle geçici olarak devre dışı bırakıldı (Circuit Breaker açık).",
                TimeSpan.FromSeconds(30),
                ex);
        }
        catch (TimeoutRejectedException)
        {
            NaminesMetrics.ResilienceExecuted(pipelineName, "timeout", sw.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Error, "Timeout");
            throw;
        }
        catch (Exception ex)
        {
            NaminesMetrics.ResilienceExecuted(pipelineName, "error", sw.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
