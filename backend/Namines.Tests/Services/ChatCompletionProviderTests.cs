using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Namines.Core.Analysis;
using Namines.Infrastructure.AI;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Sağlayıcı seam'i: adres, kimlik, model tablosu ve seçim.
///
/// <b>Neden bu testler var:</b> iki sağlayıcının model tavanları AYNI DEĞİL.
/// Groq'unkini DeepSeek'e uygulamak, bu kod tabanında canlıda 400 aldığımız
/// hatanın (istenen <c>max_tokens</c> modelin sınırının üstünde → istek komple
/// reddediliyor) aynısını üretirdi.
/// </summary>
public class ChatCompletionProviderTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in pairs) dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // --- Katalog ayrımı (Görev 3) ---

    [Fact]
    public void Groq_catalog_still_maps_the_three_tiers_to_their_models()
    {
        var catalog = new GroqModelCatalog();

        Assert.Equal("openai/gpt-oss-20b", catalog.UpstreamModel(NaiModel.Flash));
        Assert.Equal("openai/gpt-oss-20b", catalog.UpstreamModel(NaiModel.Standard));
        Assert.Equal("openai/gpt-oss-120b", catalog.UpstreamModel(NaiModel.Pro));
    }

    [Fact]
    public void Groq_catalog_clamps_a_request_above_the_model_limit()
    {
        var catalog = new GroqModelCatalog();

        Assert.Equal(65_536, catalog.ClampToModelLimit("openai/gpt-oss-20b", 100_000));
        Assert.Equal(8_000, catalog.ClampToModelLimit("openai/gpt-oss-20b", 8_000));
    }

    [Fact]
    public void An_unknown_model_id_passes_through_unclamped()
    {
        // Kimlik yapılandırmadan override edilebiliyor; bilmediğimiz bir modele
        // uydurma bir tavan dayatmak, sessizce çıktıyı kısmak olurdu.
        var catalog = new GroqModelCatalog();

        Assert.Equal(100_000, catalog.ClampToModelLimit("some/unknown-model", 100_000));
    }

    [Fact]
    public void The_product_catalog_no_longer_exposes_provider_model_ids()
    {
        // Ürün kademeleri sağlayıcıdan bağımsız kalmalı: aksi hâlde ikinci
        // sağlayıcı eklendiğinde "hangi model" sorusunun iki doğru cevabı olur.
        Assert.Null(typeof(NaiModelInfo).GetProperty("UpstreamModel"));
        Assert.Null(typeof(NaiModelInfo).GetProperty("MaxCompletionTokens"));
    }

    // --- Groq sağlayıcısı (Görev 4) ---

    [Fact]
    public void Groq_provider_points_at_the_groq_endpoint_and_carries_its_key()
    {
        var provider = new GroqChatCompletionProvider(Config(("Groq:ApiKey", "gsk-test")));

        Assert.Equal("groq", provider.Name);
        Assert.Equal(new Uri("https://api.groq.com/openai/v1/"), provider.BaseAddress);
        Assert.True(provider.IsConfigured);

        using var request = new HttpRequestMessage();
        provider.Authenticate(request);
        Assert.Equal("Bearer gsk-test", request.Headers.Authorization?.ToString());
    }

    [Fact]
    public void Groq_provider_reports_itself_unconfigured_when_the_key_is_missing()
    {
        // Anahtarsız bir sağlayıcı ağa HİÇ çıkmamalı: "MISSING_API_KEY" ile
        // gerçek bir istek yapmak on saniye bekleyip yine hata almak demekti.
        var provider = new GroqChatCompletionProvider(Config());

        Assert.False(provider.IsConfigured);
    }

    // --- DeepSeek ve seçim (Görev 5) ---

    [Fact]
    public void DeepSeek_provider_points_at_its_own_endpoint_and_models()
    {
        var provider = new DeepSeekChatCompletionProvider(Config(("DeepSeek:ApiKey", "sk-test")));

        Assert.Equal("deepseek", provider.Name);
        Assert.Equal(new Uri("https://api.deepseek.com/v1/"), provider.BaseAddress);
        Assert.True(provider.IsConfigured);
        Assert.Equal("deepseek-chat", provider.Models.UpstreamModel(NaiModel.Flash));
        Assert.Equal("deepseek-reasoner", provider.Models.UpstreamModel(NaiModel.Pro));

        using var request = new HttpRequestMessage();
        provider.Authenticate(request);
        Assert.Equal("Bearer sk-test", request.Headers.Authorization?.ToString());
    }

    [Fact]
    public void DeepSeek_model_ids_can_be_overridden_from_configuration()
    {
        // Sağlayıcı bir modeli kaldırdığında yeni sürüm beklemeden geçilebilsin.
        var provider = new DeepSeekChatCompletionProvider(Config(
            ("DeepSeek:ApiKey", "sk-test"),
            ("DeepSeek:Models:Pro", "deepseek-reasoner-v2")));

        Assert.Equal("deepseek-reasoner-v2", provider.Models.UpstreamModel(NaiModel.Pro));
    }

    [Fact]
    public void DeepSeek_catalog_clamps_to_its_own_smaller_ceiling()
    {
        var catalog = new DeepSeekModelCatalog();

        Assert.Equal(8_192, catalog.ClampToModelLimit("deepseek-chat", 65_536));
    }

    [Fact]
    public void The_factory_defaults_to_groq()
    {
        var provider = ChatCompletionProviderFactory.Create(Config(("Groq:ApiKey", "k")));
        Assert.Equal("groq", provider.Name);
    }

    [Fact]
    public void The_factory_honours_an_explicit_provider_choice()
    {
        var provider = ChatCompletionProviderFactory.Create(
            Config(("Ai:Provider", "deepseek"), ("DeepSeek:ApiKey", "k")));

        Assert.Equal("deepseek", provider.Name);
    }

    [Theory]
    [InlineData("DeepSeek")]
    [InlineData("  deepseek  ")]
    public void The_factory_ignores_case_and_surrounding_space(string configured)
    {
        var provider = ChatCompletionProviderFactory.Create(
            Config(("Ai:Provider", configured), ("DeepSeek:ApiKey", "k")));

        Assert.Equal("deepseek", provider.Name);
    }

    [Fact]
    public void An_unrecognised_provider_name_falls_back_to_groq_rather_than_failing_startup()
    {
        // Bir yazım hatası yüzünden uygulamanın hiç açılmaması, yapılandırma
        // hatasının bedelini orantısız kılardı. Düşüş loglanır.
        var provider = ChatCompletionProviderFactory.Create(Config(("Ai:Provider", "openai")));
        Assert.Equal("groq", provider.Name);
    }

    [Fact]
    public void Providers_expose_a_prose_name_separate_from_the_config_token()
    {
        // Yapılandırma jetonu düzyazıda kullanılınca hata mesajı "groq is not
        // configured on this server" oluyordu — yanındaki Gemini dalı düzgün
        // yazarken. Ayrı alan, jetonun cümleye sızmasını engelliyor.
        Assert.Equal("Groq", new GroqChatCompletionProvider(Config()).DisplayName);
        Assert.Equal("DeepSeek", new DeepSeekChatCompletionProvider(Config()).DisplayName);
    }
}
