using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Desk.Abstractions;
using Namines.Desk.Domain;

namespace Namines.Desk.Application;

public sealed record DeskSqlHistoryItem(
    string Id,
    string Sql,
    bool Truncated,
    int RowCount,
    bool Succeeded,
    string? ErrorMessage,
    int DurationMs,
    DateTime CreatedAt);

public sealed record DeskSavedQueryItem(
    string Id,
    string Name,
    string Sql,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public interface IDeskApplicationService
{
    Task<string> CreateHandoffTokenAsync(string userId, CancellationToken ct = default);
    Task<string?> ExchangeHandoffTokenAsync(string rawToken, CancellationToken ct = default);
    Task<int> PurgeExpiredHandoffTokensAsync(
        DateTime cutoffUtc,
        CancellationToken ct = default,
        int batchSize = 5000,
        TimeSpan pause = default);

    Task RecordSqlHistoryAsync(
        string projectId,
        string userId,
        string sql,
        bool succeeded,
        int rowCount,
        string? errorMessage,
        int durationMs,
        CancellationToken ct = default);

    Task<IReadOnlyList<DeskSqlHistoryItem>> GetSqlHistoryAsync(
        string projectId,
        string userId,
        int limit,
        CancellationToken ct = default);

    Task<int> ClearSqlHistoryAsync(string projectId, string userId, CancellationToken ct = default);

    Task<IReadOnlyList<DeskSavedQueryItem>> GetSavedQueriesAsync(
        string projectId,
        string userId,
        CancellationToken ct = default);

    Task<DeskSavedQueryItem> SaveQueryAsync(
        string projectId,
        string userId,
        string name,
        string sql,
        CancellationToken ct = default);

    Task<bool> DeleteSavedQueryAsync(
        string projectId,
        string userId,
        string id,
        CancellationToken ct = default);

    Task<(int TotalQueriesExecuted, DateTime? LastQueryAt)> GetProjectQuerySummaryAsync(
        string projectId,
        CancellationToken ct = default);
}

/// <summary>
/// Namines Desk modülünün uygulama katmanı servisi.
/// SSO devir jetonlarını (<see cref="DeskHandoffToken"/>), SQL geçmişini (<see cref="SqlQueryHistoryEntry"/>)
/// ve kaydedilmiş sorguları (<see cref="SavedQuery"/>) yalnızca <see cref="IDeskData"/> üzerinden yönetir.
/// </summary>
internal sealed class DeskApplicationService : IDeskApplicationService
{
    private readonly IDeskData _db;
    private readonly IDeskHandoffTokenValidator _tokenValidator;

    public DeskApplicationService(IDeskData db, IDeskHandoffTokenValidator tokenValidator)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _tokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
    }

    private static string HashHex(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    public async Task<string> CreateHandoffTokenAsync(string userId, CancellationToken ct = default)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = DateTime.UtcNow;

        _db.DeskHandoffTokens.Add(new DeskHandoffToken
        {
            UserId = userId,
            TokenHash = HashHex(raw),
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(30),
        });
        await _db.SaveChangesAsync(ct);

        return raw;
    }

    public async Task<string?> ExchangeHandoffTokenAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        var hash = HashHex(rawToken);
        var now = DateTime.UtcNow;

        var entity = await _db.DeskHandoffTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (entity is null) return null;
        var entityId = entity.Id;
        var entityUserId = entity.UserId;
        if (_tokenValidator.IsExpired(entity)) return null;

        var affected = await _db.DeskHandoffTokens
            .Where(t => t.Id == entityId && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);

        return affected == 0 ? null : entityUserId;
    }

    public async Task<int> PurgeExpiredHandoffTokensAsync(
        DateTime cutoffUtc,
        CancellationToken ct = default,
        int batchSize = 5000,
        TimeSpan pause = default)
    {
        var effectiveBatchSize = batchSize > 0 ? batchSize : 5000;
        var total = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var n = await _db.DeskHandoffTokens
                .Where(t => t.ExpiresAt < cutoffUtc)
                .OrderBy(t => t.Id)
                .Take(effectiveBatchSize)
                .ExecuteDeleteAsync(ct);
            total += n;
            if (n < effectiveBatchSize) return total;
            if (pause > TimeSpan.Zero) await Task.Delay(pause, ct);
        }
    }

    public async Task RecordSqlHistoryAsync(
        string projectId,
        string userId,
        string sql,
        bool succeeded,
        int rowCount,
        string? errorMessage,
        int durationMs,
        CancellationToken ct = default)
    {
        var truncated = sql.Length > SqlQueryHistoryEntry.MaxSqlLength;

        _db.SqlQueryHistory.Add(new SqlQueryHistoryEntry
        {
            ProjectId = projectId,
            UserId = userId,
            Sql = truncated ? sql[..SqlQueryHistoryEntry.MaxSqlLength] : sql,
            Truncated = truncated,
            RowCount = rowCount,
            Succeeded = succeeded,
            ErrorMessage = errorMessage,
            DurationMs = durationMs,
        });

        await _db.SaveChangesAsync(ct);
        await PruneHistoryAsync(projectId, userId, ct);
    }

    private async Task PruneHistoryAsync(string projectId, string userId, CancellationToken ct)
    {
        var stale = await _db.SqlQueryHistory
            .Where(h => h.UserId == userId && h.ProjectId == projectId)
            .OrderByDescending(h => h.CreatedAt)
            .Skip(SqlQueryHistoryEntry.RetentionPerUserPerProject)
            .Select(h => h.Id)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        await _db.SqlQueryHistory
            .Where(h => stale.Contains(h.Id))
            .ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<DeskSqlHistoryItem>> GetSqlHistoryAsync(
        string projectId,
        string userId,
        int limit,
        CancellationToken ct = default) =>
        await _db.SqlQueryHistory
            .AsNoTracking()
            .Where(h => h.UserId == userId && h.ProjectId == projectId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(Math.Clamp(limit, 1, SqlQueryHistoryEntry.RetentionPerUserPerProject))
            .Select(h => new DeskSqlHistoryItem(
                h.Id,
                h.Sql,
                h.Truncated,
                h.RowCount,
                h.Succeeded,
                h.ErrorMessage,
                h.DurationMs,
                h.CreatedAt))
            .ToListAsync(ct);

    public Task<int> ClearSqlHistoryAsync(string projectId, string userId, CancellationToken ct = default) =>
        _db.SqlQueryHistory
            .Where(h => h.UserId == userId && h.ProjectId == projectId)
            .ExecuteDeleteAsync(ct);

    public async Task<IReadOnlyList<DeskSavedQueryItem>> GetSavedQueriesAsync(
        string projectId,
        string userId,
        CancellationToken ct = default) =>
        await _db.SavedQueries
            .AsNoTracking()
            .Where(q => q.UserId == userId && q.ProjectId == projectId)
            .OrderBy(q => q.Name)
            .Select(q => new DeskSavedQueryItem(q.Id, q.Name, q.Sql, q.CreatedAt, q.UpdatedAt))
            .ToListAsync(ct);

    public async Task<DeskSavedQueryItem> SaveQueryAsync(
        string projectId,
        string userId,
        string name,
        string sql,
        CancellationToken ct = default)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("A name is required.");
        if (name.Length > SavedQuery.MaxNameLength)
            throw new ArgumentException($"The name cannot be longer than {SavedQuery.MaxNameLength} characters.");
        if (sql.Length > SavedQuery.MaxSqlLength)
            throw new ArgumentException($"The query cannot be longer than {SavedQuery.MaxSqlLength} characters.");

        var existing = await _db.SavedQueries
            .FirstOrDefaultAsync(q => q.UserId == userId && q.ProjectId == projectId && q.Name == name, ct);

        if (existing is not null)
        {
            existing.Sql = sql;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new DeskSavedQueryItem(existing.Id, existing.Name, existing.Sql, existing.CreatedAt, existing.UpdatedAt);
        }

        var count = await _db.SavedQueries
            .CountAsync(q => q.UserId == userId && q.ProjectId == projectId, ct);
        if (count >= SavedQuery.MaxPerUserPerProject)
            throw new InvalidOperationException(
                $"You already have {SavedQuery.MaxPerUserPerProject} saved queries in this project. " +
                "Delete one before saving another.");

        var entity = new SavedQuery { ProjectId = projectId, UserId = userId, Name = name, Sql = sql };
        _db.SavedQueries.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new DeskSavedQueryItem(entity.Id, entity.Name, entity.Sql, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task<bool> DeleteSavedQueryAsync(
        string projectId,
        string userId,
        string id,
        CancellationToken ct = default)
    {
        var deleted = await _db.SavedQueries
            .Where(q => q.Id == id && q.UserId == userId && q.ProjectId == projectId)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }

    public async Task<(int TotalQueriesExecuted, DateTime? LastQueryAt)> GetProjectQuerySummaryAsync(
        string projectId,
        CancellationToken ct = default)
    {
        var deskQueries = await _db.SqlQueryHistory.AsNoTracking()
            .CountAsync(q => q.ProjectId == projectId, ct);
        var lastQueryAt = await _db.SqlQueryHistory.AsNoTracking()
            .Where(q => q.ProjectId == projectId)
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => (DateTime?)q.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return (deskQueries, lastQueryAt);
    }
}
