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

    /// <summary>
    /// Geliştirici/sahip hesabı — hiçbir sınır uygulanmaz.
    ///
    /// <b>Satılabilir bir plan DEĞİL.</b> Stripe'tan asla çıkmaz; yalnızca
    /// <c>ApplicationUser.IsDev</c> bayrağıyla gelir ve o bayrağı da yalnızca
    /// açılışta .env'den okunan tohumlama servisi set eder. Enum'un en sonunda
    /// duruyor ki <c>ClampToPlan</c> gibi "daha büyük plan daha çok hak" varsayan
    /// karşılaştırmalar doğru çalışsın.
    /// </summary>
    Dev = 99,
}

/// <param name="BranchDatabases">Aynı anda açık tutulabilen branch veritabanı sayısı. -1 = sınırsız.</param>
/// <param name="EphemeralRunsPerDay">Günlük ephemeral test çalıştırması. -1 = sınırsız.</param>
/// <param name="ByodbConnections">Kayıtlı BYODB bağlantısı. -1 = sınırsız.</param>
/// <param name="DailyAiTokens">
/// Günlük AI token bütçesi.
///
/// <b>Eskiden plana bağlı DEĞİLDİ:</b> yapılandırmadan okunan tek bir sayıydı ve
/// ücretli kullanıcı da ücretsiz kullanıcı da aynı 20.000 token'ı alıyordu.
/// Abonelik bilgisi veritabanında duruyordu ama hiçbir sınırı etkilemiyordu —
/// yani para ödeyen karşılığını almıyor, ödemeyen de kısıtlanmıyordu.
/// </param>
/// <param name="GatewayRequestsPerMinute">
/// Gateway API anahtarı başına dakikalık istek hakkı (08 §5). Anahtar
/// oluşturulurken tavan olarak uygulanıyor: kullanıcı daha düşüğünü seçebilir,
/// planının üstüne çıkamaz.
/// </param>
/// <param name="TeamSeats">
/// Bir organizasyonda toplam kaç kişi olabilir — <b>satın alan kişi dahil.</b>
///
/// Team'de 3: sahip + davet edilebilen 2 kişi. Sayıyı "davet hakkı" olarak değil
/// TOPLAM olarak tutmak bilinçli — davet hakkı olarak tutulsaydı, sahip ekipten
/// çıkıp yerine başkasını alarak sınırı sessizce aşabilirdi.
///
/// -1 = sınırsız (yalnızca Dev).
/// </param>
public sealed record PlanLimits(
    int BranchDatabases,
    int EphemeralRunsPerDay,
    int ByodbConnections,
    int DailyAiTokens = 20_000,
    int GatewayRequestsPerMinute = 60,
    int TeamSeats = 1);

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
        //
        // ⚠️ AI token ve rpm sayıları GEÇİCİ VARSAYILAN. Gerçek rakamlar ürün
        // kararıdır ve 34-SENDEN-BEKLENENLER.md §4'te bekliyor. Free bilerek
        // "kullanılabilir ama dar": sıfır vermek ürünü denenemez kılar, cömert
        // vermek ücretliye geçme sebebini yok eder.
        PlanTier.Free => new PlanLimits(
            BranchDatabases: 0, EphemeralRunsPerDay: 3, ByodbConnections: 1,
            DailyAiTokens: 20_000, GatewayRequestsPerMinute: 60, TeamSeats: 1),

        // Pro sınırsız DEĞİL: AI gerçek para harcıyor, "sınırsız" demek tek bir
        // kullanıcının aylık ücretinin kat kat üstünde fatura üretebilmesi demek.
        PlanTier.Pro => new PlanLimits(2, 20, 3,
            DailyAiTokens: 200_000, GatewayRequestsPerMinute: 600, TeamSeats: 1),

        // Team'in AI bütçesi Pro ile AYNI ve bu bilinçli: Team'in sattığı şey daha
        // çok token değil, birlikte çalışma (3 koltuk, ortak workspace, paylaşılan
        // projeler). Token'ı da katlamak, ekip başına maliyeti üç katına çıkarıp
        // fiyatı anlamsız kılardı.
        PlanTier.Team => new PlanLimits(20, -1, 20,
            DailyAiTokens: 200_000, GatewayRequestsPerMinute: 3_000, TeamSeats: 3),

        // Enterprise sözleşmeyle belirlenir; bu değerler tavan değil, sözleşme
        // yapılandırılana kadar geçerli bir başlangıç.
        PlanTier.Enterprise => new PlanLimits(-1, -1, -1,
            DailyAiTokens: 10_000_000, GatewayRequestsPerMinute: 10_000, TeamSeats: -1),

        // Sahip hesabında sınır yok. Token tavanı -1 DEĞİL, int.MaxValue:
        // -1 "sınırsız" anlamına gelen sayaç alanları için doğru ama günlük
        // token bütçesi bir ARİTMETİK sınır (kullanılan + istenen > tavan);
        // oraya -1 koymak her isteği "tavanı aştın" saydırırdı — yani sınırsız
        // hesap hiçbir şey yapamazdı.
        PlanTier.Dev => new PlanLimits(-1, -1, -1,
            DailyAiTokens: int.MaxValue, GatewayRequestsPerMinute: int.MaxValue, TeamSeats: -1),

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
        Resolve(subscriptionStatus, planCode: null, isDev: false);

    /// <summary>
    /// Dev bayrağını da hesaba katan çözümleme, plan kodu olmadan.
    ///
    /// Geriye dönük uyumluluk için: bazı çağıranlar henüz PlanCode'u seçmiyor.
    /// Kod olmadan Pro/Team ayrımı yapılamaz, aktif abonelik varsayılan olarak
    /// Pro sayılır — yanlış yönde ucuz hata etmek (Team'i Pro göstermek),
    /// ters yönden (Pro'yu Team göstermek) daha güvenli.
    /// </summary>
    public static PlanTier Resolve(string? subscriptionStatus, bool isDev) =>
        Resolve(subscriptionStatus, planCode: null, isDev);

    /// <summary>
    /// Tam çözümleme: sahiplik, plan kodu (Pro/Team) ve abonelik durumu birlikte.
    ///
    /// <b>Sahiplik her şeyi EZER ve önce bakılır.</b> Dev hesabının Stripe'ta
    /// bir kaydı yok, yani <c>SubscriptionStatus</c>'ü boş; önce aboneliğe
    /// bakılsaydı kendi ürününün geliştiricisi Free katmanda kalırdı.
    ///
    /// <b>Abonelik aktif değilse plan kodu HİÇ okunmuyor.</b> Ödemesi aksamış
    /// bir Team hesabının eski plan kodu veritabanında kalmaya devam eder;
    /// onu okumak, ödemeyi durduran birine Team hakkı vermeye devam etmek olurdu.
    /// </summary>
    public static PlanTier Resolve(string? subscriptionStatus, string? planCode, bool isDev)
    {
        if (isDev) return PlanTier.Dev;

        var active = subscriptionStatus?.Trim().ToLowerInvariant() switch
        {
            "active" or "trialing" => true,
            _ => false,
        };

        if (!active) return PlanTier.Free;

        return planCode?.Trim().ToLowerInvariant() switch
        {
            "team" => PlanTier.Team,
            _ => PlanTier.Pro,
        };
    }

    /// <summary>Sınıra ulaşıldı mı? -1 sınırsız demektir.</summary>
    public static bool IsExceeded(int limit, int current) => limit >= 0 && current >= limit;

    /// <summary>Kullanıcıya gösterilecek mesaj — ne yapması gerektiğini söylüyor.</summary>
    public static string LimitMessage(PlanTier tier, string resource, int limit) =>
        limit == 0
            ? $"{resource} is not available on the {tier} plan. Upgrade to enable it."
            : $"You have reached the {tier} plan limit of {limit} {resource}. " +
              "Close one you no longer need, or upgrade.";
}
