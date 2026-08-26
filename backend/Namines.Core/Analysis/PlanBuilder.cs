using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Core.Analysis;

/// <param name="Name">Tablo adı.</param>
/// <param name="Reason">Bu tablonun neden var olduğu — kullanıcının cevabına bağlanıyor.</param>
public sealed record PlannedTable(string Name, string Reason);

/// <param name="Archetype">Tespit edilen iş türü.</param>
/// <param name="Tables">Üretilecek tablolar ve gerekçeleri.</param>
/// <param name="Assumptions">
/// Cevaplanmamış sorular için kullanılan varsayımlar — kullanıcı hiçbir şeye
/// cevap vermeden onaylarsa neyin varsayıldığını görmeli.
/// </param>
/// <param name="FollowUp">
/// Cevaplardan çıkan gerçek bir belirsizlik varsa ek soru; yoksa <c>null</c>.
/// Doluysa plan henüz KESİN değil — kullanıcı bu soruyu cevaplayıp planı
/// yeniden istemeli.
/// </param>
/// <param name="Round">Kaçıncı netleştirme turunda üretildiği. En fazla 3.</param>
public sealed record SchemaPlan(
    ProjectArchetype Archetype,
    IReadOnlyList<PlannedTable> Tables,
    IReadOnlyList<string> Assumptions,
    ClarifyingQuestion? FollowUp,
    int Round);

/// <summary>
/// Netleştirme cevaplarından <b>deterministik</b> bir plan çıkarır —
/// second-phase/05-PLAN-MODU.md.
///
/// <b>Tablo listesi AI'YA YAZDIRILMIYOR.</b> Modelin uydurduğu bir plan,
/// üretilecek şemadan farklı çıkabilir ve bu, kullanıcının onayını
/// anlamsız kılardı — "onayladığım plan bu değildi" durumu. Liste burada,
/// cevaplardan kural tabanlı olarak üretiliyor; üretim hattı sonra bu planı
/// GERÇEKLEŞTİRMEYE çalışıyor, yeniden icat etmiyor.
///
/// <b>En fazla bir ek soru döner, sınırsız değil.</b> Üç turdan sonra
/// (çağıran <paramref name="round"/> ile sınırlıyor) hiç soru dönmez —
/// sonsuz soru-cevap kullanıcıyı yorup terk ettirir.
/// </summary>
public static class PlanBuilder
{
    private const int MaxRounds = 3;

