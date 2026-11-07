using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Contracts;

/// <summary>Havuzun doluluk baskısı: büyütme kararını İNSANIN vermesi için üretilen veri.</summary>
public sealed record PoolPressure(
    long CurrentPool,
    long MaxPool,
    int DaysObserved,
    int DaysFull,
    bool ShouldGrow,
    long SuggestedPool);

/// <summary>Kota kontrolünün sonucu.</summary>
public enum AiQuotaDecision
{
    /// <summary>Bütçe var.</summary>
    Allowed,

    /// <summary>Paylaşılan günlük havuz doldu.</summary>
    PoolExhausted,

    /// <summary>Kullanıcının günlük tavanı doldu.</summary>
    UserExhausted,

    /// <summary>
    /// Ekibin günlük ortak havuzu doldu. Kullanıcının kendi tavanından ayrı: kişi hakkını bitirmemiş olabilir
    /// ama ekip arkadaşları havuzu tüketmiş olabilir; "kendi hakkın dolu" demek yanlış olurdu.
    /// </summary>
    TeamExhausted,
}

/// <summary>Kotanın dar arayüzü: yalnızca rezervasyon isteyenler (Flow, Gateway) bunu görür.</summary>
public interface IAiQuotaReserver
{
    Task<AiQuotaDecision> TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct = default);
}

/// <summary>Kullanıcının günlük AI durumu; ekran ve middleware aynı kaynaktan okur.</summary>
public sealed record AiQuotaStatus(
    PlanTier Plan,
    int DailyLimit,
    int Used,
    DateTime LastResetDate,
    long PoolLimit,
    long PoolUsed);

/// <summary>
/// AI bütçesinin TEK sahibi (22 §5). Rezervasyonlar atomiktir: kullanıcı tavanı, paylaşılan havuz ve ekip
/// havuzu tek işlemde koşullu artırılır, biri tutmazsa hiçbiri yazılmaz.
/// </summary>
public interface IAiQuota : IAiQuotaReserver
{
    long DailyPool { get; }

    /// <summary>Bu istek kapsamında rezervasyon yapıldı mı: middleware aynı işi ikinci kez düşmesin.</summary>
    bool HasReservedInScope { get; }

    Task<PlanTier> TierAsync(string userId, CancellationToken ct = default);

    Task<int> PerUserCapAsync(string userId, CancellationToken ct = default);

    Task<PoolPressure> PoolPressureAsync(int lookbackDays = 7, CancellationToken ct = default);

    /// <summary>Okuma: yazmaz, gün sıfırlamasını hesaplayarak döner (GET idempotent kalır).</summary>
    Task<AiQuotaStatus> GetStatusAsync(string userId, CancellationToken ct = default);

    /// <summary>Satırı güncel plana göre senkronlar, gün değiştiyse sayacı sıfırlar ve durumu döndürür.</summary>
    Task<AiQuotaStatus> EnsureAsync(string userId, CancellationToken ct = default);

    Task<AiQuotaDecision> CheckAsync(string userId, int estimatedTokens, CancellationToken ct = default);

    Task ConsumeAsync(string userId, int estimatedTokens, CancellationToken ct = default);

    Task RefundAsync(string userId, int tokens, CancellationToken ct = default);

    Task ReconcileAsync(string userId, int reservedTokens, int actualTokens, CancellationToken ct = default);
}

/// <summary>Bir kaynağın dönem özeti satırı.</summary>
public sealed record UsageResourceLine(
    UsageResource Resource,
    decimal Used,
    decimal? Included,
    decimal OverageUnitPriceUsd,
    decimal OverageUnitSize,
    decimal OverageCostUsd);

public sealed record UsageOverview(
    PlanTier Plan,
    DateTime BillingPeriod,
    bool OverageEnabled,
    decimal? MonthlyCapUsd,
    decimal TotalOverageUsd,
    IReadOnlyList<UsageResourceLine> Resources);

/// <summary>Ölçüm ve aşırı kullanım ayarı: faturalama tabloları Billing'in.</summary>
public interface IUsageMeter
{
    Task RecordAsync(string userId, UsageResource resource, decimal quantity = 1, string? contextLabel = null, CancellationToken ct = default);

    Task<UsageDecision> EvaluateAsync(string userId, UsageResource resource, decimal requested = 1, CancellationToken ct = default);

