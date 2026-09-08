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
            new PlannedTable("products", "The things you sell."),
            new PlannedTable("orders", "One order header — status, date, total."),
            new PlannedTable("order_items", "The lines of an order; quantity and the price at the time."),
        },
        [ProjectArchetype.Marketplace] = new[]
        {
            new PlannedTable("sellers", "The people offering products or services."),
            new PlannedTable("listings", "A product or service published by a seller."),
            new PlannedTable("orders", "An order placed by a buyer."),
            new PlannedTable("payouts", "What the platform pays the seller — SEPARATE from what the buyer paid."),
        },
        [ProjectArchetype.Saas] = new[]
        {
            new PlannedTable("tenants", "Each customer organisation — the unit data is isolated by."),
            new PlannedTable("subscriptions", "A tenant's plan and billing period."),
        },
        [ProjectArchetype.Erp] = new[]
        {
            new PlannedTable("stock_movements", "A stock movement record — a ledger, not a single quantity column."),
            new PlannedTable("invoices", "Invoice header."),
            new PlannedTable("invoice_items", "Invoice lines."),
        },
        [ProjectArchetype.Game] = new[]
        {
            new PlannedTable("players", "Player account."),
            new PlannedTable("player_progress", "How far a player has got — level and score."),
        },
        [ProjectArchetype.Social] = new[]
        {
            new PlannedTable("posts", "What users publish."),
            new PlannedTable("follows", "A directed follow — who follows whom."),
        },
        [ProjectArchetype.Fintech] = new[]
        {
            new PlannedTable("accounts", "An account that holds a balance."),
            new PlannedTable("ledger_entries", "Append-only ledger record; the balance is DERIVED from here, never kept in its own column."),
        },
        [ProjectArchetype.Healthcare] = new[]
        {
            new PlannedTable("patients", "Patient record."),
            new PlannedTable("encounters", "Every visit or consultation — history is kept, never overwritten."),
        },
        [ProjectArchetype.Education] = new[]
        {
            new PlannedTable("courses", "The course definition."),
            new PlannedTable("course_offerings", "A course run in a given term — teacher, dates, capacity."),
            new PlannedTable("enrolments", "A student signed up to one offering."),
        },
        [ProjectArchetype.Logistics] = new[]
        {
            new PlannedTable("shipments", "Shipment header."),
            new PlannedTable("shipment_events", "Every scan point — current status is derived from these."),
        },
        [ProjectArchetype.Iot] = new[]
        {
            new PlannedTable("devices", "Device metadata — small and stable."),
            new PlannedTable("readings", "The measurements — the biggest table, keyed by device and time."),
        },
        [ProjectArchetype.Booking] = new[]
        {
            new PlannedTable("resources", "The bookable thing — a room, a seat, an hour."),
            new PlannedTable("reservations", "A booking — resource plus time range must be unique, which is what prevents double booking."),
        },
        [ProjectArchetype.Crm] = new[]
        {
            new PlannedTable("contacts", "Person record."),
            new PlannedTable("accounts", "The organisation a contact belongs to — people change employers."),
            new PlannedTable("activities", "Calls, notes and emails — one timeline table."),
        },
        [ProjectArchetype.Cms] = new[]
        {
            new PlannedTable("content_items", "Pages and articles — carries the draft/published status."),
            new PlannedTable("revisions", "The history of each publish — overwriting loses the previous copy."),
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
        if (auth.Contains("simple"))
        {
            tables.Add(new PlannedTable("users", "User account — email + password."));
        }
        else if (auth.Contains("roles"))
        {
            tables.Add(new PlannedTable("users", "User account."));
            tables.Add(new PlannedTable("roles", "Role definition."));
            tables.Add(new PlannedTable("permissions", "What a role is allowed to do."));
        }

        if (answers.GetValueOrDefault("environment", "").Contains("Production"))
        {
            tables.Add(new PlannedTable("audit_logs", "Who changed what in production — the audit trail."));
        }
    }

    /// <summary>İş türüne özel cevapların tabloya etkisi.</summary>
    private static readonly Dictionary<ProjectArchetype, Func<IReadOnlyDictionary<string, string>, List<PlannedTable>>> ConditionalTables = new()
    {
        [ProjectArchetype.Ecommerce] = a =>
        {
            var extra = new List<PlannedTable>();
            if (a.GetValueOrDefault("variants", "").Contains("with variants"))
            {
                extra.Add(new PlannedTable("product_variants",
                    a.GetValueOrDefault("variants.followup", "").Contains("share")
                        ? "Variants like size and colour — stock lives here, price is shared from the product."
                        : "Variants like size and colour — stock, price and SKU live here, not on the product."));
            }
            var payment = a.GetValueOrDefault("payment", "");
            if (payment.Contains("payment"))
                extra.Add(new PlannedTable("payments", "Payment record — SEPARATE from the order, since one order can have several payments."));
            if (payment.Contains("shipment"))
            {
                if (a.GetValueOrDefault("payment.followup", "").Contains("carrier"))
                    extra.Add(new PlannedTable("shipment_tracking", "Carrier tracking number and status."));
                else
                    extra.Add(new PlannedTable("shipment_status", "Status only: preparing / shipped / delivered."));
            }
            return extra;
        },
        [ProjectArchetype.Saas] = a =>
        {
            var extra = new List<PlannedTable>();
            if (a.GetValueOrDefault("tenancy", "").Contains("tenant column"))
                extra.Add(new PlannedTable("invoices",
                    a.GetValueOrDefault("tenancy.followup", "").Contains("Per user")
                        ? "Invoices are issued per user."
                        : "Invoices are issued per tenant."));
            return extra;
        },
        [ProjectArchetype.Game] = a =>
        {
            var extra = new List<PlannedTable>();
            var mp = a.GetValueOrDefault("multiplayer", "");
            if (mp.Contains("guild") || mp.Contains("team"))
            {
                var followUp = a.GetValueOrDefault("multiplayer.followup", "");
                if (followUp.Contains("Guild"))
                    extra.Add(new PlannedTable("guilds", "A permanent, large group of players."));
                else if (followUp.Contains("Team"))
                    extra.Add(new PlannedTable("teams", "A temporary, small match group."));
                else
                {
                    extra.Add(new PlannedTable("guilds", "A permanent, large group of players."));
                    extra.Add(new PlannedTable("teams", "A temporary, small match group."));
                }
            }
            if (mp.Contains("matchmaking"))
                extra.Add(new PlannedTable("matches", "The result of one match."));

            // Soru kimliği "progression"; burası "progress" okuyordu, yani cevap HİÇ
            // bulunamıyor ve envanter/görev tabloları kullanıcı ne seçerse seçsin
            // plana hiç girmiyordu.
            var progress = a.GetValueOrDefault("progression", "");
            if (progress.Contains("Inventory"))
                extra.Add(new PlannedTable("inventory_items", "What a player owns — grows with players × items, so keep it narrow."));
            if (progress.Contains("quests"))
                extra.Add(new PlannedTable("quest_progress", "How far a player has got in each quest."));
            return extra;
        },
        [ProjectArchetype.Erp] = a =>
        {
            var extra = new List<PlannedTable>();
            if (a.GetValueOrDefault("companies", "").Contains("Multiple"))
            {
                extra.Add(a.GetValueOrDefault("companies.followup", "").Contains("Separate")
                    ? new PlannedTable("warehouses", "A separate warehouse per company — stock_movements is keyed from here.")
                    : new PlannedTable("warehouses", "One shared warehouse pool across all companies."));
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
            ("multiplayer", "guild", new ClarifyingQuestion(
                "multiplayer.followup",
                "Guilds or teams?",
                new[] { "Guild (permanent, large)", "Team (temporary, small)", "Both" },
                "A guild is a permanent, large group; a team is a temporary, small match group — they are different tables.",
                "Both")),
        },
        [ProjectArchetype.Ecommerce] = new[]
        {
            ("payment", "shipment", new ClarifyingQuestion(
                "payment.followup",
                "Integrated with the carrier, or status only?",
                new[] { "Status only (preparing / shipped / delivered)", "Integrated with the carrier API (tracking number, carrier)" },
                "Carrier integration needs its own table for the tracking number and carrier name; status alone fits in a single column.",
                "Status only (preparing / shipped / delivered)")),
            ("variants", "with variants", new ClarifyingQuestion(
                "variants.followup",
                "Will variants have their own price and SKU?",
                new[] { "Yes, their own SKU and price", "No, they share the product's price" },
                "Variants with their own price add price/SKU columns to product_variants; with a shared price those columns stay on the product.",
                "Yes, their own SKU and price")),
        },
        [ProjectArchetype.Saas] = new[]
        {
            ("tenancy", "tenant column", new ClarifyingQuestion(
                "tenancy.followup",
                "Is billing per tenant or per user?",
                new[] { "Per tenant (one invoice covering all users)", "Per user (seat based)" },
                "The two need different invoices schemas — one hangs off the tenant, the other off the user.",
                "Per tenant (one invoice covering all users)")),
        },
        [ProjectArchetype.Erp] = new[]
        {
            ("companies", "Multiple", new ClarifyingQuestion(
                "companies.followup",
                "Is stock shared across companies, or separate per company?",
                new[] { "One shared stock pool", "Separate stock per company" },
                "A shared pool feeds every company from one warehouse; separate stock gives each company its own warehouse and stock_movements key — splitting later means dividing existing movements by company.",
                "Separate stock per company")),
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
        // Cevaplanmayan sorular VARSAYILANIYLA dolduruluyor. Eskiden tablo
        // listesi ham cevapları okuyordu ve plan kendi yazdığı varsayımla
        // ÇELİŞİYORDU: ekranda "varsayılan: Ödeme kayıtları da olsun" yazarken
        // tablo listesinde `payments` yoktu. Aynı çelişki üretim prompt'una da
        // taşınıyordu — `ClarifyingQuestions.ToPromptContext` varsayılanları
        // dolduruyor, plan doldurmuyordu, yani prompt'un iki yarısı birbirini
        // yalanlıyordu.
        var effective = WithDefaults(archetype, answers);

        var tables = new List<PlannedTable>();

        if (BaseTables.TryGetValue(archetype, out var baseTables))
            tables.AddRange(baseTables);

        ApplyCoreAnswers(tables, effective);

        if (ConditionalTables.TryGetValue(archetype, out var conditional))
            tables.AddRange(conditional(effective));

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
    /// <summary>
    /// Cevaplanmayan her soruyu varsayılanıyla dolduran bir kopya.
    ///
    /// <b>Yalnızca TABLO kurmak için kullanılıyor, belirsizlik tespiti için
    /// DEĞİL.</b> Belirsizlik (takip sorusu) kullanıcının GERÇEKTEN verdiği
    /// cevaplara bakmalı: ana soruyu atlayan birine onun alt sorusunu sormak,
    /// atlamayı görmezden gelmek olur — ör. "varyant olacak mı"yı hiç
    /// cevaplamayan kullanıcıya "varyantların kendi fiyatı var mı" diye
    /// sormak.
    /// </summary>
    private static Dictionary<string, string> WithDefaults(
        ProjectArchetype archetype, IReadOnlyDictionary<string, string> answers)
    {
        var effective = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var q in ClarifyingQuestions.For(archetype))
        {
            if (!string.IsNullOrWhiteSpace(q.DefaultOption))
                effective[q.Id] = q.DefaultOption;
        }

        // Gerçek cevaplar varsayılanı EZER (takip cevapları da dahil).
        foreach (var (key, value) in answers)
        {
            if (!string.IsNullOrWhiteSpace(value)) effective[key] = value;
        }

        return effective;
    }

    private static List<string> BuildAssumptions(ProjectArchetype archetype, IReadOnlyDictionary<string, string> answers)
    {
        var notes = new List<string>();
        foreach (var q in ClarifyingQuestions.For(archetype))
        {
            if (answers.ContainsKey(q.Id)) continue;
            if (string.IsNullOrWhiteSpace(q.DefaultOption)) continue;
            notes.Add($"{q.Text} — default: {q.DefaultOption}");
        }
        return notes;
    }
}
