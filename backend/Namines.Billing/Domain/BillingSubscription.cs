namespace Namines.Billing.Domain;

/// <summary>
/// Kullanıcının güncel aboneliği (<c>billing.subscriptions</c>, kullanıcı başına en çok bir satır).
/// Eskiden <c>AspNetUsers</c> üzerindeki dört alandı (abonelik kimliği, durum, plan kodu, dönem sonu).
/// Plan kullanıcıya bağlı: ekip planı, kurucunun aboneliğinden okunur (org kendi aboneliğini taşımıyor).
/// </summary>
public class BillingSubscription
{
    public string UserId { get; set; } = null!;

    public string? StripeSubscriptionId { get; set; }

    /// <summary>Stripe durumu: active | past_due | canceled | unpaid | ...</summary>
    public string? Status { get; set; }

    /// <summary>"pro" | "team"; durum tek başına Pro ile Team'i ayırt edemez.</summary>
    public string? PlanCode { get; set; }

    public DateTime? CurrentPeriodEnd { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
