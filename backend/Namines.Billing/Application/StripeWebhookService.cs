using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Billing.Domain;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Stripe;
// Stripe SDK'sında da PlanTier var; kendi katman enum'umuzu takma adla alıyoruz.
using PlanTier = Namines.Core.Analysis.PlanTier;

namespace Namines.Billing.Application;

/// <summary>Stripe'tan okunan abonelik özeti: ağ çağrısı sınır kapısı, testte sahte ile değiştirilebilir.</summary>
internal sealed record StripeSubscriptionInfo(DateTime? CurrentPeriodEnd, string? PriceId, string? Status = null);

internal interface IStripeGateway
{
    Task<StripeSubscriptionInfo> GetSubscriptionAsync(string subscriptionId, CancellationToken ct);
}

/// <summary>
/// Stripe webhook'u. Her olay: imza doğrulanır (zorunlu), olay kimliği birincil anahtar olarak kayda yazılır,
/// etki aynı işlemde uygulanır. Kayıt ile etki birlikte geri alınır: işleyici patlarsa kayıt kalmaz, Stripe
/// yeniden dener ve ödeme yapan müşteri "zaten işlendi" diye yutulmaz.
/// </summary>
internal sealed class StripeWebhookService : IStripeWebhookService
{
    private readonly IBillingData _db;
    private readonly IIdentityDirectory _identity;
    private readonly ICrossModuleTransaction _transaction;
    private readonly IConfiguration _config;
    private readonly IStripeGateway _stripe;
    private readonly ILogger<StripeWebhookService> _logger;

    public StripeWebhookService(
        IBillingData db, IIdentityDirectory identity, ICrossModuleTransaction transaction,
        IConfiguration config, IStripeGateway stripe, ILogger<StripeWebhookService> logger)
    {
        _db = db;
        _identity = identity;
        _transaction = transaction;
        _config = config;
        _stripe = stripe;
        _logger = logger;
    }

    private enum Outcome { Processed, Duplicate, Stale }

    private sealed class DuplicateEventException : Exception
    {
    }

    public async Task<WebhookResult> HandleAsync(string payload, string? signatureHeader, CancellationToken ct = default)
    {
        var webhookSecret = _config["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            _logger.LogError("[STRIPE WEBHOOK] Stripe:WebhookSecret is not configured. Rejecting all webhook calls.");
            return new WebhookResult(500, "Webhook secret not configured.");
        }

        // Başlık hiç yoksa Stripe.net null üzerinde patlar (500); eksik imza geçersiz imzayla aynı: 400.
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return new WebhookResult(400, new { error = "Invalid Stripe signature." });

        // İmza doğrulaması: başlık yoksa, geçersizse ya da zaman damgası eskiyse ConstructEvent atar ve hiçbir olay işlenmez.
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, webhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (Exception ex) when (ex is StripeException or NullReferenceException or System.Text.Json.JsonException or Newtonsoft.Json.JsonException or InvalidCastException)
        {
            // İmza geçerli olsa bile gövde ayrıştırılamıyorsa (boş, data.object eksik) Stripe.net StripeException yerine
            // NullReferenceException atar; istemci hatasıdır, 500 olmamalı ve Stripe bozuk yükü yeniden denememeli.
            _logger.LogWarning("[STRIPE WEBHOOK] Signature or payload rejected: {Type}: {Message}. Rejecting with 400.", ex.GetType().Name, ex.Message);
            return new WebhookResult(400, new { error = "Invalid Stripe signature." });
        }

        // İmzalı ama içi boş yük (data.object yok): işlenecek bir şey yok ve yeniden denemek de çözmez.
        if (stripeEvent is null || string.IsNullOrEmpty(stripeEvent.Id) || stripeEvent.Data?.Object is null)
            return new WebhookResult(400, new { error = "Invalid Stripe event payload." });

        _logger.LogInformation("[STRIPE WEBHOOK] Verified event: {Type} | ID: {Id}", stripeEvent.Type, stripeEvent.Id);

        // Tekrar iletim: Stripe aynı olayı başarılı 200'den sonra da gönderebilir; kimliğe bakılmasa iptal edilmiş
        // abonelik sessizce geri gelirdi (docs/last-review M-2). Hızlı yol; asıl koruma birincil anahtar.
        if (await _db.StripeWebhookEvents.AsNoTracking().AnyAsync(e => e.Id == stripeEvent.Id, ct))
        {
            _logger.LogInformation("[STRIPE WEBHOOK] Event {Id} already processed, skipping (redelivery).", stripeEvent.Id);
            return new WebhookResult(200, new { received = true, duplicate = true });
        }

        var customerId = CustomerIdOf(stripeEvent);
        Outcome outcome;
        try
        {
            outcome = Outcome.Processed;
            await _transaction.ExecuteAsync(async token => outcome = await ProcessAsync(stripeEvent, customerId, token), ct);
        }
        catch (DuplicateEventException)
        {
            // Yarış: aynı olayı başka bir istek bizden önce yazdı. İşlenmeden 200 dönülür.
            return new WebhookResult(200, new { received = true, duplicate = true });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[STRIPE WEBHOOK] Event {Id} ({Type}) could not be processed; record rolled back, Stripe will retry.",
                stripeEvent.Id, stripeEvent.Type);
            return new WebhookResult(500, new { error = "Webhook processing failed." });
        }

