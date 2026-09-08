using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Core.Analysis;

/// <param name="Id">
/// Cevabın DEĞERİ — istemci bunu geri gönderir ve plan kuralları buna bakar.
///
/// <b>Neden ayrı bir kimlik var:</b> önce cevap olarak seçeneğin GÖRÜNEN METNİ
/// gidiyordu ve <see cref="Namines.Core.Analysis.PlanBuilder"/> kuralları o metnin
/// içinde parça arıyordu (<c>answers["auth"].Contains("basit")</c>). Yani bir
/// seçeneğin yazımını düzeltmek, hiçbir uyarı vermeden tablo planlamasını
/// bozuyordu — metinler İngilizceye çevrilirken tam olarak bu oldu, on dokuz
/// eşleşme parçasının hepsi elle senkronlanmak zorunda kaldı.
///
/// Kimlik kısa ve sabit; metin serbestçe değişebilir.
/// </param>
/// <param name="Label">Kullanıcıya gösterilen metin. Serbestçe değiştirilebilir.</param>
public sealed record ClarifyingOption(string Id, string Label);

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
/// <param name="DefaultOption">
/// Kullanıcı atlarsa varsayılacak cevabın <see cref="ClarifyingOption.Id"/>'si.
/// </param>
public sealed record ClarifyingQuestion(
    string Id,
    string Text,
    IReadOnlyList<ClarifyingOption> Options,
    string Why,
    string? DefaultOption = null)
{
    /// <summary>Bir seçenek kimliğinin gösterilecek metni; bilinmiyorsa kimliğin kendisi.</summary>
    public string LabelFor(string? optionId) =>
        Options.FirstOrDefault(o => o.Id == optionId)?.Label ?? optionId ?? string.Empty;

    /// <summary>
    /// Gelen cevabı seçenek kimliğine çevirir.
    ///
    /// <b>Metin de kabul ediliyor</b> çünkü dağıtım sırasında elindeki eski
    /// istemci hâlâ görünen metni gönderiyor olabilir; o istekleri reddetmek,
    /// kullanıcıya sebepsiz boş bir plan göstermek olurdu.
    /// </summary>
    public string? ResolveOptionId(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return null;
        if (Options.Any(o => o.Id == answer)) return answer;
        return Options.FirstOrDefault(o =>
            string.Equals(o.Label, answer, StringComparison.OrdinalIgnoreCase))?.Id;
    }
}

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
            Text: "How much data will this hold?",
            Options: new ClarifyingOption[]
            {
                new("small", "Small (a few hundred rows)"),
                new("medium", "Medium (thousands)"),
                new("large", "Large (millions)"),
            },
            Why: "Large data needs an index strategy, pagination and archive tables; on a small project those are needless complexity.",
            DefaultOption: "medium"),

        new(
            Id: "environment",
            Text: "Where will this run?",
            Options: new ClarifyingOption[]
            {
                new("experiment", "Experiment / learning"),
                new("internal", "Internal use"),
                new("production", "Production (real customers)"),
            },
            Why: "Production needs an audit trail, soft deletes and timestamps; on a throwaway project those slow you down.",
            DefaultOption: "production"),

        new(
            Id: "auth",
            Text: "Will people sign in?",
            Options: new ClarifyingOption[]
            {
                new("none", "No"),
                new("simple", "Yes, simple (email + password)"),
                new("roles", "Yes, with roles and permissions"),
            },
            Why: "A role/permission model means three or four extra tables; adding it later means changing foreign keys that already exist.",
            DefaultOption: "simple"),
    };

    /// <summary>İş türüne özel sorular.</summary>
    private static readonly Dictionary<ProjectArchetype, ClarifyingQuestion[]> Specific = new()
    {
        [ProjectArchetype.Ecommerce] = new[]
        {
            new ClarifyingQuestion("variants", "Do products come in variants (size, colour)?",
                new ClarifyingOption[]
                {
                    new("none", "No, one product is one row"),
                    new("variants", "Yes, with variants"),
                },
                "Variants split the product table in two and move stock down to the variant level — adding them later means migrating stock data.",
                "variants"),
            new ClarifyingQuestion("payment", "Should payments and shipping be part of the schema?",
                new ClarifyingOption[]
                {
                    new("orders-only", "Orders only"),
                    new("payments", "Also payment records"),
                    new("payments-shipping", "Payments + shipment tracking"),
                },
                "Payment and shipping have their own lifecycles; folding them into the order breaks the moment one order has two payments.",
                "payments"),
        },

        [ProjectArchetype.Saas] = new[]
        {
            new ClarifyingQuestion("tenancy", "How are customers kept apart?",
                new ClarifyingOption[]
                {
                    new("single", "Single customer (no separation)"),
                    new("tenant-column", "A tenant column on every table"),
                    new("schema-per-tenant", "A separate schema per customer"),
                },
                "This decision touches EVERY table, and changing it later means rewriting the whole schema.",
                "tenant-column"),
            new ClarifyingQuestion("billing", "Should subscriptions and billing be in the schema?",
                new ClarifyingOption[]
                {
                    new("none", "No"),
                    new("plans", "Plans + subscriptions"),
                    new("plans-usage", "Plans + subscriptions + usage metering"),
                },
                "Usage-based billing means a high-volume metering table — its indexes have to be right from day one.",
                "plans"),
        },

        [ProjectArchetype.Erp] = new[]
        {
            new ClarifyingQuestion("companies", "Will there be more than one company or branch?",
                new ClarifyingOption[]
                {
                    new("single", "Single company"),
                    new("multi", "Multiple companies"),
                    new("multi-branch", "Multiple companies + branches"),
                },
                "Multiple companies add a separating column to nearly every table; adding it later means splitting existing data.",
                "single"),
            new ClarifyingQuestion("accounting", "Will accounting be integrated?",
                new ClarifyingOption[]
                {
                    new("none", "No"),
                    new("balances", "Account balance tracking"),
                    new("double-entry", "Full double-entry ledger"),
                },
                "A double-entry ledger needs append-only records — a different design from ordinary tables.",
                "balances"),
        },

        [ProjectArchetype.Game] = new[]
        {
            new ClarifyingQuestion("progression", "How is player progress stored?",
                new ClarifyingOption[]
                {
                    new("simple", "Simple (level + score)"),
                    new("inventory", "Inventory + items"),
                    new("inventory-quests", "Inventory + quests + achievements"),
                },
                "Inventory means a lot of rows per player; the shape of the progress table drives the whole game.",
                "inventory"),
            new ClarifyingQuestion("multiplayer", "Is it multiplayer?",
                new ClarifyingOption[]
                {
                    new("single", "Single player"),
                    new("groups", "Multiplayer (guild/team)"),
                    new("matchmaking", "Multiplayer + matchmaking"),
                },
                "Guilds and matchmaking add player-to-player relation tables; in single player they just sit there empty.",
                "single"),
        },

        [ProjectArchetype.Social] = new[]
        {
            new ClarifyingQuestion("graph", "How do users connect?",
                new ClarifyingOption[]
                {
                    new("follow", "Follow (one way)"),
                    new("friendship", "Friendship (mutual, approved)"),
                    new("both", "Both"),
                },
                "One-way follows and approved friendships are different table shapes; changing later means converting every relation row.",
                "follow"),
            new ClarifyingQuestion("media", "Will posts carry media?",
                new ClarifyingOption[]
                {
                    new("text", "Text only"),
                    new("images", "Text + images"),
                    new("video", "Text + images + video"),
                },
                "Media means its own table and file references; embedding it in the post breaks as soon as one post has several files.",
                "images"),
        },

        [ProjectArchetype.Fintech] = new[]
        {
            new ClarifyingQuestion("ledger", "How is money movement stored?",
                new ClarifyingOption[]
                {
                    new("balance-column", "A simple balance column"),
                    new("transactions", "Transaction history + derived balance"),
                    new("double-entry", "Double-entry ledger"),
                },
                "Keeping the balance in a column loses money under concurrent transactions — one of the most expensive bugs to fix later.",
                "transactions"),
            new ClarifyingQuestion("currency", "Will there be more than one currency?",
                new ClarifyingOption[]
                {
                    new("single", "Single currency"),
                    new("multi", "Multiple currencies"),
                },
                "Multiple currencies add a unit and an exchange-rate record to every amount; adding it later means converting every amount.",
                "single"),
        },

        [ProjectArchetype.Healthcare] = new[]
        {
            new ClarifyingQuestion("records", "How detailed are patient records?",
                new ClarifyingOption[]
                {
                    new("basics", "Basics + appointments"),
                    new("history", "Diagnosis and treatment history"),
                    new("full", "Full medical record + prescriptions"),
                },
                "Medical records need immutable history and a who-did-what trail — a different design from an ordinary table.",
                "history"),
            new ClarifyingQuestion("privacy", "Should personal health data be masked?",
                new ClarifyingOption[]
                {
                    new("none", "No"),
                    new("flag-sensitive", "Yes, flag the sensitive columns"),
                },
                "Flagged columns can be masked in the API; flagging later means the data has already been read.",
                "flag-sensitive"),
        },

        [ProjectArchetype.Education] = new[]
        {
            new ClarifyingQuestion("structure", "How is the teaching structured?",
                new ClarifyingOption[]
                {
                    new("courses", "Courses + students"),
                    new("lessons", "Courses + lessons + assignments"),
                    new("exams", "Courses + lessons + assignments + exams + grades"),
                },
                "Grades and exams need their own assessment model; squeezing them into the course table breaks on the second exam.",
                "lessons"),
        },

        [ProjectArchetype.Logistics] = new[]
        {
            new ClarifyingQuestion("tracking", "How detailed is tracking?",
                new ClarifyingOption[]
                {
                    new("status", "Status only"),
                    new("location", "Status + location history"),
                    new("fleet", "Status + location + vehicle/driver"),
                },
                "Location history is a high-volume time series; get its indexes wrong and it slows down within months.",
                "location"),
        },

        [ProjectArchetype.Iot] = new[]
        {
            new ClarifyingQuestion("volume", "How often do readings arrive?",
                new ClarifyingOption[]
                {
                    new("hourly", "A few per hour"),
                    new("per-minute", "A few per minute"),
                    new("per-second", "A few per second"),
                },
                "High frequency means partitioning the readings table and archiving old data.",
                "per-minute"),
        },

        [ProjectArchetype.Booking] = new[]
        {
            new ClarifyingQuestion("resource", "What is being booked?",
                new ClarifyingOption[]
                {
                    new("single-kind", "One kind of resource (room/table)"),
                    new("many-kinds", "Several kinds of resources"),
                    new("with-staff", "Resource + staff together"),
                },
                "Different resource kinds do not fit one table; adding staff means checking availability on both sides.",
                "single-kind"),
        },

        [ProjectArchetype.Crm] = new[]
        {
            new ClarifyingQuestion("pipeline", "Will you track a sales process?",
                new ClarifyingOption[]
                {
                    new("contacts", "Contacts and companies only"),
                    new("deals", "Deals + stages"),
                    new("deals-activity", "Deals + stages + activity history"),
                },
                "Stage history is its own table; a single column on the deal leaves 'which stage was this deal in, and when' unanswerable.",
                "deals"),
        },

        [ProjectArchetype.Cms] = new[]
        {
            new ClarifyingQuestion("versioning", "Should content keep a version history?",
                new ClarifyingOption[]
                {
                    new("none", "No"),
                    new("draft-published", "Yes, draft + published"),
                    new("full-history", "Yes, full version history"),
                },
                "Version history splits the content table in two; adding it later means migrating the content you already have.",
                "draft-published"),
        },

        [ProjectArchetype.Marketplace] = new[]
        {
            new ClarifyingQuestion("payouts", "Will seller payouts be tracked?",
                new ClarifyingOption[]
                {
                    new("none", "No"),
                    new("commission", "Commission + earnings"),
                    new("commission-history", "Commission + earnings + payout history"),
                },
                "Earnings are a separate money flow from the order; folding them into the order table breaks on refunds and partial payments.",
                "commission"),
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

            // Modele KİMLİK değil METİN gidiyor: "tenant-column" bir dil modeline
            // hiçbir şey anlatmaz, "A tenant column on every table" anlatır.
            lines.Add($"{question.Text} → {question.LabelFor(question.ResolveOptionId(answer) ?? answer)}");
        }

        return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
    }
}
