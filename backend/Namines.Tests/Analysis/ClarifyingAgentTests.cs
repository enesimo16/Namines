using Namines.Core.Analysis;

namespace Namines.Tests.Analysis;

/// <summary>
/// Netleştirme ajanı ve NAI model kataloğu (36 §2, §3).
///
/// <b>Bu iki parçanın ortak amacı AI tüketimini azaltmak.</b> İş türü tespiti ve
/// sorular tamamen deterministik — kullanıcı ilk soruyu görene kadar tek bir
/// token harcanmıyor. Model kataloğu ise seçimi bizim elimizde tutuyor ki bütçe
/// tahmin edilebilir kalsın.
/// </summary>
public class ClarifyingAgentTests
{
    // ── İş türü tespiti ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Bir e-ticaret sitesi için şema", ProjectArchetype.Ecommerce)]
    [InlineData("online mağaza, sepet ve sipariş", ProjectArchetype.Ecommerce)]
    [InlineData("SaaS uygulaması, abonelik ve tenant", ProjectArchetype.Saas)]
    [InlineData("ERP: stok, fatura ve üretim takibi", ProjectArchetype.Erp)]
    [InlineData("oyun için oyuncu envanteri ve skor tablosu", ProjectArchetype.Game)]
    [InlineData("hastane randevu ve hasta kayıt sistemi", ProjectArchetype.Healthcare)]
    [InlineData("okul için öğrenci, ders ve sınav notları", ProjectArchetype.Education)]
    public void The_kind_of_project_is_recognised_without_ai(string prompt, ProjectArchetype expected)
    {
        // Bu soruyu bir dil modeline sormak, kullanıcı daha hiçbir şey görmeden
        // token harcamak olurdu. Anahtar kelime yeterli: kullanıcı zaten o
        // kelimeleri kendisi yazıyor.
        Assert.Equal(expected, ArchetypeDetector.Detect(prompt));
    }

    [Theory]
    [InlineData("E-TICARET SITESI")]
    [InlineData("e-ticaret sitesi")]
    [InlineData("E-Ticaret Sitesi")]
    public void Case_and_turkish_characters_do_not_break_detection(string prompt)
    {
        // "Sipariş" ile "siparis"i ayrı saymak tanımanın yarısını kaybettirir.
        Assert.Equal(ProjectArchetype.Ecommerce, ArchetypeDetector.Detect(prompt));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("bir şey yap")]
    [InlineData("veritabanı lazım")]
    public void An_unrecognised_prompt_falls_back_to_generic(string? prompt)
    {
        // Zorla bir tür seçmek, alakasız sorular sorup kullanıcıyı yanlış yöne
        // itmek olurdu.
        Assert.Equal(ProjectArchetype.Generic, ArchetypeDetector.Detect(prompt));
    }

    [Fact]
    public void A_tie_is_not_guessed()
    {
        // İki tür aynı puanı aldıysa hangisi olduğunu gerçekten bilmiyoruz;
        // tahmin etmek, yanlış soruyu güvenle sormaktan kötüdür.
        var prompt = "oyun ve blog";

        Assert.Equal(ProjectArchetype.Generic, ArchetypeDetector.Detect(prompt));
    }

    // ── Sorular ──────────────────────────────────────────────────────────────

    [Fact]
    public void Every_project_gets_at_most_five_questions()
    {
        // Daha fazlası bir form; kullanıcı yarıda bırakır ve elde hiçbir şey
        // kalmaz.
        foreach (ProjectArchetype archetype in Enum.GetValues<ProjectArchetype>())
            Assert.True(ClarifyingQuestions.For(archetype).Count <= 5,
                $"{archetype} asked more than five questions.");
    }

    [Fact]
    public void The_type_specific_questions_come_first()
    {
        // Kullanıcı ilk gördüğü sorunun kendi işiyle ilgili olduğunu anlarsa
        // formu ciddiye alıyor.
        var questions = ClarifyingQuestions.For(ProjectArchetype.Ecommerce);

        Assert.Equal("variants", questions[0].Id);
    }

