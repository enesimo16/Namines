# Hız Sınırı Dayanıklılığı ve Sağlayıcı Soyutlaması — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 429'da otomatik bekleyip yeniden deneyerek büyük şema üretimini mevcut
hesapta çalışır hâle getirmek, ve sohbet sağlayıcısını yapılandırmayla
değiştirilebilir kılıp DeepSeek'i ikinci sağlayıcı olarak eklemek.

**Architecture:** Yeniden deneme `GroqAIService.PostAsync` içinde — sınıftaki
16 çağrı noktasının tamamının geçtiği tek yer. Sağlayıcı soyutlaması ise
tüketicileri değil, `GroqAIService`'in ALTINI değiştiriyor: temel adres, kimlik
doğrulama, model kimlikleri, model sınırları ve 429 okuması
`IChatCompletionProvider`'a taşınıyor. Böylece `GroqAIService`'i somut tip
olarak alan altı tüketici hiç değişmeden ikinci sağlayıcıyı da kullanabiliyor.

**Tech Stack:** .NET 8, `HttpClient` / `IHttpClientFactory`, `System.Text.Json`,
xUnit.

**Spec:** `docs/superpowers/specs/2026-09-17-provider-abstraction-design.md`

## Global Constraints

- **Groq davranışı birebir korunmalı.** Mevcut testlerin hiçbiri
  DEĞİŞTİRİLMEDEN geçmeli. Bir testi düzeltmek zorunda kalıyorsan davranışı
  değiştirmişsindir — dur ve sebebini yaz.
- **Gemini dalına dokunulmayacak.** `PostAsync` içindeki
  `modelInPayload.StartsWith("gemini-")` dalı görüntü (vision) için ayrı bir
  yan yol; sağlayıcı soyutlamasının parçası DEĞİL ve olduğu gibi kalacak.
- **Yeniden deneme yalnızca hız sınırı için.** Ölçüt `ThrowForFailure` ile
  BİREBİR aynı olmalı: HTTP 429 **veya** gövdede `rate_limit_exceeded`. İki yer
  farklı düşünürse, sınıflandırılan ama yeniden denenmeyen (ya da tersi) bir
  boşluk doğar.
- **Sağlayıcı model kimlikleri yalnızca katalogda.** Kod tabanının başka hiçbir
  yerinde model adı yazılı olmayacak (mevcut `NaiCatalog` kuralı).
- **Testte gerçek ağ yok.** Her test sahte bir `HttpMessageHandler` kullanacak.
- **Gecikmeler testte gerçek zaman harcamayacak.** Uyku bir temsilciye
  (delegate) verilecek ve testte kaydedilip anında dönecek.
- Commit mesajlarına Claude/Anthropic atfı EKLENMEYECEK.

---

## Dosya yapısı

**Yeni:**
- `Namines.Core/Interfaces/AiRetryPolicy.cs` — saf bekleme bütçesi mantığı
- `Namines.Core/Interfaces/IChatCompletionProvider.cs` — sağlayıcı seam'i
- `Namines.Core/Analysis/IModelCatalog.cs` — sağlayıcıya özgü model tablosu
- `Namines.Infrastructure/AI/GroqChatCompletionProvider.cs`
- `Namines.Infrastructure/AI/GroqModelCatalog.cs`
- `Namines.Infrastructure/AI/DeepSeekChatCompletionProvider.cs`
- `Namines.Infrastructure/AI/DeepSeekModelCatalog.cs`
- `Namines.Infrastructure/AI/ChatCompletionProviderFactory.cs`
- `Namines.Tests/Services/RecordingHttpMessageHandler.cs` — paylaşılan sahte
- `Namines.Tests/Services/AiRetryPolicyTests.cs`
- `Namines.Tests/Services/GroqRateLimitRetryTests.cs`
- `Namines.Tests/Services/ChatCompletionProviderTests.cs`

**Değişecek:**
- `Namines.Core/Analysis/NaiCatalog.cs` — sağlayıcıya özgü iki alan çıkıyor
- `Namines.Infrastructure/AI/GroqAIService.cs` — yeniden deneme + sağlayıcı
- `Namines.API/Extensions/ServiceCollectionExtensions.cs` — kayıtlar
- `Namines.API/appsettings.json` — yeni anahtarlar

---

### Task 1: Bekleme bütçesi mantığı

Yeniden denemenin KARAR kısmı saf bir sınıfa alınıyor: ne kadar beklenecek,
bütçe bitti mi. HTTP'den ayrı olduğu için gerçek zaman ve gerçek ağ olmadan
sınanabiliyor — ve asıl kazanç bu, çünkü "20 saniye bekledi mi" sorusunu
gerçekten 20 saniye bekleyerek sınamak sürdürülebilir değil.

**Files:**
- Create: `backend/Namines.Core/Interfaces/AiRetryPolicy.cs`
- Test: `backend/Namines.Tests/Services/AiRetryPolicyTests.cs`

**Interfaces:**
- Consumes: yok.
- Produces:
  - `sealed class AiRetryPolicy`
  - `AiRetryPolicy(int maxTotalWaitSeconds, int maxSingleWaitSeconds)`
  - `static AiRetryPolicy Disabled { get; }`
  - `bool TryNextDelay(TimeSpan requested, out TimeSpan granted)` — örnek
    durumludur, ardışık çağrılar toplamı biriktirir
  - `static bool TryParseRetryAfter(HttpResponseMessage?, string?, out TimeSpan)`
    — ayrı bir statik yardımcı olarak Task 2'de kullanılacak; bu görevde
    `HttpResponseMessage` bağımlılığı olmasın diye **ayrı** tutuluyor:
    `AiRetryPolicy` saf kalır.

- [ ] **Step 1: Write the failing test**

`backend/Namines.Tests/Services/AiRetryPolicyTests.cs`:

```csharp
using System;
using Namines.Core.Interfaces;
using Xunit;

namespace Namines.Tests.Services;

public class AiRetryPolicyTests
{
    [Fact]
    public void Grants_the_requested_delay_when_within_budget()
    {
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 600, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(20), out var granted));
        Assert.Equal(TimeSpan.FromSeconds(20), granted);
    }

    [Fact]
    public void Caps_a_single_absurd_delay_instead_of_hanging_the_request()
    {
        // Sağlayıcı "3600 saniye bekle" derse isteği bir saat asılı bırakmak,
        // hata döndürmekten daha kötü: kullanıcı ne olduğunu hiç öğrenemez.
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 600, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(3600), out var granted));
        Assert.Equal(TimeSpan.FromSeconds(60), granted);
    }

    [Fact]
    public void Stops_once_the_total_budget_is_spent()
    {
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 50, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(30), out _));
        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(20), out _));
        Assert.False(policy.TryNextDelay(TimeSpan.FromSeconds(1), out _));
    }

    [Fact]
    public void Trims_the_last_delay_to_what_is_left_rather_than_refusing_it()
    {
        // Bütçenin 10 saniyesi kalmışken 30 saniyelik bir bekleme isteğini
        // tamamen reddetmek, elimizdeki bütçeyi kullanmadan pes etmek olurdu.
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 40, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(30), out _));
        Assert.True(policy.TryNextDelay(TimeSpan.FromSeconds(30), out var granted));
        Assert.Equal(TimeSpan.FromSeconds(10), granted);
    }

    [Fact]
    public void Disabled_policy_never_waits()
    {
        Assert.False(AiRetryPolicy.Disabled.TryNextDelay(TimeSpan.FromSeconds(1), out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_budget_disables_retrying(int budget)
    {
        var policy = new AiRetryPolicy(budget, maxSingleWaitSeconds: 60);
        Assert.False(policy.TryNextDelay(TimeSpan.FromSeconds(1), out _));
    }

    [Fact]
    public void A_zero_or_negative_requested_delay_still_costs_nothing_and_is_allowed()
    {
        // Sağlayıcı "Retry-After: 0" derse hemen yeniden denemek doğru davranış.
        var policy = new AiRetryPolicy(maxTotalWaitSeconds: 600, maxSingleWaitSeconds: 60);

        Assert.True(policy.TryNextDelay(TimeSpan.Zero, out var granted));
        Assert.Equal(TimeSpan.Zero, granted);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test backend/Namines.Tests --filter FullyQualifiedName~AiRetryPolicyTests
```