    /// <summary>Her iş türünde her zaman var olan çekirdek tablolar.</summary>
    private static readonly Dictionary<ProjectArchetype, PlannedTable[]> BaseTables = new()
    {
        [ProjectArchetype.Ecommerce] = new[]
        {
            new PlannedTable("products", "Satılan ürünler."),
            new PlannedTable("orders", "Bir siparişin başlığı — durum, tarih, toplam."),
            new PlannedTable("order_items", "Siparişin satırları; ürün başına miktar ve o anki fiyat."),
        },
        [ProjectArchetype.Marketplace] = new[]
        {
            new PlannedTable("sellers", "Ürün/hizmet sunan satıcılar."),
            new PlannedTable("listings", "Bir satıcının yayınladığı ürün/hizmet."),
            new PlannedTable("orders", "Alıcının verdiği sipariş."),
            new PlannedTable("payouts", "Platformun satıcıya yaptığı ödeme — alıcının ödemesinden AYRI."),
        },
        [ProjectArchetype.Saas] = new[]
        {
            new PlannedTable("tenants", "Her müşteri kuruluş — verinin izole edildiği birim."),
            new PlannedTable("subscriptions", "Tenant'ın planı ve dönemi."),
        },
        [ProjectArchetype.Erp] = new[]
        {
            new PlannedTable("stock_movements", "Stok hareketi kaydı — tek bir miktar kolonu değil, defter."),
            new PlannedTable("invoices", "Fatura başlığı."),
            new PlannedTable("invoice_items", "Fatura satırları."),
        },
        [ProjectArchetype.Game] = new[]
        {
            new PlannedTable("players", "Oyuncu hesabı."),
            new PlannedTable("player_progress", "Oyuncunun ilerlemesi — seviye/puan."),
        },
        [ProjectArchetype.Social] = new[]
        {
            new PlannedTable("posts", "Kullanıcı gönderileri."),
            new PlannedTable("follows", "Yönlü takip ilişkisi — kimin kimi takip ettiği."),
        },
        [ProjectArchetype.Fintech] = new[]
        {
            new PlannedTable("accounts", "Bakiyesi olan hesap."),
            new PlannedTable("ledger_entries", "Ekle-only defter kaydı; bakiye BURADAN türetilir, ayrı bir kolonda tutulmaz."),
        },
        [ProjectArchetype.Healthcare] = new[]
        {
            new PlannedTable("patients", "Hasta kaydı."),
            new PlannedTable("encounters", "Hastanın her başvurusu/görüşmesi — geçmiş korunur, üzerine yazılmaz."),
        },
        [ProjectArchetype.Education] = new[]
        {
            new PlannedTable("courses", "Ders tanımı."),
            new PlannedTable("course_offerings", "Bir dönemde açılan ders — eğitmen, tarih, kontenjan."),
            new PlannedTable("enrolments", "Öğrencinin bir offering'e kaydı."),
        },
        [ProjectArchetype.Logistics] = new[]
        {
            new PlannedTable("shipments", "Sevkiyat başlığı."),
            new PlannedTable("shipment_events", "Sevkiyatın taradığı her nokta — güncel durum bunlardan türetilir."),
        },
        [ProjectArchetype.Iot] = new[]
        {
            new PlannedTable("devices", "Cihaz meta verisi — küçük ve sabit."),
            new PlannedTable("readings", "Ölçüm — en büyük tablo, cihaz+zamana göre anahtarlı."),
        },
        [ProjectArchetype.Booking] = new[]
        {
            new PlannedTable("resources", "Rezerve edilebilir şey — oda, koltuk, saat."),
            new PlannedTable("reservations", "Rezervasyon — kaynak+zaman aralığı benzersiz olmalı, çifte rezervasyonu önler."),
        },
        [ProjectArchetype.Crm] = new[]
        {
            new PlannedTable("contacts", "Kişi kaydı."),
            new PlannedTable("accounts", "Kişinin bağlı olduğu kuruluş — kişi hesap değiştirebilir."),
            new PlannedTable("activities", "Görüşme/not/e-posta — tek bir zaman çizelgesi tablosu."),
        },
        [ProjectArchetype.Cms] = new[]
        {
            new PlannedTable("content_items", "Sayfa/makale — durumu (taslak/yayında) taşır."),
            new PlannedTable("revisions", "Her yayının geçmişi — üzerine yazmak son kopyayı kaybettirir."),
        },
    };

    /// <summary>
    /// Çekirdek soru cevaplarının tabloya etkisi. Anahtar: soru id'si + cevap
    /// içeriğinin bir parçası (tam eşleşme aranmaz, `Contains` kullanılır —
    /// seçenek metni ileride küçük bir kelime değişse bile kural kırılmasın).
    /// </summary>
    private static void ApplyCoreAnswers(List<PlannedTable> tables, IReadOnlyDictionary<string, string> answers)
    {
        var auth = answers.GetValueOrDefault("auth", "");
        if (auth.Contains("basit"))
        {
            tables.Add(new PlannedTable("users", "Kullanıcı hesabı — e-posta + şifre."));
        }
        else if (auth.Contains("roller"))
        {
            tables.Add(new PlannedTable("users", "Kullanıcı hesabı."));
            tables.Add(new PlannedTable("roles", "Rol tanımı."));
            tables.Add(new PlannedTable("permissions", "Rolün yapabildiği eylemler."));
        }

        if (answers.GetValueOrDefault("environment", "").Contains("Üretim"))
        {
            tables.Add(new PlannedTable("audit_logs", "Üretimde kim ne değiştirdi kaydı — denetim için."));
        }
    }

