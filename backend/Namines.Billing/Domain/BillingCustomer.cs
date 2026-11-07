namespace Namines.Billing.Domain;

/// <summary>
/// Kullanıcı ile Stripe müşterisi arasındaki eşleme (<c>billing.customers</c>). Eskiden
/// <c>AspNetUsers.StripeCustomerId</c> idi: kimlik tablosu ödeme sağlayıcısının kimliğini taşıyordu.
/// Müşteri kimliği benzersiz: iki kullanıcı aynı Stripe müşterisine bağlanırsa webhook yanlış kişiyi yükseltirdi.
/// </summary>
public class BillingCustomer
{
    public string UserId { get; set; } = null!;

    public string StripeCustomerId { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
