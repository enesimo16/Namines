using System;
using System.Diagnostics;

namespace Namines.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry W3C Dağıtık İzleme (Distributed Tracing) için merkezi ActivitySource.
/// Tüm modüller ve kritik süreçler (Schema, Resilience, EventBus, Provisioning, Gateway)
/// standart W3C trace context'i ile etiketlenmiş span'lar üretir.
///
/// Dinleyici olmadığında (listener = null) sıfır tahsisat (zero-allocation) ile çalışır.
/// Bir collector veya OpenTelemetry TracerProvider bağlandığında ise tüm ağaç görünür hale gelir.
/// </summary>
public static class NaminesActivitySource
{
    public const string SourceName = "Namines";
    public const string Version = "1.0.0";

    public static readonly ActivitySource Source = new(SourceName, Version);

    public static Activity? StartSchemaGeneration(string prompt, string? model = null)
    {
        var activity = Source.StartActivity("Schema.Generate", ActivityKind.Internal);
        if (activity != null)
        {
            activity.SetTag("namines.prompt_length", prompt.Length);
            if (!string.IsNullOrEmpty(model))
                activity.SetTag("ai.model", model);
        }
        return activity;
    }

    public static Activity? StartResilienceExecution(string pipelineName)
    {
        var activity = Source.StartActivity($"Resilience.{pipelineName}", ActivityKind.Internal);
        activity?.SetTag("resilience.pipeline", pipelineName);
        return activity;
    }

    public static Activity? StartEventPublish(string eventType)
    {
        var activity = Source.StartActivity($"EventBus.Publish.{eventType}", ActivityKind.Producer);
        activity?.SetTag("event.type", eventType);
        return activity;
    }

    public static Activity? StartDatabaseProvisioning(string provider, string engine, string mode)
    {
        var activity = Source.StartActivity("Database.Provision", ActivityKind.Client);
        if (activity != null)
        {
            activity.SetTag("db.provider", provider);
            activity.SetTag("db.engine", engine);
            activity.SetTag("db.mode", mode);
        }
        return activity;
    }

    public static Activity? StartGatewayExecution(string operation, string table)
    {
        var activity = Source.StartActivity($"Gateway.{operation}", ActivityKind.Server);
        if (activity != null)
        {
            activity.SetTag("db.operation", operation);
            activity.SetTag("db.table", table);
        }
        return activity;
    }
}