    /// <summary>İş türüne özel cevapların tabloya etkisi.</summary>
    private static readonly Dictionary<ProjectArchetype, Func<IReadOnlyDictionary<string, string>, List<PlannedTable>>> ConditionalTables = new()
    {
        [ProjectArchetype.Ecommerce] = a =>
        {
            var extra = new List<PlannedTable>();
            if (a.GetValueOrDefault("variants", "").Contains("varyantlı"))
            {
                extra.Add(new PlannedTable("product_variants",
                    a.GetValueOrDefault("variants.followup", "").Contains("paylaşırlar")
                        ? "Beden/renk gibi varyantlar — stok burada tutulur, fiyat üründen paylaşılır."
                        : "Beden/renk gibi varyantlar — stok, fiyat ve SKU burada tutulur, üründe değil."));
            }
            var payment = a.GetValueOrDefault("payment", "");
            if (payment.Contains("Ödeme"))
                extra.Add(new PlannedTable("payments", "Ödeme kaydı — siparişten AYRI, bir siparişin birden çok ödemesi olabilir."));
            if (payment.Contains("kargo"))
            {
                if (a.GetValueOrDefault("payment.followup", "").Contains("firma"))
                    extra.Add(new PlannedTable("shipment_tracking", "Kargo firması takip numarası ve durumu."));
                else
                    extra.Add(new PlannedTable("shipment_status", "Sadece durum: hazırlanıyor/kargoda/teslim edildi."));
            }
            return extra;
        },
        [ProjectArchetype.Saas] = a =>
        {
            var extra = new List<PlannedTable>();
            if (a.GetValueOrDefault("tenancy", "").Contains("tenant kolonu"))
                extra.Add(new PlannedTable("invoices",
                    a.GetValueOrDefault("tenancy.followup", "").Contains("kullanıcı")
                        ? "Fatura kullanıcı başına kesiliyor."
                        : "Fatura tenant başına kesiliyor."));
            return extra;
        },
        [ProjectArchetype.Game] = a =>
        {
            var extra = new List<PlannedTable>();
            var mp = a.GetValueOrDefault("multiplayer", "");
            if (mp.Contains("lonca") || mp.Contains("takım"))
            {
                var followUp = a.GetValueOrDefault("multiplayer.followup", "");
                if (followUp.Contains("Lonca"))
                    extra.Add(new PlannedTable("guilds", "Kalıcı, büyük oyuncu grubu."));
                else if (followUp.Contains("Takım"))
                    extra.Add(new PlannedTable("teams", "Geçici, küçük maç grubu."));
                else
                {
                    extra.Add(new PlannedTable("guilds", "Kalıcı, büyük oyuncu grubu."));
                    extra.Add(new PlannedTable("teams", "Geçici, küçük maç grubu."));
                }
            }
            if (mp.Contains("eşleştirme"))
                extra.Add(new PlannedTable("matches", "Bir eşleştirmenin sonucu."));
            var progress = a.GetValueOrDefault("progress", "");
            if (progress.Contains("eşya"))
                extra.Add(new PlannedTable("inventory_items", "Oyuncunun eşyaları — oyuncu × eşya sayısıyla büyür, dar tutulmalı."));
            if (progress.Contains("görev"))
                extra.Add(new PlannedTable("quest_progress", "Oyuncunun görev ilerlemesi."));
            return extra;
        },
        [ProjectArchetype.Erp] = a =>
        {
            var extra = new List<PlannedTable>();
            if (a.GetValueOrDefault("companies", "").Contains("Çoklu"))
            {
                extra.Add(a.GetValueOrDefault("companies.followup", "").Contains("ayrı")
                    ? new PlannedTable("warehouses", "Şirket başına ayrı depo — stok_movements buradan anahtarlanır.")
                    : new PlannedTable("warehouses", "Tüm şirketlerin paylaştığı ortak depo havuzu."));
            }
            return extra;
        },
    };

    /// <summary>
    /// Cevaptan gerçek bir belirsizlik doğduğunda sorulacak ek soru.
    ///
    /// <b>Neden ayrı bir katman:</b> ana soru bankası (ClarifyingQuestions) her
    /// projede sorulan sabit sorular; buradakiler yalnızca BELİRLİ bir cevap
    /// geldiğinde anlamlı oluyor. "Çok oyunculu" cevabı tek başına loncalı mı
    /// takımlı mı belli etmiyor — ikisi çok farklı tablo demek.
    /// </summary>
    private static readonly Dictionary<ProjectArchetype, (string QuestionId, string AnswerContains, ClarifyingQuestion FollowUp)[]> Ambiguities = new()
    {
        [ProjectArchetype.Game] = new[]
        {
            ("multiplayer", "lonca", new ClarifyingQuestion(
                "multiplayer.followup",
                "Lonca mı takım mı?",
                new[] { "Lonca (kalıcı, büyük)", "Takım (geçici, küçük)", "İkisi de" },
                "Lonca kalıcı ve büyük bir grup, takım geçici ve küçük bir maç grubu — ikisi farklı tablo demek.",
                "İkisi de")),
        },
        [ProjectArchetype.Ecommerce] = new[]
        {
            ("payment", "kargo", new ClarifyingQuestion(
                "payment.followup",
                "Kargo firmasıyla entegre mi, yalnızca durum mu?",
                new[] { "Yalnızca durum (hazırlanıyor/kargoda/teslim edildi)", "Kargo firması API'siyle entegre (takip no, firma)" },
                "Firma entegrasyonu takip numarası ve firma adı için ayrı bir tablo gerektiriyor; yalnızca durum tek bir kolonla çözülür.",
                "Yalnızca durum (hazırlanıyor/kargoda/teslim edildi)")),
            ("variants", "varyantlı", new ClarifyingQuestion(
                "variants.followup",
                "Varyantların kendi fiyatı/SKU'su olacak mı?",
                new[] { "Evet, kendi SKU ve fiyatları var", "Hayır, ürünün fiyatını paylaşırlar" },
                "Kendi fiyatı olan varyant product_variants'a price/SKU kolonu ekliyor; paylaşılan fiyatta bu kolonlar üründe kalır.",
                "Evet, kendi SKU ve fiyatları var")),
        },
        [ProjectArchetype.Saas] = new[]
        {
            ("tenancy", "tenant kolonu", new ClarifyingQuestion(
                "tenancy.followup",
                "Faturalama tenant başına mı, kullanıcı başına mı?",
                new[] { "Tenant başına (tek fatura, tüm kullanıcılar dahil)", "Kullanıcı başına (koltuk bazlı)" },
                "İkisi farklı bir invoices şeması gerektiriyor — biri tenant'a, diğeri kullanıcıya bağlanıyor.",
                "Tenant başına (tek fatura, tüm kullanıcılar dahil)")),
        },
        [ProjectArchetype.Erp] = new[]
        {
            ("companies", "Çoklu", new ClarifyingQuestion(
                "companies.followup",
                "Stok tüm şirketler arasında ortak mı, şirket başına ayrı mı?",
                new[] { "Ortak stok havuzu", "Şirket başına ayrı stok" },
                "Ortak havuzda tek bir depo tüm şirketleri besler; ayrı stokta her şirketin kendi deposu ve stock_movements anahtarı olur — sonradan ayırmak mevcut hareketleri şirkete göre bölmeyi gerektirir.",
                "Şirket başına ayrı stok")),
        },
    };

