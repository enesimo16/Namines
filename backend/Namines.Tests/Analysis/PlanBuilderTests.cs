using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// Plan modu (second-phase/05-PLAN-MODU.md) — cevaplardan deterministik
/// tablo listesi.
///
/// <b>Ortak tema:</b> tablo listesi AI'ya yazdırılmıyor; burada test edilen
/// her davranış "aynı cevaplar her zaman aynı planı üretir mi" sorusunu
/// kilitliyor. AI'ya yazdırılsaydı bu testlerin hiçbiri anlamlı olmazdı.
/// </summary>
public class PlanBuilderTests
{
    private static readonly Dictionary<string, string> NoAnswers = new();

    [Fact]
    public void The_same_answers_always_produce_the_same_plan()
    {
        var answers = new Dictionary<string, string> { ["auth"] = "Evet, basit (e-posta + şifre)" };

        var first = PlanBuilder.Build(ProjectArchetype.Ecommerce, answers, round: 1);
        var second = PlanBuilder.Build(ProjectArchetype.Ecommerce, answers, round: 1);

        Assert.Equal(first.Tables.Select(t => t.Name), second.Tables.Select(t => t.Name));
    }

    [Fact]
    public void Every_recognised_archetype_has_at_least_one_base_table()
    {
        foreach (ProjectArchetype archetype in Enum.GetValues<ProjectArchetype>())
        {
            if (archetype == ProjectArchetype.Generic) continue;
            var plan = PlanBuilder.Build(archetype, NoAnswers, round: 1);
            Assert.True(plan.Tables.Count > 0, $"{archetype} has no base tables.");
        }
    }

    [Fact]
    public void An_unrecognised_project_plans_only_what_the_core_defaults_imply()
    {
        // Generic'in BaseTables'ta karşılığı yok — bu bir hata değil, "hiçbir
        // tür tanınmadı" durumunun doğal sonucu. İstisna fırlatmamalı.
        //
        // Ama tablo listesi BOŞ da olmamalı: çekirdek sorular (auth, environment)
        // Generic'te de soruluyor ve varsayılanları var. Plan ekranı
        // "varsayılan: Evet, basit (e-posta + şifre)" derken users tablosunu
        // planlamamak, planın kendi varsayımıyla çelişmesi olurdu — testin ilk
        // hâli tam olarak o tutarsızlığı kilitliyordu.
        var plan = PlanBuilder.Build(ProjectArchetype.Generic, NoAnswers, round: 1);

        Assert.Contains(plan.Tables, t => t.Name == "users");        // auth varsayılanı
        Assert.Contains(plan.Tables, t => t.Name == "audit_logs");   // environment varsayılanı
        // Türe özel hiçbir tablo yok — tanınmayan tür için uydurulmuyor.
        Assert.DoesNotContain(plan.Tables, t => t.Name is "products" or "orders");
    }

    [Fact]
    public void No_answers_still_produces_a_base_plan()
    {
        // Kullanıcı hiçbir soruyu cevaplamadan planı isteyebilmeli — plan
        // modu bir form değil, atlanabilir bir yardımcı.
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce, NoAnswers, round: 1);