    [Fact]
    public void Every_question_explains_why_it_is_asked()
    {
        // Gerekçesiz soru, doldurulacak bir form gibi hissettiriyor.
        foreach (ProjectArchetype archetype in Enum.GetValues<ProjectArchetype>())
            Assert.All(ClarifyingQuestions.For(archetype),
                q => Assert.False(string.IsNullOrWhiteSpace(q.Why), $"{q.Id} has no reason."));
    }

    [Fact]
    public void Every_question_has_a_default_so_it_can_be_skipped()
    {
        // Hızlı bir taslak isteyen kullanıcıyı forma mahkûm etmemek için.
        foreach (ProjectArchetype archetype in Enum.GetValues<ProjectArchetype>())
            Assert.All(ClarifyingQuestions.For(archetype),
                q => Assert.False(string.IsNullOrWhiteSpace(q.DefaultOption), $"{q.Id} has no default."));
    }

    [Fact]
    public void Question_ids_are_unique_within_a_project()
    {
        // Aynı kimlikten iki soru, cevabın hangisine ait olduğunu belirsiz
        // bırakır ve biri sessizce ezilir.
        foreach (ProjectArchetype archetype in Enum.GetValues<ProjectArchetype>())
        {
            var ids = ClarifyingQuestions.For(archetype).Select(q => q.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }

    // ── Cevapların prompt'a dönüşü ───────────────────────────────────────────

    [Fact]
    public void Answers_reach_the_model()
    {
        var questions = ClarifyingQuestions.For(ProjectArchetype.Ecommerce);
        var context = ClarifyingQuestions.ToPromptContext(
            ProjectArchetype.Ecommerce, questions,
            new Dictionary<string, string> { ["variants"] = "Evet, varyantlı" });

        Assert.Contains("Ecommerce", context);
        Assert.Contains("Evet, varyantlı", context);
    }

    [Fact]
    public void An_unanswered_question_is_written_with_its_default_not_dropped()
    {
        // Atlamak, modelin o boşluğu yine kendi doldurması demek — sormanın
        // amacı tam olarak buydu.
        var questions = ClarifyingQuestions.For(ProjectArchetype.Saas);
        var context = ClarifyingQuestions.ToPromptContext(ProjectArchetype.Saas, questions, answers: null);

        Assert.Contains("tenant", context, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(questions.Count + 1, context.Split('\n').Length); // +1: "Project type" satırı
    }

    // ── Alan rolleri ─────────────────────────────────────────────────────────

    [Fact]
    public void Every_recognised_project_gets_a_domain_role()
    {
        // Tür tanındığı hâlde rol yoksa, o kullanıcı "iyi bir şema tasarla"dan
        // fazlasını almıyor demektir — tanımanın hiçbir karşılığı olmaz.
        foreach (ProjectArchetype archetype in Enum.GetValues<ProjectArchetype>())
        {
            if (archetype == ProjectArchetype.Generic) continue;

            Assert.False(string.IsNullOrWhiteSpace(ArchetypeRoles.For(archetype)),
                $"{archetype} has no domain role.");
        }
    }

    [Fact]
    public void An_unrecognised_project_gets_no_role_text_at_all()
    {
        // Genel bir "iyi bir şema tasarla" metni eklemek, taslak prompt'unda
        // zaten yazan şeyi tekrarlamak ve her istekte boşuna token harcamak olurdu.
        Assert.Equal(string.Empty, ArchetypeRoles.For(ProjectArchetype.Generic));
    }

    [Fact]
    public void The_finance_role_forbids_floating_point_money()
    {
        // Bu tam olarak modelin sorulmadıkça yaptığı hata: parayı float'ta
        // tutmak, kuruşların sessizce kaybolması demek.
        var role = ArchetypeRoles.For(ProjectArchetype.Fintech);

        Assert.Contains("NEVER a floating point", role);
    }

    [Fact]
    public void Roles_are_concrete_enough_to_change_the_schema()
    {
        // "Dikkatli ol" gibi bir metin hiçbir şeyi değiştirmez; rolün somut bir
        // tablo/kolon kararına dönüşmesi gerekiyor. Kısa metin bunu yapamıyor.
        foreach (ProjectArchetype archetype in Enum.GetValues<ProjectArchetype>())
        {
            if (archetype == ProjectArchetype.Generic) continue;

            Assert.True(ArchetypeRoles.For(archetype).Length > 120,
                $"{archetype} role is too vague to change anything.");
        }
    }

    // ── NAI model kataloğu ───────────────────────────────────────────────────

    [Fact]
    public void Provider_model_names_are_never_part_of_the_public_id()
    {
        // Kullanıcının "llama" ya da "gpt" görmesi gerekmiyor; kimliği göstermek,
        // sağlayıcı o modeli kaldırdığında ürünün bozulmuş görünmesi demekti.
        foreach (var model in NaiCatalog.All)
        {
            Assert.StartsWith("nai", model.Id);
            Assert.DoesNotContain("llama", model.Id);
            Assert.DoesNotContain("gpt", model.Id);
            Assert.DoesNotContain("qwen", model.Id);
        }
    }

    [Fact]
    public void There_are_at_most_three_choices()
    {
        // Sekiz seçenek kullanıcıyı karar veremez hâle getiriyordu.
        Assert.True(NaiCatalog.All.Count <= 3);
    }

    [Theory]
    [InlineData("nai-pro", NaiModel.Pro)]
    [InlineData("nai-flash", NaiModel.Flash)]
    [InlineData("nai", NaiModel.Standard)]
    [InlineData("NAI-PRO", NaiModel.Pro)]
    public void A_known_name_resolves(string id, NaiModel expected)
    {
        Assert.Equal(expected, NaiCatalog.Resolve(id));
    }

    [Theory]
    [InlineData("mixtral-8x7b-32768")]
    [InlineData("gpt-4o")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_name_falls_back_instead_of_failing(string? id)
    {
        // Eski bir istemcinin ölü bir model adı göndermesi, isteği tamamen
        // reddettirmemeli — kullanıcı açısından bu, ürünün çalışmayı bırakması.
        Assert.Equal(NaiModel.Standard, NaiCatalog.Resolve(id));
    }

    [Fact]
    public void The_free_plan_cannot_reach_the_most_expensive_model()
    {
        // En pahalı modeli ücretsiz vermek, paylaşılan havuzu birkaç kullanıcının
        // tüketmesi demek — o noktada ödeme yapan da hizmet alamaz.
        Assert.Equal(NaiModel.Standard, NaiCatalog.ClampToPlan(NaiModel.Pro, PlanTier.Free));
        Assert.Equal(NaiModel.Pro, NaiCatalog.ClampToPlan(NaiModel.Pro, PlanTier.Pro));
    }

    [Fact]
    public void Clamping_downgrades_instead_of_refusing()
    {
        // Kullanıcı bir şema üretmek istiyor; model seçimi onun asıl derdi değil.
        // "Pro'ya geçmelisin" diye hata vermek, işi bitirmesini engellemek olurdu.
        Assert.Equal(NaiModel.Flash, NaiCatalog.ClampToPlan(NaiModel.Flash, PlanTier.Free));
    }

    [Fact]
    public void A_bigger_model_costs_more_budget()
    {
        // Hepsini aynı saymak, kullanıcının her işi Pro'da yapmasını teşvik eder
        // ve bütçe bir günde biter.
        Assert.True(NaiCatalog.CostOf(NaiModel.Pro, 1000) > NaiCatalog.CostOf(NaiModel.Standard, 1000));
        Assert.True(NaiCatalog.CostOf(NaiModel.Standard, 1000) > NaiCatalog.CostOf(NaiModel.Flash, 1000));
    }
}
