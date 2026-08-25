using Namines.Core.Analysis;
using Namines.Core.Models.Auth;

namespace Namines.Tests.Analysis;

/// <summary>
/// Team koltukları, NAI v1 adlandırması, Flash varsayılanları ve gelişmiş
/// AI tercihlerinin gerçekten okunması.
///
/// <b>Ortak tema:</b> bu testlerin hepsi "gösterilen şey ile yapılan şey aynı mı"
/// sorusunu kilitliyor. Bu kod tabanında ayarların süs olduğu (yalnızca
/// localStorage'a yazılıp hiç okunmadığı) bir kez gerçekten yaşandı.
/// </summary>
public class TeamAndAdvancedSettingsTests
{
    // ── Koltuklar ────────────────────────────────────────────────────────────

    [Fact]
    public void Only_the_team_plan_can_hold_more_than_one_person()
    {
        // Free/Pro tek kişilik: oralarda ekip arayüzü açmak, satın alınmamış bir
        // özelliği varmış gibi sunmak olurdu.
        Assert.Equal(1, PlanQuotas.For(PlanTier.Free).TeamSeats);
        Assert.Equal(1, PlanQuotas.For(PlanTier.Pro).TeamSeats);
        Assert.Equal(3, PlanQuotas.For(PlanTier.Team).TeamSeats);
    }

    [Fact]
    public void The_team_seat_count_includes_the_buyer()
    {
        // Üç koltuk = satın alan + davet edilebilen 2 kişi. "Davet hakkı" olarak
        // tutulsaydı, sahip ekipten çıkıp yerine başkasını alarak sınırı sessizce
        // aşabilirdi.
        var seats = PlanQuotas.For(PlanTier.Team).TeamSeats;

        Assert.Equal(2, seats - 1);
    }

    [Fact]
    public void The_dev_account_has_no_seat_limit()
    {
        Assert.Equal(-1, PlanQuotas.For(PlanTier.Dev).TeamSeats);
    }

    [Fact]
    public void A_team_seat_is_worth_exactly_one_pro_account()
    {
        // DailyAiTokens Team'de KOLTUK BAŞINA pay. Bir koltuğun bir Pro hesabıyla
        // aynı hakkı taşıması bilinçli: ekip olmak kimseyi kısıtlamamalı.
        Assert.Equal(
            PlanQuotas.For(PlanTier.Pro).DailyAiTokens,
            PlanQuotas.For(PlanTier.Team).DailyAiTokens);
    }

    [Fact]
    public void The_team_pool_is_the_seat_share_times_the_seats()
    {
        // 3 koltuk × 200.000 = 600.000 günlük ekip havuzu.
        var team = PlanQuotas.For(PlanTier.Team);

        Assert.Equal(600_000L, (long)team.DailyAiTokens * team.TeamSeats);
    }

    [Fact]
    public void A_team_is_never_worse_off_than_the_same_people_on_pro()
    {
        // Bu testin varlık sebebi: fiyatlandırma incelemesinde Team'in küçük
        // ekipler için MATEMATİKSEL OLARAK kötü bir anlaşma olduğu çıktı —
        // 3 koltuk 200K paylaşıyordu, yani 3 Pro'nun (600K) üçte biri.
        // Ekip kurmanın cezası olamaz.
        var pro = PlanQuotas.For(PlanTier.Pro);
        var team = PlanQuotas.For(PlanTier.Team);

        var teamTotal = (long)team.DailyAiTokens * team.TeamSeats;
        var samePeopleOnPro = (long)pro.DailyAiTokens * team.TeamSeats;

        Assert.True(teamTotal >= samePeopleOnPro,
            $"Team {team.TeamSeats} koltukta {teamTotal} token veriyor; " +
            $"aynı kişiler Pro'da {samePeopleOnPro} alırdı. Ekip olmak kayıp olamaz.");
    }

    [Fact]
    public void The_paid_plans_are_not_unlimited()
    {
        // "Sınırsız" demek, tek bir kullanıcının aylık ücretinin kat kat üstünde
        // fatura üretebilmesi demek. Yalnızca Dev sınırsız.
        Assert.True(PlanQuotas.For(PlanTier.Pro).DailyAiTokens < int.MaxValue);
        Assert.True(PlanQuotas.For(PlanTier.Team).DailyAiTokens < int.MaxValue);
        Assert.True(PlanQuotas.For(PlanTier.Pro).DailyAiTokens >
                    PlanQuotas.For(PlanTier.Free).DailyAiTokens);
    }

    // ── NAI v1 adlandırması ──────────────────────────────────────────────────

    [Fact]
    public void Every_model_carries_its_version_in_the_id()
    {
        // v2 geldiğinde iki kuşak yan yana yaşayacak; sürümsüz bir kimlik o gün
        // hangi modelin kastedildiğini belirsiz bırakırdı.
        foreach (var model in NaiCatalog.All)
            Assert.StartsWith("nai-v1", model.Id);
    }

    [Theory]
    [InlineData("nai-flash", NaiModel.Flash)]
    [InlineData("nai", NaiModel.Standard)]
    [InlineData("nai-pro", NaiModel.Pro)]
    public void Version_less_ids_saved_before_the_rename_still_resolve(string oldId, NaiModel expected)
    {
        // Kullanıcıların kayıtlı tercihi tarayıcıda duruyor. Eski kimlikleri
        // atmak, Pro seçmiş bir kullanıcıyı sessizce Standard'a düşürürdü ve
        // bunu ancak sonuç kalitesi düşünce fark ederdi.
        Assert.Equal(expected, NaiCatalog.Resolve(oldId));
    }

