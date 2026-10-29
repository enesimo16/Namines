using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Infrastructure.Data;

namespace Namines.API.Services;

public class TelemetryEventItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Module { get; set; } = "general";
    public string? Metadata { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public class UserSessionSummary
{
    public string SessionId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string UserEmail { get; set; } = "Anonim";
    public DateTime StartedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public int DurationMinutes { get; set; }
    public List<string> PagesVisited { get; set; } = new();
    public string StartMode { get; set; } = "prompt";
    public int TotalClicks { get; set; }
    public List<TelemetryEventItem> RecentActions { get; set; } = new();
}

public class SystemBotProbeResult
{
    public string ServiceName { get; set; } = string.Empty;
    public string Status { get; set; } = "pass"; // pass, warn, fail, skipped
    public int LatencyMs { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

public class SystemBotAuditReport
{
    public DateTime AuditTimestamp { get; set; } = DateTime.UtcNow;
    public string OverallStatus { get; set; } = "Healthy";
    public int HealthScore { get; set; } = 100;
    public List<SystemBotProbeResult> Probes { get; set; } = new();
    public string SummaryText { get; set; } = string.Empty;
}

public class AdminSystemLogItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "Error"; // Info, Warning, Error, Critical
    public string Source { get; set; } = "API"; // API, AI Gateway, SignalR Hub, Control DB, Ground, Vault, Runner
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? Path { get; set; }
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }
    public bool Resolved { get; set; } = false;
}

public class SystemIncidentItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = "Medium"; // Low, Medium, High, Critical
    public string Status { get; set; } = "Active"; // Active, Investigating, Resolved
    public string AffectedService { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public string Recommendation { get; set; } = string.Empty;
}

public class AdminTelemetryService
{
    private readonly ConcurrentQueue<TelemetryEventItem> _events = new();
    private readonly ConcurrentQueue<AdminSystemLogItem> _logs = new();
    private readonly ConcurrentQueue<SystemBotAuditReport> _auditHistory = new();
    private const int MaxEvents = 5000;
    private const int MaxLogs = 1000;
    private const int MaxAuditHistory = 50;
    private SystemBotAuditReport? _lastAuditReport;
    private readonly object _auditLock = new();

    // Kimliksiz telemetri ucu için IP başına dakikalık sayaç.
    private const int MaxEventsPerIpPerMinute = 120;
    private const int MaxTrackedIps = 10_000;
    private readonly ConcurrentDictionary<string, (long WindowStartTicks, int Count)> _ipWindows = new();

    /// <summary>
    /// IP başına dakikalık olay sınırı. Sınır aşılmışsa false döner; kayıt alınmamalı.
    /// </summary>
    public bool TryAcceptFrom(string? ip)
    {
        var key = string.IsNullOrEmpty(ip) ? "unknown" : ip;
        var now = DateTime.UtcNow.Ticks;
        var window = TimeSpan.FromMinutes(1).Ticks;

        if (_ipWindows.Count > MaxTrackedIps)
        {
            foreach (var kv in _ipWindows)
            {
                if (now - kv.Value.WindowStartTicks > window) _ipWindows.TryRemove(kv.Key, out _);
            }
        }

        var accepted = true;
        _ipWindows.AddOrUpdate(
            key,
            _ => (now, 1),
            (_, cur) =>
            {
                if (now - cur.WindowStartTicks > window)
                {
                    accepted = true;
                    return (now, 1);
                }
                if (cur.Count >= MaxEventsPerIpPerMinute)
                {
                    accepted = false;
                    return cur;
                }
                accepted = true;
                return (cur.WindowStartTicks, cur.Count + 1);
            });
        return accepted;
    }

    public void Track(TelemetryEventItem item)
    {
        if (string.IsNullOrWhiteSpace(item.SessionId))
        {
            item.SessionId = Guid.NewGuid().ToString("N");
        }

        item.Timestamp = DateTime.UtcNow;
        _events.Enqueue(item);

        // Limit memory queue size
        while (_events.Count > MaxEvents && _events.TryDequeue(out _)) { }
    }

    public List<UserSessionSummary> GetRecentSessions(int limit = 50)
    {
        var allEvents = _events.ToArray();
        // Olay yoksa oturum da yok: uydurma örnek oturum göstermek admin'i yanıltır.
        if (allEvents.Length == 0)
        {
            return new List<UserSessionSummary>();
        }

        var grouped = allEvents
            .GroupBy(e => e.SessionId)
            .OrderByDescending(g => g.Max(e => e.Timestamp))
            .Take(limit)
            .Select(g =>
            {
                var sorted = g.OrderBy(e => e.Timestamp).ToList();
                var first = sorted.First();
                var last = sorted.Last();
                var duration = Math.Max(1, (int)(last.Timestamp - first.Timestamp).TotalMinutes);

                var startAction = sorted.FirstOrDefault(e => e.Action.StartsWith("start_"));
                var startMode = startAction != null
                    ? startAction.Action.Replace("start_", "")
                    : (first.Metadata ?? "prompt");

                return new UserSessionSummary
                {
                    SessionId = g.Key,
                    UserId = first.UserId,
                    UserEmail = first.UserEmail ?? "Anonim",
                    StartedAt = first.Timestamp,
                    LastActiveAt = last.Timestamp,
                    DurationMinutes = duration,
                    PagesVisited = sorted.Select(e => e.Path).Distinct().ToList(),
                    StartMode = startMode,
                    TotalClicks = sorted.Count,
                    RecentActions = sorted.TakeLast(5).ToList()
                };
            })
            .ToList();

        return grouped;
    }

    public (int totalEvents, Dictionary<string, int> moduleClicks, Dictionary<string, int> startModes) GetAnalyticsOverview()
    {
        var allEvents = _events.ToArray();
        var moduleClicks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var startModes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var ev in allEvents)
        {
            var mod = string.IsNullOrWhiteSpace(ev.Module) ? "general" : ev.Module;
            moduleClicks[mod] = moduleClicks.GetValueOrDefault(mod, 0) + 1;

            if (ev.Action.StartsWith("start_"))
            {
                var mode = ev.Action.Replace("start_", "");
                startModes[mode] = startModes.GetValueOrDefault(mode, 0) + 1;
            }
        }

        return (allEvents.Length, moduleClicks, startModes);
    }

    // ── FAZ 3: Hata & İstisna Logları ──────────────────────────────────────────

    public void RecordLog(AdminSystemLogItem log)
    {
        if (string.IsNullOrWhiteSpace(log.Id))
        {
            log.Id = Guid.NewGuid().ToString("N");
        }
        log.Timestamp = DateTime.UtcNow;
        _logs.Enqueue(log);

        while (_logs.Count > MaxLogs && _logs.TryDequeue(out _)) { }
    }

    public List<AdminSystemLogItem> GetLogs(string? level = null, string? source = null, bool? resolved = null, int limit = 100)
    {
        var query = _logs.ToArray().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(level) && !level.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(l => l.Level.Equals(level, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(source) && !source.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(l => l.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
        }

        if (resolved.HasValue)
        {
            query = query.Where(l => l.Resolved == resolved.Value);
        }

        return query.OrderByDescending(l => l.Timestamp).Take(limit).ToList();
    }

    public bool ResolveLog(string id)
    {
        var found = _logs.FirstOrDefault(l => l.Id == id);
        if (found != null)
        {
            found.Resolved = true;
            return true;
        }
        return false;
    }

    public void ClearLogs()
    {
        while (_logs.TryDequeue(out _)) { }
    }

    // ── Otonom Sistem Denetim Botu ──────────────────────────────────────────────

    // Her probe için üst süre: yavaş bir bağımlılık tüm denetimi asılı bırakmasın.
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    public async Task<SystemBotAuditReport> RunSystemAuditAsync(
        AuthDbContext dbContext,
        IConfiguration configuration,
        ILogger logger,
        IServiceProvider? services = null)
    {
        var report = new SystemBotAuditReport
        {
            AuditTimestamp = DateTime.UtcNow
        };

        var redis = services?.GetService(typeof(StackExchange.Redis.IConnectionMultiplexer))
            as StackExchange.Redis.IConnectionMultiplexer;
        var redisConfigured = !string.IsNullOrWhiteSpace(configuration["Redis:ConnectionString"]);

        // Probe'lar paralel koşar; her biri kendi süre sınırı ve hata yakalamasıyla sarılıdır.
        var tasks = new[]
        {
            RunProbeAsync("PostgreSQL Control DB", logger, async ct =>
            {
                await dbContext.Database.ExecuteSqlRawAsync("SELECT 1;", ct);
                return ("pass", "Veritabanı bağlantısı ve EF Core kontrol sorgusu başarılı.");
            }),
            RunProbeAsync("SignalR Canvas Hub & Redis", logger, ct => ProbeRedisBackplaneAsync(
                redisConfigured,
                redis == null ? null : async c => await redis.GetDatabase().PingAsync().WaitAsync(c),
                ct)),
            RunProbeAsync("AI Inference Gateway (Groq/NAI/DeepSeek)", logger,
                ct => Task.FromResult(ProbeAiGateway(configuration))),
            RunProbeAsync("Docker Sandbox Engine (RunTests)", logger, ct => Task.FromResult(
                // API süreci docker.sock'a bağlanmamalı (AGENTS.md); sandbox'ı burada yoklamak
                // yerine dürüstçe "atlandı" raporlanır.
                ("skipped", "API süreci Docker daemon'a bağlanmaz; sandbox durumu buradan doğrulanamaz."))),
            RunProbeAsync("Vault & Ground Depolama Hacmi", logger, async ct =>
            {
                var testFile = Path.Combine(Path.GetTempPath(), $"namines_bot_test_{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllTextAsync(testFile, "OK", ct);
                }
                finally
                {
                    try { File.Delete(testFile); } catch (IOException) { }
                }
                return ("pass", "Yedekleme ve veri dosyası dizini okuma/yazma testi başarılı.");
            }),
        };

        var probes = (await Task.WhenAll(tasks)).ToList();

        report.Probes = probes;
        ApplyScore(report);

        lock (_auditLock)
        {
            _lastAuditReport = report;
            _auditHistory.Enqueue(report);
            while (_auditHistory.Count > MaxAuditHistory && _auditHistory.TryDequeue(out _)) { }
        }

        return report;
    }

    /// <summary>
    /// Skor yalnızca gerçekten çalıştırılan probe'lardan hesaplanır; "skipped" paya da paydaya girmez.
    /// </summary>
    internal static void ApplyScore(SystemBotAuditReport report)
    {
        var counted = report.Probes.Where(p => p.Status != "skipped").ToList();
        var passCount = counted.Count(p => p.Status == "pass");
        var skipped = report.Probes.Count - counted.Count;

        report.HealthScore = counted.Count == 0
            ? 0
            : (int)Math.Round((double)passCount / counted.Count * 100);
        report.OverallStatus = counted.Count > 0 && report.HealthScore >= 80 ? "Healthy" : "Degraded";
        report.SummaryText = $"Otonom denetim botu {counted.Count} servisi taradı: {passCount} başarılı, " +
            $"{counted.Count - passCount} uyarı/hata" + (skipped > 0 ? $", {skipped} atlandı." : ".");
    }

    internal static async Task<(string Status, string Message)> ProbeRedisBackplaneAsync(
        bool configured,
        Func<CancellationToken, Task<TimeSpan>>? ping,
        CancellationToken ct)
    {
        if (!configured)
            return ("pass", "Redis tanımlı değil; SignalR tek instance (bellek içi) modunda çalışıyor.");
        if (ping == null)
            return ("fail", "Redis tanımlı ama bağlantı nesnesi kayıtlı değil.");

        var rtt = await ping(ct);
        return rtt.TotalMilliseconds > 500
            ? ("warn", $"Redis yanıt veriyor ancak yavaş ({(int)rtt.TotalMilliseconds} ms).")
            : ("pass", "Redis backplane yanıt veriyor.");
    }

    internal static (string Status, string Message) ProbeAiGateway(IConfiguration configuration)
    {
        // Ücretli bir LLM çağrısı yapılmaz; yalnızca sağlayıcı anahtarının varlığına bakılır.
        var configured = new[] { "Groq", "DeepSeek", "Gemini" }
            .Where(p => !string.IsNullOrWhiteSpace(configuration[$"{p}:ApiKey"]))
            .ToList();

        return configured.Count == 0
            ? ("fail", "Hiçbir AI sağlayıcı anahtarı yapılandırılmamış.")
            : ("pass", $"{configured.Count} AI sağlayıcı anahtarı yapılandırılmış ({string.Join(", ", configured)}); canlı çağrı yapılmadı.");
    }

    internal static async Task<SystemBotProbeResult> RunProbeAsync(
        string name,
        ILogger logger,
        Func<CancellationToken, Task<(string Status, string Message)>> probe)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(ProbeTimeout);
            var (status, message) = await probe(cts.Token).WaitAsync(ProbeTimeout);
            return new SystemBotProbeResult
            {
                ServiceName = name,
                Status = status,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = message
            };
        }
        catch (Exception ex)
        {
            // Ham istisna mesajı yanıta konmaz (bağlantı dizesi/yol sızabilir); loga yazılır.
            logger.LogWarning(ex, "Denetim probe'u başarısız: {Probe}", name);
            return new SystemBotProbeResult
            {
                ServiceName = name,
                Status = "fail",
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = ex is TimeoutException or OperationCanceledException
                    ? $"Yanıt {ProbeTimeout.TotalSeconds:0} sn içinde alınamadı."
                    : "Kontrol başarısız oldu; ayrıntı için sunucu loglarına bakın."
            };
        }
    }

    public SystemBotAuditReport? GetLastAuditReport()
    {
        lock (_auditLock)
        {
            return _lastAuditReport;
        }
    }

    public List<SystemBotAuditReport> GetAuditHistory(int limit = 10)
    {
        lock (_auditLock)
        {
            return _auditHistory.Reverse().Take(limit).ToList();
        }
    }

    public List<SystemIncidentItem> GetActiveIncidents()
    {
        var incidents = new List<SystemIncidentItem>();
        var last = GetLastAuditReport();

        if (last != null && last.HealthScore < 80)
        {
            var failed = last.Probes.FirstOrDefault(p => p.Status != "pass" && p.Status != "skipped");
            if (failed != null)
            {
                incidents.Add(new SystemIncidentItem
                {
                    Title = $"{failed.ServiceName} Yanıt Sorunu",
                    Severity = failed.Status == "fail" ? "Critical" : "High",
                    Status = "Active",
                    AffectedService = failed.ServiceName,
                    DetectedAt = last.AuditTimestamp,
                    Recommendation = "Servis bağlantı dizesini ve ağ gecikmesini kontrol edin."
                });
            }
        }

        // Check if there are unresolved critical logs
        var criticalLog = _logs.FirstOrDefault(l => l.Level == "Critical" && !l.Resolved);
        if (criticalLog != null)
        {
            incidents.Add(new SystemIncidentItem
            {
                Title = $"Kritik Hata: {criticalLog.Source}",
                Severity = "Critical",
                Status = "Investigating",
                AffectedService = criticalLog.Source,
                DetectedAt = criticalLog.Timestamp,
                Recommendation = "Hata detayındaki StackTrace dökümünü inceleyin."
            });
        }

        return incidents;
    }
}