    /// <summary>
    /// Cevaplardan deterministik bir plan üretir.
    /// </summary>
    /// <param name="round">
    /// Kaçıncı netleştirme turu. <see cref="MaxRounds"/>'a ulaşıldıysa
    /// belirsizlik olsa bile ek soru DÖNDÜRÜLMEZ — sonsuz soru-cevap
    /// kullanıcıyı yorup terk ettirir.
    /// </param>
    public static SchemaPlan Build(
        ProjectArchetype archetype,
        IReadOnlyDictionary<string, string> answers,
        int round)
    {
        var tables = new List<PlannedTable>();

        if (BaseTables.TryGetValue(archetype, out var baseTables))
            tables.AddRange(baseTables);

        ApplyCoreAnswers(tables, answers);

        if (ConditionalTables.TryGetValue(archetype, out var conditional))
            tables.AddRange(conditional(answers));

        // Aynı ada iki kez düşülebilir (ör. Ecommerce hem base'de hem
        // koşulda "orders" gibi bir şey eklerse) — tekilleştir, ilk gerekçe kalır.
        var deduped = tables
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var assumptions = BuildAssumptions(archetype, answers);

        ClarifyingQuestion? followUp = null;
        if (round < MaxRounds && Ambiguities.TryGetValue(archetype, out var rules))
        {
            foreach (var (questionId, answerContains, question) in rules)
            {
                // Zaten cevaplanmış bir takip sorusu tekrar sorulmuyor.
                if (answers.ContainsKey(question.Id)) continue;

                var given = answers.GetValueOrDefault(questionId, "");
                if (given.Contains(answerContains, StringComparison.OrdinalIgnoreCase))
                {
                    followUp = question;
                    break; // Bir turda en fazla bir ek soru — art arda sorgulamak diyaloğu yorar.
                }
            }
        }

        return new SchemaPlan(archetype, deduped, assumptions, followUp, round);
    }

    /// <summary>
    /// Cevaplanmamış sorular için hangi varsayımın kullanılacağını listeler.
    ///
    /// Kullanıcı hiçbir soruyu cevaplamadan planı onaylarsa bile NE
    /// varsayıldığını görmeli — sessiz varsayım, "planı onayladım ama
    /// çıkan şema beklediğim gibi değildi" hissi yaratır.
    /// </summary>
    private static List<string> BuildAssumptions(ProjectArchetype archetype, IReadOnlyDictionary<string, string> answers)
    {
        var notes = new List<string>();
        foreach (var q in ClarifyingQuestions.For(archetype))
        {
            if (answers.ContainsKey(q.Id)) continue;
            if (string.IsNullOrWhiteSpace(q.DefaultOption)) continue;
            notes.Add($"{q.Text} — varsayılan: {q.DefaultOption}");
        }
        return notes;
    }
}