Beklenen: DERLEME hatası — `AiRetryPolicy` tipi yok.

- [ ] **Step 3: Write minimal implementation**

`backend/Namines.Core/Interfaces/AiRetryPolicy.cs`:

```csharp
using System;

namespace Namines.Core.Interfaces;

/// <summary>
/// Hız sınırına takılan bir isteğin ne kadar bekleyebileceğine karar verir.
///
/// <b>Neden ayrı ve saf bir sınıf:</b> karar HTTP'den bağımsız. Bütçe
/// tükendiğinde durmak, tek bir beklemeyi kısmak, kalan bütçeye sığdırmak —
/// hepsi gerçek zaman geçirmeden sınanabilmeli. Aynı mantık <c>PostAsync</c>'in
/// içine gömülseydi, "bütçe bitince duruyor mu" sorusunu ancak on dakika
/// bekleyen bir test sorabilirdi ve o test yazılmazdı.
///
/// <b>Örnek durumludur</b> (harcanan süreyi biriktirir): bir istek boyunca tek
/// bir örnek kullanılır, istekler arasında paylaşılmaz.
/// </summary>
public sealed class AiRetryPolicy
{
    private readonly TimeSpan _maxTotal;
    private readonly TimeSpan _maxSingle;
    private TimeSpan _spent;

    public AiRetryPolicy(int maxTotalWaitSeconds, int maxSingleWaitSeconds)
    {
        _maxTotal = TimeSpan.FromSeconds(Math.Max(0, maxTotalWaitSeconds));
        _maxSingle = TimeSpan.FromSeconds(Math.Max(0, maxSingleWaitSeconds));
    }

    /// <summary>Hiç beklemeyen politika — yeniden deneme kapalı demek.</summary>
    public static AiRetryPolicy Disabled => new(0, 0);

    /// <summary>Şu ana kadar beklenen toplam süre. Yalnızca loglama için.</summary>
    public TimeSpan Spent => _spent;

    /// <summary>
    /// Sağlayıcının istediği <paramref name="requested"/> beklemeyi bütçeye
    /// göre değerlendirir.
    /// </summary>
    /// <returns>
    /// Beklenip yeniden denenecekse <c>true</c> ve <paramref name="granted"/>
    /// beklenecek süre; bütçe bittiyse <c>false</c>.
    /// </returns>
    public bool TryNextDelay(TimeSpan requested, out TimeSpan granted)
    {
        granted = TimeSpan.Zero;

        if (_maxTotal <= TimeSpan.Zero) return false;

        var remaining = _maxTotal - _spent;
        if (remaining <= TimeSpan.Zero) return false;

        // Negatif ya da sıfır istek: hemen yeniden dene, bütçeden bir şey yeme.
        if (requested <= TimeSpan.Zero)
        {
            granted = TimeSpan.Zero;
            return true;
        }

        // Önce tek seferlik tavan, sonra kalan bütçe. Sıra önemli: sağlayıcının
        // saçma bir değeri kalan bütçeyi tek kalemde yutmasın.
        var capped = requested > _maxSingle ? _maxSingle : requested;
        if (capped > remaining) capped = remaining;

        _spent += capped;
        granted = capped;
        return true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test backend/Namines.Tests --filter FullyQualifiedName~AiRetryPolicyTests
```

Beklenen: 8 test PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/Namines.Core/Interfaces/AiRetryPolicy.cs backend/Namines.Tests/Services/AiRetryPolicyTests.cs
git commit -m "feat: add a pure wait-budget policy for rate-limit retries"
```

---

### Task 2: `PostAsync` 429'da bekleyip yeniden denesin

**Files:**
- Modify: `backend/Namines.Infrastructure/AI/GroqAIService.cs`
- Modify: `backend/Namines.API/appsettings.json`
- Create: `backend/Namines.Tests/Services/RecordingHttpMessageHandler.cs`
- Test: `backend/Namines.Tests/Services/GroqRateLimitRetryTests.cs`

**Interfaces:**
- Consumes: `AiRetryPolicy` (Task 1).
- Produces:
  - `GroqAIService.ParseRetryAfter(HttpResponseMessage?, string?, out TimeSpan)`
    — `internal static bool`
  - `GroqAIService`'in kurucusuna eklenen **iki isteğe bağlı** parametre:
    `Microsoft.Extensions.Logging.ILogger<GroqAIService>? logger = null`,
    `Func<TimeSpan, CancellationToken, Task>? delay = null`. İkisinin de
    varsayılanı olduğu için mevcut çağrı yerleri ve testler DEĞİŞMEZ.
  - `RecordingHttpMessageHandler` (test yardımcısı) — Task 5 de kullanacak.

- [ ] **Step 1: Write the failing test**

Önce paylaşılan sahte, `backend/Namines.Tests/Services/RecordingHttpMessageHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Tests.Services;

/// <summary>
/// Sırayla verilen yanıtları döndüren ve gördüğü istekleri kaydeden sahte
/// aktarım katmanı.
///
/// <b>Neden gerçek ağ yerine bu:</b> sınanan şey sağlayıcının davranışı değil,
/// BİZİM 429'a verdiğimiz tepki. Gerçek bir 429 üretmek için gerçekten hız
/// sınırına çarpmak gerekirdi — yani test, hesabın o anki kotasına bağlı olurdu.
/// </summary>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<string> RequestBodies { get; } = new();
    public List<Uri?> RequestUris { get; } = new();
    public List<string?> AuthorizationHeaders { get; } = new();
    public int CallCount => RequestBodies.Count;

    public RecordingHttpMessageHandler Enqueue(Func<HttpResponseMessage> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public RecordingHttpMessageHandler EnqueueRateLimited(string? retryAfterSeconds)
    {
        return Enqueue(() =>
        {
            var body = retryAfterSeconds is null
                ? "{\"error\":{\"code\":\"rate_limit_exceeded\"}}"
                : $"{{\"error\":{{\"code\":\"rate_limit_exceeded\",\"message\":\"Please try again in {retryAfterSeconds}s\"}}}}";

            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(body),
            };
        });
    }

    public RecordingHttpMessageHandler EnqueueChatCompletion(string content)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(content);
        return Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"choices\":[{{\"finish_reason\":\"stop\",\"message\":{{\"content\":{escaped}}}}}]}}"),
        });
    }

    public RecordingHttpMessageHandler EnqueueStatus(HttpStatusCode status, string body)
        => Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body) });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUris.Add(request.RequestUri);
        AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        if (_responses.Count == 0)
            throw new InvalidOperationException("Sahte aktarıma beklenenden fazla istek geldi.");

        return _responses.Dequeue()();
    }
}
```

Sonra testler, `backend/Namines.Tests/Services/GroqRateLimitRetryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Namines.Core.Interfaces;
using Namines.Infrastructure.AI;
using Xunit;

namespace Namines.Tests.Services;

