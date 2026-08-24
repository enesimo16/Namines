using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Core.Analysis;

/// <param name="Id">Cevabı geri gönderirken kullanılan anahtar.</param>
/// <param name="Text">Kullanıcıya sorulan soru.</param>
/// <param name="Options">
/// Seçenekler. Boşsa serbest metin.
///
/// <b>Seçenek tercih ediliyor:</b> serbest metin kullanıcıyı düşünmeye zorluyor
/// ve çoğu kişi boş bırakıyor. Şık seçmek bir saniye sürüyor ve cevap
/// makine-okunur oluyor.
/// </param>
/// <param name="Why">
/// Bu sorunun şemayı nasıl değiştireceği.
///
/// Gerekçesiz soru, doldurulacak bir form gibi hissettiriyor. "Neden
/// soruyorsun" cevabı görünürse kullanıcı düşünerek cevaplıyor.
/// </param>
/// <param name="DefaultOption">Kullanıcı atlarsa varsayılacak cevap.</param>
public sealed record ClarifyingQuestion(
    string Id,
    string Text,
    IReadOnlyList<string> Options,
    string Why,
    string? DefaultOption = null);

/// <summary>
/// İlk prompt'tan sonra sorulacak sorular — <b>AI KULLANMADAN.</b>
///
/// <b>Çözdüğü sorun:</b> "bir e-ticaret şeması yap" cümlesi çok az şey söylüyor.
/// Model boşlukları kendi doldurunca ortaya çıkan şema teknik olarak doğru ama
/// kullanıcının aklındakinden farklı oluyor — ve kullanıcı bunu ancak şemayı
/// gördükten sonra fark ediyor. O noktada düzeltmek, baştan sormaktan çok daha
/// pahalı: hem tur harcanmış hem de kullanıcı yanlış bir şeye alışmış oluyor.
///
/// <b>Sorular STATİK ve iş türüne bağlı.</b> Soruları da modele ürettirmek
/// mümkündü ve daha "akıllı" görünürdü; yapılmadı çünkü:
/// <list type="bullet">
/// <item>Kullanıcı daha hiçbir şey görmeden token harcanırdı.</item>
/// <item>Sorular her seferinde değişirdi — aynı isteğe aynı soruları sormayan
/// bir ürün, kararsız hissettirir.</item>
/// <item>Model alakasız ya da cevaplanamaz soru üretebilir; sabit bir bankada
/// her sorunun neden sorulduğu bilinir.</item>
/// </list>
/// </summary>
public static class ClarifyingQuestions
{
    /// <summary>
    /// Her projede sorulan çekirdek sorular.
    ///
    /// Üçü de şemanın ŞEKLİNİ değiştiriyor; "hoş olur" diye sorulan hiçbir soru
    /// yok. Soru sayısı bilinçli olarak az: on soruluk bir form, kullanıcının
    /// vazgeçmesi demek.
    /// </summary>
    private static readonly ClarifyingQuestion[] Core =
    {
        new(
            Id: "scale",
            Text: "Bu proje ne büyüklükte olacak?",
            Options: new[] { "Küçük (birkaç yüz kayıt)", "Orta (binlerce)", "Büyük (milyonlarca)" },
            Why: "Büyük veride index stratejisi, sayfalama ve arşivleme tabloları gerekir; küçük projede bunlar gereksiz karmaşıklıktır.",
            DefaultOption: "Orta (binlerce)"),

        new(
            Id: "environment",
            Text: "Nerede kullanılacak?",
            Options: new[] { "Deneme / öğrenme", "İç kullanım", "Üretim (gerçek müşteri)" },
            Why: "Üretimde denetim kaydı, yumuşak silme ve zaman damgaları şart; deneme projesinde bunlar yolu uzatır.",
            DefaultOption: "Üretim (gerçek müşteri)"),

        new(
            Id: "auth",
            Text: "Kullanıcı girişi olacak mı?",
            Options: new[] { "Hayır", "Evet, basit (e-posta + şifre)", "Evet, roller ve izinlerle" },
            Why: "Rol/izin modeli üç dört ek tablo demek; sonradan eklemek var olan yabancı anahtarları değiştirmeyi gerektirir.",
            DefaultOption: "Evet, basit (e-posta + şifre)"),
    };

