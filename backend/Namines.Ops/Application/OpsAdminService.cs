using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Security;
using Namines.Ops.Domain;
using Namines.Ops.Infrastructure.Persistence;

namespace Namines.Ops.Application;

/// <summary>Yönetici olmayan aktör; controller bunu 403'e çevirir.</summary>
public sealed class OpsAdminRequiredException : UnauthorizedAccessException
{
    public OpsAdminRequiredException() : base("Admin access required.") { }
}

/// <summary>
/// Ops'un yönetim yüzeyi. Her yöntem aktörü kendisi doğrular: <c>AdminController</c>'daki denetimi
/// geçmiş sayılmaz, servis başka bir yoldan çağrılırsa da yetkisiz okuma olmasın.
/// Geri bildirim metni ve e-postası kişisel veridir; bu arayüz dışında okunmaz.
/// </summary>
public interface IOpsAdminService
{
    Task<int> CountFeedbacksAsync(string? actorUserId, CancellationToken ct = default);

    Task<FeedbackPage> ListFeedbacksAsync(string? actorUserId, string? category, int page, int pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<FeedbackView>> ListFeedbacksForUserAsync(string? actorUserId, string userId, string? email, int take, CancellationToken ct = default);

    Task<IReadOnlyList<FeedbackView>> ExportFeedbacksAsync(string? actorUserId, int take, CancellationToken ct = default);

    /// <summary>Kayıt yoksa false.</summary>
    Task<bool> DeleteFeedbackAsync(string? actorUserId, int id, CancellationToken ct = default);

    Task<IReadOnlyList<AdminFeatureFlag>> ListFlagsAsync(string? actorUserId, CancellationToken ct = default);

    /// <summary>Anahtar katalogda yoksa null.</summary>
    Task<AdminFeatureFlag?> UpdateFlagAsync(string? actorUserId, string featureKey, bool isEnabled, string mode, string? noticeMessage, CancellationToken ct = default);

    Task<IReadOnlyList<AiToolRoute>> ListRoutesAsync(string? actorUserId, CancellationToken ct = default);

    Task<AiToolRoute> UpdateRouteAsync(string? actorUserId, string toolKey, string provider, string modelName, double temperature, string? fallbackProvider, CancellationToken ct = default);
}

internal sealed class OpsAdminService : IOpsAdminService
{
    private readonly OpsDbContext _db;
    private readonly IPlatformAdminAuthority _admins;
    private readonly IFeatureFlagService _flags;
    private readonly IAiRouteCatalog _routes;

    public OpsAdminService(OpsDbContext db, IPlatformAdminAuthority admins, IFeatureFlagService flags, IAiRouteCatalog routes)
    {
        _db = db;
        _admins = admins;
        _flags = flags;
        _routes = routes;
    }

    private async Task RequireAdminAsync(string? actorUserId, CancellationToken ct)
    {
        if (!await _admins.IsPlatformAdminAsync(actorUserId, ct))
            throw new OpsAdminRequiredException();
    }

    public async Task<int> CountFeedbacksAsync(string? actorUserId, CancellationToken ct = default)
    {
        await RequireAdminAsync(actorUserId, ct);
        return await _db.Feedbacks.CountAsync(ct);
    }

    public async Task<FeedbackPage> ListFeedbacksAsync(string? actorUserId, string? category, int page, int pageSize, CancellationToken ct = default)
    {
        await RequireAdminAsync(actorUserId, ct);

        if (page < 1) page = 1;
        if (pageSize < 5) pageSize = 5;

        var query = _db.Feedbacks.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(category) && category != "all")
        {
            var wanted = category.Trim().ToLowerInvariant();
            query = query.Where(f => f.Category.ToLower() == wanted);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new FeedbackView(f.Id, f.UserId, f.Email, f.Category, f.Message, f.CreatedAt))
            .ToListAsync(ct);

        return new FeedbackPage(items, total, page, pageSize, (int)Math.Ceiling((double)total / pageSize));
    }

    public async Task<IReadOnlyList<FeedbackView>> ListFeedbacksForUserAsync(string? actorUserId, string userId, string? email, int take, CancellationToken ct = default)
    {
        await RequireAdminAsync(actorUserId, ct);
        return await _db.Feedbacks
            .AsNoTracking()
            .Where(f => f.UserId == userId || (email != null && f.Email == email))
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .Select(f => new FeedbackView(f.Id, f.UserId, f.Email, f.Category, f.Message, f.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FeedbackView>> ExportFeedbacksAsync(string? actorUserId, int take, CancellationToken ct = default)
    {
        await RequireAdminAsync(actorUserId, ct);
        return await _db.Feedbacks
            .AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .Select(f => new FeedbackView(f.Id, f.UserId, f.Email, f.Category, f.Message, f.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteFeedbackAsync(string? actorUserId, int id, CancellationToken ct = default)
    {
        await RequireAdminAsync(actorUserId, ct);
        return await _db.Feedbacks.Where(f => f.Id == id).ExecuteDeleteAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<AdminFeatureFlag>> ListFlagsAsync(string? actorUserId, CancellationToken ct = default)
    {
        await RequireAdminAsync(actorUserId, ct);
        return await _flags.GetAllFlagsAsync(ct);
    }

    public async Task<AdminFeatureFlag?> UpdateFlagAsync(string? actorUserId, string featureKey, bool isEnabled, string mode, string? noticeMessage, CancellationToken ct = default)
    {
        await RequireAdminAsync(actorUserId, ct);
        if (await _flags.GetFlagAsync(featureKey, ct) is null) return null;
        await _flags.UpdateFlagAsync(featureKey, isEnabled, mode, noticeMessage, ct);
        return await _flags.GetFlagAsync(featureKey, ct);
    }

    public async Task<IReadOnlyList<AiToolRoute>> ListRoutesAsync(string? actorUserId, CancellationToken ct = default)
    {
        await RequireAdminAsync(actorUserId, ct);
        return await _routes.GetAllRoutesAsync(ct);
    }

    public async Task<AiToolRoute> UpdateRouteAsync(string? actorUserId, string toolKey, string provider, string modelName, double temperature, string? fallbackProvider, CancellationToken ct = default)
    {
        await RequireAdminAsync(actorUserId, ct);
        await _routes.UpdateRouteAsync(toolKey, provider, modelName, temperature, fallbackProvider, ct);
        return await _routes.GetRouteAsync(toolKey, ct);
    }
}
