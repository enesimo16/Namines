using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Stripe;
using Namines.Core.Analysis;
// Stripe SDK'sında da PlanTier var; kendi katman enum'umuzu takma adla alıyoruz.
using PlanTier = Namines.Core.Analysis.PlanTier;
using System.Linq;

namespace Namines.API.Controllers;

/// <summary>
/// Stripe Webhook Listener — the cryptographic security perimeter.
/// 
/// Security Architecture:
/// - Raw request body is read BEFORE the framework deserializes it,
///   because Stripe signs the raw bytes. Any transformation breaks the signature.
/// - ConstructEvent() validates the HMAC-SHA256 signature using the webhook signing secret.
/// - If the signature is invalid, we return 400 immediately — no event processing.
/// - The endpoint is [AllowAnonymous] because Stripe cannot authenticate via JWT.
///   Security is enforced exclusively through signature validation.
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class StripeWebhookController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AuthDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        UserManager<ApplicationUser> userManager,
        AuthDbContext db,
        IConfiguration config,
        ILogger<StripeWebhookController> logger)
    {
        _userManager = userManager;
        _db = db;
        _config = config;
        _logger = logger;
    }

    // POST /api/webhooks/stripe
    // Stripe calls this endpoint for every event.
    // We validate the signature, then process only the two events we care about.
    [HttpPost("stripe")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [Consumes("application/json")]
    public async Task<IActionResult> HandleStripeWebhook()
    {
        // Read the raw request body — must be done BEFORE any framework binding
        // because Stripe signs the raw bytes
        string json;
        using (var reader = new System.IO.StreamReader(Request.Body, leaveOpen: false))
        {
            json = await reader.ReadToEndAsync();
        }

        var webhookSecret = _config["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            _logger.LogError("[STRIPE WEBHOOK] Stripe:WebhookSecret is not configured. Rejecting all webhook calls.");
            return StatusCode(500, "Webhook secret not configured.");
        }

        // ── CRYPTOGRAPHIC SIGNATURE VALIDATION ────────────────────────────────
        // If the signature header is missing, invalid, or timestamp is expired,
        // ConstructEvent() throws and we return 400. No event is processed.
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                webhookSecret,
                throwOnApiVersionMismatch: false
            );
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("[STRIPE WEBHOOK] ⚠ Signature validation FAILED: {Message}. Rejecting with 400.", ex.Message);
            return BadRequest(new { error = "Invalid Stripe signature." });
        }

        _logger.LogInformation("[STRIPE WEBHOOK] ✓ Verified event: {Type} | ID: {Id}", stripeEvent.Type, stripeEvent.Id);

        // ── EVENT DISPATCH ─────────────────────────────────────────────────────
        // ── TEKRAR İLETİM (IDEMPOTENCY) ────────────────────────────────────
        //
        // <b>Bulunan açık (docs/last-review M-2):</b> Stripe aynı olayı başarılı bir
        // 200'den sonra bile tekrar gönderebiliyor (ağ zaman aşımı, kendi yeniden
        // deneme kuyruğu, panelden elle "resend"). Olay kimliğine hiç
        // bakılmıyordu: tekrar iletilen bir <c>updated / active</c> olayı, arada iptal
        // edilmiş bir aboneliği sessizce GERİ GETİRİYORDU — kullanıcı ödemediği hâlde
        // ücretli plana dönüyordu.
        var customerId = CustomerIdOf(stripeEvent);

        if (await _db.StripeWebhookEvents.AnyAsync(e => e.Id == stripeEvent.Id))
        {
            _logger.LogInformation(
                "[STRIPE WEBHOOK] Olay {Id} ZATEN islenmis, atlaniyor (tekrar iletim).", stripeEvent.Id);
            return Ok(new { received = true, duplicate = true });
        }

        // ── ATOMİK İŞLEME ──────────────────────────────────────────────────
        //
        // <b>Bulunan açık (Oct-2 security A-1):</b> tekrar-iletim kaydı işleyiciden
        // ÖNCE ayrı bir SaveChanges ile yazılıyordu. İşleyici (ör. kullanıcı
        // güncellemesi) patlarsa kayıt kalıyor, Stripe'ın yeniden denemesi "zaten
        // işlenmiş" diye 200 alıyor ve ÖDEME YAPAN müşteri hiç yükseltilmiyordu.
        // Üstüne her `DbUpdateException` "yarış" sayılıyordu: bağlantı kopması da
        // sessizce "duplicate" oluyordu.
        //
        // Şimdi kayıt + işleyici TEK veritabanı işleminde: işleyici başarısız olursa
        // kayıt da geri alınır, 500 döner ve Stripe aynı olayı yeniden dener. Yalnızca
        // GERÇEK bir benzersizlik ihlali (SQLSTATE 23505) "başka istek aldı" demektir.
        WebhookOutcome outcome;
        try
        {
            // EnableRetryOnFailure açık: elle açılan işlem bir yürütme stratejisinin
            // içinde olmak ZORUNDA. Strateji yeniden denerse temsilci baştan çalışır,
            // o yüzden önceki denemeden kalan izleyici durumu temizleniyor.
            var strategy = _db.Database.CreateExecutionStrategy();
            outcome = await strategy.ExecuteAsync(() => ProcessInTransactionAsync(stripeEvent, customerId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[STRIPE WEBHOOK] Olay {Id} ({Type}) islenemedi; kayit geri alindi, Stripe yeniden deneyecek.",
                stripeEvent.Id, stripeEvent.Type);
            return StatusCode(500, new { error = "Webhook processing failed." });
        }

        return outcome switch
        {
            WebhookOutcome.Duplicate => Ok(new { received = true, duplicate = true }),
            WebhookOutcome.Stale => Ok(new { received = true, stale = true }),
            _ => Ok(new { received = true }),
        };
    }

    private enum WebhookOutcome { Processed, Duplicate, Stale }

    /// <summary>
    /// Olay kaydını ve işleyiciyi tek işlemde çalıştırır. Hata → işlem geri alınır,
    /// istisna dışarı çıkar (500).
    /// </summary>
    private async Task<WebhookOutcome> ProcessInTransactionAsync(Event stripeEvent, string? customerId)
    {
        _db.ChangeTracker.Clear();

        await using var tx = await _db.Database.BeginTransactionAsync();

        // ── SIRA DIŞI TESLİMAT ───────────────────────────────────────
        //
        // Idempotency tek başına yetmiyor: Stripe teslimat SIRASINI garanti
        // etmiyor. <c>deleted</c> işlendikten sonra ondan ESKİ bir <c>active</c>
        // olayı gelirse kimlik farklı olduğu için idempotency onu durdurmaz ve
        // iptal geri alınır. Bu yüzden abonelik DURUMUNU değiştiren olaylar,
        // o müşteri için daha yeni bir olay işlenmişse UYGULANMIYOR.
        //
        // Olayın ÜRETİLME zamanı (<c>Event.Created</c>) kullanılıyor, bizim aldığımız
        // zaman değil — alınma zamanı sıra dışı teslimatta tam olarak yanlış cevabı verir.
        var isLatest = true;
        if (!string.IsNullOrEmpty(customerId))
        {
            var newestProcessed = await _db.StripeWebhookEvents
                .Where(e => e.StripeCustomerId == customerId)
                .MaxAsync(e => (DateTime?)e.EventCreatedAt);

            isLatest = newestProcessed is null || stripeEvent.Created >= newestProcessed;
        }

        // Kayıt İŞLEMEDEN ÖNCE (ama aynı işlemde) yazılıyor: birincil anahtar
        // çakışması eşzamanlı ikinci isteği burada durdurur — Postgres ikincinin
        // eklemesini birincinin commit/rollback'ine kadar bekletir.
        _db.StripeWebhookEvents.Add(new StripeWebhookEvent
        {
            Id = stripeEvent.Id,
            EventType = stripeEvent.Type,
            EventCreatedAt = stripeEvent.Created,
            StripeCustomerId = customerId,
            LatestProcessedForCustomer = isLatest,
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) when (DbUniqueViolation.IsUniqueViolation(ex))
        {
            // Yarış: aynı olayı başka bir istek bizden önce yazdı. İşlemeden
            // 200 dönülüyor — Stripe'ın yeniden denemesine gerek yok.
            _logger.LogInformation(
                "[STRIPE WEBHOOK] Olay {Id} esZAMANLI olarak baska bir istek tarafindan alindi.", stripeEvent.Id);
            return WebhookOutcome.Duplicate;
        }

        if (!isLatest)
        {
            _logger.LogWarning(
                "[STRIPE WEBHOOK] Olay {Id} ({Type}) SIRA DISI geldi: {Customer} icin daha yeni bir olay " +
                "zaten islenmis. Abonelik durumu DEGISTIRILMIYOR.",
                stripeEvent.Id, stripeEvent.Type, customerId);
            await tx.CommitAsync();
            return WebhookOutcome.Stale;
        }

        // ── EVENT DISPATCH ─────────────────────────────────────────────────
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompleted(stripeEvent);
                break;

            case "customer.subscription.deleted":
                await HandleSubscriptionDeleted(stripeEvent);
                break;

            case "customer.subscription.updated":
                await HandleSubscriptionUpdated(stripeEvent);
                break;

            default:
                // Acknowledge the event but take no action — avoids Stripe retries
                _logger.LogDebug("[STRIPE WEBHOOK] Unhandled event type: {Type}", stripeEvent.Type);
                break;
        }

        await tx.CommitAsync();
        return WebhookOutcome.Processed;
    }

    /// <summary>
    /// Olayın ilgili olduğu Stripe müşteri kimliği — sıra kontrolü müşteri başına.
    ///
    /// Tanınmayan olay türünde <c>null</c>: o olay abonelik durumunu değiştirmiyor,
    /// dolayısıyla sırası da önemli değil. Uydurma bir kimlik yazmak, ilgisiz
    /// olayları aynı sıra zincirine sokup meşru olayları "eski" saydırırdı.
    /// </summary>
    private static string? CustomerIdOf(Event stripeEvent) => stripeEvent.Data.Object switch
    {
        Stripe.Subscription subscription => subscription.CustomerId,
        Stripe.Checkout.Session session => session.CustomerId,
        _ => null,
    };

    // ── checkout.session.completed ─────────────────────────────────────────────
    // User completed payment. Activate their subscription and store Stripe IDs.
    private async Task HandleCheckoutCompleted(Event stripeEvent)
    {
        var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
        if (session == null) return;

        // We stored our internal user ID in ClientReferenceId at checkout creation
        var userId = session.ClientReferenceId;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogError("[STRIPE WEBHOOK] checkout.session.completed: Missing ClientReferenceId. Cannot identify user.");
            return;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogError("[STRIPE WEBHOOK] checkout.session.completed: User {UserId} not found.", userId);
            return;
        }

        // Retrieve the subscription to get period end date AND which price was
        // bought — Checkout only tells us a session happened, not which plan.
        DateTime? periodEnd = null;
        string? planCode = null;
        if (!string.IsNullOrEmpty(session.SubscriptionId))
        {
            try
            {
                var subService = new SubscriptionService();
                var subscription = await subService.GetAsync(session.SubscriptionId);
                periodEnd = subscription.CurrentPeriodEnd;
                planCode = ResolvePlanCode(subscription.Items?.Data?.FirstOrDefault()?.Price?.Id);
            }
            catch (Exception ex)
            {
                // Yutulmuyor: ödeme alındı ama plan okunamadıysa kullanıcıyı PlanCode'suz
                // "ücretli" bırakmak yerine istek 500 dönüp Stripe'ın yeniden denemesine
                // izin veriyoruz (kayıt işlemle birlikte geri alınır).
                _logger.LogWarning("[STRIPE WEBHOOK] Could not fetch subscription details: {Message}", ex.Message);
                throw;
            }
        }

        // Update user entity — elevate to paid plan
        user.StripeCustomerId     = session.CustomerId;
        user.StripeSubscriptionId = session.SubscriptionId;
        user.SubscriptionStatus   = "active";
        user.CurrentPeriodEnd     = periodEnd;
        user.PlanCode             = planCode;
        user.Type                 = UserType.Corporate; // Elevate to paid

        var result = await _userManager.UpdateAsync(user);
        ThrowIfFailed(result, "elevate", userId);
        _logger.LogInformation("[STRIPE WEBHOOK] ✅ User {UserId} elevated to Pro Member. Subscription: {SubId}", userId, session.SubscriptionId);
    }

    // ── customer.subscription.deleted ─────────────────────────────────────────
    // Subscription was canceled (by user or due to payment failure).
    // Downgrade user back to Free tier immediately.
    private async Task HandleSubscriptionDeleted(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (subscription == null) return;

        // Find user by their Stripe Customer ID
        var user = _userManager.Users.FirstOrDefault(u => u.StripeCustomerId == subscription.CustomerId);
        if (user == null)
        {
            _logger.LogWarning("[STRIPE WEBHOOK] customer.subscription.deleted: No user found for CustomerId {CustomerId}.", subscription.CustomerId);
            return;
        }

        user.SubscriptionStatus   = "canceled";
        user.StripeSubscriptionId = null;
        user.CurrentPeriodEnd     = null;
        user.PlanCode             = null;
        user.Type                 = UserType.Individual; // Downgrade to Free

        var result = await _userManager.UpdateAsync(user);
        ThrowIfFailed(result, "downgrade", user.Id);
        _logger.LogInformation("[STRIPE WEBHOOK] 🔴 User {UserId} downgraded to Free. Subscription canceled.", user.Id);
    }

    // ── customer.subscription.updated ─────────────────────────────────────────
    // Handles renewals, payment failures (past_due), and reactivations.
    private async Task HandleSubscriptionUpdated(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        if (subscription == null) return;

        var user = _userManager.Users.FirstOrDefault(u => u.StripeCustomerId == subscription.CustomerId);
        if (user == null) return;

        user.SubscriptionStatus = subscription.Status; // active | past_due | canceled | etc.
        user.CurrentPeriodEnd   = subscription.CurrentPeriodEnd;
        // Plan değişebilir (Pro→Team upgrade portal üzerinden): her güncellemede
        // fiyattan yeniden okunuyor, yalnızca ilk abonelikte değil.
        user.PlanCode           = ResolvePlanCode(subscription.Items?.Data?.FirstOrDefault()?.Price?.Id);

        // Restore paid access if payment recovered
        if (subscription.Status == "active")
            user.Type = UserType.Corporate;
        // Soft downgrade on past_due — keep access for now, will hard downgrade on deletion
        else if (subscription.Status is "canceled" or "unpaid")
            user.Type = UserType.Individual;

        ThrowIfFailed(await _userManager.UpdateAsync(user), "update", user.Id);
        _logger.LogInformation("[STRIPE WEBHOOK] 🔄 Subscription updated for user {UserId}: {Status}", user.Id, subscription.Status);
    }

    /// <summary>
    /// Başarısız bir Identity sonucu sessizce loglanıp yutulmuyor: istisna işlemi geri
    /// alır, istek 500 döner ve Stripe olayı yeniden gönderir.
    /// </summary>
    private static void ThrowIfFailed(IdentityResult result, string action, string userId)
    {
        if (result.Succeeded) return;

        throw new InvalidOperationException(
            $"Failed to {action} user {userId}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }

    /// <summary>
    /// Stripe fiyat ID'sinden plan kodunu ("pro" | "team") çıkarır.
    ///
    /// <b>Eşleşmeyen ya da eksik fiyat "pro" sayılır, null DEĞİL.</b> Webhook
    /// zaten aboneliği "active" işaretlemiş durumda; kullanıcıyı ödemesiz Free'ye
    /// düşürmek yerine daha ucuz plana (Pro) yerleştirmek, ödeyen birine hizmet
    /// vermeme riskini azaltıyor. Yapılandırma hatası varsa loglanıyor.
    ///
    /// <b>Team'in AYLIK ve YILLIK fiyatı ayrı iki Stripe kimliği.</b> Yalnızca
    /// aylığa bakmak, yıllık Team alan bir müşteriyi Pro'ya düşürürdü — yani en
    /// çok ödeyen müşteri en az hakkı alırdı.
    /// </summary>
    private string ResolvePlanCode(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId))
        {
            _logger.LogWarning("[STRIPE WEBHOOK] Subscription has no price line item; defaulting PlanCode to 'pro'.");
            return "pro";
        }

        var teamPriceIds = PricingCatalog.For(PlanTier.Team)
            .Select(p => _config[p.ConfigKey])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        // YAPILANDIRMA EKSIKLIGI ile "fiyat gercekten Pro" AYRI seylerdir
        // (docs/last-review L-3).
        //
        // Team fiyat kimlikleri hic tanimli degilse esleme MATEMATIKSEL OLARAK
        // calisamaz: her Team musterisi sessizce "pro" olur, yani EN COK odeyen
        // musteri EN AZ hakki alir. Bu bir dagitim hatasi ve WARNING olarak
        // gecmemeli — uyari akisinda kaybolur, fatura farki ise aylar sonra
        // fark edilir.
        if (teamPriceIds.Count == 0)
        {
            _logger.LogError(
                "[STRIPE WEBHOOK] Team fiyat kimlikleri yapilandirilmamis (Stripe:*TeamPriceId). " +
                "Her abonelik 'pro' olarak cozulecek, yani Team musterileri EKSIK hak alir. " +
                "PricingCatalog'un bekledigi anahtarlari tanimlayin.");

            return "pro";
        }

        if (teamPriceIds.Any(id => id == priceId)) return "team";

        // Fiyat tanimli listelerden hicbiriyle eslesmiyor: musteri gercekten Pro
        // aldi ya da katalogda olmayan bir fiyat kullaniliyor. "pro"ya dusmek
        // bilincli (odeyen birine hizmet vermemektense daha az hak vermek), ama
        // artik hangi fiyatin taninmadigi da kayda giriyor.
        _logger.LogWarning(
            "[STRIPE WEBHOOK] Fiyat {PriceId} katalogda yok; PlanCode 'pro' olarak cozuldu.", priceId);

        return "pro";
    }
}
