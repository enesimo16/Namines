using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Events;
using Namines.Infrastructure.Events;
using Namines.Infrastructure.Observability;
using Namines.Infrastructure.Resilience;
using Xunit;

namespace Namines.Tests.Architecture;

/// <summary>
/// FAZ 4: Dağıtık Gözlemlenebilirlik (OpenTelemetry W3C Distributed Tracing,
/// ActivitySource, Prometheus Metrik Enstrümantasyonu ve Korelasyon) testleri.
/// </summary>
public sealed class ObservabilityTracingAndMetricsTests
{
    private sealed class TestVaultHandler : IModuleEventHandler<VaultBackupCompletedEvent>
    {
        public Task HandleAsync(VaultBackupCompletedEvent @event, System.Threading.CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public void ActivitySource_SchemaGeneration_Tagleri_Ve_Span_Ozellikleri_Dogrudur()
    {
        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == NaminesActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => captured = a
        };
        ActivitySource.AddActivityListener(listener);

        const string prompt = "CREATE TABLE accounts (id UUID PRIMARY KEY);";
        using (var activity = NaminesActivitySource.StartSchemaGeneration(prompt, "llama-3.3-70b"))
        {
            Assert.NotNull(activity);
            Assert.Equal("Schema.Generate", activity.DisplayName);
            Assert.Equal(prompt.Length, activity.GetTagItem("namines.prompt_length"));
            Assert.Equal("llama-3.3-70b", activity.GetTagItem("ai.model"));
        }
    }

    [Fact]
    public void ActivitySource_Resilience_Ve_Gateway_Spanlari_Basariyla_Uretilir()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == NaminesActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using (var resActivity = NaminesActivitySource.StartResilienceExecution(NaminesResiliencePipelineNames.AiPipeline))
        {
            Assert.NotNull(resActivity);
            Assert.Equal($"Resilience.{NaminesResiliencePipelineNames.AiPipeline}", resActivity.DisplayName);
            Assert.Equal(NaminesResiliencePipelineNames.AiPipeline, resActivity.GetTagItem("resilience.pipeline"));
        }

        using (var gwActivity = NaminesActivitySource.StartGatewayExecution("select", "orders"))
        {
            Assert.NotNull(gwActivity);
            Assert.Equal("Gateway.select", gwActivity.DisplayName);
            Assert.Equal("select", gwActivity.GetTagItem("db.operation"));
            Assert.Equal("orders", gwActivity.GetTagItem("db.table"));
        }
    }

    [Fact]
    public void ActivitySource_DatabaseProvisioning_Span_Detaylari_Eksiksizdir()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == NaminesActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var dbActivity = NaminesActivitySource.StartDatabaseProvisioning("Neon", "PostgreSql", "branch");
        Assert.NotNull(dbActivity);
        Assert.Equal("Database.Provision", dbActivity.DisplayName);
        Assert.Equal("Neon", dbActivity.GetTagItem("db.provider"));
        Assert.Equal("PostgreSql", dbActivity.GetTagItem("db.engine"));
        Assert.Equal("branch", dbActivity.GetTagItem("db.mode"));
    }

    [Fact]
    public void NaminesMetrics_Resilience_Ve_Event_Metrikleri_Olcum_Yapar()
    {
        var measurements = new List<(string InstrumentName, object Value)>();

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == NaminesMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        meterListener.SetMeasurementEventCallback<long>((inst, val, tags, state) =>
        {
            measurements.Add((inst.Name, val));
        });

        meterListener.SetMeasurementEventCallback<double>((inst, val, tags, state) =>
        {
            measurements.Add((inst.Name, val));
        });

        meterListener.Start();

        // Metrik kayıtlarını tetikleyelim
        NaminesMetrics.ResilienceExecuted(NaminesResiliencePipelineNames.AiPipeline, "success", TimeSpan.FromMilliseconds(120));
        NaminesMetrics.CircuitBreakerTransition(NaminesResiliencePipelineNames.AiPipeline, "opened");
        NaminesMetrics.EventPublished("TestEvent", true, TimeSpan.FromMilliseconds(45));

        Assert.Contains(measurements, m => m.InstrumentName == "namines_resilience_executions_total" && (long)m.Value == 1);
        Assert.Contains(measurements, m => m.InstrumentName == "namines_resilience_circuit_breaker_transitions_total" && (long)m.Value == 1);
        Assert.Contains(measurements, m => m.InstrumentName == "namines_events_published_total" && (long)m.Value == 1);
    }

    [Fact]
    public async Task ModuleEventBus_Olay_Yayinladiginda_Trace_Ve_Metrik_Birlikte_Uretilir()
    {
        var recordedSpans = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == NaminesActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => recordedSpans.Add(a.DisplayName)
        };
        ActivitySource.AddActivityListener(listener);

        var services = new ServiceCollection();
        services.AddSingleton<IModuleEventHandler<VaultBackupCompletedEvent>, TestVaultHandler>();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var bus = new ModuleEventBus(scopeFactory, NullLogger<ModuleEventBus>.Instance);

        await bus.PublishAsync(new VaultBackupCompletedEvent("b-1", "p-1", "db-1", 2048, true));

        Assert.Contains("EventBus.Publish.VaultBackupCompletedEvent", recordedSpans);
    }

    [Fact]
    public async Task ResilienceProvider_Calistiginda_Trace_Span_Olusturur()
    {
        var recordedSpans = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == NaminesActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a => recordedSpans.Add(a.DisplayName)
        };
        ActivitySource.AddActivityListener(listener);

        var provider = new NaminesResilienceProvider(NullLoggerFactory.Instance);

        var result = await provider.ExecuteAsync(
            NaminesResiliencePipelineNames.ExternalHttpPipeline,
            ct => Task.FromResult("ok"));

        Assert.Equal("ok", result);
        Assert.Contains($"Resilience.{NaminesResiliencePipelineNames.ExternalHttpPipeline}", recordedSpans);
    }
}
