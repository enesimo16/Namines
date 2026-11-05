using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Security;
using Namines.Infrastructure.Services;

namespace Namines.Infrastructure.Data;

/// <summary>SSRF politikası Infrastructure'da; Studio bağlantı dizesini yalnızca bu dar kapıdan sordurur.</summary>
public sealed class ConnectionHostInspector : IConnectionHostInspector
{
    private readonly IDbHostAccessPolicy _policy;

    public ConnectionHostInspector(IDbHostAccessPolicy policy) => _policy = policy;

    public string? FindDisallowedHost(string connectionString, string connectionDbType) =>
        DbIntrospectionService.FindDisallowedHost(_policy, connectionString, connectionDbType);
}

/// <summary>Faturalama tabloları D-2'ye kadar AuthDbContext'te; modüller bu dar kapıdan yazar.</summary>
public sealed class UsageMeterBridge : IUsageMeter
{
    private readonly AuthDbContext _context;

    public UsageMeterBridge(AuthDbContext context) => _context = context;

    public Task RecordAsync(string userId, Namines.Core.Models.Auth.UsageResource resource, decimal quantity = 1, string? contextLabel = null, CancellationToken ct = default) =>
        _context.RecordAsync(userId, resource, quantity, contextLabel, ct);

    public Task<Namines.Core.Analysis.UsageDecision> EvaluateAsync(string userId, Namines.Core.Models.Auth.UsageResource resource, decimal requested = 1, CancellationToken ct = default) =>
        _context.EvaluateAsync(userId, resource, requested, ct);
}

/// <summary>Değişiklik isteği sayımı Governance'ın (D-2); Hub vitrini yalnızca sayıyı görür.</summary>
public sealed class ChangeRequestStats : IChangeRequestStats
{
    private readonly AuthDbContext _context;

    public ChangeRequestStats(AuthDbContext context) => _context = context;

    public async Task<(int Open, int Closed)> CountAsync(string projectId, CancellationToken ct = default)
    {
        var open = await _context.ChangeRequests.AsNoTracking()
            .CountAsync(c => c.ProjectId == projectId && c.Status == Namines.Core.Enums.ChangeRequestStatus.PendingReview, ct);
        var closed = await _context.ChangeRequests.AsNoTracking()
            .CountAsync(c => c.ProjectId == projectId && c.Status != Namines.Core.Enums.ChangeRequestStatus.PendingReview, ct);
        return (open, closed);
    }
}
