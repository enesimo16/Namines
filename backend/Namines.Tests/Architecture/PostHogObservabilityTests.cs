using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Namines.API.Extensions;
using Namines.Core.Interfaces;
using Namines.Infrastructure.Observability;
using Xunit;

namespace Namines.Tests.Architecture;

/// <summary>
/// FAZ 6: PostHog Ürün Analitiği, Feature Flags & Kota Koruma Zırhı Testleri (21-OBSERVABILITY.md §8).
/// </summary>
public sealed class PostHogObservabilityTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> RecordedRequests { get; } = new();
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":1}")
        };
        public Exception? ExceptionToThrow { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            var body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : string.Empty;
            RecordedRequests.Add((request, body));
            return ResponseToReturn;
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public TestHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Namines.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void IsConfigured_Anahtar_Yoksa_Veya_Devredisi_Ise_False_Doner()
    {
        var factory = new TestHttpClientFactory(new HttpClient());

        // 1. ApiKey null/boş
        var clientNoKey = new PostHogClient(
            factory,
            Options.Create(new PostHogOptions { ApiKey = null }),
            NullLogger<PostHogClient>.Instance);
        Assert.False(clientNoKey.IsConfigured);

        // 2. Enabled false
        var clientDisabled = new PostHogClient(
            factory,
            Options.Create(new PostHogOptions { ApiKey = "phc_valid_key", Enabled = false }),
            NullLogger<PostHogClient>.Instance);
        Assert.False(clientDisabled.IsConfigured);

        // 3. Yapılandırılmış
        var clientValid = new PostHogClient(
            factory,
            Options.Create(new PostHogOptions { ApiKey = "phc_valid_key", Enabled = true }),
            NullLogger<PostHogClient>.Instance);
        Assert.True(clientValid.IsConfigured);
    }

    [Fact]
    public async Task CaptureAsync_Yapilandirilmamisken_Hicbir_Http_Cagrisi_Yapmaz()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://eu.i.posthog.com") };
        var factory = new TestHttpClientFactory(httpClient);

        var client = new PostHogClient(
            factory,
            Options.Create(new PostHogOptions { ApiKey = null }),
            NullLogger<PostHogClient>.Instance);

        await client.CaptureAsync("usr_1", "schema_generated", new Dictionary<string, object> { ["tables"] = 5 });

        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public async Task CaptureAsync_Yapilandirildiginda_Kanonik_Olayi_PostHog_Formatinda_Iletir()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://eu.i.posthog.com") };
        var factory = new TestHttpClientFactory(httpClient);

        var client = new PostHogClient(
            factory,
            Options.Create(new PostHogOptions { ApiKey = "phc_test_123" }),
            NullLogger<PostHogClient>.Instance);

        await client.CaptureAsync("usr_alice", "database_provisioned", new Dictionary<string, object>
        {
            ["provider"] = "docker",
            ["engine"] = "postgresql",
            ["durationMs"] = 350
        });

        Assert.Single(handler.RecordedRequests);
        var (req, body) = handler.RecordedRequests[0];

        Assert.Equal("/capture/", req.RequestUri?.AbsolutePath);
        Assert.Equal(HttpMethod.Post, req.Method);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("phc_test_123", root.GetProperty("api_key").GetString());
        Assert.Equal("usr_alice", root.GetProperty("distinct_id").GetString());
        Assert.Equal("database_provisioned", root.GetProperty("event").GetString());

        var props = root.GetProperty("properties");
        Assert.Equal("docker", props.GetProperty("provider").GetString());
        Assert.Equal("postgresql", props.GetProperty("engine").GetString());
        Assert.Equal(350, props.GetProperty("durationMs").GetInt32());
    }

    [Fact]
    public async Task CaptureAsync_Kota_Korumasi_1500ms_Icindeki_Ayni_Olayi_Filtreler()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://eu.i.posthog.com") };
        var factory = new TestHttpClientFactory(httpClient);

        var client = new PostHogClient(
            factory,
            Options.Create(new PostHogOptions { ApiKey = "phc_test_123" }),
            NullLogger<PostHogClient>.Instance);

        // 1. çağrı
        await client.CaptureAsync("usr_bob", "quota_exceeded", new Dictionary<string, object> { ["resource"] = "ai_tokens" });
        Assert.Single(handler.RecordedRequests);

        // 2. hemen ardışık çağrı (aynı distinctId + eventName) -> kotayı korumak için elenmeli!
        await client.CaptureAsync("usr_bob", "quota_exceeded", new Dictionary<string, object> { ["resource"] = "ai_tokens" });
        Assert.Single(handler.RecordedRequests);

        // 3. Farklı bir olay ise filtrelenmemeli
        await client.CaptureAsync("usr_bob", "migration_applied");
        Assert.Equal(2, handler.RecordedRequests.Count);
    }

    [Fact]
    public async Task IdentifyAsync_PostHog_Identify_Olayini_Set_Alanlariyla_Gonderir()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://eu.i.posthog.com") };
        var factory = new TestHttpClientFactory(httpClient);

        var client = new PostHogClient(
            factory,
            Options.Create(new PostHogOptions { ApiKey = "phc_test_123" }),
            NullLogger<PostHogClient>.Instance);

        await client.IdentifyAsync("usr_charlie", new Dictionary<string, object>
        {
            ["email"] = "charlie@namines.com",
            ["plan"] = "team"
        });

        Assert.Single(handler.RecordedRequests);
        var (_, body) = handler.RecordedRequests[0];

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("$identify", root.GetProperty("event").GetString());
        Assert.Equal("usr_charlie", root.GetProperty("distinct_id").GetString());

        var props = root.GetProperty("properties");
        Assert.True(props.TryGetProperty("$set", out var setObj));
        Assert.Equal("charlie@namines.com", setObj.GetProperty("email").GetString());
        Assert.Equal("team", setObj.GetProperty("plan").GetString());
    }

    [Fact]
    public async Task IsFeatureFlagEnabledAsync_PostHog_Decide_Yanitini_Dogru_Ayristirir()
    {
        var decideJson = JsonSerializer.Serialize(new
        {
            featureFlags = new Dictionary<string, object>
            {
                ["dark_mode"] = true,
                ["legacy_mode"] = false,
                ["ab_variant"] = "variant_b"
            }
        });

        var handler = new MockHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(decideJson)
            }
        };

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://eu.i.posthog.com") };
        var factory = new TestHttpClientFactory(httpClient);

        var client = new PostHogClient(
            factory,
            Options.Create(new PostHogOptions { ApiKey = "phc_test_123" }),
            NullLogger<PostHogClient>.Instance);

        var isDark = await client.IsFeatureFlagEnabledAsync("usr_test", "dark_mode");
        Assert.True(isDark);

        var isLegacy = await client.IsFeatureFlagEnabledAsync("usr_test", "legacy_mode", defaultValue: true);
        Assert.False(isLegacy);

        var isAb = await client.IsFeatureFlagEnabledAsync("usr_test", "ab_variant");
        Assert.True(isAb); // string variant "variant_b" non-empty is true

        var notFound = await client.IsFeatureFlagEnabledAsync("usr_test", "non_existent_flag", defaultValue: false);
        Assert.False(notFound);
    }

    [Fact]
    public async Task PostHog_Ag_Hatasi_Durumunda_Asla_Istisna_Firlatmaz_Fail_Open_Calisir()
    {
        var handler = new MockHttpMessageHandler
        {
            ExceptionToThrow = new HttpRequestException("DNS resolution failed or connection timeout")
        };

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://eu.i.posthog.com") };
        var factory = new TestHttpClientFactory(httpClient);

        var client = new PostHogClient(
            factory,
            Options.Create(new PostHogOptions { ApiKey = "phc_test_123" }),
            NullLogger<PostHogClient>.Instance);

        // Capture exception fırlatmamalı
        var captureException = await Record.ExceptionAsync(() => client.CaptureAsync("usr_fail", "test_event"));
        Assert.Null(captureException);

        // Feature flag exception fırlatmamalı, varsayılan değere dönmeli
        var flagResult = await client.IsFeatureFlagEnabledAsync("usr_fail", "some_flag", defaultValue: true);
        Assert.True(flagResult);
    }

    [Fact]
    public void AddNaminesObservability_IPostHogService_Kaydini_Eksiksiz_Yapar()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PostHog:ApiKey"] = "phc_di_test_key",
                ["PostHog:Host"] = "https://eu.i.posthog.com",
                ["PostHog:Enabled"] = "true"
            })
            .Build();

        var env = new TestHostEnvironment();

        services.AddNaminesObservability(config, env);
        var sp = services.BuildServiceProvider();

        var postHogService = sp.GetService<IPostHogService>();
        Assert.NotNull(postHogService);
        Assert.True(postHogService.IsConfigured);
    }
}