public class GroqRateLimitRetryTests
{
    private static (GroqAIService Service, List<TimeSpan> Waits) Build(
        RecordingHttpMessageHandler handler,
        int maxTotalWaitSeconds = 600,
        int maxSingleWaitSeconds = 60)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "test-key",
                ["Groq:Model"] = "openai/gpt-oss-20b",
                ["Ai:RateLimitRetry:MaxTotalWaitSeconds"] = maxTotalWaitSeconds.ToString(),
                ["Ai:RateLimitRetry:MaxSingleWaitSeconds"] = maxSingleWaitSeconds.ToString(),
            })
            .Build();

        var waits = new List<TimeSpan>();

        var service = new GroqAIService(
            new HttpClient(handler),
            config,
            new HttpContextAccessor(),
            new MemoryCache(new MemoryCacheOptions()),
            logger: null,
            delay: (d, _) => { waits.Add(d); return Task.CompletedTask; });

        return (service, waits);
    }

    private static readonly IReadOnlyList<AgentChatMessage> Messages =
        new[] { new AgentChatMessage("user", "merhaba") };

    [Fact]
    public async Task Waits_the_advertised_interval_then_succeeds()
    {
        var handler = new RecordingHttpMessageHandler()
            .EnqueueRateLimited("7")
            .EnqueueChatCompletion("tamam");

        var (service, waits) = Build(handler);

        var response = await service.CompleteAsync(
            Messages, Array.Empty<AgentToolDefinition>(), 0.2);

        Assert.Equal("tamam", response.Content);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, waits);
    }

    [Fact]
    public async Task Gives_up_with_the_rate_limit_exception_once_the_budget_is_spent()
    {
        // Bütçe 10 saniye; sağlayıcı her seferinde 6 saniye istiyor.
        // İki bekleme sığar, üçüncüsü sığmaz — o noktada bugünkü davranışa
        // dönülmeli, yani kullanıcı susturulmuş bir hata değil AiRateLimit almalı.
        var handler = new RecordingHttpMessageHandler()
            .EnqueueRateLimited("6")
            .EnqueueRateLimited("6")
            .EnqueueRateLimited("6");

        var (service, waits) = Build(handler, maxTotalWaitSeconds: 10);

        await Assert.ThrowsAsync<AiRateLimitException>(() => service.CompleteAsync(
            Messages, Array.Empty<AgentToolDefinition>(), 0.2));

        Assert.Equal(3, handler.CallCount);
        Assert.Equal(new[] { TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(4) }, waits);
    }

    [Fact]
    public async Task Does_not_retry_a_non_rate_limit_failure()
    {
        // 400'ü yeniden denemek yalnızca aynı 400'ü tekrar almaktır.
        var handler = new RecordingHttpMessageHandler()
            .EnqueueStatus(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"invalid_request\"}}");

        var (service, waits) = Build(handler);

        await Assert.ThrowsAnyAsync<Exception>(() => service.CompleteAsync(
            Messages, Array.Empty<AgentToolDefinition>(), 0.2));

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Cancellation_interrupts_the_wait_instead_of_sitting_it_out()
    {
        var handler = new RecordingHttpMessageHandler().EnqueueRateLimited("30");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "test-key",
                ["Ai:RateLimitRetry:MaxTotalWaitSeconds"] = "600",
            })
            .Build();

        using var cts = new CancellationTokenSource();

        var service = new GroqAIService(
            new HttpClient(handler),
            config,
            new HttpContextAccessor(),
            new MemoryCache(new MemoryCacheOptions()),
            logger: null,
            // Beklemeye girildiği anda kullanıcı iptal etmiş gibi davran.
            delay: (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompleteAsync(
            Messages, Array.Empty<AgentToolDefinition>(), 0.2, cts.Token));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Retries_even_when_the_provider_gives_no_interval()
    {
        // Süre bildirilmediğinde beklemeden vazgeçmek, yeniden denemeyi
        // sağlayıcının nezaketine bağlamak olurdu. Varsayılan tek seferlik
        // tavan kullanılır.
        var handler = new RecordingHttpMessageHandler()
            .EnqueueRateLimited(null)
            .EnqueueChatCompletion("oldu");

        var (service, waits) = Build(handler, maxSingleWaitSeconds: 60);

        var response = await service.CompleteAsync(
            Messages, Array.Empty<AgentToolDefinition>(), 0.2);

        Assert.Equal("oldu", response.Content);
        Assert.Equal(new[] { TimeSpan.FromSeconds(60) }, waits);
    }

    [Theory]
    [InlineData("Please try again in 12.5s", 12.5)]
    [InlineData("rate limit reached, try again in 3s", 3)]
    public void ParseRetryAfter_reads_the_interval_from_the_error_body(string body, double expected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        Assert.True(GroqAIService.ParseRetryAfter(response, body, out var wait));
        Assert.Equal(TimeSpan.FromSeconds(expected), wait);
    }

    [Fact]
    public void ParseRetryAfter_prefers_the_Retry_After_header_over_the_body()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter =
            new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(9));

        Assert.True(GroqAIService.ParseRetryAfter(response, "try again in 99s", out var wait));
        Assert.Equal(TimeSpan.FromSeconds(9), wait);
    }

    [Fact]
    public void ParseRetryAfter_reports_failure_when_nothing_says_how_long()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        Assert.False(GroqAIService.ParseRetryAfter(response, "rate_limit_exceeded", out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test backend/Namines.Tests --filter FullyQualifiedName~GroqRateLimitRetryTests
```

Beklenen: DERLEME hatası — kurucunun `logger`/`delay` parametreleri ve
`ParseRetryAfter` yok.

- [ ] **Step 3: Write minimal implementation**

`GroqAIService.cs` içinde, sırayla:

**(a)** Dosyanın başına ekle:

```csharp
using Microsoft.Extensions.Logging;
using System.Threading;
```

**(b)** Alanlara ekle (`private readonly IMemoryCache _cache;` satırının altına):

```csharp
    private readonly ILogger<GroqAIService>? _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _retryMaxTotalWaitSeconds;
    private readonly int _retryMaxSingleWaitSeconds;
```

**(c)** Kurucunun imzasını değiştir — **iki parametre de isteğe bağlı**, bu sayede
mevcut çağrı yerleri ve testler olduğu gibi derlenmeye devam eder:

```csharp
    public GroqAIService(
        HttpClient httpClient,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IMemoryCache cache,
        ILogger<GroqAIService>? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
```

**(d)** Kurucunun gövdesine, `_modelName = ...` satırının hemen altına:

```csharp
        _logger = logger;
        // Testte gerçek zaman harcanmasın diye uyku dışarıdan verilebiliyor.
        _delay = delay ?? Task.Delay;

        // Varsayılanlar: toplam 600 sn, tek seferde en çok 60 sn. Daha yüksek
        // bir sağlayıcı katmanında bu beklemelerin HİÇ oluşmaması hedefleniyor;
        // buradaki değerler bir emniyet ağı, çalışma biçimi değil. Sıfır vermek
        // yeniden denemeyi tamamen kapatır (bkz. AiRetryPolicy).
        _retryMaxTotalWaitSeconds = ReadInt(configuration, "Ai:RateLimitRetry:MaxTotalWaitSeconds", 600);
        _retryMaxSingleWaitSeconds = ReadInt(configuration, "Ai:RateLimitRetry:MaxSingleWaitSeconds", 60);
```

**(e)** Sınıfa yardımcıyı ekle (kurucunun hemen altına):

```csharp
    private static int ReadInt(IConfiguration configuration, string key, int fallback)
        => int.TryParse(configuration[key], out var value) ? value : fallback;
```

**(f)** `GetRetryAfterSeconds`'ın hemen üstüne sayısal ayrıştırıcıyı ekle:

```csharp
    /// <summary>
    /// Sağlayıcının bildirdiği bekleme süresini sayıya çevirir.
    ///
    /// <b>Neden <see cref="GetRetryAfterSeconds"/>'dan ayrı:</b> o metot
    /// kullanıcıya gösterilecek METNİ üretiyor ve bilinmeyen durumda "unknown"
    /// diyebiliyor. Yeniden deneme ise bir SAYI istiyor ve "unknown"ı
    /// sıfır saniye sanmamalı. İki soru farklı, cevapları da farklı.
    /// </summary>
    internal static bool ParseRetryAfter(
        HttpResponseMessage? response, string? errorContent, out TimeSpan retryAfter)
    {
        if (response?.Headers.RetryAfter?.Delta is { } delta)
        {
            retryAfter = delta;
            return true;
        }

        if (!string.IsNullOrEmpty(errorContent))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                errorContent,
                @"try again in ([0-9.]+)s",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success &&
                double.TryParse(
                    match.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds))
            {
                retryAfter = TimeSpan.FromSeconds(seconds);
                return true;
            }
        }

        retryAfter = TimeSpan.Zero;
        return false;
    }

    /// <summary>
    /// Bu yanıt bir hız sınırı mı?
    ///
    /// Ölçüt <see cref="ThrowForFailure"/> ile BİREBİR aynı olmak zorunda.
    /// Ayrıştıkları anda, sınıflandırması "hız sınırı" olan ama yeniden
    /// denenmeyen (ya da tersi) bir yanıt türü doğar.
    /// </summary>
    private static bool IsRateLimited(HttpResponseMessage response, string errorContent)
        => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
           errorContent.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase);
