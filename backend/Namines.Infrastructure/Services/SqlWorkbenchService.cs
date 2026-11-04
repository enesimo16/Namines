using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Namines.Desk.Abstractions;
using Namines.Desk.Application;

namespace Namines.Infrastructure.Services;

/// <summary>Bir sorgu geçmişi kaydının okunabilir hâli.</summary>
public sealed record SqlHistoryItem(
    string Id, string Sql, bool Truncated, int RowCount, bool Succeeded,
    string? ErrorMessage, int DurationMs, DateTime CreatedAt);

/// <summary>Kaydedilmiş bir sorgunun okunabilir hâli.</summary>
public sealed record SavedQueryItem(
    string Id, string Name, string Sql, DateTime CreatedAt, DateTime UpdatedAt);

public interface ISqlWorkbenchService
{
    Task RecordAsync(
        string projectId, string userId, string sql, bool succeeded,
        int rowCount, string? errorMessage, int durationMs, CancellationToken ct);

    Task<IReadOnlyList<SqlHistoryItem>> GetHistoryAsync(
        string projectId, string userId, int limit, CancellationToken ct);

    Task<int> ClearHistoryAsync(string projectId, string userId, CancellationToken ct);

    Task<IReadOnlyList<SavedQueryItem>> GetSavedAsync(
        string projectId, string userId, CancellationToken ct);

    /// <summary>Kaydeder ya da aynı addaki kaydı GÜNCELLER.</summary>
    Task<SavedQueryItem> SaveAsync(
        string projectId, string userId, string name, string sql, CancellationToken ct);

    /// <summary>Siler. Başkasının kaydına dokunamaz — bulunamadı döner.</summary>
    Task<bool> DeleteSavedAsync(string projectId, string userId, string id, CancellationToken ct);
}

public sealed class SqlWorkbenchService : ISqlWorkbenchService
{
    private readonly IDeskApplicationService _deskApp;

    [ActivatorUtilitiesConstructor]
    public SqlWorkbenchService(IDeskModule deskModule)
    {
        ArgumentNullException.ThrowIfNull(deskModule);
        _deskApp = deskModule.Application;
    }

    public SqlWorkbenchService(IDeskApplicationService deskApp)
    {
        _deskApp = deskApp ?? throw new ArgumentNullException(nameof(deskApp));
    }

    public Task RecordAsync(
        string projectId, string userId, string sql, bool succeeded,
        int rowCount, string? errorMessage, int durationMs, CancellationToken ct) =>
        _deskApp.RecordSqlHistoryAsync(
            projectId, userId, sql, succeeded, rowCount, errorMessage, durationMs, ct);

    public async Task<IReadOnlyList<SqlHistoryItem>> GetHistoryAsync(
        string projectId, string userId, int limit, CancellationToken ct)
    {
        var items = await _deskApp.GetSqlHistoryAsync(projectId, userId, limit, ct);
        return items.Select(h => new SqlHistoryItem(
            h.Id, h.Sql, h.Truncated, h.RowCount, h.Succeeded,
            h.ErrorMessage, h.DurationMs, h.CreatedAt)).ToList();
    }

    public Task<int> ClearHistoryAsync(string projectId, string userId, CancellationToken ct) =>
        _deskApp.ClearSqlHistoryAsync(projectId, userId, ct);

    public async Task<IReadOnlyList<SavedQueryItem>> GetSavedAsync(
        string projectId, string userId, CancellationToken ct)
    {
        var items = await _deskApp.GetSavedQueriesAsync(projectId, userId, ct);
        return items.Select(q => new SavedQueryItem(q.Id, q.Name, q.Sql, q.CreatedAt, q.UpdatedAt)).ToList();
    }

    public async Task<SavedQueryItem> SaveAsync(
        string projectId, string userId, string name, string sql, CancellationToken ct)
    {
        var saved = await _deskApp.SaveQueryAsync(projectId, userId, name, sql, ct);
        return new SavedQueryItem(saved.Id, saved.Name, saved.Sql, saved.CreatedAt, saved.UpdatedAt);
    }

    public Task<bool> DeleteSavedAsync(
        string projectId, string userId, string id, CancellationToken ct) =>
        _deskApp.DeleteSavedQueryAsync(projectId, userId, id, ct);
}
