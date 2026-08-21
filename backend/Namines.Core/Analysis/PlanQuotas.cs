using System;

namespace Namines.Core.Analysis;

/// <summary>
/// Plan katmanları (new-phase/06-DATA-PLANE.md §10, 22-BUSINESS-MODEL.md).
///
/// <b>Team ve Enterprise şu an TEMSİL EDİLEMİYOR.</b> Kullanıcı modelinde plan alanı
/// yok; elde yalnızca Stripe'ın <c>SubscriptionStatus</c>'ü var, o da "abone mi
/// değil mi" sorusunu cevaplıyor. Enum'da yer tutucu olarak duruyorlar ki kota
/// tablosu dokümanla aynı şekli korusun, ama <see cref="PlanQuotas.Resolve"/> bugün
/// yalnızca Free/Pro döndürebilir. Olmayan bir ayrımı varmış gibi kodlamak,
/// ileride "neden Team hiç seçilmiyor" diye aranan sessiz bir hataya dönüşürdü.
/// </summary>
public enum PlanTier
{
    Free = 0,
    Pro = 1,
    Team = 2,
    Enterprise = 3,
}

/// <param name="BranchDatabases">Aynı anda açık tutulabilen branch veritabanı sayısı. -1 = sınırsız.</param>
/// <param name="EphemeralRunsPerDay">Günlük ephemeral test çalıştırması. -1 = sınırsız.</param>
/// <param name="ByodbConnections">Kayıtlı BYODB bağlantısı. -1 = sınırsız.</param>
public sealed record PlanLimits(int BranchDatabases, int EphemeralRunsPerDay, int ByodbConnections);

/// <summary>
/// Plan başına kaynak sınırları.
///
/// <b>Yalnızca VAR OLAN kaynaklar için sınır tanımlı.</b> Doküman §10 managed DB,
/// veri hacmi, yedek saklama, bölge seçimi ve Bridge agent için de sınır veriyor;
/// bunların hiçbiri henüz kodda yok. Olmayan bir özelliğe kota koymak, uygulanmayan
/// bir kuralı "uygulanıyor" diye kaydetmek olurdu.
/// </summary>
public static class PlanQuotas
{
    public static PlanLimits For(PlanTier tier) => tier switch
    {
        // Free'de branch veritabanı yok: her biri host'ta kalıcı bir container ve
        // bellek tutuyor, ücretsiz katmanda sınırsız açılması sunucuyu düşürür.
        PlanTier.Free => new PlanLimits(BranchDatabases: 0, EphemeralRunsPerDay: 3, ByodbConnections: 1),
        PlanTier.Pro => new PlanLimits(2, 20, 3),
        PlanTier.Team => new PlanLimits(20, -1, 20),
        PlanTier.Enterprise => new PlanLimits(-1, -1, -1),
        _ => For(PlanTier.Free),
    };

    /// <summary>
    /// Stripe abonelik durumundan planı çıkarır.
    ///
    /// Stripe'ın <c>active</c> ve <c>trialing</c> dışındaki durumları (<c>past_due</c>,
    /// <c>canceled</c>, <c>unpaid</c>) Free sayılır — ödemesi aksayan bir hesabın
    /// ücretli kaynak açmaya devam etmesi, faturayı büyütmekten başka işe yaramaz.
    /// </summary>
    public static PlanTier Resolve(string? subscriptionStatus) =>
        subscriptionStatus?.Trim().ToLowerInvariant() switch
        {
            "active" or "trialing" => PlanTier.Pro,
            _ => PlanTier.Free,
        };

    /// <summary>Sınıra ulaşıldı mı? -1 sınırsız demektir.</summary>
    public static bool IsExceeded(int limit, int current) => limit >= 0 && current >= limit;

    /// <summary>Kullanıcıya gösterilecek mesaj — ne yapması gerektiğini söylüyor.</summary>
    public static string LimitMessage(PlanTier tier, string resource, int limit) =>
        limit == 0
            ? $"{resource} is not available on the {tier} plan. Upgrade to enable it."
            : $"You have reached the {tier} plan limit of {limit} {resource}. " +
              "Close one you no longer need, or upgrade.";
}