        return outcome switch
        {
            Outcome.Duplicate => new WebhookResult(200, new { received = true, duplicate = true }),
            Outcome.Stale => new WebhookResult(200, new { received = true, stale = true }),
            _ => new WebhookResult(200, new { received = true }),
        };
    }

    private async Task<Outcome> ProcessAsync(Event stripeEvent, string? customerId, CancellationToken ct)
    {
        // Yürütme stratejisi işlemi yeniden denerse önceki denemeden kalan izleyici durumu temizlenir.
        _db.ChangeTracker.Clear();

        // Aynı müşterinin olayları SERİ işlenir: sıra kontrolü ile yazma arasında boşluk kalırsa iki farklı olay
        // (updated@t, deleted@t+1) birbirini görmeden "ben en yenisiyim" der ve iptal geri alınabilirdi. Kilit işlem
        // sonunda kendiliğinden düşer ve kontrol KİLİT ALTINDA yapılır.
        if (!string.IsNullOrEmpty(customerId) && _db.Database.IsNpgsql())
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"stripe-customer:" + customerId}, 0))", ct);

        var writesState = stripeEvent.Type is "checkout.session.completed" or "customer.subscription.updated" or "customer.subscription.deleted";
        var isCheckout = stripeEvent.Type == "checkout.session.completed";

        // Sıra dışı teslimat: Stripe sırayı garanti etmiyor. Karşılaştırma yalnızca o müşteri için UYGULANMIŞ durum
        // olaylarıyla yapılır ve Stripe'ın olayı ÜRETME zamanına göredir (aldığımız zamana göre değil).
        // Eşleme (checkout) hiçbir zaman "eski" sayılıp atılmaz: müşteri henüz bağlı değilse ödeme yapan kullanıcıyı
        // bağlayan tek olay budur ve ondan sonra Stripe yeniden denemez. Bağlı olmayan müşterinin güncelleme/silme olayı
        // yazacak satır bulamaz ama sıra hesabına GİRER: aksi halde eşlemeden önce gelen bir iptal unutulur ve sonradan
        // gelen daha eski bir checkout kullanıcıyı yeniden "active" yapardı. Checkout böyle bir olayı gördüyse
        // (newerStateSeen) eşlemeyi kurar ama durumu sabit varsaymaz; gerçek durumu Stripe'tan okur.
        var mapped = !string.IsNullOrEmpty(customerId)
            && await _db.Customers.AsNoTracking().AnyAsync(c => c.StripeCustomerId == customerId, ct);
        var isLatest = true;
        if (!string.IsNullOrEmpty(customerId) && writesState)
        {
            var newest = await _db.StripeWebhookEvents
                .Where(e => e.StripeCustomerId == customerId && e.LatestProcessedForCustomer)
                .MaxAsync(e => (DateTime?)e.EventCreatedAt, ct);
            isLatest = newest is null || stripeEvent.Created >= newest;
        }

        var apply = writesState && (isCheckout ? (!mapped || isLatest) : (mapped && isLatest));
        var newerStateSeen = isCheckout && !mapped && !isLatest;
        // Daha yeni olay bir iptalse checkout durumu "iptal" olarak yazar (kullanıcı ücretli kalmaz, durum satırı da doğru olur).
        var newerWasDeletion = false;
        if (newerStateSeen)
            newerWasDeletion = await _db.StripeWebhookEvents
                .Where(e => e.StripeCustomerId == customerId && e.LatestProcessedForCustomer)
                .OrderByDescending(e => e.EventCreatedAt)
                .Select(e => e.EventType == "customer.subscription.deleted")
                .FirstOrDefaultAsync(ct);

        // Kayıt işlemeden ÖNCE (ama aynı işlemde): birincil anahtar çakışması eşzamanlı ikinci isteği burada
        // durdurur, PostgreSQL ikincinin eklemesini birincinin commit/rollback'ine kadar bekletir.
        _db.StripeWebhookEvents.Add(new StripeWebhookEvent
        {
            Id = stripeEvent.Id,
            EventType = stripeEvent.Type,
            EventCreatedAt = stripeEvent.Created,
            StripeCustomerId = customerId,
            // "Sıra hesabına girer": bağlı olmayan müşterinin güncelleme/silmesi de dahil (etkisiz olsa bile zamanı kalır).
            // Bağlı müşterinin eski (stale) olayı sayılmaz.
            LatestProcessedForCustomer = writesState && (!mapped || isLatest),
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicateEventException();
        }

        if (writesState && mapped && !isLatest)
        {
            _logger.LogWarning(
                "[STRIPE WEBHOOK] Event {Id} ({Type}) arrived out of order for {Customer}; subscription state NOT changed.",
                stripeEvent.Id, stripeEvent.Type, customerId);
            return Outcome.Stale;
        }

        if (!apply) return Outcome.Processed;

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync(stripeEvent, newerStateSeen, newerWasDeletion, ct);
                break;
            case "customer.subscription.deleted":
                await HandleSubscriptionDeletedAsync(stripeEvent, ct);
                break;
            case "customer.subscription.updated":
                await HandleSubscriptionUpdatedAsync(stripeEvent, ct);
                break;
            default:
                // Kabul edilir ama işlem yapılmaz: Stripe'ın yeniden denemesini önler.
                _logger.LogDebug("[STRIPE WEBHOOK] Unhandled event type: {Type}", stripeEvent.Type);
                break;
        }

        return Outcome.Processed;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        DbException { SqlState: "23505" } => true,
        DbException { ErrorCode: 19 } => true,
        _ => false,
    };

    private static string? CustomerIdOf(Event stripeEvent) => stripeEvent.Data.Object switch
    {
        Subscription subscription => subscription.CustomerId,
        Stripe.Checkout.Session session => session.CustomerId,
        _ => null,
    };

    private async Task<string?> UserIdByCustomerAsync(string? customerId, CancellationToken ct) =>
        string.IsNullOrEmpty(customerId)
            ? null
            : await _db.Customers.Where(c => c.StripeCustomerId == customerId).Select(c => c.UserId).FirstOrDefaultAsync(ct);

    private async Task<BillingSubscription> SubscriptionOfAsync(string userId, CancellationToken ct)
    {
        var row = await _db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (row is not null) return row;
        row = new BillingSubscription { UserId = userId };
        _db.Subscriptions.Add(row);
        return row;
    }

    private async Task HandleCheckoutCompletedAsync(Event stripeEvent, bool newerStateSeen, bool newerWasDeletion, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Stripe.Checkout.Session session) return;

        // Dahili kullanıcı kimliğini ödeme oturumu açılırken ClientReferenceId'ye yazmıştık.
        var userId = session.ClientReferenceId;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogError("[STRIPE WEBHOOK] checkout.session.completed: Missing ClientReferenceId. Cannot identify user.");
            return;
        }

        if (await _identity.GetUserFactsAsync(userId, ct) is null)
        {
            _logger.LogError("[STRIPE WEBHOOK] checkout.session.completed: User {UserId} not found.", userId);
            return;
        }

        // Checkout yalnızca bir oturumun olduğunu söyler: dönem sonu ve hangi fiyatın alındığı abonelikten okunur.
        // Hata yutulmaz: ödeme alındı ama plan okunamadıysa kullanıcıyı planı belirsiz bırakmak yerine istek 500
        // dönüp Stripe yeniden dener (kayıt işlemle birlikte geri alınır).
        DateTime? periodEnd = null;
        string? planCode = null;
        string? stripeStatus = null;
        if (!string.IsNullOrEmpty(session.SubscriptionId))
        {
            var info = await _stripe.GetSubscriptionAsync(session.SubscriptionId, ct);
            periodEnd = info.CurrentPeriodEnd;
            planCode = ResolvePlanCode(info.PriceId);
            stripeStatus = info.Status;
        }

        if (!string.IsNullOrEmpty(session.CustomerId))
        {
            // Aynı Stripe müşterisi başka bir kullanıcıya bağlıysa benzersizlik ihlali 500 üretir ve Stripe sonsuza
            // kadar yeniden dener; çakışma kayda geçer, bağlantı kurulmaz (yeniden deneme bu durumu düzeltmez).
            var heldByOther = await _db.Customers.AsNoTracking()
                .AnyAsync(c => c.StripeCustomerId == session.CustomerId && c.UserId != userId, ct);
            if (heldByOther)
            {
                _logger.LogError(
                    "[STRIPE WEBHOOK] Stripe customer {Customer} is already linked to another user; link for {User} skipped.",
                    session.CustomerId, userId);
            }
            else
            {
                var customer = await _db.Customers.FirstOrDefaultAsync(c => c.UserId == userId, ct);
                if (customer is null)
                    _db.Customers.Add(new BillingCustomer { UserId = userId, StripeCustomerId = session.CustomerId });
                else
                    customer.StripeCustomerId = session.CustomerId;
            }
        }

        // Durum: abonelik okunabildiyse Stripe'ın gerçek durumu. Okunamadıysa (abonelik kimliği yok) ve bu müşteri için
        // daha yeni bir durum olayı zaten görüldüyse durum YAZILMAZ (bilinmiyor; eski checkout yeni iptali ezemez).
        // Yalnızca yeni olay görülmediyse ödeme oturumunun kendisi "aktif" anlamına gelir.
        if (stripeStatus is null && newerStateSeen && newerWasDeletion)
        {
            var canceled = await SubscriptionOfAsync(userId, ct);
            canceled.Status = "canceled";
            canceled.StripeSubscriptionId = null;
            canceled.CurrentPeriodEnd = null;
            canceled.PlanCode = null;
            canceled.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _identity.SetUserTypeAsync(userId, UserType.Individual, ct);
            return;
        }

        if (stripeStatus is null && newerStateSeen)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("[STRIPE WEBHOOK] checkout for user {UserId} linked the customer but a newer state event was already seen; state left unchanged.", userId);
            return;
        }

        var status = stripeStatus ?? "active";
        var subscription = await SubscriptionOfAsync(userId, ct);
        subscription.StripeSubscriptionId = session.SubscriptionId;
        subscription.Status = status;
        subscription.CurrentPeriodEnd = periodEnd;
        subscription.PlanCode = planCode;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // İptal edilmiş/ödenmemiş abonelik erişim açmaz (sıra dışı gelen eski checkout'un yükseltmesi buna takılır);
        // deneme (trialing) dahil diğer durumlar eskisi gibi ücretli plana yükseltir.
        if (status is "canceled" or "unpaid" or "incomplete_expired")
        {
            await _identity.SetUserTypeAsync(userId, UserType.Individual, ct);
        }
        else
        {
            await _identity.SetUserTypeAsync(userId, UserType.Corporate, ct); // ücretli plana yükselt
            _logger.LogInformation("[STRIPE WEBHOOK] User {UserId} elevated to a paid plan. Subscription: {SubId}", userId, session.SubscriptionId);
        }
    }

    private async Task HandleSubscriptionDeletedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Subscription subscription) return;

        var userId = await UserIdByCustomerAsync(subscription.CustomerId, ct);
        if (userId is null)
        {
            _logger.LogWarning("[STRIPE WEBHOOK] customer.subscription.deleted: No user found for CustomerId {CustomerId}.", subscription.CustomerId);
            return;
        }

        var row = await SubscriptionOfAsync(userId, ct);
        row.Status = "canceled";
        row.StripeSubscriptionId = null;
        row.CurrentPeriodEnd = null;
        row.PlanCode = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _identity.SetUserTypeAsync(userId, UserType.Individual, ct); // ücretsize indir
        _logger.LogInformation("[STRIPE WEBHOOK] User {UserId} downgraded to Free. Subscription canceled.", userId);
    }

    /// <summary>
    /// Yenileme, ödeme hatası (past_due) ve yeniden etkinleştirme. "Dönem sonunda iptal" ayarı da buraya düşer:
    /// durum dönem sonuna kadar active kalır, erişim <c>deleted</c> olayında kesilir.
    /// </summary>
    private async Task HandleSubscriptionUpdatedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Subscription subscription) return;

        var userId = await UserIdByCustomerAsync(subscription.CustomerId, ct);
        if (userId is null) return;

        var row = await SubscriptionOfAsync(userId, ct);
        row.Status = subscription.Status; // active | past_due | canceled | ...
        row.CurrentPeriodEnd = subscription.CurrentPeriodEnd;
        // Plan değişebilir (Pro→Team portal üzerinden): her güncellemede fiyattan yeniden okunur.
        row.PlanCode = ResolvePlanCode(subscription.Items?.Data?.FirstOrDefault()?.Price?.Id);
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Ödeme düzeldiyse erişim geri gelir. past_due'da yumuşak davranılır: erişim sürer, kesin düşüş deleted'ta.
        if (subscription.Status == "active")
            await _identity.SetUserTypeAsync(userId, UserType.Corporate, ct);
        else if (subscription.Status is "canceled" or "unpaid")
            await _identity.SetUserTypeAsync(userId, UserType.Individual, ct);

        _logger.LogInformation("[STRIPE WEBHOOK] Subscription updated for user {UserId}: {Status}", userId, subscription.Status);
    }

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

        // YAPILANDIRMA EKSİKLİĞİ ile "fiyat gerçekten Pro" ayrı şeylerdir (docs/last-review L-3). Team fiyat kimlikleri
        // hiç tanımlı değilse eşleme matematiksel olarak çalışamaz: her Team müşterisi sessizce "pro" olur.
        if (teamPriceIds.Count == 0)
        {
            _logger.LogError(
                "[STRIPE WEBHOOK] Team price ids are not configured (Stripe:*TeamPriceId). Every subscription resolves to 'pro', so Team customers get fewer rights.");
            return "pro";
        }

        if (teamPriceIds.Any(id => id == priceId)) return "team";

        // Tanımlı listelerden hiçbiriyle eşleşmiyor: "pro"ya düşmek bilinçli (ödeyen birine hizmet vermemektense
        // daha az hak vermek), ama hangi fiyatın tanınmadığı kayda girer.
        _logger.LogWarning("[STRIPE WEBHOOK] Price {PriceId} is not in the catalog; PlanCode resolved to 'pro'.", priceId);
        return "pro";
    }
}