    Task<UsageOverview> GetOverviewAsync(string userId, CancellationToken ct = default);

    /// <summary>Tavan sıfır ya da negatifse <see cref="ArgumentOutOfRangeException"/>; ayar yoksa oluşturur.</summary>
    Task<(bool OverageEnabled, decimal? MonthlyCapUsd)> UpdateSettingsAsync(string userId, bool overageEnabled, decimal? monthlyCapUsd, CancellationToken ct = default);
}

/// <summary>Aboneliğin modül dışı görünümü; Stripe müşteri kimliği değil, var olup olmadığı.</summary>
public sealed record SubscriptionView(
    string UserId,
    string? Status,
    string? PlanCode,
    DateTime? CurrentPeriodEnd,
    bool HasStripeCustomer);

/// <summary>Yönetim panelinin plan sayımları.</summary>
public sealed record PlanCounts(int Free, int Pro, int Team);

public sealed record QuotaRow(string UserId, int DailyLimit, int DailyUsageCount, DateTime LastResetDate);

public sealed record PendingCancellationRow(
    string Id, string SubscriptionId, DateTime CreatedAt, int Attempts, DateTime? LastAttemptAt, DateTime NextAttemptAt, bool Stuck);

/// <summary>Açık iptal kuyruğu: toplam ve takılı sayısı GERÇEK sayıdır (listeyle sınırlı değil, BL-3).</summary>
public sealed record CancellationBacklog(int Total, int Stuck, IReadOnlyList<PendingCancellationRow> Items);

public sealed record TokenDayRow(DateTime Date, long Tokens);

/// <summary>Plan, abonelik ve kota okumaları. Kimlik tablolarına dokunmaz; kullanıcı bilgisi için <see cref="IIdentityDirectory"/>.</summary>
public interface IBillingAccounts
{
    Task<UserPlanSnapshot?> GetPlanSnapshotAsync(string userId, CancellationToken ct = default);

    Task<SubscriptionView?> GetSubscriptionAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, SubscriptionView>> GetSubscriptionsAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default);

    /// <summary>Dev olmayan kullanıcıların plan kodu sayımı; kodu boş olan "free" sayılır. Abonelik satırı olmayan kullanıcılar çağıranın toplamından çıkarılır.</summary>
    Task<(int Pro, int Team)> CountPaidPlansAsync(IReadOnlyCollection<string> excludeUserIds, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListUserIdsByPlanAsync(string planCode, CancellationToken ct = default);

    /// <summary>Plan kodu dolu ve "free" olmayan kullanıcılar: yönetim listesinde "free" süzgeci bunların DIŞINDAKİLERDİR.</summary>
    Task<IReadOnlyList<string>> ListUserIdsWithPaidPlanAsync(CancellationToken ct = default);

    /// <summary>Kayıt: varsayılan kota satırı (günlük 100); varsa dokunmaz.</summary>
    Task<QuotaRow> ProvisionDefaultQuotaAsync(string userId, CancellationToken ct = default);

    /// <summary>Giriş: satırı açar ya da düzeltir (geliştirici sınırsız, alt sınır 100) ve gün değiştiyse sıfırlar.</summary>
    Task<QuotaRow> SyncQuotaOnLoginAsync(string userId, bool isDev, CancellationToken ct = default);

    /// <summary>
    /// Yönetici planı: plan kodunu yazar (abonelik satırı yoksa açar, durumuna dokunmaz) ve kota sınırını plana göre ayarlar
    /// (geliştirici sınırsız; team 2000, pro 500, diğeri 100). Güncel kota satırını döndürür.
    /// </summary>
    Task<QuotaRow> AdminSetPlanAsync(string userId, string planCode, bool isDev, CancellationToken ct = default);

    Task<QuotaRow?> GetQuotaRowAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, QuotaRow>> GetQuotaRowsAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default);

    /// <summary>
    /// Yönetici kota ayarı: isteğe bağlı sıfırlama ve bonus puan (üst sınırlı; sınırsız hesaba eklenmez). Güncel satırı döndürür.
    /// </summary>
    Task<QuotaRow> AdminAdjustQuotaAsync(string userId, bool isDev, bool resetToZero, int? bonusPoints, int maxBonus, CancellationToken ct = default);

    Task<long> SumQuotaUsedTodayAsync(CancellationToken ct = default);

    Task<CancellationBacklog> GetCancellationBacklogAsync(int take, CancellationToken ct = default);

    Task<IReadOnlyList<TokenDayRow>> ListGlobalUsageAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>AI çağrı olayları gün gün; <c>Tokens</c> alanı olay SAYISIDIR.</summary>
    Task<IReadOnlyList<TokenDayRow>> ListUsageCallsPerDayAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<QuotaRow>> ListTopQuotaUsersAsync(int take, CancellationToken ct = default);

    /// <summary>Hesap silme: iptal edilecek Stripe aboneliğini outbox'a yazar (çağıranın işleminde). Aboneliği yoksa null.</summary>
    Task<string?> QueueSubscriptionCancellationAsync(string userId, CancellationToken ct = default);

    /// <summary>Outbox satırını hemen bir kez dener (hesap silme sonrası ilk deneme); başarıyla bittiyse true.</summary>
    Task<bool> TryCancelNowAsync(string outboxId, CancellationToken ct = default);
}

