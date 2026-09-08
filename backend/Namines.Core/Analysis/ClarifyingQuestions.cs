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
            Text: "How much data will this hold?",
            Options: new[] { "Small (a few hundred rows)", "Medium (thousands)", "Large (millions)" },
            Why: "Large data needs an index strategy, pagination and archive tables; on a small project those are needless complexity.",
            DefaultOption: "Medium (thousands)"),

        new(
            Id: "environment",
            Text: "Where will this run?",
            Options: new[] { "Experiment / learning", "Internal use", "Production (real customers)" },
            Why: "Production needs an audit trail, soft deletes and timestamps; on a throwaway project those slow you down.",
            DefaultOption: "Production (real customers)"),

        new(
            Id: "auth",
            Text: "Will people sign in?",
            Options: new[] { "No", "Yes, simple (email + password)", "Yes, with roles and permissions" },
            Why: "A role/permission model means three or four extra tables; adding it later means changing foreign keys that already exist.",
            DefaultOption: "Yes, simple (email + password)"),
    };

    /// <summary>İş türüne özel sorular.</summary>
    private static readonly Dictionary<ProjectArchetype, ClarifyingQuestion[]> Specific = new()
    {
        [ProjectArchetype.Ecommerce] = new[]
        {
            new ClarifyingQuestion("variants", "Do products come in variants (size, colour)?",
                new[] { "No, one product is one row", "Yes, with variants" },
                "Variants split the product table in two and move stock down to the variant level — adding them later means migrating stock data.",
                "Yes, with variants"),
            new ClarifyingQuestion("payment", "Should payments and shipping be part of the schema?",
                new[] { "Orders only", "Also payment records", "Payments + shipment tracking" },
                "Payment and shipping have their own lifecycles; folding them into the order breaks the moment one order has two payments.",
                "Also payment records"),
        },

        [ProjectArchetype.Saas] = new[]
        {
            new ClarifyingQuestion("tenancy", "How are customers kept apart?",
                new[] { "Single customer (no separation)", "A tenant column on every table", "A separate schema per customer" },
                "This decision touches EVERY table, and changing it later means rewriting the whole schema.",
                "A tenant column on every table"),
            new ClarifyingQuestion("billing", "Should subscriptions and billing be in the schema?",
                new[] { "No", "Plans + subscriptions", "Plans + subscriptions + usage metering" },
                "Usage-based billing means a high-volume metering table — its indexes have to be right from day one.",
                "Plans + subscriptions"),
        },

        [ProjectArchetype.Erp] = new[]
        {
            new ClarifyingQuestion("companies", "Will there be more than one company or branch?",
                new[] { "Single company", "Multiple companies", "Multiple companies + branches" },
                "Multiple companies add a separating column to nearly every table; adding it later means splitting existing data.",
                "Single company"),
            new ClarifyingQuestion("accounting", "Will accounting be integrated?",
                new[] { "No", "Account balance tracking", "Full double-entry ledger" },
                "A double-entry ledger needs append-only records — a different design from ordinary tables.",
                "Account balance tracking"),
        },

        [ProjectArchetype.Game] = new[]
        {
            new ClarifyingQuestion("progression", "How is player progress stored?",
                new[] { "Simple (level + score)", "Inventory + items", "Inventory + quests + achievements" },
                "Inventory means a lot of rows per player; the shape of the progress table drives the whole game.",
                "Inventory + items"),
            new ClarifyingQuestion("multiplayer", "Is it multiplayer?",
                new[] { "Single player", "Multiplayer (guild/team)", "Multiplayer + matchmaking" },
                "Guilds and matchmaking add player-to-player relation tables; in single player they just sit there empty.",
                "Single player"),
        },

        [ProjectArchetype.Social] = new[]
        {
            new ClarifyingQuestion("graph", "How do users connect?",
                new[] { "Follow (one way)", "Friendship (mutual, approved)", "Both" },
                "One-way follows and approved friendships are different table shapes; changing later means converting every relation row.",
                "Follow (one way)"),
            new ClarifyingQuestion("media", "Will posts carry media?",
                new[] { "Text only", "Text + images", "Text + images + video" },
                "Media means its own table and file references; embedding it in the post breaks as soon as one post has several files.",
                "Text + images"),
        },

        [ProjectArchetype.Fintech] = new[]
        {
            new ClarifyingQuestion("ledger", "How is money movement stored?",
                new[] { "A simple balance column", "Transaction history + derived balance", "Double-entry ledger" },
                "Keeping the balance in a column loses money under concurrent transactions — one of the most expensive bugs to fix later.",
                "Transaction history + derived balance"),
            new ClarifyingQuestion("currency", "Will there be more than one currency?",
                new[] { "Single currency", "Multiple currencies" },
                "Multiple currencies add a unit and an exchange-rate record to every amount; adding it later means converting every amount.",
                "Single currency"),
        },

        [ProjectArchetype.Healthcare] = new[]
        {
            new ClarifyingQuestion("records", "How detailed are patient records?",
                new[] { "Basics + appointments", "Diagnosis and treatment history", "Full medical record + prescriptions" },
                "Medical records need immutable history and a who-did-what trail — a different design from an ordinary table.",
                "Diagnosis and treatment history"),
            new ClarifyingQuestion("privacy", "Should personal health data be masked?",
                new[] { "No", "Yes, flag the sensitive columns" },
                "Flagged columns can be masked in the API; flagging later means the data has already been read.",
                "Yes, flag the sensitive columns"),
        },

        [ProjectArchetype.Education] = new[]
        {
            new ClarifyingQuestion("structure", "How is the teaching structured?",
                new[] { "Courses + students", "Courses + lessons + assignments", "Courses + lessons + assignments + exams + grades" },
                "Grades and exams need their own assessment model; squeezing them into the course table breaks on the second exam.",
                "Courses + lessons + assignments"),
        },

        [ProjectArchetype.Logistics] = new[]
        {
            new ClarifyingQuestion("tracking", "How detailed is tracking?",
                new[] { "Status only", "Status + location history", "Status + location + vehicle/driver" },
                "Location history is a high-volume time series; get its indexes wrong and it slows down within months.",
                "Status + location history"),
        },

        [ProjectArchetype.Iot] = new[]
        {
            new ClarifyingQuestion("volume", "How often do readings arrive?",
                new[] { "A few per hour", "A few per minute", "A few per second" },
                "High frequency means partitioning the readings table and archiving old data.",
                "A few per minute"),
        },

        [ProjectArchetype.Booking] = new[]
        {
            new ClarifyingQuestion("resource", "What is being booked?",
                new[] { "One kind of resource (room/table)", "Several kinds of resources", "Resource + staff together" },
                "Different resource kinds do not fit one table; adding staff means checking availability on both sides.",
                "One kind of resource (room/table)"),
        },

        [ProjectArchetype.Crm] = new[]
        {
            new ClarifyingQuestion("pipeline", "Will you track a sales process?",
                new[] { "Contacts and companies only", "Deals + stages", "Deals + stages + activity history" },
                "Stage history is its own table; a single column on the deal leaves 'which stage was this deal in, and when' unanswerable.",
                "Deals + stages"),
        },

        [ProjectArchetype.Cms] = new[]
        {
            new ClarifyingQuestion("versioning", "Should content keep a version history?",
                new[] { "No", "Yes, draft + published", "Yes, full version history" },
                "Version history splits the content table in two; adding it later means migrating the content you already have.",
                "Yes, draft + published"),
        },

        [ProjectArchetype.Marketplace] = new[]
        {
            new ClarifyingQuestion("payouts", "Will seller payouts be tracked?",
                new[] { "No", "Commission + earnings", "Commission + earnings + payout history" },
                "Earnings are a separate money flow from the order; folding them into the order table breaks on refunds and partial payments.",
                "Commission + earnings"),
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