/// <summary>Stripe SDK sınırı: abonelik ayrıntısı. Anahtar yoksa çağrı başarısız olur ve webhook yeniden denenir.</summary>
internal sealed class StripeGateway : IStripeGateway
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);
    private readonly IConfiguration _config;

    public StripeGateway(IConfiguration config) => _config = config;

    public async Task<StripeSubscriptionInfo> GetSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        var key = _config["Stripe:SecretKey"];
        var service = string.IsNullOrWhiteSpace(key) ? new SubscriptionService() : new SubscriptionService(new StripeClient(key));
        // Bu çağrı müşteri başına advisory kilit altında yapılır; Stripe yavaşlarsa kilit süresiz tutulmasın.
        // Zaman aşımında istek iptal olur, webhook 5xx döner ve Stripe olayı yeniden gönderir.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CallTimeout);
        var subscription = await service.GetAsync(subscriptionId, cancellationToken: timeoutCts.Token);
        return new StripeSubscriptionInfo(subscription.CurrentPeriodEnd, subscription.Items?.Data?.FirstOrDefault()?.Price?.Id, subscription.Status);
    }
}

/// <summary>
/// Hesap silinirken aboneliği Stripe'ta iptal eder. Anahtar yoksa ya da çağrı başarısızsa <c>false</c> döner;
/// hesap silme bloklanmaz, iptal outbox'ta yeniden denenir.
/// </summary>
internal sealed class StripeSubscriptionCanceller : ISubscriptionCanceller
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeSubscriptionCanceller> _logger;
    private readonly IHttpClient? _http;

    public StripeSubscriptionCanceller(IConfiguration configuration, ILogger<StripeSubscriptionCanceller> logger, IHttpClient? http = null)
    {
        _configuration = configuration;
        _logger = logger;
        _http = http;
    }

    public async Task<bool> CancelAsync(string subscriptionId, CancellationToken ct)
    {
        var key = _configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(key)) return false;

        try
        {
            var service = new SubscriptionService(new StripeClient(key, httpClient: _http));
            await service.CancelAsync(subscriptionId, cancellationToken: ct);
            return true;
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
        {
            // Abonelik zaten yok/iptal: istenen son durum sağlanmış.
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe aboneligi iptal edilemedi: {Sub}", subscriptionId);
            return false;
        }
    }
}