```

**(g)** `PostAsync`'i yeniden yaz. Gövde kurulumu döngünün İÇİNE alınıyor:
bir `HttpRequestMessage` ikinci kez gönderilemez, yani her denemede yeniden
kurulmalı.

```csharp
    private async Task<HttpResponseMessage> PostAsync(
        string relativeUri, object payload, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(payload);

        // Resolve the model name from the payload to detect Gemini routing
        string modelInPayload = "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("model", out var modelEl))
                modelInPayload = modelEl.GetString() ?? "";
        }
        catch { }

        json = ClampMaxTokensToModelLimit(json, modelInPayload);

        var retryPolicy = new AiRetryPolicy(_retryMaxTotalWaitSeconds, _retryMaxSingleWaitSeconds);

        while (true)
        {
            var response = await SendOnceAsync(relativeUri, json, modelInPayload, cancellationToken);

            // Hız sınırı DIŞINDAKİ her yanıt — başarı da, başka hata da —
            // olduğu gibi çağırana gider. Yorumlamak çağıranın işi.
            var errorContent = response.IsSuccessStatusCode
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode || !IsRateLimited(response, errorContent))
                return response;

            // Sağlayıcı süre bildirmediyse tek seferlik tavan kadar bekle:
            // yeniden denemeyi sağlayıcının nezaketine bağlamak, tam da
            // dayanıklılık istediğimiz yerde onu kaybetmek olurdu.
            var requested = ParseRetryAfter(response, errorContent, out var parsed)
                ? parsed
                : TimeSpan.FromSeconds(_retryMaxSingleWaitSeconds);

            if (!retryPolicy.TryNextDelay(requested, out var wait))
            {
                // Bütçe bitti: bugünkü davranış. Yanıtı çağırana döndürüyoruz ki
                // hata gövdesini her zamanki gibi kendisi okuyup
                // ThrowForFailure'a versin — yeniden deneme, hata yolunu
                // DEĞİŞTİRMİYOR, yalnızca ondan önce araya giriyor.
                _logger?.LogWarning(
                    "AI rate limit retry budget exhausted after {Spent}s; surfacing the rate limit to the caller.",
                    retryPolicy.Spent.TotalSeconds);
                return response;
            }

            _logger?.LogInformation(
                "AI provider rate limited; waiting {Wait}s before retrying (spent {Spent}s of budget).",
                wait.TotalSeconds, retryPolicy.Spent.TotalSeconds);

            response.Dispose();

            // Token beklemenin İÇİNDE de geçerli: kullanıcı iptal ettiğinde
            // bir dakika daha beklemek, iptali yok saymak olurdu.
            await _delay(wait, cancellationToken);
        }
    }

    /// <summary>
    /// Tek bir gönderim. <see cref="HttpRequestMessage"/> ikinci kez
    /// gönderilemediği için her denemede yeniden kuruluyor.
    /// </summary>
    private async Task<HttpResponseMessage> SendOnceAsync(
        string relativeUri, string json, string modelInPayload, CancellationToken cancellationToken)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, relativeUri) { Content = content };

        bool isGeminiModel = modelInPayload.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase);

        if (isGeminiModel)
        {
            // Sunucu-taraflı Gemini API key'i — configuration'dan al
            var httpContext = _httpContextAccessor.HttpContext;
            var config = httpContext?.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
            var geminiApiKey = config?["Gemini:ApiKey"] ?? "";
            if (string.IsNullOrWhiteSpace(geminiApiKey))
                throw new AiNotConfiguredException("Gemini");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", geminiApiKey);
            request.RequestUri = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/" + relativeUri);
        }
        else
        {
            // Anahtar yoksa ağa HİÇ çıkma. Önceden "MISSING_API_KEY" gerçek bir istek
            // olarak Groq'a gidiyordu: ~10 saniye bekleniyor, dış servise gereksiz yük
            // biniyor ve sonuç yine hata oluyordu — üstelik genel bir 500 olarak, yani
            // arayüz onu "beklenmedik hata" sayıp sessizce yutuyordu.
            if (_groqApiKey == MissingApiKeySentinel)
                throw new AiNotConfiguredException("Groq");

            // Standart Groq isteği: key per-request inject edilir (DefaultRequestHeaders KULLANILMAZ)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _groqApiKey);
        }

        // Token GEÇİRİLİYOR: bu metot artık bir agent turu içinde zincirleme
        // çağrılabiliyor (bkz. CompleteAsync/RepairWithToolsAsync) — kullanıcı
        // isteği iptal ettiğinde kuyruktaki her çağrının upstream'de tamamlanmayı
        // beklemeye devam etmesi, hem gereksiz gecikme hem gereksiz fatura demek.
        var response = await _httpClient.SendAsync(request, cancellationToken);
        await RecordUsageAsync(response);
        return response;
    }
```

**(h)** `backend/Namines.API/appsettings.json` — kök düzeyde, `"Groq"` bloğunun
hemen ardına ekle:

```json
  "Ai": {
    "RateLimitRetry": {
      "MaxTotalWaitSeconds": 600,
      "MaxSingleWaitSeconds": 60
    }
  },
```

**(i)** DI'da logger'ın gerçekten enjekte edilmesi için
`backend/Namines.API/Extensions/ServiceCollectionExtensions.cs` içindeki
`services.AddHttpClient<GroqAIService>(...)` kaydına DOKUNMA: `AddHttpClient<T>`
kalan kurucu parametrelerini zaten DI'dan çözer ve `ILogger<T>` kayıtlıdır.
İsteğe bağlı `delay` parametresi DI'da kayıtlı olmadığı için varsayılan
`Task.Delay` kullanılır — istenen davranış budur.

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test backend/Namines.Tests --filter FullyQualifiedName~GroqRateLimitRetryTests
```

Beklenen: 9 test PASS.

Sonra tam paket — Groq davranışının birebir korunduğunu doğrulamak için:

```bash
dotnet test backend/Namines.Tests
```

Beklenen: `LaunchServiceTests.Empty_target_gets_provisioned_and_ddl_applied_and_backed_up`
ve `DdlExecutionTests+PostgresTests.Generated_ddl_executes(fixtureName: "08-trigger-postgres-only")`
dışında hepsi PASS. Bu iki test Docker'a bağlı ve bu dalın öncesinde de
başarısızdı — başka bir test kırmızıya dönerse DUR.

- [ ] **Step 5: Commit**

```bash
git add backend/Namines.Infrastructure/AI/GroqAIService.cs backend/Namines.API/appsettings.json backend/Namines.Tests/Services/RecordingHttpMessageHandler.cs backend/Namines.Tests/Services/GroqRateLimitRetryTests.cs
git commit -m "feat: wait and retry when the AI provider reports a rate limit"
```

---

### Task 3: Model kataloğunu ürün ve sağlayıcı olarak ayır

`NaiCatalog` bugün iki farklı şeyi birden tutuyor: ürünün kendi kademeleri
(Flash/Standard/Pro, fiyat çarpanı, plan kısıtı) ve GROQ'un model kimlikleri ile
sınırları. İkincisi sağlayıcıya göre değişiyor; ayrılmadan ikinci sağlayıcı
eklenemez. Bu görevde davranış DEĞİŞMİYOR, yalnızca sahiplik ayrılıyor.

**Files:**
- Modify: `backend/Namines.Core/Analysis/NaiCatalog.cs`
- Create: `backend/Namines.Core/Analysis/IModelCatalog.cs`
- Create: `backend/Namines.Infrastructure/AI/GroqModelCatalog.cs`
- Modify: `backend/Namines.Infrastructure/AI/GroqAIService.cs`
- Modify: mevcut `NaiCatalog.Get(...).UpstreamModel` / `.MaxCompletionTokens` /
  `ClampToModelLimit` kullanan her yer (aşağıdaki adımda bulunuyor)

**Interfaces:**
- Consumes: yok.
- Produces:
  - `public interface IModelCatalog { string UpstreamModel(NaiModel model); int MaxCompletionTokens(NaiModel model); int ClampToModelLimit(string? upstreamModel, int requestedMaxTokens); }`
  - `public sealed class GroqModelCatalog : IModelCatalog`
  - `NaiModelInfo` artık `UpstreamModel` ve `MaxCompletionTokens` İÇERMİYOR:
    `NaiModelInfo(string Id, string DisplayName, string Description, double TokenMultiplier)`
  - `NaiCatalog`'da kalanlar değişmeden: `All`, `Get`, `Resolve`, `MaxFor`,
    `ClampToPlan`, `CostOf`.

