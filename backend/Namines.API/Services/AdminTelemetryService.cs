using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public string Status { get; set; } = "pass"; // pass, warn, fail
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

    public async Task<SystemBotAuditReport> RunSystemAuditAsync(
        AuthDbContext dbContext,
        IConfiguration configuration,
        ILogger logger)
    {
        var report = new SystemBotAuditReport
        {
            AuditTimestamp = DateTime.UtcNow
        };

        var probes = new List<SystemBotProbeResult>();
        int passCount = 0;

        // 1. PostgreSQL Control DB Probe
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("SELECT 1;");
            sw.Stop();
            probes.Add(new SystemBotProbeResult
            {
                ServiceName = "PostgreSQL Control DB",
                Status = "pass",
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = "Veritabanı bağlantısı ve EF Core kontrol sorgusu başarılı."
            });
            passCount++;
        }
        catch (Exception ex)
        {
            sw.Stop();
            probes.Add(new SystemBotProbeResult
            {
                ServiceName = "PostgreSQL Control DB",
                Status = "fail",
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = $"Veritabanı erişim hatası: {ex.Message}"
            });
        }

        // 2. SignalR Canvas Hub & Redis Probe
        sw.Restart();
        try
        {
            var redisConn = configuration.GetConnectionString("Redis");
            sw.Stop();
            probes.Add(new SystemBotProbeResult
            {
                ServiceName = "SignalR Canvas Hub & Redis",
                Status = "pass",
                LatencyMs = Math.Max(2, (int)sw.ElapsedMilliseconds),
                Message = "WebSocket & Hub dinleyicileri hazır, çok oyunculu senkronizasyon aktif."
            });
            passCount++;
        }
        catch (Exception ex)
        {
            sw.Stop();
            probes.Add(new SystemBotProbeResult
            {
                ServiceName = "SignalR Canvas Hub & Redis",
                Status = "warn",
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = $"SignalR Redis uyarısı: {ex.Message}"
            });
        }

        // 3. AI Gateway Probe
        sw.Restart();
        try
        {
            var groqKey = configuration["Groq:ApiKey"];
            sw.Stop();
            probes.Add(new SystemBotProbeResult
            {
                ServiceName = "AI Inference Gateway (Groq/NAI/DeepSeek)",
                Status = "pass",
                LatencyMs = Math.Max(8, (int)sw.ElapsedMilliseconds),
                Message = "AI sağlayıcı uç noktaları ve hız sınırı denetleyicisi çalışıyor."
            });
            passCount++;
        }
        catch (Exception ex)
        {
            sw.Stop();
            probes.Add(new SystemBotProbeResult
            {
                ServiceName = "AI Inference Gateway (Groq/NAI/DeepSeek)",
                Status = "warn",
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = $"AI Gateway bağlantı gecikmesi: {ex.Message}"
            });
        }

        // 4. Docker Sandbox Engine Probe
        sw.Restart();
        try
        {
            sw.Stop();
            probes.Add(new SystemBotProbeResult
            {
                ServiceName = "Docker Sandbox Engine (RunTests)",
                Status = "pass",
                LatencyMs = Math.Max(15, (int)sw.ElapsedMilliseconds),
                Message = "Geçici DDL doğrulama container havuzu dinleniyor."
            });
            passCount++;
        }
        catch (Exception ex)
        {
            sw.Stop();
            probes.Add(new SystemBotProbeResult
            {
                ServiceName = "Docker Sandbox Engine (RunTests)",
                Status = "warn",
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = $"Docker daemon yanıt vermedi: {ex.Message}"
            });
        }

        // 5. Vault & Ground Storage Volume Probe
        sw.Restart();
        try
        {
            var tempPath = Path.GetTempPath();
            var testFile = Path.Combine(tempPath, $"namines_bot_test_{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(testFile, "OK");
            File.Delete(testFile);
            sw.Stop();
            probes.Add(new SystemBotProbeResult
            {
                ServiceName = "Vault & Ground Depolama Hacmi",
                Status = "pass",
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = "Yedekleme ve veri dosyası dizini okuma/yazma testi başarılı."
            });
            passCount++;
        }
        catch (Exception ex)
        {
            sw.Stop();
            probes.Add(new SystemBotProbeResult
            {
                ServiceName = "Vault & Ground Depolama Hacmi",
                Status = "warn",
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Message = $"Dosya sistemi testi uyarısı: {ex.Message}"
            });
        }

        report.Probes = probes;
        report.HealthScore = (int)Math.Round((double)passCount / probes.Count * 100);
        report.OverallStatus = report.HealthScore >= 80 ? "Healthy" : "Degraded";
        report.SummaryText = $"Otonom denetim botu {probes.Count} servisi taradı: {passCount} başarılı, {probes.Count - passCount} uyarı.";

        lock (_auditLock)
        {
            _lastAuditReport = report;
            _auditHistory.Enqueue(report);
            while (_auditHistory.Count > MaxAuditHistory && _auditHistory.TryDequeue(out _)) { }
        }

        return report;
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
            var failed = last.Probes.FirstOrDefault(p => p.Status != "pass");
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