/// <summary>Saklama süresi ve hesap silme temizliği.</summary>
public interface IBillingMaintenance
{
    /// <summary>Kullanıcının faturalama kişisel verisini kaldırır; kullanım olaylarının kullanıcı kimliğini boşaltır. Çağıranın işleminde.</summary>
    Task<int> ErasePersonalDataAsync(string userId, CancellationToken ct = default);

    Task<int> PurgeUsageEventsAsync(DateTime olderThanUtc, TimeSpan pause, CancellationToken ct = default);

    /// <summary>Yalnızca TAMAMLANANLAR: açık satır henüz iptal edilmemiş bir abonelik demek, ne kadar eski olursa olsun silinmez.</summary>
    Task<int> PurgeCompletedCancellationsAsync(DateTime olderThanUtc, TimeSpan pause, CancellationToken ct = default);

    Task<int> PurgeStripeWebhookEventsAsync(DateTime olderThanUtc, TimeSpan pause, CancellationToken ct = default);
}

public sealed record PlanOffer(string Plan, int YearlyDiscountPercent, IReadOnlyList<PlanOfferPrice> Prices);

public sealed record PlanOfferPrice(string Interval, decimal AmountUsd, decimal MonthlyEquivalentUsd, bool Available);

public sealed record CheckoutResult(int StatusCode, string? Url = null, string? Redirect = null, string? Error = null);

public sealed record SubscriptionStatusView(string Status, string Plan, bool IsPro, bool IsDev, DateTime? CurrentPeriodEnd, bool HasStripeCustomer);

/// <summary>Ödeme sayfası ve portal oturumları; kart verisi hiçbir zaman sunucuya gelmez.</summary>
public interface ISubscriptionService
{
    IReadOnlyList<PlanOffer> GetPlans();

    Task<CheckoutResult> CreateCheckoutAsync(string userId, string plan, string interval, CancellationToken ct = default);

    Task<CheckoutResult> CreatePortalAsync(string userId, CancellationToken ct = default);

    Task<SubscriptionStatusView?> GetStatusAsync(string userId, CancellationToken ct = default);
}

public sealed record WebhookResult(int StatusCode, object Body);

/// <summary>Stripe webhook'u: imza zorunlu, olay kimliği tekil, sıra dışı teslimat durum değiştirmez.</summary>
public interface IStripeWebhookService
{
    Task<WebhookResult> HandleAsync(string payload, string? signatureHeader, CancellationToken ct = default);
}

/// <summary>Stripe aboneliğini iptal eden dış çağrı; outbox işçisi bunu kullanır.</summary>
public interface ISubscriptionCanceller
{
    Task<bool> CancelAsync(string subscriptionId, CancellationToken ct);
}

/// <summary>
/// Abonelik iptali outbox'ta tamamlanınca haber alan modül (Governance denetim izini tamamlar). Billing hangi
/// modülün dinlediğini bilmez; denetim satırı değiştirilemez olduğu için dinleyici yeni satır ekler (BL-4).
/// </summary>
public interface ISubscriptionCancellationObserver
{
    Task OnCompletedAsync(string outboxId, string subscriptionId, CancellationToken ct);
}