    /// <summary>İş türüne özel sorular.</summary>
    private static readonly Dictionary<ProjectArchetype, ClarifyingQuestion[]> Specific = new()
    {
        [ProjectArchetype.Ecommerce] = new[]
        {
            new ClarifyingQuestion("variants", "Ürünlerin varyantı olacak mı (beden, renk)?",
                new[] { "Hayır, tek ürün tek kayıt", "Evet, varyantlı" },
                "Varyant, ürün tablosunu ikiye bölüyor ve stoğu varyant seviyesine taşıyor — sonradan eklemek stok verisini taşımayı gerektirir.",
                "Evet, varyantlı"),
            new ClarifyingQuestion("payment", "Ödeme ve kargo takibi şemada olacak mı?",
                new[] { "Sadece sipariş yeter", "Ödeme kayıtları da olsun", "Ödeme + kargo takibi" },
                "Ödeme ve kargo ayrı yaşam döngüleri; siparişin içine gömmek, bir siparişin iki ödemesi olduğunda kırılır.",
                "Ödeme kayıtları da olsun"),
        },

        [ProjectArchetype.Saas] = new[]
        {
            new ClarifyingQuestion("tenancy", "Müşteriler birbirinden nasıl ayrılacak?",
                new[] { "Tek müşteri (ayrım yok)", "Her tabloda tenant kolonu", "Müşteri başına ayrı şema" },
                "Bu karar HER tabloyu etkiliyor ve sonradan değiştirmek şemanın tamamını yeniden yazmak demek.",
                "Her tabloda tenant kolonu"),
            new ClarifyingQuestion("billing", "Abonelik ve faturalama şemada olacak mı?",
                new[] { "Hayır", "Plan + abonelik", "Plan + abonelik + kullanım ölçümü" },
                "Kullanım bazlı faturalama, yüksek hacimli bir ölçüm tablosu demek — index'i baştan doğru kurmak gerekir.",
                "Plan + abonelik"),
        },

        [ProjectArchetype.Erp] = new[]
        {
            new ClarifyingQuestion("companies", "Birden fazla şirket/şube olacak mı?",
                new[] { "Tek şirket", "Çoklu şirket", "Çoklu şirket + şube" },
                "Çoklu şirket, neredeyse her tabloya bir ayrım kolonu ekliyor; sonradan eklemek mevcut veriyi bölmeyi gerektirir.",
                "Tek şirket"),
            new ClarifyingQuestion("accounting", "Muhasebe entegrasyonu olacak mı?",
                new[] { "Hayır", "Cari hesap takibi", "Tam çift taraflı defter" },
                "Çift taraflı defter, değiştirilemez kayıt (append-only) tasarımı ister — normal tablolardan farklı bir yaklaşım.",
                "Cari hesap takibi"),
        },

        [ProjectArchetype.Game] = new[]
        {
            new ClarifyingQuestion("progression", "Oyuncu ilerlemesi nasıl saklanacak?",
                new[] { "Basit (seviye + puan)", "Envanter + eşyalar", "Envanter + görevler + başarımlar" },
                "Envanter, oyuncu başına yüksek satır sayısı demek; ilerleme tablosunun şekli oyunun tamamını belirliyor.",
                "Envanter + eşyalar"),
            new ClarifyingQuestion("multiplayer", "Çok oyunculu mu?",
                new[] { "Tek oyunculu", "Çok oyunculu (lonca/takım)", "Çok oyunculu + eşleştirme" },
                "Lonca ve eşleştirme, oyuncular arası ilişki tabloları ekliyor; tek oyuncuda bunlar boş yere durur.",
                "Tek oyunculu"),
        },

        [ProjectArchetype.Social] = new[]
        {
            new ClarifyingQuestion("graph", "Kullanıcılar arası bağ nasıl?",
                new[] { "Takip (tek yönlü)", "Arkadaşlık (çift yönlü onaylı)", "İkisi de" },
                "Tek yönlü takip ile onaylı arkadaşlık farklı tablo şekilleri; sonradan değiştirmek tüm ilişki verisini dönüştürmeyi gerektirir.",
                "Takip (tek yönlü)"),
            new ClarifyingQuestion("media", "Gönderiler medya içerecek mi?",
                new[] { "Sadece metin", "Metin + görsel", "Metin + görsel + video" },
                "Medya ayrı bir tablo ve dosya referansı demek; gönderiye gömmek, bir gönderiye çok medya eklendiğinde kırılır.",
                "Metin + görsel"),
        },

        [ProjectArchetype.Fintech] = new[]
        {
            new ClarifyingQuestion("ledger", "Para hareketleri nasıl tutulacak?",
                new[] { "Basit bakiye kolonu", "İşlem geçmişi + hesaplanan bakiye", "Çift taraflı defter" },
                "Bakiyeyi kolonda tutmak, eşzamanlı işlemlerde para kaybettirir — bu, düzeltilmesi en pahalı hatalardan biri.",
                "İşlem geçmişi + hesaplanan bakiye"),
            new ClarifyingQuestion("currency", "Birden fazla para birimi olacak mı?",
                new[] { "Tek para birimi", "Çoklu para birimi" },
                "Çoklu para birimi, her tutar kolonuna bir birim ve kur kaydı ekliyor; sonradan eklemek tüm tutarları dönüştürmeyi gerektirir.",
                "Tek para birimi"),
        },

        [ProjectArchetype.Healthcare] = new[]
        {
            new ClarifyingQuestion("records", "Hasta kayıtları ne kadar ayrıntılı?",
                new[] { "Temel bilgi + randevu", "Tanı ve tedavi geçmişi", "Tam tıbbi kayıt + reçete" },
                "Tıbbi kayıt, değiştirilemez geçmiş ve kim-ne-zaman izi ister; bu, normal bir tablodan farklı bir tasarım.",
                "Tanı ve tedavi geçmişi"),
            new ClarifyingQuestion("privacy", "Kişisel sağlık verisi maskelenecek mi?",
                new[] { "Hayır", "Evet, hassas kolonlar işaretlensin" },
                "İşaretlenen kolonlar API'de maskelenebiliyor; sonradan işaretlemek, o veriye çoktan erişilmiş olması demek.",
                "Evet, hassas kolonlar işaretlensin"),
        },

        [ProjectArchetype.Education] = new[]
        {
            new ClarifyingQuestion("structure", "Eğitim yapısı nasıl?",
                new[] { "Kurs + öğrenci", "Kurs + ders + ödev", "Kurs + ders + ödev + sınav + not" },
                "Not ve sınav, ayrı bir değerlendirme modeli demek; kurs tablosuna sıkıştırmak birden fazla sınavda kırılır.",
                "Kurs + ders + ödev"),
        },

        [ProjectArchetype.Logistics] = new[]
        {
            new ClarifyingQuestion("tracking", "Takip ne kadar ayrıntılı?",
                new[] { "Sadece durum", "Durum + konum geçmişi", "Durum + konum + araç/sürücü" },
                "Konum geçmişi yüksek hacimli bir zaman serisi; index'i baştan doğru kurmazsan birkaç ayda yavaşlar.",
                "Durum + konum geçmişi"),
        },

        [ProjectArchetype.Iot] = new[]
        {
            new ClarifyingQuestion("volume", "Ne sıklıkta ölçüm gelecek?",
                new[] { "Saatte birkaç", "Dakikada birkaç", "Saniyede birkaç" },
                "Yüksek frekans, ölçüm tablosunu bölümlemeyi (partition) ve eski veriyi arşivlemeyi gerektirir.",
                "Dakikada birkaç"),
        },

        [ProjectArchetype.Booking] = new[]
        {
            new ClarifyingQuestion("resource", "Ne rezerve ediliyor?",
                new[] { "Tek tip kaynak (oda/masa)", "Farklı tiplerde kaynaklar", "Kaynak + personel birlikte" },
                "Farklı kaynak tipleri, tek bir tabloya sığmıyor; personel eklendiğinde çift taraflı müsaitlik kontrolü gerekir.",
                "Tek tip kaynak (oda/masa)"),
        },

        [ProjectArchetype.Crm] = new[]
        {
            new ClarifyingQuestion("pipeline", "Satış süreci takip edilecek mi?",
                new[] { "Sadece kişi/şirket kaydı", "Fırsat + aşamalar", "Fırsat + aşamalar + aktivite geçmişi" },
                "Aşama geçmişi ayrı bir tablo; fırsat kaydında tek kolon tutmak, 'bu fırsat ne zaman hangi aşamadaydı' sorusunu cevapsız bırakır.",
                "Fırsat + aşamalar"),
        },

        [ProjectArchetype.Cms] = new[]
        {
            new ClarifyingQuestion("versioning", "İçeriğin sürüm geçmişi tutulacak mı?",
                new[] { "Hayır", "Evet, taslak + yayın", "Evet, tam sürüm geçmişi" },
                "Sürüm geçmişi, içerik tablosunu ikiye bölüyor; sonradan eklemek mevcut içeriği taşımayı gerektirir.",
                "Evet, taslak + yayın"),
        },

        [ProjectArchetype.Marketplace] = new[]
        {
            new ClarifyingQuestion("payouts", "Satıcı ödemeleri takip edilecek mi?",
                new[] { "Hayır", "Komisyon + hakediş", "Komisyon + hakediş + ödeme geçmişi" },
                "Hakediş, siparişten ayrı bir para akışı; sipariş tablosuna gömmek, iade ve kısmi ödemede kırılır.",
                "Komisyon + hakediş"),
        },
    };

