using System;

namespace Namines.Core.Models.Auth;

/// <summary>
/// İşlenmiş bir Stripe webhook olayı — <b>tekrar iletimi (redelivery) tanımak için</b>.
///
/// <b>Neden gerekli (docs/last-review M-2):</b> Stripe aynı olayı başarılı bir
/// 200'den sonra bile tekrar gönderebiliyor (ağ zaman aşımı, kendi yeniden deneme
/// kuyruğu, ya da panelden elle "resend"). İşleyici olay kimliğine hiç bakmıyordu,
/// yani tekrar iletilen bir <c>customer.subscription.updated / active</c> olayı,
/// arada iptal edilmiş bir aboneliği <b>sessizce geri getirebiliyordu</b>: kullanıcı
/// ödemediği hâlde ücretli plana dönüyordu. Ters yönü de mümkün — tekrar iletilen
/// bir <c>deleted</c> olayı, yeniden abone olmuş bir kullanıcıyı Free'ye düşürürdü.
///
/// Tablo bilerek küçük: yalnızca "bu olayı gördüm" bilgisini taşıyor. Olay gövdesini
/// saklamak, müşterinin faturalama verisini bizim veritabanımıza kopyalamak olurdu.
/// </summary>
public class StripeWebhookEvent
{
    /// <summary>
    /// Stripe'ın olay kimliği (<c>evt_...</c>). <b>Birincil anahtar</b>: ikinci bir
    /// ekleme denemesi veritabanı seviyesinde çakışır, yani iki webhook isteği aynı
    /// anda gelse bile yalnızca biri işleyebilir. Uygulama içi bir "var mı?"
    /// kontrolü bu yarışı çözmezdi.
    /// </summary>
    public string Id { get; set; } = null!;

    public string EventType { get; set; } = null!;

    /// <summary>
    /// Stripe'ın olayı ÜRETTİĞİ zaman (<c>Event.Created</c>), bizim aldığımız zaman
    /// değil. <see cref="LatestProcessedForCustomer"/> karşılaştırması bunu
    /// kullanıyor; alınma zamanı sıra dışı teslimatta yanlış cevap verirdi.
    /// </summary>
    public DateTime EventCreatedAt { get; set; }

    /// <summary>Hangi Stripe müşterisiyle ilgili — sıra kontrolü müşteri başına.</summary>
    public string? StripeCustomerId { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Bu olay, işlendiği anda o müşteri için görülmüş EN YENİ olay mıydı?
    ///
    /// <b>Neden ayrı bir bayrak:</b> idempotency tek başına SIRA sorununu çözmüyor.
    /// Stripe teslimat sırasını garanti etmiyor; <c>deleted</c> olayı işlendikten
    /// sonra ondan ESKİ bir <c>active</c> olayı gelirse, kimlik farklı olduğu için
    /// idempotency onu durdurmaz ve iptal geri alınır. Bu bayrak, atlanan olayların
    /// niçin atlandığını sonradan okunabilir kılıyor.
    /// </summary>
    public bool LatestProcessedForCustomer { get; set; }
}
