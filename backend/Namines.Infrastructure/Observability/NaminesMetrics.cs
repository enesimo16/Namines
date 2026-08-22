using System;
using System.Diagnostics.Metrics;

namespace Namines.Infrastructure.Observability;

/// <summary>
/// İş metrikleri (new-phase/21-OBSERVABILITY.md §3).
///
/// HTTP'nin RED metrikleri (istek sayısı, süre, uçuştaki istek) OpenTelemetry'nin
/// ASP.NET Core enstrümantasyonundan HAZIR geliyor — onları elle yazmak, çerçevenin
/// zaten ürettiği seriyi ikinci kez ve muhtemelen farklı etiketlerle üretmek olurdu.
/// Burada yalnızca ÜRÜNE özgü olanlar var: hangi motora derlendi, kaç veritabanı
/// sağlandı, hangi risk seviyesinde kaç migration uygulandı.
///
/// Metrik adları dokümandaki <c>namines_*</c> biçimini izliyor; Prometheus'a
/// aktarılırken bu adlar sözleşmedir — değiştirmek kurulmuş dashboard'ları
/// sessizce boşaltır.
/// </summary>
public static class NaminesMetrics
{
    /// <summary>OpenTelemetry kaydı bu adı dinler.</summary>
    public const string MeterName = "Namines";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> SchemaCompilations =
        Meter.CreateCounter<long>("namines_schema_compilations_total",
            description: "Schema compilations by engine, target and result.");

    private static readonly Histogram<double> SchemaCompilationDuration =
        Meter.CreateHistogram<double>("namines_schema_compilation_duration_seconds",
            unit: "s", description: "Time spent compiling a schema.");

    private static readonly Counter<long> DatabasesProvisioned =
        Meter.CreateCounter<long>("namines_databases_provisioned_total",
            description: "Databases provisioned by provider, engine, mode and result.");

    private static readonly Histogram<double> ProvisionDuration =
        Meter.CreateHistogram<double>("namines_database_provision_duration_seconds",
            unit: "s", description: "Time spent provisioning a database.");

    private static readonly Counter<long> MigrationsApplied =
        Meter.CreateCounter<long>("namines_migrations_applied_total",
            description: "Migrations applied by risk level and result.");

    private static readonly Counter<long> GatewayRequests =
        Meter.CreateCounter<long>("namines_gateway_requests_total",
            description: "Gateway data requests by operation and outcome.");

    /// <param name="result">"success" | "failure". Serbest metin DEĞİL: her çağıran
    /// kendi kelimesini kullanırsa ("ok", "succeeded", "true") seriler bölünür ve
    /// dashboard toplamları sessizce yanlış çıkar.</param>
    public static void SchemaCompiled(string engine, string target, bool success, TimeSpan elapsed)
    {
        var result = success ? "success" : "failure";
        SchemaCompilations.Add(1,
            new("engine", engine), new("target", target), new("result", result));
        SchemaCompilationDuration.Record(elapsed.TotalSeconds,
            new("engine", engine), new("target", target));
    }

    public static void DatabaseProvisioned(
        string provider, string engine, string mode, bool success, TimeSpan elapsed)
    {
        var result = success ? "success" : "failure";
        DatabasesProvisioned.Add(1,
            new("provider", provider), new("engine", engine),
            new("mode", mode), new("result", result));
        ProvisionDuration.Record(elapsed.TotalSeconds,
            new("provider", provider), new("mode", mode));
    }

    public static void MigrationApplied(string risk, bool success) =>
        MigrationsApplied.Add(1,
            new("risk", risk), new("result", success ? "success" : "failure"));

    /// <param name="outcome">"ok" | "denied" | "error". <b>Reddedilenler ayrı
    /// sayılıyor:</b> hata ile birleştirilseydi, izin yapılandırmasının yanlış
    /// olduğu bir kurulum "sistem bozuk" gibi görünürdü.</param>
    public static void GatewayRequest(string operation, string outcome) =>
        GatewayRequests.Add(1, new("operation", operation), new("outcome", outcome));
}
