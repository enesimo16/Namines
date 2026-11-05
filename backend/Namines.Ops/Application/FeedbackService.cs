using Microsoft.EntityFrameworkCore;
using Namines.Ops.Domain;
using Namines.Ops.Infrastructure.Persistence;

namespace Namines.Ops.Application;

public sealed record FeedbackSubmission(string? UserId, string? Email, string? Category, string? Message);

public sealed record FeedbackView(int Id, string? UserId, string? Email, string Category, string Message, DateTime CreatedAt);

public sealed record FeedbackPage(IReadOnlyList<FeedbackView> Items, int TotalCount, int Page, int PageSize, int TotalPages);

/// <summary>Kullanıcı geri bildiriminin yaşam döngüsü: gönderme herkese açık, okuma/silme IOpsAdminService'te.</summary>
public interface IFeedbackService
{
    /// <summary>Metin çok kısaysa false; uzun metin kesilir, tanınmayan kategori "general" olur.</summary>
    Task<bool> SubmitAsync(FeedbackSubmission submission, CancellationToken ct = default);

    /// <summary>Metin ürün geri bildirimi olarak kalır, kişiyi tanımlayan alanlar gider. Hesap silme akışı için.</summary>
    Task<int> AnonymizeByUserAsync(string userId, CancellationToken ct = default);
}

internal sealed class FeedbackService : IFeedbackService
{
    public const int MinMessageLength = 3;
    public const int MaxMessageLength = 4000;

    private readonly OpsDbContext _db;

    public FeedbackService(OpsDbContext db) => _db = db;

    public async Task<bool> SubmitAsync(FeedbackSubmission submission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var message = submission.Message?.Trim() ?? string.Empty;
        if (message.Length < MinMessageLength) return false;
        if (message.Length > MaxMessageLength) message = message[..MaxMessageLength];

        var category = submission.Category is "bug" or "idea" or "general" ? submission.Category : "general";

        _db.Feedbacks.Add(new Feedback
        {
            UserId = submission.UserId,
            Email = string.IsNullOrWhiteSpace(submission.Email) ? null : submission.Email.Trim(),
            Category = category,
            Message = message,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<int> AnonymizeByUserAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return _db.Feedbacks
            .Where(f => f.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Email, (string?)null)
                .SetProperty(f => f.UserId, (string?)null), ct);
    }
}