    [Theory]
    [InlineData("nai-v1-flash", NaiModel.Flash)]
    [InlineData("nai-v1", NaiModel.Standard)]
    [InlineData("nai-v1-pro", NaiModel.Pro)]
    public void The_new_versioned_ids_resolve(string id, NaiModel expected)
    {
        Assert.Equal(expected, NaiCatalog.Resolve(id));
    }

    // ── Varsayılan model = Flash ─────────────────────────────────────────────

    [Fact]
    public void A_brand_new_policy_defaults_to_the_cheap_model()
    {
        // Önceden hepsi HighMixtral (en pahalı karşılık) idi: hiçbir ayara
        // dokunmamış bir kullanıcı en ucuz işi bile en pahalı modelde çalıştırıp
        // günlük bütçesini gereksiz yere iki kat hızlı tüketiyordu.
        var policy = new UserAIPolicy();

        Assert.Equal(AIMode.Low, policy.SmartSeed);
        Assert.Equal(AIMode.Low, policy.Documentation);
        Assert.Equal(AIMode.Low, policy.Scaffolding);
        Assert.Equal(AIMode.Low, policy.SchemaRevision);
        Assert.Equal(AIMode.Low, policy.DbaAnalysis);
        Assert.Equal(AIMode.Low, policy.Migration);
        Assert.Equal(AIMode.Low, policy.Voice);
    }

    [Fact]
    public void Schema_generation_is_the_one_feature_that_defaults_higher()
    {
        // Kullanıcının ürünle ilk teması ve en kritik çıktı burası; Flash'a
        // düşürmek ilk izlenimi bozardı.
        Assert.Equal(AIMode.Medium, new UserAIPolicy().SchemaGeneration);
    }

    [Fact]
    public void No_default_points_at_a_mode_the_user_can_no_longer_choose()
    {
        // Arayüzde yalnızca üç seçenek var (1/2/4). Varsayılan bunların dışında
        // bir değer olsaydı, kullanıcı ayarı hiç açmadığında listede seçili
        // görünen hiçbir şey olmazdı.
        var selectable = new[] { AIMode.Low, AIMode.Medium, AIMode.Ultra };
        var policy = new UserAIPolicy();

        foreach (var mode in new[]
                 {
                     policy.SmartSeed, policy.Documentation, policy.Scaffolding,
                     policy.SchemaGeneration, policy.SchemaRevision, policy.DbaAnalysis,
                     policy.Migration, policy.Voice,
                 })
            Assert.Contains(mode, selectable);
    }

    // ── Gelişmiş ayarlar ─────────────────────────────────────────────────────

    [Fact]
    public void The_default_delete_action_never_loses_data()
    {
        // Bu kod tabanının kuralı: varsayılan asla veri kaybına doğru düşmemeli
        // (bkz. ReferentialActionSql). CASCADE varsayılan olsaydı, ayara hiç
        // dokunmamış bir kullanıcı bir satır silerken ilişkili tüm kayıtları da
        // sessizce silerdi.
        Assert.Equal("restrict", AiAdvancedSettings.Default.FkAction);
    }

    [Fact]
    public void Broken_stored_preferences_fall_back_instead_of_failing()
    {
        // Bunlar tercih, zorunluluk değil: kullanıcının şema üretme isteğinin,
        // kayıtlı bir tercih satırı bozuk diye tamamen düşmesi orantısız olurdu.
        Assert.Equal(AiAdvancedSettings.Default, AiAdvancedSettings.Parse("{ not json"));
        Assert.Equal(AiAdvancedSettings.Default, AiAdvancedSettings.Parse(null));
        Assert.Equal(AiAdvancedSettings.Default, AiAdvancedSettings.Parse("   "));
    }

    [Fact]
    public void Saved_preferences_survive_a_round_trip()
    {
        var settings = AiAdvancedSettings.Default with
        {
            NamingConvention = "camelCase",
            FkAction = "set_null",
            Temperature = "0.7",
            MaxTokens = "8192",
        };

        var parsed = AiAdvancedSettings.Parse(settings.ToJson());

        Assert.Equal(settings, parsed);
    }

    [Fact]
    public void Preferences_actually_reach_the_model()
    {
        // Bu testin varlık sebebi: bu ayarlar arayüzde vardı ama YALNIZCA
        // localStorage'a yazılıyordu. Kullanıcı "snake_case" seçip kaydediyor,
        // sonuç hiç değişmiyordu. Ayar göstermek onu uygulamak demektir.
        var settings = AiAdvancedSettings.Default with
        {
            NamingConvention = "camelCase",
            FkAction = "set_null",
        };

        var context = settings.ToSchemaPromptContext();

        Assert.Contains("camelCase", context);
        Assert.Contains("SET NULL", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_broken_temperature_does_not_produce_a_nonsense_value()
    {
        // Sağlayıcı geçersiz bir sıcaklıkta isteği tamamen reddediyor; kayıtlı
        // bozuk bir değer, kullanıcının her şema üretimini kırardı.
        var settings = AiAdvancedSettings.Default with { Temperature = "not-a-number" };

        Assert.InRange(settings.TemperatureValue, 0.0, 2.0);
    }

    [Fact]
    public void A_broken_token_limit_does_not_produce_a_nonsense_value()
    {
        var settings = AiAdvancedSettings.Default with { MaxTokens = "" };

        Assert.True(settings.MaxTokensValue > 0);
    }
}