    /// <summary>
    /// Bu iş için sorulacak sorular.
    ///
    /// <b>En fazla beş soru.</b> Daha fazlası bir form; kullanıcı yarıda bırakır
    /// ve elde hiçbir şey kalmaz. Beş soru bir dakikada cevaplanıyor.
    /// </summary>
    public static IReadOnlyList<ClarifyingQuestion> For(ProjectArchetype archetype)
    {
        var specific = Specific.TryGetValue(archetype, out var list) ? list : Array.Empty<ClarifyingQuestion>();

        // Türe özel sorular ÖNCE: kullanıcı ilk gördüğü sorunun kendi işiyle
        // ilgili olduğunu anlarsa formu ciddiye alıyor.
        return specific.Concat(Core).Take(5).ToList();
    }

    /// <summary>
    /// Cevapları modele verilecek ek bağlama çevirir.
    ///
    /// <b>Cevaplanmamış sorular varsayılanıyla yazılıyor, atlanmıyor.</b>
    /// Atlamak, modelin o boşluğu yine kendi doldurması demek — sormanın amacı
    /// tam olarak bunu engellemekti.
    /// </summary>
    public static string ToPromptContext(
        ProjectArchetype archetype,
        IReadOnlyList<ClarifyingQuestion> questions,
        IReadOnlyDictionary<string, string>? answers)
    {
        var lines = new List<string>();

        if (archetype != ProjectArchetype.Generic)
            lines.Add($"Project type: {archetype}");

        foreach (var question in questions)
        {
            var answered = answers is not null && answers.TryGetValue(question.Id, out var value) &&
                           !string.IsNullOrWhiteSpace(value);

            var answer = answered ? answers![question.Id] : question.DefaultOption;
            if (string.IsNullOrWhiteSpace(answer)) continue;

            lines.Add($"{question.Text} → {answer}");
        }

        return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
    }
}