        Assert.Contains(plan.Tables, t => t.Name == "products");
        Assert.Contains(plan.Tables, t => t.Name == "orders");
    }

    [Fact]
    public void Every_table_has_a_reason()
    {
        // Gerekçesiz bir tablo, kullanıcının "bu neden var" sorusuna cevap
        // vermez — soru bankasındaki aynı kural burada da geçerli.
        foreach (ProjectArchetype archetype in Enum.GetValues<ProjectArchetype>())
        {
            if (archetype == ProjectArchetype.Generic) continue;
            var plan = PlanBuilder.Build(archetype, NoAnswers, round: 1);
            Assert.All(plan.Tables, t => Assert.False(string.IsNullOrWhiteSpace(t.Reason)));
        }
    }

    [Fact]
    public void Unanswered_questions_become_visible_assumptions()
    {
        // Sessiz varsayım, "planı onayladım ama şema beklediğim gibi değildi"
        // hissi yaratır. Varsayım GÖRÜNÜR olmalı.
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce, NoAnswers, round: 1);

        Assert.NotEmpty(plan.Assumptions);
        Assert.Contains(plan.Assumptions, a => a.Contains("varsayılan"));
    }

    [Fact]
    public void An_answered_question_does_not_appear_as_an_assumption()
    {
        var answers = new Dictionary<string, string> { ["scale"] = "Büyük (milyonlarca)" };
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce, answers, round: 1);

        Assert.DoesNotContain(plan.Assumptions, a => a.StartsWith("Bu proje ne büyüklükte"));
    }

    // ── Cevapların tabloya somut etkisi ─────────────────────────────────────

    [Fact]
    public void Variants_answer_adds_the_variants_table()
    {
        var withVariants = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string> { ["variants"] = "Evet, varyantlı" }, round: 1);
        var withoutVariants = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string> { ["variants"] = "Hayır, tek ürün tek kayıt" }, round: 1);

        Assert.Contains(withVariants.Tables, t => t.Name == "product_variants");
        Assert.DoesNotContain(withoutVariants.Tables, t => t.Name == "product_variants");
    }

    [Fact]
    public void Role_based_auth_adds_more_tables_than_simple_auth()
    {
        var simple = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string> { ["auth"] = "Evet, basit (e-posta + şifre)" }, round: 1);
        var withRoles = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string> { ["auth"] = "Evet, roller ve izinlerle" }, round: 1);

        Assert.Contains(simple.Tables, t => t.Name == "users");
        Assert.DoesNotContain(simple.Tables, t => t.Name == "roles");

        Assert.Contains(withRoles.Tables, t => t.Name == "roles");
        Assert.Contains(withRoles.Tables, t => t.Name == "permissions");
    }

    [Fact]
    public void No_auth_does_not_add_a_users_table()
    {
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce, new Dictionary<string, string> { ["auth"] = "Hayır" }, round: 1);
        Assert.DoesNotContain(plan.Tables, t => t.Name == "users");
    }

    // ── Belirsizlik / takip sorusu ───────────────────────────────────────────

    [Fact]
    public void An_ambiguous_answer_produces_exactly_one_follow_up()
    {
        // "Çok oyunculu (lonca/takım)" tek başına loncalı mı takımlı mı belli
        // etmiyor — second-phase/05'in kendi örneği bu.
        var plan = PlanBuilder.Build(ProjectArchetype.Game,
            new Dictionary<string, string> { ["multiplayer"] = "Çok oyunculu (lonca/takım)" }, round: 1);

        Assert.NotNull(plan.FollowUp);
        Assert.Equal("multiplayer.followup", plan.FollowUp!.Id);
    }

    [Fact]
    public void An_unambiguous_answer_produces_no_follow_up()
    {
        var plan = PlanBuilder.Build(ProjectArchetype.Game,
            new Dictionary<string, string> { ["multiplayer"] = "Tek oyunculu" }, round: 1);

        Assert.Null(plan.FollowUp);
    }

    [Fact]
    public void Answering_the_follow_up_resolves_it_and_shapes_the_plan()
    {
        var answers = new Dictionary<string, string>
        {
            ["multiplayer"] = "Çok oyunculu (lonca/takım)",
            ["multiplayer.followup"] = "Lonca (kalıcı, büyük)",
        };

        var plan = PlanBuilder.Build(ProjectArchetype.Game, answers, round: 2);

        // Cevaplanmış bir takip sorusu bir daha SORULMAZ.
        Assert.Null(plan.FollowUp);
        Assert.Contains(plan.Tables, t => t.Name == "guilds");
        Assert.DoesNotContain(plan.Tables, t => t.Name == "teams");
    }

    [Fact]
    public void No_follow_up_is_ever_asked_past_the_round_limit()
    {
        // Sonsuz soru-cevap kullanıcıyı yorup terk ettirir — üç turdan sonra
        // belirsizlik olsa bile susulur, elde olanla devam edilir.
        var plan = PlanBuilder.Build(ProjectArchetype.Game,
            new Dictionary<string, string> { ["multiplayer"] = "Çok oyunculu (lonca/takım)" }, round: 3);

        Assert.Null(plan.FollowUp);
    }

    [Fact]
    public void At_most_one_follow_up_per_round()
    {
        // Art arda birden çok soru sormak diyaloğu forma çevirir.
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string> { ["variants"] = "Evet, varyantlı",
                ["payment"] = "Ödeme + kargo takibi",
            }, round: 1);

        // payment.kargo belirsizliği tetiklenir; ikinci bir belirsizlik olsa
        // bile aynı turda sorulmaz.
        Assert.NotNull(plan.FollowUp);
    }

    [Fact]
    public void Production_environment_adds_an_audit_table()
    {
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string> { ["environment"] = "Üretim (gerçek müşteri)" }, round: 1);

        Assert.Contains(plan.Tables, t => t.Name == "audit_logs");
    }

    [Fact]
    public void Duplicate_table_names_collapse_to_one_entry()
    {
        // Base ve koşullu kurallar aynı ada düşerse (ör. Saas'ta invoices hem
        // base'te yok ama koşulda var), sonuç listede o ad TEK kez geçmeli.
        var plan = PlanBuilder.Build(ProjectArchetype.Saas,
            new Dictionary<string, string> { ["tenancy"] = "Her tabloda tenant kolonu" }, round: 1);

        var names = plan.Tables.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ── Plan, kendi yazdığı varsayımlarla tutarlı olmalı ─────────────────────

    [Fact]
    public void Unanswered_questions_apply_their_defaults_to_the_table_list()
    {
        // Ekranda "varsayılan: Ödeme kayıtları da olsun" yazarken tablo
        // listesinde payments'ın OLMAMASI, planın kendi kendisiyle çelişmesiydi.
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string>(), round: 1);

        Assert.Contains(plan.Tables, t => t.Name == "payments");     // payment varsayılanı
        Assert.Contains(plan.Tables, t => t.Name == "audit_logs");   // environment = Üretim
        Assert.Contains(plan.Tables, t => t.Name == "users");        // auth = Evet, basit
    }

    [Fact]
    public void Every_stated_assumption_is_actually_reflected_in_the_plan()
    {
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string>(), round: 1);

        // "Kullanıcı girişi olacak mı — varsayılan: Evet, basit" deniyorsa
        // users tablosu planda OLMALI; aksi hâlde kullanıcı onayladığı şeyden
        // farklı bir şey alır.
        Assert.Contains(plan.Assumptions, a => a.Contains("Kullanıcı girişi"));
        Assert.Contains(plan.Tables, t => t.Name == "users");
    }

    [Fact]
    public void A_real_answer_still_overrides_the_default()
    {
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string> { ["auth"] = "Hayır" }, round: 1);

        Assert.DoesNotContain(plan.Tables, t => t.Name == "users");
        // Cevaplandığı için varsayım listesinde de yer almamalı.
        Assert.DoesNotContain(plan.Assumptions, a => a.Contains("Kullanıcı girişi"));
    }

    [Fact]
    public void A_default_never_triggers_a_follow_up_for_a_question_the_user_skipped()
    {
        // "variants" varsayılanı "Evet, varyantlı" ve bu bir belirsizlik
        // kuralını tetikliyor. Ama kullanıcı ana soruyu ATLADI — alt sorusunu
        // sormak, atlamayı görmezden gelmek olur.
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string>(), round: 1);

        Assert.Null(plan.FollowUp);
    }

    // ── second-phase/08-PROMPT-DENEYIMI.md §8.1: ikinci seviye sorular ────────

    [Fact]
    public void Variant_pricing_ambiguity_is_asked_when_variants_are_chosen_and_shapes_the_reason()
    {
        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce,
            new Dictionary<string, string> { ["variants"] = "Evet, varyantlı" }, round: 1);

        Assert.NotNull(plan.FollowUp);
        Assert.Equal("variants.followup", plan.FollowUp!.Id);
    }

    [Fact]
    public void Shared_variant_pricing_answer_keeps_price_off_the_variants_table()
    {
        var answers = new Dictionary<string, string>
        {
            ["variants"] = "Evet, varyantlı",
            ["variants.followup"] = "Hayır, ürünün fiyatını paylaşırlar",
        };

        var plan = PlanBuilder.Build(ProjectArchetype.Ecommerce, answers, round: 2);

        Assert.Null(plan.FollowUp);
        var variants = plan.Tables.Single(t => t.Name == "product_variants");
        Assert.Contains("paylaşılır", variants.Reason);
    }

    [Fact]
    public void Erp_multi_company_answer_triggers_a_warehouse_scoping_question()
    {
        var plan = PlanBuilder.Build(ProjectArchetype.Erp,
            new Dictionary<string, string> { ["companies"] = "Çoklu şirket" }, round: 1);

        Assert.NotNull(plan.FollowUp);
        Assert.Equal("companies.followup", plan.FollowUp!.Id);
    }

    [Fact]
    public void Separate_warehouse_answer_adds_a_warehouses_table_for_erp()
    {
        var answers = new Dictionary<string, string>
        {
            ["companies"] = "Çoklu şirket",
            ["companies.followup"] = "Şirket başına ayrı stok",
        };

        var plan = PlanBuilder.Build(ProjectArchetype.Erp, answers, round: 2);

        Assert.Null(plan.FollowUp);
        Assert.Contains(plan.Tables, t => t.Name == "warehouses");
    }

    [Fact]
    public void Single_company_erp_never_asks_the_warehouse_question()
    {
        var plan = PlanBuilder.Build(ProjectArchetype.Erp,
            new Dictionary<string, string> { ["companies"] = "Tek şirket" }, round: 1);

        Assert.Null(plan.FollowUp);
        Assert.DoesNotContain(plan.Tables, t => t.Name == "warehouses");
    }
}
