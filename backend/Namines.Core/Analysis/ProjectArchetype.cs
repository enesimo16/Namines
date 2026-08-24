using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Core.Analysis;

/// <summary>
/// Kullanıcının anlattığı işin türü.
///
/// Bu, sorulacak soruları belirliyor: bir oyun şemasında "envanter ve oyuncu
/// ilerlemesi" sorulur, bir ERP'de "muhasebe entegrasyonu ve çoklu şirket".
/// Aynı beş soruyu herkese sormak, yarısını alakasız kılar ve kullanıcı formu
/// kapatır.
/// </summary>
public enum ProjectArchetype
{
    /// <summary>Tanınmadı — genel sorular sorulur.</summary>
    Generic = 0,

    Ecommerce,
    Saas,
    Erp,
    Crm,
    Game,
    Social,
    Cms,
    Fintech,
    Healthcare,
    Education,
    Logistics,
    Iot,
    Marketplace,
    Booking,
}

/// <summary>
/// Kullanıcının cümlesinden iş türünü çıkarır — <b>AI KULLANMADAN.</b>
///
/// <b>Neden deterministik:</b> "bu bir e-ticaret mi" sorusunu bir dil modeline
/// sormak, kullanıcı daha ilk soruyu görmeden token harcamak demek. Anahtar
/// kelime eşleşmesi bu iş için yeterli: kullanıcı zaten "e-ticaret", "mağaza",
/// "sipariş" gibi kelimeleri kendisi yazıyor. Yanılırsa maliyeti düşük — yalnızca
/// birkaç soru alakasız olur, üretilen şema değil.
///
/// <b>Türkçe ve İngilizce birlikte</b>: kullanıcıların çoğu Türkçe yazıyor ama
/// teknik terimleri İngilizce kullanıyor ("kullanıcı authentication olsun").
/// Tek dile bakmak, karışık cümlelerde tanımayı kaybettirir.
/// </summary>
public static class ArchetypeDetector
{
    private static readonly Dictionary<ProjectArchetype, string[]> Keywords = new()
    {
        [ProjectArchetype.Ecommerce] = new[]
        {
            "e-ticaret", "eticaret", "ecommerce", "e-commerce", "mağaza", "magaza", "shop", "store",
            "sipariş", "siparis", "order", "sepet", "cart", "ürün", "urun", "product", "kargo", "checkout",
        },
        [ProjectArchetype.Marketplace] = new[]
        {
            "pazaryeri", "marketplace", "satıcı", "satici", "vendor", "seller", "komisyon", "commission",
        },
        [ProjectArchetype.Saas] = new[]
        {
            "saas", "abonelik", "subscription", "tenant", "kiracı", "kiraci", "multi-tenant", "workspace", "plan",
        },
        [ProjectArchetype.Erp] = new[]
        {
            "erp", "muhasebe", "accounting", "stok", "inventory", "depo", "warehouse", "fatura", "invoice",
            "cari", "üretim", "uretim", "manufacturing", "bordro", "payroll",
        },
        [ProjectArchetype.Crm] = new[]
        {
            "crm", "müşteri ilişkileri", "musteri iliskileri", "lead", "fırsat", "firsat", "opportunity",
            "pipeline", "deal", "kontak", "contact",
        },
        [ProjectArchetype.Game] = new[]
        {
            "oyun", "game", "oyuncu", "player", "envanter", "skor", "score", "leaderboard", "seviye",
            "level", "quest", "guild", "lonca", "eşya", "esya", "item",
        },
        [ProjectArchetype.Social] = new[]
        {
            "sosyal", "social", "takip", "follow", "beğeni", "begeni", "like", "gönderi", "gonderi",
            "post", "yorum", "comment", "feed", "arkadaş", "arkadas", "friend", "mesajlaşma", "chat",
        },
        [ProjectArchetype.Cms] = new[]
        {
            "blog", "cms", "içerik", "icerik", "content", "makale", "article", "sayfa", "page",
            "yayın", "yayin", "publish", "kategori", "category", "etiket", "tag",
        },
        [ProjectArchetype.Fintech] = new[]
        {
            "fintech", "banka", "bank", "ödeme", "odeme", "payment", "cüzdan", "cuzdan", "wallet",
            "işlem", "islem", "transaction", "bakiye", "balance", "kredi", "loan", "muhasebe defteri", "ledger",
        },
        [ProjectArchetype.Healthcare] = new[]
        {
            "hastane", "hospital", "sağlık", "saglik", "health", "hasta", "patient", "randevu",
            "appointment", "doktor", "doctor", "reçete", "recete", "prescription", "klinik", "clinic",
        },
        [ProjectArchetype.Education] = new[]
        {
            "okul", "school", "eğitim", "egitim", "education", "öğrenci", "ogrenci", "student",
            "kurs", "course", "ders", "lesson", "sınav", "sinav", "exam", "not", "grade", "lms",
        },
        [ProjectArchetype.Logistics] = new[]
        {
            "lojistik", "logistics", "sevkiyat", "shipment", "araç", "arac", "vehicle", "rota", "route",
            "teslimat", "delivery", "filo", "fleet", "taşıma", "tasima",
        },
        [ProjectArchetype.Iot] = new[]
        {
            "iot", "sensör", "sensor", "cihaz", "device", "telemetri", "telemetry", "ölçüm",
            "olcum", "reading", "zaman serisi", "time series",
        },
        [ProjectArchetype.Booking] = new[]
        {
            "rezervasyon", "reservation", "booking", "otel", "hotel", "müsaitlik", "musaitlik",
            "availability", "koltuk", "seat", "bilet", "ticket",
        },
    };

    /// <summary>
    /// En çok eşleşen türü döndürür.
    ///
    /// <b>Eşitlikte ve hiç eşleşme olmadığında <see cref="ProjectArchetype.Generic"/>.</b>
    /// Zorla bir tür seçmek, alakasız sorular sorup kullanıcıyı yanlış yöne
    /// itmek olurdu; genel sorular her zaman geçerlidir.
    /// </summary>
    public static ProjectArchetype Detect(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return ProjectArchetype.Generic;

        var text = Normalize(prompt);

        var scores = Keywords
            .Select(pair => (Archetype: pair.Key, Score: pair.Value.Count(k => text.Contains(Normalize(k)))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scores.Count == 0) return ProjectArchetype.Generic;

        // Berabere kalırsa da Generic: iki tür aynı puanı aldıysa hangisi olduğunu
        // gerçekten bilmiyoruz demektir ve tahmin etmek, yanlış soruyu güvenle
        // sormaktan kötüdür.
        if (scores.Count > 1 && scores[0].Score == scores[1].Score) return ProjectArchetype.Generic;

        return scores[0].Archetype;
    }

    /// <summary>
    /// Türkçe karakterleri sadeleştirir ve küçük harfe indirger.
    ///
    /// Kullanıcı "Sipariş" de yazabilir "siparis" de; ikisini ayrı saymak
    /// tanımanın yarısını kaybettirir. <c>ToLowerInvariant</c> kullanılıyor —
    /// Türkçe kültürde <c>ToLower</c>, "I"yı "ı" yapıp ASCII karşılaştırmayı
    /// bozuyor (bu kod tabanında bir kez gerçek hataya yol açtı).
    /// </summary>
    private static string Normalize(string value) =>
        value.ToLowerInvariant()
             .Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g')
             .Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c')
             .Replace('İ', 'i');
}