- [ ] **Step 1: Tüm etkilenen çağrı yerlerini bul**

```bash
grep -rn "UpstreamModel\|MaxCompletionTokens\|ClampToModelLimit" --include=*.cs backend/ | grep -v "/obj/" | grep -v "/bin/"
```

Bu listeyi not al; Step 3'te hepsinin kapandığını buna bakarak doğrulayacaksın.

- [ ] **Step 2: Write the failing test**

`backend/Namines.Tests/Services/ChatCompletionProviderTests.cs` (bu dosya Task 5'te
büyüyecek):

```csharp
using Namines.Core.Analysis;
using Namines.Infrastructure.AI;
using Xunit;

namespace Namines.Tests.Services;

public class ChatCompletionProviderTests
{
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
}
```

- [ ] **Step 3: Run test to verify it fails**

```bash
dotnet test backend/Namines.Tests --filter FullyQualifiedName~ChatCompletionProviderTests
```

Beklenen: DERLEME hatası — `GroqModelCatalog` yok.

- [ ] **Step 4: Write the implementation**

`backend/Namines.Core/Analysis/IModelCatalog.cs`:

```csharp
namespace Namines.Core.Analysis;

/// <summary>
/// Ürün kademelerinin (<see cref="NaiModel"/>) bir SAĞLAYICIDAKİ karşılıkları.
///
/// <b>Neden <see cref="NaiCatalog"/>'dan ayrı:</b> "Standard ne kadar pahalı" ve
/// "Free planda Pro var mı" soruları ürünün kendi kararları, sağlayıcı
/// değişince değişmezler. "Standard hangi model" ve "o model en fazla kaç token
/// yazabilir" soruları ise tamamen sağlayıcının kararı. İkisi aynı tabloda
/// durursa, ikinci sağlayıcı eklendiğinde aynı sorunun iki doğru cevabı olur.
/// </summary>
public interface IModelCatalog
{
    /// <summary>Sağlayıcıdaki gerçek model kimliği. Kullanıcıya GÖSTERİLMEZ.</summary>
    string UpstreamModel(NaiModel model);

    /// <summary>Bu modelin tek çağrıda yazabileceği en fazla token.</summary>
    int MaxCompletionTokens(NaiModel model);

    /// <summary>
    /// İstenen <c>max_tokens</c>'ı modelin sağlayıcı tarafındaki sınırına çeker.
    ///
    /// <b>Neden şart:</b> plan tavanları ürünün bütçe kararı, modelin üst sınırı
    /// sağlayıcının kararı ve ikisi birbirinden habersiz. Sınırın üstünde bir
    /// değer gönderildiğinde sağlayıcı isteği kısmen değil KOMPLE reddediyor
    /// (400 invalid_request): kullanıcı küçük bir şema değil, hiçbir şey almıyor.
    ///
    /// <b>Tanınmayan model olduğu gibi geçer</b> (fail-open): kimlik
    /// yapılandırmadan override edilebiliyor ve bilmediğimiz bir modele uydurma
    /// bir tavan dayatmak, sessizce çıktıyı kısmak olurdu.
    /// </summary>
    int ClampToModelLimit(string? upstreamModel, int requestedMaxTokens);
}
```

`backend/Namines.Infrastructure/AI/GroqModelCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;
using Namines.Core.Analysis;

namespace Namines.Infrastructure.AI;

/// <summary>
/// Groq'taki model kimlikleri ve sınırları.
///
/// <b>Bu tablonun geçmişi, neden tek yerde durması gerektiğini bire bir
/// anlatıyor — üç kez üst üste yanıldı:</b>
/// <list type="number">
/// <item><c>qwen/qwen3.6-27b</c> bir gün 404 (model_not_found) vermeye başladı
/// ve şema üretimi tamamen durdu.</item>
/// <item>Yerine AKLA YATKIN görünen bir ad yazıldı
/// (<c>llama-3.3-70b-versatile</c>). O da bu hesapta yoktu: ölü model ölü
/// modelle değiştirildi, bütün birim testler yeşil kaldı, hata ancak CANLI
/// istekte ortaya çıktı.</item>
/// <item>Sağlayıcının kataloğu sorulunca doğru ad bulundu
/// (<c>qwen3.8-27b</c> — model kaldırılmamış, sürümü artmış). Ama canlı istek
/// yine başarısız oldu: o modelin bu hesapta dakika başına ÇIKTI TOKENI sınırı
/// 1.000, yani kabaca iki tablo.</item>
/// </list>
/// Sonuç: <c>gpt-oss</c> ikilisi aynı hesapta 8.000 TPM ile sorunsuz çalışıyor.
/// Standard'ın Flash ile aynı modele düşmesi bilinçli bir taviz — katmanları
/// ayrı tutmak uğruna VARSAYILAN katmanı hiç şema üretemez bırakmak, ayrımı
/// anlamlı değil işlevsiz yapardı. Pro (120b) farkını koruyor.
///
/// <b>Kural:</b> burayı değiştirmeden önce hem modelin VAR olduğunu
/// (<c>GET /openai/v1/models</c>) hem de kotasının iş için yettiğini (küçük bir
/// istekle <c>x-ratelimit-*</c> başlıkları) doğrula. Tahmin etme.
/// </summary>
public sealed class GroqModelCatalog : IModelCatalog
{
    private static readonly Dictionary<NaiModel, (string Id, int MaxCompletionTokens)> Models = new()
    {
        [NaiModel.Flash] = ("openai/gpt-oss-20b", 65_536),
        [NaiModel.Standard] = ("openai/gpt-oss-20b", 65_536),
        [NaiModel.Pro] = ("openai/gpt-oss-120b", 65_536),
    };

    public string UpstreamModel(NaiModel model) => Models[model].Id;

    public int MaxCompletionTokens(NaiModel model) => Models[model].MaxCompletionTokens;

    public int ClampToModelLimit(string? upstreamModel, int requestedMaxTokens)
    {
        if (string.IsNullOrWhiteSpace(upstreamModel)) return requestedMaxTokens;

        foreach (var entry in Models.Values)
        {
            if (string.Equals(entry.Id, upstreamModel, StringComparison.OrdinalIgnoreCase))
                return Math.Min(requestedMaxTokens, entry.MaxCompletionTokens);
        }

        return requestedMaxTokens;
    }
}
```

`backend/Namines.Core/Analysis/NaiCatalog.cs` düzenlemeleri:

1. `NaiModelInfo` kaydından son iki parametreyi ve onların XML doc satırlarını
   çıkar; kalan hâli:

```csharp
public sealed record NaiModelInfo(
    string Id,
    string DisplayName,
    string Description,
    double TokenMultiplier);
```

2. `Models` sözlüğündeki üç girdiden `UpstreamModel:` ve
   `MaxCompletionTokens:` argümanlarını çıkar. Groq'a özgü uzun yorum bloğu
   `GroqModelCatalog`'a TAŞINDI (yukarıda) — buradan sil.

