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
    /// <c>ApplicationUser.IsDev</c> bayrağıyla gelir. O bayrak yalnızca yapılandırmadaki
    /// <c>Dev:Email</c> hesabına verilir: açılıştaki <c>DevAccountSeeder</c> ve
    /// aynı e-postayla giriş/admin doğrulaması set eder. Admin panelindeki plan
    /// değişikliği ucu bu bayrağı DEĞİŞTİRMEZ. Enum'un en sonunda
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
/// <param name="CrossDatabaseRelations">
/// second-phase/10-COKLU-DB.md'nin açık bıraktığı soru: "kaç DB, hangi planda?"
///
/// Sınırlanan şey veritabanı SAYISI değil, aralarındaki mantıksal ilişki
/// sayısı — çünkü asıl değer (ve asıl maliyet) orada: her ilişki, silme
/// öncesi çalışan bir etki analizi ve karşı projenin şemasının çözülmesi
/// demek. Ücretsiz katmanda birkaç tanesi "gör ve anla"ya yeter; mikroservis
/// filosunu haritalamak ödeyen kullanıcının işi.
/// </param>
/// <param name="ManagedDatabaseStorageWarningBytes">
/// Namines Ground'un kendi barındırdığı (LocalPostgres) bir veritabanı bu
/// boyutu aşarsa kullanıcıya UYARI gösterilir. -1 = uyarı yok.
///
/// <b>Bilerek yalnızca uyarı, kısıtlama değil</b> — PostgreSQL'de bir
/// veritabanının yazmasını disk boyutuna göre durdurmanın standart bir yolu
/// yok; olan yol (rolden yazma yetkisini geri almak) kullanıcının
/// UYGULAMASINI kırar, sessizce başarısız isteklere yol açar. Ölçülmeden
/// otomatik bir kısıtlama koymak, yanlış anda kullanıcının verisine
/// erişimini kesme riskini taşırdı. Uyarı, kısıtlamadan önceki dürüst adım.
/// </param>
/// <param name="MaxAgentRepairTurns">
/// Şema ajanının kaç kez DÜZELTME turu atabileceği. Plan ve taslak turları
/// bunun DIŞINDA (bkz. <c>SchemaAgentPipeline.FixedRounds</c>).
///
/// <b>Neden plana bağlı:</b> önceden tavan çağıranda sabitti, yani ödeyen
/// kullanıcının tek farkı daha büyük bir token havuzuydu — agent yine aynı
/// noktada pes ediyordu. Derinlik, ücretli planın somut karşılığı.
///
/// <b>Neden token bütçesinden AYRI:</b> token havuzu "bugün ne kadar
/// harcayabilirsin", bu ise "tek bir şemada ne kadar ısrar edilsin" sorusunu
/// cevaplıyor. Yalnızca token'a bakmak, havuzu bol bir kullanıcının tek
/// istekte çözülemeyen bir bulgu için turlarca dönmesine izin verirdi.
/// </param>
public sealed record PlanLimits(
    int BranchDatabases,
    int EphemeralRunsPerDay,
    int ByodbConnections,
    int DailyAiTokens = 20_000,
    int GatewayRequestsPerMinute = 60,
    int TeamSeats = 1,
    int CrossDatabaseRelations = 3,
    int ManagedDatabases = 0,
    long ManagedDatabaseStorageWarningBytes = -1,
    int MaxAgentRepairTurns = 2);

/// <summary>
/// Plan başına kaynak sınırları.
///
/// <b>Yalnızca VAR OLAN kaynaklar için sınır tanımlı.</b> Doküman §10 veri
/// hacmi, yedek saklama, bölge seçimi ve Bridge agent için de sınır veriyor;
/// bunların hiçbiri henüz kodda yok. Olmayan bir özelliğe kota koymak, uygulanmayan
/// bir kuralı "uygulanıyor" diye kaydetmek olurdu.
///
/// <see cref="PlanLimits.ManagedDatabases"/> tam da bu sebeple SONRADAN eklendi:
/// Namines Ground gerçekten var olduğu an kota da meşrulaştı
/// (<c>namines-ground/02-V1-KARARLARI.md</c> §2.1).
/// </summary>
public static class PlanQuotas
{
    public static PlanLimits For(PlanTier tier) => tier switch
    {
        PlanTier.Free => new PlanLimits(
            BranchDatabases: 0, EphemeralRunsPerDay: 3, ByodbConnections: 1,
            DailyAiTokens: 100_000, GatewayRequestsPerMinute: 60, TeamSeats: 1,
            CrossDatabaseRelations: 3,
            ManagedDatabases: 1,
            ManagedDatabaseStorageWarningBytes: 500L * 1024 * 1024,
            // Yeni NSL-first diyalektik orkestratör için temel özerklik derinliği.
            MaxAgentRepairTurns: 4),

        PlanTier.Pro => new PlanLimits(2, 20, 3,
            DailyAiTokens: 1_000_000, GatewayRequestsPerMinute: 600, TeamSeats: 1,
            CrossDatabaseRelations: 25,
            ManagedDatabases: 2,
            MaxAgentRepairTurns: 8),

        // Team'de DailyAiTokens KOLTUK BAŞINA pay: 3 koltuk × 1.000.000 = 3.000.000
        // günlük ekip havuzu.
        PlanTier.Team => new PlanLimits(20, -1, 20,
            DailyAiTokens: 1_000_000, GatewayRequestsPerMinute: 3_000, TeamSeats: 3,
            CrossDatabaseRelations: 100,
            ManagedDatabases: 20,
            MaxAgentRepairTurns: 8),

        PlanTier.Enterprise => new PlanLimits(-1, -1, -1,
            DailyAiTokens: 25_000_000, GatewayRequestsPerMinute: 10_000, TeamSeats: -1,
            CrossDatabaseRelations: -1,
            ManagedDatabases: -1,
            MaxAgentRepairTurns: 12),

        // Sahip hesabında sınır yok.
        PlanTier.Dev => new PlanLimits(-1, -1, -1,
            DailyAiTokens: int.MaxValue, GatewayRequestsPerMinute: int.MaxValue, TeamSeats: -1,
            CrossDatabaseRelations: -1,
            ManagedDatabases: -1,
            MaxAgentRepairTurns: 16),

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