3. `ClampToModelLimit` metodunu tamamen SİL (artık `GroqModelCatalog`'da).

4. Sınıfın XML doc'undaki "Sağlayıcı model kimliği yalnızca burada geçiyor"
   cümlesini şununla değiştir:

```
/// Sağlayıcı model kimlikleri burada DEĞİL, sağlayıcının kendi kataloğunda
/// (<see cref="IModelCatalog"/>). Burada kalan her şey ürünün kendi kararı:
/// kademe adları, fiyat çarpanı, plan kısıtı — sağlayıcı değişse de değişmezler.
```

`backend/Namines.Infrastructure/AI/GroqAIService.cs` düzenlemeleri:

1. Alan ekle: `private readonly IModelCatalog _modelCatalog;`
2. Kurucuda, `_modelName` atamasının ÜSTÜNE: `_modelCatalog = new GroqModelCatalog();`
   (Task 4'te dışarıdan enjekte edilecek — şimdilik yerinde kuruluyor ki bu
   görev kendi başına derlensin ve test edilebilsin.)
3. `_modelName = configuration["Groq:Model"] ?? NaiCatalog.Get(NaiModel.Standard).UpstreamModel;`
   → `_modelName = configuration["Groq:Model"] ?? _modelCatalog.UpstreamModel(NaiModel.Standard);`
4. `UpstreamModel(NaiModel model)` metodunda
   `NaiCatalog.Get(model).UpstreamModel` → `_modelCatalog.UpstreamModel(model)`
5. `ClampMaxTokensToModelLimit` içinde `NaiCatalog.ClampToModelLimit(...)` →
   `_modelCatalog.ClampToModelLimit(...)`, ve metodu `static`likten çıkar
   (`private string ClampMaxTokensToModelLimit(...)`).

Step 1'deki listede kalan her yeri de aynı şekilde kapat. `NaiCatalog.All`'ı
API'ye model listesi olarak veren yerler yalnızca `Id`/`DisplayName`/
`Description`/`TokenMultiplier` kullanıyorsa değişiklik gerekmez; sağlayıcı
kimliğini okuyorlarsa bu bir hataydı (kullanıcıya gösterilmemeli) — okumayı sil.

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test backend/Namines.Tests --filter FullyQualifiedName~ChatCompletionProviderTests
dotnet test backend/Namines.Tests
```

Beklenen: yeni testler PASS; tam pakette yalnızca bilinen iki Docker testi
başarısız.

- [ ] **Step 6: Commit**

```bash
git add -A backend/
git commit -m "refactor: separate the product model tiers from the provider's model table"
```

---

### Task 4: `IChatCompletionProvider` ve Groq implementasyonu

Sağlayıcıya özgü kalan her şey (temel adres, kimlik doğrulama, katalog) tek bir
arayüzün arkasına alınıyor. Bu görevde de davranış DEĞİŞMİYOR — ikinci sağlayıcı
bir sonraki görevde geliyor. Ayırmayı önce tek sağlayıcıyla yapmak bilinçli:
soyutlamanın doğru olduğunu, hiçbir testin değişmemesiyle kanıtlayabiliyoruz.

**Files:**
- Create: `backend/Namines.Core/Interfaces/IChatCompletionProvider.cs`
- Create: `backend/Namines.Infrastructure/AI/GroqChatCompletionProvider.cs`
- Modify: `backend/Namines.Infrastructure/AI/GroqAIService.cs`
- Modify: `backend/Namines.API/Extensions/ServiceCollectionExtensions.cs`
- Test: `backend/Namines.Tests/Services/ChatCompletionProviderTests.cs` (ekleme)

**Interfaces:**
- Consumes: `IModelCatalog`, `GroqModelCatalog` (Task 3).
- Produces:

```csharp
public interface IChatCompletionProvider
{
    string Name { get; }
    Uri BaseAddress { get; }
    bool IsConfigured { get; }
    IModelCatalog Models { get; }
    void Authenticate(HttpRequestMessage request);
}
```
  - `public sealed class GroqChatCompletionProvider : IChatCompletionProvider`
    — kurucu: `GroqChatCompletionProvider(IConfiguration configuration)`
  - `GroqAIService` kurucusuna eklenen **isteğe bağlı** parametre:
    `IChatCompletionProvider? provider = null` (varsayılan:
    `new GroqChatCompletionProvider(configuration)`), `logger`'dan ÖNCE değil
    SONRA gelir — mevcut isteğe bağlı parametrelerin sırası bozulmasın:
    `(..., IMemoryCache cache, ILogger<GroqAIService>? logger = null, Func<TimeSpan, CancellationToken, Task>? delay = null, IChatCompletionProvider? provider = null)`

- [ ] **Step 1: Write the failing test**

`ChatCompletionProviderTests.cs` dosyasına ekle:

```csharp
    [Fact]
    public void Groq_provider_points_at_the_groq_endpoint_and_carries_its_key()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Groq:ApiKey"] = "gsk-test",
            })
            .Build();

        var provider = new GroqChatCompletionProvider(config);

        Assert.Equal("groq", provider.Name);
        Assert.Equal(new System.Uri("https://api.groq.com/openai/v1/"), provider.BaseAddress);
        Assert.True(provider.IsConfigured);

        using var request = new System.Net.Http.HttpRequestMessage();
        provider.Authenticate(request);
        Assert.Equal("Bearer gsk-test", request.Headers.Authorization?.ToString());
    }

    [Fact]
    public void Groq_provider_reports_itself_unconfigured_when_the_key_is_missing()
    {
        // Anahtarsız bir sağlayıcı ağa HİÇ çıkmamalı: "MISSING_API_KEY" ile
        // gerçek bir istek yapmak on saniye bekleyip yine hata almak demekti.
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var provider = new GroqChatCompletionProvider(config);

        Assert.False(provider.IsConfigured);
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test backend/Namines.Tests --filter FullyQualifiedName~ChatCompletionProviderTests
```

Beklenen: DERLEME hatası — `GroqChatCompletionProvider` yok.

- [ ] **Step 3: Write the implementation**

`backend/Namines.Core/Interfaces/IChatCompletionProvider.cs`:

```csharp
using System;
using System.Net.Http;
using Namines.Core.Analysis;

namespace Namines.Core.Interfaces;

/// <summary>
/// Bir sohbet-tamamlama sağlayıcısına özgü olan HER ŞEY.
///
/// <b>Neden tüketiciler değil de burası soyutlandı:</b> <c>GroqAIService</c>'i
/// somut tip olarak alan altı yer var (<c>GatewayController</c>,
/// <c>AIDbaService</c>, <c>AutomationExecutor</c>, <c>GroqSchemaDraftSource</c>,
/// <c>MigrationService</c>, <c>SmartSeedService</c>) ve hepsi
/// <see cref="IAIService"/>'te bulunmayan metotlara ihtiyaç duyuyor. Altısını da
/// bir arayüze çevirmek, o arayüzü yirmi metoda çıkarır ve ikinci sağlayıcının
/// yalnızca şema üretiminde çalışmasıyla sonuçlanırdı. Sağlayıcıyı ALTTAN
/// değiştirince altı tüketici hiç değişmeden ikinci sağlayıcıyı da kullanıyor.
/// </summary>
public interface IChatCompletionProvider
{
    /// <summary>Yapılandırmada geçen ad: <c>groq</c>, <c>deepseek</c>.</summary>
    string Name { get; }

    /// <summary>Sağlayıcının OpenAI uyumlu kök adresi, sonunda eğik çizgiyle.</summary>
    Uri BaseAddress { get; }

    /// <summary>API anahtarı var mı. Yoksa ağa hiç çıkılmaz.</summary>
    bool IsConfigured { get; }

    /// <summary>Bu sağlayıcıdaki model kimlikleri ve sınırları.</summary>
    IModelCatalog Models { get; }

    /// <summary>
    /// İsteğe kimlik başlığını yazar.
    ///
    /// <b>İstek başına, <c>DefaultRequestHeaders</c>'a DEĞİL:</b> paylaşılan
    /// <c>HttpClient</c>'ta başlık değiştirmek, paralel isteklerde yarış koşulu
    /// ve log sızıntısı riski demek.
    /// </summary>
    void Authenticate(HttpRequestMessage request);
}
```

`backend/Namines.Infrastructure/AI/GroqChatCompletionProvider.cs`:

```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Namines.Core.Analysis;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.AI;

/// <summary>Varsayılan sağlayıcı. Bugünkü davranışın birebir karşılığı.</summary>
public sealed class GroqChatCompletionProvider : IChatCompletionProvider
{
    private readonly string? _apiKey;

    public GroqChatCompletionProvider(IConfiguration configuration)
    {
        var key = configuration["Groq:ApiKey"];
        _apiKey = string.IsNullOrWhiteSpace(key) ? null : key;
    }

    public string Name => "groq";

    public Uri BaseAddress => new("https://api.groq.com/openai/v1/");

    public bool IsConfigured => _apiKey is not null;

    public IModelCatalog Models { get; } = new GroqModelCatalog();

    public void Authenticate(HttpRequestMessage request)
    {
        if (_apiKey is null) throw new AiNotConfiguredException("Groq");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }
}
```

`GroqAIService.cs` düzenlemeleri:

1. Alan ekle: `private readonly IChatCompletionProvider _provider;`
2. Kurucuya son parametre olarak `IChatCompletionProvider? provider = null` ekle.
3. Kurucunun gövdesinde, `var apiKey = configuration["Groq:ApiKey"];` ile
   başlayan bloğu ve `_groqApiKey`/`MissingApiKeySentinel` kullanımını KALDIR;
   yerine:

```csharp
        _provider = provider ?? new GroqChatCompletionProvider(configuration);
        _modelCatalog = _provider.Models;
```

   `MissingApiKeySentinel` sabitini ve `_groqApiKey` alanını da sil — bu bilgi
   artık `_provider.IsConfigured`.
4. `_httpClient.BaseAddress = new Uri("https://api.groq.com/openai/v1/");` →
   `_httpClient.BaseAddress = _provider.BaseAddress;`
5. `SendOnceAsync` içindeki `else` dalını şuna indir:

```csharp
        else
        {
            // Anahtar yoksa ağa HİÇ çıkma. Önceden "MISSING_API_KEY" gerçek bir
            // istek olarak gidiyordu: ~10 saniye bekleniyor, dış servise gereksiz
            // yük biniyor ve sonuç yine hata oluyordu — üstelik genel bir 500
            // olarak, yani arayüz onu "beklenmedik hata" sayıp sessizce yutuyordu.
            if (!_provider.IsConfigured)
                throw new AiNotConfiguredException(_provider.Name);

            _provider.Authenticate(request);
        }
```

   Gemini dalına DOKUNMA.

`ServiceCollectionExtensions.cs`: `services.AddHttpClient<GroqAIService>(...)`
kaydından ÖNCE ekle:

```csharp
        // Sağlayıcı seçimi Task 5'te yapılandırmaya bağlanıyor; şimdilik tek
        // sağlayıcı kayıtlı ve davranış bugünküyle birebir aynı.
        services.AddSingleton<IChatCompletionProvider, GroqChatCompletionProvider>();
```

`AddHttpClient<GroqAIService>` lambda'sında `client.BaseAddress` ayarlanıyorsa
bırak; kurucu zaten `_provider.BaseAddress` ile üzerine yazıyor.

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test backend/Namines.Tests
```

Beklenen: yeni testler PASS; **hiçbir mevcut test değişmeden** geçiyor (bilinen
iki Docker testi hariç). Bir test düzeltmek zorunda kaldıysan davranışı
değiştirmişsindir — dur ve raporla.

- [ ] **Step 5: Commit**

```bash
git add -A backend/
git commit -m "refactor: move provider-specific endpoint and auth behind IChatCompletionProvider"
```

---

### Task 5: DeepSeek sağlayıcısı ve yapılandırmayla seçim

**Files:**
- Create: `backend/Namines.Infrastructure/AI/DeepSeekModelCatalog.cs`
- Create: `backend/Namines.Infrastructure/AI/DeepSeekChatCompletionProvider.cs`
- Create: `backend/Namines.Infrastructure/AI/ChatCompletionProviderFactory.cs`
- Modify: `backend/Namines.API/Extensions/ServiceCollectionExtensions.cs`
- Modify: `backend/Namines.API/appsettings.json`
- Modify: `backend/Namines.API/appsettings.secrets.example.json`
- Test: `backend/Namines.Tests/Services/ChatCompletionProviderTests.cs` (ekleme)

**Interfaces:**
- Consumes: `IChatCompletionProvider`, `IModelCatalog` (Task 3, 4).
- Produces:
  - `public sealed class DeepSeekChatCompletionProvider : IChatCompletionProvider`
    — kurucu: `DeepSeekChatCompletionProvider(IConfiguration configuration)`
  - `public static class ChatCompletionProviderFactory` —
    `static IChatCompletionProvider Create(IConfiguration configuration, ILogger? logger = null)`

- [ ] **Step 1: Write the failing test**

`ChatCompletionProviderTests.cs` dosyasına ekle:

```csharp
    private static Microsoft.Extensions.Configuration.IConfiguration Config(
        params (string Key, string Value)[] pairs)
    {
        var dict = new System.Collections.Generic.Dictionary<string, string?>();
        foreach (var (key, value) in pairs) dict[key] = value;
        return new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void DeepSeek_provider_points_at_its_own_endpoint_and_models()
    {
        var provider = new DeepSeekChatCompletionProvider(
            Config(("DeepSeek:ApiKey", "sk-test")));

        Assert.Equal("deepseek", provider.Name);
        Assert.Equal(new System.Uri("https://api.deepseek.com/v1/"), provider.BaseAddress);
        Assert.True(provider.IsConfigured);
        Assert.Equal("deepseek-chat", provider.Models.UpstreamModel(NaiModel.Flash));
        Assert.Equal("deepseek-reasoner", provider.Models.UpstreamModel(NaiModel.Pro));

        using var request = new System.Net.Http.HttpRequestMessage();
        provider.Authenticate(request);
        Assert.Equal("Bearer sk-test", request.Headers.Authorization?.ToString());
    }

    [Fact]
    public void DeepSeek_catalog_clamps_to_its_own_smaller_ceiling()
    {
        // Sağlayıcıların tavanları AYNI DEĞİL; Groq'unkini DeepSeek'e uygulamak,
        // tam da bu oturumda 400 aldığımız hatanın aynısını üretirdi.
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
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test backend/Namines.Tests --filter FullyQualifiedName~ChatCompletionProviderTests
```

Beklenen: DERLEME hatası — `DeepSeekChatCompletionProvider` yok.

- [ ] **Step 3: Write the implementation**

`backend/Namines.Infrastructure/AI/DeepSeekModelCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;
using Namines.Core.Analysis;

namespace Namines.Infrastructure.AI;

/// <summary>
/// DeepSeek'teki model kimlikleri ve sınırları.
///
/// <b>DOĞRULANMADI.</b> Bu tablo sağlayıcının belgelerine göre yazıldı; bu kod
/// tabanında DeepSeek'e karşı tek bir canlı istek yapılmadı (anahtar yok).
/// Groq tarafında tam olarak bu durum üç kez yanılttı: birim testler yeşilken
/// model canlıda yoktu, sonra vardı ama kotası yetmiyordu. İlk canlı istekten
/// önce hem kimliklerin var olduğunu hem de kotanın işe yettiğini doğrula.
///
/// <c>deepseek-chat</c> ve <c>deepseek-reasoner</c> kimlikleri
/// <c>DeepSeek:Models:Flash|Standard|Pro</c> ile override edilebilir — sağlayıcı
/// bir modeli kaldırdığında yeni sürüm beklemeden geçilebilsin.
/// </summary>
public sealed class DeepSeekModelCatalog : IModelCatalog
{
    private readonly Dictionary<NaiModel, (string Id, int MaxCompletionTokens)> _models;

    public DeepSeekModelCatalog(
        string? flash = null, string? standard = null, string? pro = null)
    {
        _models = new Dictionary<NaiModel, (string, int)>
        {
            [NaiModel.Flash] = (Or(flash, "deepseek-chat"), 8_192),
            [NaiModel.Standard] = (Or(standard, "deepseek-chat"), 8_192),
            [NaiModel.Pro] = (Or(pro, "deepseek-reasoner"), 8_192),
        };
    }

    private static string Or(string? configured, string fallback)
        => string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();

    public string UpstreamModel(NaiModel model) => _models[model].Id;

    public int MaxCompletionTokens(NaiModel model) => _models[model].MaxCompletionTokens;

    public int ClampToModelLimit(string? upstreamModel, int requestedMaxTokens)
    {
        if (string.IsNullOrWhiteSpace(upstreamModel)) return requestedMaxTokens;

        foreach (var entry in _models.Values)
        {
            if (string.Equals(entry.Id, upstreamModel, StringComparison.OrdinalIgnoreCase))
                return Math.Min(requestedMaxTokens, entry.MaxCompletionTokens);
        }

        return requestedMaxTokens;
    }
}
```

`backend/Namines.Infrastructure/AI/DeepSeekChatCompletionProvider.cs`:

```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Namines.Core.Analysis;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.AI;

/// <summary>
/// İkinci sağlayıcı. OpenAI uyumlu <c>chat/completions</c> kullandığı için
/// gövde şekli Groq'unkiyle aynı — değişen yalnızca adres, anahtar ve modeller.
///
/// <b>CANLI DOĞRULANMADI:</b> bkz. <see cref="DeepSeekModelCatalog"/>.
/// </summary>
public sealed class DeepSeekChatCompletionProvider : IChatCompletionProvider
{
    private readonly string? _apiKey;

    public DeepSeekChatCompletionProvider(IConfiguration configuration)
    {
        var key = configuration["DeepSeek:ApiKey"];
        _apiKey = string.IsNullOrWhiteSpace(key) ? null : key;

        Models = new DeepSeekModelCatalog(
            configuration["DeepSeek:Models:Flash"],
            configuration["DeepSeek:Models:Standard"],
            configuration["DeepSeek:Models:Pro"]);
    }

    public string Name => "deepseek";

    public Uri BaseAddress => new("https://api.deepseek.com/v1/");

    public bool IsConfigured => _apiKey is not null;

    public IModelCatalog Models { get; }

    public void Authenticate(HttpRequestMessage request)
    {
        if (_apiKey is null) throw new AiNotConfiguredException("DeepSeek");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }
}
```

`backend/Namines.Infrastructure/AI/ChatCompletionProviderFactory.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.AI;

/// <summary>
/// <c>Ai:Provider</c> yapılandırmasına göre sağlayıcıyı seçer.
/// Varsayılan Groq — bugün çalışan yol odur.
/// </summary>
public static class ChatCompletionProviderFactory
{
    public static IChatCompletionProvider Create(
        IConfiguration configuration, ILogger? logger = null)
    {
        var configured = configuration["Ai:Provider"]?.Trim();

        if (string.IsNullOrWhiteSpace(configured))
            return new GroqChatCompletionProvider(configuration);

        switch (configured.ToLowerInvariant())
        {
            case "groq":
                return new GroqChatCompletionProvider(configuration);

            case "deepseek":
                return new DeepSeekChatCompletionProvider(configuration);

            default:
                // Tanınmayan ad varsayılana düşer, uygulamayı DÜŞÜRMEZ: bir
                // yazım hatası yüzünden hiç açılmamak, yapılandırma hatasının
                // bedelini orantısız kılardı. Ama sessizce de geçilmez —
                // yoksa yanlış sağlayıcıyla çalıştığı hiç fark edilmez.
                logger?.LogWarning(
                    "Unknown Ai:Provider value '{Configured}'; falling back to groq.", configured);
                return new GroqChatCompletionProvider(configuration);
        }
    }
}
```

`ServiceCollectionExtensions.cs` — Task 4'te eklenen tek satırlık kaydı
fabrikayla değiştir:

```csharp
        services.AddSingleton<IChatCompletionProvider>(sp =>
            ChatCompletionProviderFactory.Create(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetService<ILoggerFactory>()?.CreateLogger(nameof(ChatCompletionProviderFactory))));
```

`appsettings.json` — Task 2'de eklenen `"Ai"` bloğuna `"Provider"` ekle:

```json
  "Ai": {
    "Provider": "groq",
    "RateLimitRetry": {
      "MaxTotalWaitSeconds": 600,
      "MaxSingleWaitSeconds": 60
    }
  },
```

`appsettings.secrets.example.json` — `"Groq"` bloğunun yanına ekle:

```json
  "DeepSeek": {
    "ApiKey": ""
  },
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test backend/Namines.Tests
```

Beklenen: tüm yeni testler PASS; mevcut testler değişmeden geçiyor (bilinen iki
Docker testi hariç).

- [ ] **Step 5: Uygulamanın gerçekten açıldığını doğrula**

DI kaydı yanlışsa bunu hiçbir birim test yakalamaz — bu kod tabanında tam olarak
bu yaşandı (`IAiQuotaReserver` kayıtsız kalmıştı ve uygulama açılmıyordu).

```bash
dotnet build backend/Namines.API
```

Beklenen: derleme başarılı. **Sunucu BAŞLATMA** — bu oturumda başka süreçler
çalışıyor olabilir.

- [ ] **Step 6: Commit**

```bash
git add -A backend/
git commit -m "feat: add DeepSeek as a configuration-selectable chat provider"
```

---

### Task 6: Dokümantasyon ve dürüst kapanış

**Files:**
- Modify: `docs/superpowers/specs/2026-09-17-provider-abstraction-design.md`
- Modify: `README.md` (yapılandırma bölümü varsa; yoksa bu adımı atla ve
  raporda belirt)

- [ ] **Step 1: Spec'e "Uygulandı" bölümü ekle**

Spec'in sonuna ekle:

```markdown
---

## Uygulama durumu

**Faz 1 — 429 yeniden deneme: uygulandı ve birim testlerle doğrulandı.**
Canlı hız sınırına karşı doğrulanmadı; test sahte bir aktarım katmanı kullanıyor.

**Faz 2 — sağlayıcı soyutlaması: uygulandı. DeepSeek CANLI DOĞRULANMADI.**
Hesapta DeepSeek anahtarı yok. Bu oturumda, tamamı yeşil bir birim test
paketinin arkasında beş ayrı canlı hata saklandı: ölü model kimliği, yerine
yazılan ikinci ölü kimlik, çıktı token kotası işe yetmeyen model, model sınırını
aşan `max_tokens` (400), ve istenen `max_tokens` üzerinden sayılan dakikalık
kota. DeepSeek yolundaki model kimlikleri ve 8.192'lik tavan belgelerden
alındı — ilk canlı istekten önce ikisi de doğrulanmalı.

## Yapılandırma

| Anahtar | Varsayılan | Ne işe yarıyor |
|---|---|---|
| `Ai:Provider` | `groq` | `groq` veya `deepseek`. Tanınmayan değer Groq'a düşer ve loglanır. |
| `Ai:RateLimitRetry:MaxTotalWaitSeconds` | `600` | Bir istek boyunca hız sınırı için beklenebilecek toplam süre. `0` yeniden denemeyi kapatır. |
| `Ai:RateLimitRetry:MaxSingleWaitSeconds` | `60` | Tek bir beklemenin tavanı; sağlayıcı süre bildirmediğinde kullanılan süre de budur. |
| `DeepSeek:ApiKey` | — | DeepSeek seçiliyse zorunlu. |
| `DeepSeek:Models:Flash|Standard|Pro` | katalog | Sağlayıcı bir modeli kaldırırsa sürüm beklemeden geçmek için. |

**Daha yüksek bir sağlayıcı katmanına geçildiğinde:** bu beklemelerin HİÇ
oluşmaması hedefleniyor. `MaxTotalWaitSeconds` sıfırlanmamalı ama log'da
"rate limited; waiting" satırı görülüyorsa bu, katmanın hâlâ dar olduğunun
işareti sayılmalı — yeniden deneme bir emniyet ağı, çalışma biçimi değil.
```

- [ ] **Step 2: Commit**

```bash
git add docs/
git commit -m "docs: record the provider configuration surface and what is not live-verified"
```

---

## Self-Review

**Spec kapsamı:** Faz 1 → Task 1-2. Faz 2 → Task 3-5 (katalog ayrımı, seam,
DeepSeek + seçim). Test stratejisi → her görevin kendi testleri; regresyon
kuralı Global Constraints'te ve Task 4 Step 4'te. Kapsam dışı maddelerin hiçbiri
için görev yok — doğru.

**Placeholder taraması:** her kod adımında gerçek kod var; "uygun hata yönetimi
ekle" türü yönerge yok. Task 6'daki README adımı koşullu ve koşulu açıkça
yazılmış.

**Tip tutarlılığı:** `AiRetryPolicy.TryNextDelay` Task 1'de tanımlandı, Task
2'de aynı imzayla kullanıldı. `IModelCatalog`'un üç metodu Task 3'te tanımlandı,
Task 5'te aynı imzalarla implemente edildi. `IChatCompletionProvider`'ın beş
üyesi Task 4'te tanımlandı, Task 5'te eksiksiz karşılandı.
`GroqAIService` kurucusunun isteğe bağlı parametre SIRASI üç görevde de aynı:
`logger`, `delay`, `provider`.
