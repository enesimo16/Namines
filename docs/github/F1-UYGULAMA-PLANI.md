# F1 — Public Repo Tarama → Şema / Drift — Uygulama Planı

> **Ajan çalışanlar için:** `superpowers:subagent-driven-development` veya
> `superpowers:executing-plans` ile görev görev uygula. Adımlar checkbox'lı.

**Hedef:** Kullanıcı bir GitHub deposu linki yapıştırsın; Namines depoyu
tarasın, içindeki model/migration tanımlarından şemayı çıkarsın ve canvas'a
koysun — ya da canvas'takiyle karşılaştırıp farkı göstersin.

**Mimari:** Yeni ayrıştırıcı YAZILMIYOR. Var olan zincire (`CodeSchemaExtractor`
→ `SchemaUuidAligner` → `SchemaImpactAnalyzer`) yalnızca yeni bir **dosya
kaynağı** ekleniyor: GitHub'ın ağaç (tree) API'si. Aday dosya seçimi saf ve
ağdan bağımsız bir sınıfta (`RepositoryCandidateSelector`), böylece taramanın
asıl kararları HTTP olmadan test edilebiliyor.

**Teknoloji:** .NET 8, xUnit + sahte `HttpMessageHandler`; Next.js 16 + React 19.

**Spec:** [`01-DEPO-TARAMA.md`](01-DEPO-TARAMA.md), [`07-FAZ-PLANI.md`](07-FAZ-PLANI.md) F1.

## Kapsam kararları

1. **Yalnızca public depo.** Private depo net bir mesajla F2'ye yönlendirilir;
   yarım bir kimlik yolu uydurulmaz.
2. **GitHub kaynağı katalogda `import` grubunda, `watch` YETENEĞİ OLMADAN.**
   Bugün sürekli bir ilişki kurmuyoruz; "Connect" grubuna koymak kullanıcıya
   tutamayacağımız bir söz vermek olurdu. F3'te drift geldiğinde `connect`'e
   terfi eder (katalog testindeki `Watch ⟹ Connect` değişmezi bunu zaten
   zorluyor).
3. **Canvas entegrasyonu bu fazda `/new` ekranından.** Menüden seçim → tarama
   → şema canvas'a yüklenip `/canvas`'a gidilir; üretim akışının yaptığının
   aynısı. Canvas içindeki `CodeImportPanel`'e ikinci bir giriş eklemek ayrı
   ve küçük bir iş, F1'i bloke etmemeli.

## Global kısıtlar

- Yorumlar sadece NEDEN için, Türkçe (AGENTS.md).
- **Depo klonlanmaz**, yalnızca API'den okunur; hiçbir SQL çalıştırılmaz,
  hiçbir dosya yazılmaz.
- **Host beyaz listesi:** yalnızca `github.com`. `DbIntrospectController`'ın
  SSRF modeliyle aynı çizgi.
- Sınırlar: en çok **200 dosya / 2 MB** (mevcut `CodeSchemaExtractor` sabitleri
  yeniden kullanılır, ikinci bir sayı tanımlanmaz).
- **Atlanan her şey nedeniyle bildirilir**; kısmi sonuç sessizce tam sonuç gibi
  sunulmaz.
- Frontend işi `FRONTEND.md`'ye tabi; `npm run check:design` ve `lint` yeşil.
- Commit'i kullanıcı onaylamadan atma.

---

### Görev 1: Depo URL'i ve aday dosya seçimi (saf, ağsız)

> ✅ **Bitmiştir.** Aday dosya seçimi yazıldı; `Github/RepositoryCandidateSelectorTests.cs` kapsıyor.

**Dosyalar:**
- Oluştur: `backend/Namines.Core/Github/GithubRepositoryUrl.cs`
- Oluştur: `backend/Namines.Core/Github/RepositoryCandidateSelector.cs`
- Test: `backend/Namines.Tests/Github/RepositoryCandidateSelectorTests.cs`

**Arayüzler:**
- Kullanır: `SkippedItem` (`Namines.Core.Analysis`).
- Üretir:
  - `GithubRepositoryUrl.TryParse(string url, out GithubRepository repo)` → `bool`
  - `RepositoryCandidateSelector.Select(IReadOnlyList<string> paths, int maxFiles)`
    → `CandidateSelection(IReadOnlyList<string> Paths, IReadOnlyList<SkippedItem> Skipped)`

- [ ] **Adım 1: Başarısız testi yaz**

```csharp
using Namines.Core.Analysis;
using Namines.Core.Github;

namespace Namines.Tests.Github;

/// <summary>github/01-DEPO-TARAMA.md — aday seçimi ve sınırlar.</summary>
public class RepositoryCandidateSelectorTests
{
    [Theory]
    [InlineData("https://github.com/acme/shop", "acme", "shop")]
    [InlineData("https://github.com/acme/shop.git", "acme", "shop")]
    [InlineData("https://github.com/acme/shop/tree/main/src", "acme", "shop")]
    [InlineData("github.com/acme/shop", "acme", "shop")]
    public void Repository_urls_in_the_shapes_people_paste_are_understood(string url, string owner, string name)
    {
        Assert.True(GithubRepositoryUrl.TryParse(url, out var repo));
        Assert.Equal(owner, repo!.Owner);
        Assert.Equal(name, repo.Name);
    }

    [Theory]
    [InlineData("https://gitlab.com/acme/shop")]
    [InlineData("https://github.com.evil.example/acme/shop")]
    [InlineData("http://localhost/acme/shop")]
    [InlineData("https://github.com/acme")]
    [InlineData("not a url")]
    public void Anything_that_is_not_a_github_repository_is_refused(string url)
    {
        // Host beyaz listesi bir SSRF sınırı: sunucu yalnızca github.com'a
        // çıkar. "github.com.evil.example" tam da naif bir `Contains`
        // kontrolünün kaçıracağı biçim.
        Assert.False(GithubRepositoryUrl.TryParse(url, out _));
    }

    [Fact]
    public void Schema_files_are_preferred_over_everything_else()
    {
        var paths = new[] { "README.md", "src/app.ts", "prisma/schema.prisma" };

        var selection = RepositoryCandidateSelector.Select(paths, maxFiles: 200);

        Assert.Equal("prisma/schema.prisma", selection.Paths[0]);
    }

    [Fact]
    public void Namines_own_schema_file_wins_when_present()
    {
        var paths = new[] { "prisma/schema.prisma", ".namines/schema.nsl" };

        var selection = RepositoryCandidateSelector.Select(paths, maxFiles: 200);

        Assert.Equal(".namines/schema.nsl", selection.Paths[0]);
    }

    [Fact]
    public void Build_output_and_dependencies_never_become_candidates()
    {
        // Bu filtre olmadan bir Next.js deposunda 200 dosyalık bütçe ilk 200
        // node_modules dosyasıyla dolar ve gerçek şema dosyasına hiç sıra
        // gelmez.
        var paths = new[]
        {
            "node_modules/pkg/schema.prisma",
            ".next/cache/x.sql",
            "backend/bin/Debug/Model.cs",
            "vendor/lib/schema.sql",
            "prisma/schema.prisma",
        };

        var selection = RepositoryCandidateSelector.Select(paths, maxFiles: 200);

        Assert.Equal(new[] { "prisma/schema.prisma" }, selection.Paths);
        Assert.Equal(4, selection.Skipped.Count);
        Assert.All(selection.Skipped, s => Assert.Contains("Build output", s.Reason));
    }

    [Fact]
    public void Files_that_no_parser_understands_are_not_candidates()
    {
        var selection = RepositoryCandidateSelector.Select(
            new[] { "README.md", "infra/main.tf", "src/app.ts" }, maxFiles: 200);

        Assert.Empty(selection.Paths);
    }

    [Fact]
    public void Over_the_limit_files_are_reported_not_dropped_silently()
    {
        var paths = Enumerable.Range(0, 5).Select(i => $"db/migrations/{i:D3}_init.sql").ToArray();

        var selection = RepositoryCandidateSelector.Select(paths, maxFiles: 2);

        Assert.Equal(2, selection.Paths.Count);
        Assert.Equal(3, selection.Skipped.Count);
        Assert.All(selection.Skipped, s => Assert.Contains("file budget", s.Reason));
    }
}
```

- [ ] **Adım 2: Testi çalıştır, DERLENMEDİĞİNİ gör**

Çalıştır: `dotnet test backend/Namines.Tests --filter RepositoryCandidateSelectorTests`
Beklenen: `GithubRepositoryUrl` / `RepositoryCandidateSelector` yok → derleme hatası.
**Not:** "0 test çalıştı" bir kırmızı değildir; en az bir testin gerçekten
çalışıp başarısız olduğunu ya da derlemenin adı geçen tip yüzünden düştüğünü gör.

- [ ] **Adım 3: `GithubRepositoryUrl`'i yaz**

```csharp
using System;

namespace Namines.Core.Github;

/// <summary>
/// Kullanıcının yapıştırdığı adresten depo kimliğini çıkarır.
///
/// <b>Host beyaz listesi bir güvenlik sınırı</b>, kolaylık değil: bu adres
/// sunucunun dışarı çıkacağı yeri belirliyor. <c>Contains("github.com")</c>
/// gibi bir kontrol <c>github.com.evil.example</c>'ı kabul ederdi, o yüzden
/// host TAM eşleşmeyle karşılaştırılıyor.
/// </summary>
public static class GithubRepositoryUrl
{
    public static bool TryParse(string? url, out GithubRepository? repository)
    {
        repository = null;
        if (string.IsNullOrWhiteSpace(url)) return false;

        var text = url.Trim();
        // Şema yazılmadan yapıştırmak yaygın; eklemek kullanıcıyı zorlamamak için.
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;

        var host = uri.Host.ToLowerInvariant();
        if (host != "github.com" && host != "www.github.com") return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return false;

        var name = segments[1];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        if (segments[0].Length == 0 || name.Length == 0) return false;

        repository = new GithubRepository(segments[0], name);
        return true;
    }
}
```

- [ ] **Adım 4: `RepositoryCandidateSelector`'ı yaz**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Analysis;

namespace Namines.Core.Github;

/// <param name="Paths">İçeriği çekilecek dosyalar, önem sırasıyla.</param>
/// <param name="Skipped">Aday olmayan her şey ve NEDENİ.</param>
public sealed record CandidateSelection(
    IReadOnlyList<string> Paths,
    IReadOnlyList<SkippedItem> Skipped);

/// <summary>
/// github/01-DEPO-TARAMA.md — depo ağacından hangi dosyaların okunacağına
/// karar verir.
///
/// <b>Ağdan bağımsız ve saf.</b> Taramanın asıl kararları (öncelik, gürültü
/// filtresi, bütçe) buradadır; HTTP olmadan test edilebilmeleri, bir depoyu
/// yanlış okuduğumuzda sebebin ağda mı kararda mı olduğunu ayırt edebilmemizi
/// sağlıyor.
/// </summary>
public static class RepositoryCandidateSelector
{
    /// <summary>
    /// Aday bile sayılmayan yollar. <b>Bütçeden ÖNCE uygulanır:</b> sonra
    /// uygulansaydı 200 dosyalık bütçe bir Next.js deposunda ilk 200
    /// <c>node_modules</c> dosyasıyla dolar ve gerçek şema dosyasına hiç sıra
    /// gelmezdi.
    /// </summary>
    private static readonly string[] IgnoredSegments =
    {
        "node_modules/", ".next/", "/bin/", "/obj/", "vendor/", "dist/", "build/", ".git/",
    };

    /// <summary>
    /// Öncelik sırası. Küçük sayı önce alınır — bütçe dolarsa kaybedilen
    /// rastgele bir <c>.sql</c> olur, <c>schema.prisma</c> değil.
    /// </summary>
    private static int? Priority(string path)
    {
        var lower = path.ToLowerInvariant();

        // Namines'in kendi IR'ı: varsa tahmin bile gerekmiyor.
        if (lower.EndsWith("/schema.nsl") || lower == "schema.nsl" ||
            lower.EndsWith("/ir.json") || lower == "ir.json") return 0;

        if (lower.EndsWith(".prisma")) return 1;
        if (lower.Contains("migrations/") && lower.EndsWith(".sql")) return 2;
        if (lower.EndsWith("dbcontext.cs")) return 3;
        if (lower.EndsWith(".cs") &&
            (lower.Contains("/entities/") || lower.Contains("/models/") || lower.Contains("/domain/"))) return 4;
        if (lower.EndsWith(".sql")) return 5;

        return null;
    }

    public static CandidateSelection Select(IReadOnlyList<string> paths, int maxFiles)
    {
        var skipped = new List<SkippedItem>();
        var ranked = new List<(string Path, int Priority)>();

        foreach (var path in paths)
        {
            var probe = "/" + path.Replace('\\', '/');

            if (IgnoredSegments.Any(s => probe.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                skipped.Add(new SkippedItem(path, "Build output or dependency directory."));
                continue;
            }

            var priority = Priority(path);
            if (priority is null) continue;   // sessizce atlanır: README bir "atlanan şema" değil

            ranked.Add((path, priority.Value));
        }

        var ordered = ranked
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Path, StringComparer.Ordinal)
            .ToList();

        var kept = ordered.Take(maxFiles).Select(r => r.Path).ToList();

        foreach (var (path, _) in ordered.Skip(maxFiles))
            skipped.Add(new SkippedItem(path, $"Exceeded the {maxFiles} file budget for one scan."));

        return new CandidateSelection(kept, skipped);
    }
}
```

- [ ] **Adım 5: Testleri çalıştır**

Çalıştır: `dotnet test backend/Namines.Tests --filter RepositoryCandidateSelectorTests`
Beklenen: 11 test PASS (Theory satırları dahil).

- [ ] **Adım 6: Commit (onay sonrası)**

```bash
git add backend/Namines.Core/Github/GithubRepositoryUrl.cs backend/Namines.Core/Github/RepositoryCandidateSelector.cs backend/Namines.Tests/Github/RepositoryCandidateSelectorTests.cs
git commit -m "feat: pick schema files out of a repository tree"
```

---

### Görev 2: İstemcinin OKUMA yüzeyi (public depo dahil)

> ✅ **Bitmiştir.** İstemcinin okuma yüzeyi yazıldı; `Github/GithubClientReadTests.cs` kapsıyor.

**Dosyalar:**
- Değiştir: `backend/Namines.Core/Github/IGithubClient.cs`
- Değiştir: `backend/Namines.Infrastructure/Services/GithubClient.cs`
- Test: `backend/Namines.Tests/Github/GithubClientReadTests.cs`

**Arayüzler:**
- Üretir:
  - `Task<string?> GetDefaultBranchAsync(GithubRepository repo, long? installationId, CancellationToken ct)`
  - `Task<RepositoryTree> GetRepositoryTreeAsync(GithubRepository repo, string reference, long? installationId, CancellationToken ct)`
    — `RepositoryTree(IReadOnlyList<string> Paths, bool Truncated)`
  - `GetFileContentAsync`'in mevcut imzası `long installationId` → `long? installationId`.

**Neden imza değişiyor:** aynı okuma yolunun hem kurulumla (private) hem
kurulumsuz (public) çalışması gerekiyor. İki ayrı metot, aynı işi yapan iki
kod yolu demekti; `null` = "anonim çık" tek bir yerde karara bağlanıyor.
Mevcut çağrı yerleri `long` geçiyor ve örtük dönüşümle derlenmeye devam ediyor.

- [ ] **Adım 1: Başarısız testi yaz**

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Github;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Github;

/// <summary>github/01-DEPO-TARAMA.md — public depo okuma yolu.</summary>
public class GithubClientReadTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Respond!(request));
        }

        public static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
    }

    private static GithubClient Client(FakeHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection().Build(),   // App kimliği YOK
        NullLogger<GithubClient>.Instance,
        new GithubInstallationTokenCache());

    [Fact]
    public async Task A_public_repository_is_read_without_any_credentials()
    {
        // App yapılandırılmamışken bile public depo okunabilmeli — F1'in
        // GitHub App'i beklememesinin tek sebebi bu.
        var handler = new FakeHandler
        {
            Respond = _ => FakeHandler.Json(HttpStatusCode.OK, new
            {
                tree = new[]
                {
                    new { path = "prisma/schema.prisma", type = "blob" },
                    new { path = "src", type = "tree" },
                },
                truncated = false,
            }),
        };

        var tree = await Client(handler).GetRepositoryTreeAsync(
            new GithubRepository("acme", "shop"), "main", installationId: null);

        Assert.Equal(new[] { "prisma/schema.prisma" }, tree.Paths);   // sadece blob'lar
        Assert.False(tree.Truncated);
        Assert.Null(handler.Requests[0].Headers.Authorization);
        Assert.Single(handler.Requests);                              // token istenmedi
    }

    [Fact]
    public async Task A_truncated_tree_is_reported_not_hidden()
    {
        // Dev depolarda GitHub ağacı kesiyor. Bunu yutmak, "şemanı çıkardım"
        // derken deponun görülmemiş bir kısmını gizlemek olurdu.
        var handler = new FakeHandler
        {
            Respond = _ => FakeHandler.Json(HttpStatusCode.OK, new
            {
                tree = new[] { new { path = "a.sql", type = "blob" } },
                truncated = true,
            }),
        };

        var tree = await Client(handler).GetRepositoryTreeAsync(
            new GithubRepository("acme", "shop"), "main", installationId: null);

        Assert.True(tree.Truncated);
    }

    [Fact]
    public async Task The_default_branch_is_read_from_the_repository()
    {
        var handler = new FakeHandler
        {
            Respond = _ => FakeHandler.Json(HttpStatusCode.OK, new { default_branch = "develop" }),
        };

        var branch = await Client(handler).GetDefaultBranchAsync(
            new GithubRepository("acme", "shop"), installationId: null);

        Assert.Equal("develop", branch);
    }

    [Fact]
    public async Task A_private_repository_without_credentials_is_refused_clearly()
    {
        // GitHub public olmayan depoya 404 döner (varlığını sızdırmamak için).
        // Kullanıcıya "bulunamadı" demek yanıltıcı olurdu: depo VAR, biz
        // göremiyoruz. Ayrımı çağıran katman yapabilsin diye tipli hata.
        var handler = new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };

        await Assert.ThrowsAsync<GithubRepositoryUnavailableException>(() =>
            Client(handler).GetRepositoryTreeAsync(
                new GithubRepository("acme", "secret"), "main", installationId: null));
    }

    [Fact]
    public async Task Rate_limiting_is_reported_as_itself()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("x-ratelimit-remaining", "0");
        var handler = new FakeHandler { Respond = _ => response };

        await Assert.ThrowsAsync<GithubRateLimitedException>(() =>
            Client(handler).GetRepositoryTreeAsync(
                new GithubRepository("acme", "shop"), "main", installationId: null));
    }
}
```

- [ ] **Adım 2: Testi çalıştır, düştüğünü gör**

Çalıştır: `dotnet test backend/Namines.Tests --filter GithubClientReadTests`
Beklenen: derleme hatası (yeni metotlar ve iki istisna tipi yok).

- [ ] **Adım 3: Arayüzü genişlet**

`IGithubClient.cs` içine:

```csharp
/// <param name="Paths">Ağaçtaki dosya (blob) yolları.</param>
/// <param name="Truncated">
/// GitHub ağacı kesti mi. <b>Taşınması şart:</b> kesilmiş bir ağaçta
/// "deponda şema dosyası yok" demek yanlış olur — bakmadığımız bir kısım var.
/// </param>
public sealed record RepositoryTree(IReadOnlyList<string> Paths, bool Truncated);

/// <summary>Depo yok ya da kimliğimizle görünmüyor (GitHub ikisine de 404 der).</summary>
public sealed class GithubRepositoryUnavailableException : Exception
{
    public GithubRepositoryUnavailableException(string message) : base(message) { }
}

/// <summary>Anonim okumanın saatlik sınırı doldu.</summary>
public sealed class GithubRateLimitedException : Exception
{
    public GithubRateLimitedException(string message) : base(message) { }
}
```

ve `IGithubClient` içine üç üye (yukarıdaki **Arayüzler** bloğundaki imzalar).
`GetFileContentAsync`'in `long installationId` parametresi `long? installationId`
yapılır.

- [ ] **Adım 4: İstemciyi yaz**

`GithubClient.cs` içinde:

```csharp
public async Task<string?> GetDefaultBranchAsync(
    GithubRepository repository, long? installationId, CancellationToken cancellationToken = default)
{
    using var request = await RequestAsync(HttpMethod.Get,
        $"/repos/{repository.Owner}/{repository.Name}", installationId, cancellationToken);

    using var response = await _http.SendAsync(request, cancellationToken);
    await EnsureReadSuccessAsync(response, repository, cancellationToken);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    return json.TryGetProperty("default_branch", out var b) ? b.GetString() : null;
}

public async Task<RepositoryTree> GetRepositoryTreeAsync(
    GithubRepository repository, string reference, long? installationId,
    CancellationToken cancellationToken = default)
{
    // recursive=1: ağacın tamamı TEK çağrıda. Dizin dizin gezmek büyük bir
    // depoda yüzlerce istek ve anonim kotanın (saatte 60) anında tükenmesi
    // demek olurdu.
    using var request = await RequestAsync(HttpMethod.Get,
        $"/repos/{repository.Owner}/{repository.Name}/git/trees/{Uri.EscapeDataString(reference)}?recursive=1",
        installationId, cancellationToken);

    using var response = await _http.SendAsync(request, cancellationToken);
    await EnsureReadSuccessAsync(response, repository, cancellationToken);

    var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

    var paths = new List<string>();
    if (json.TryGetProperty("tree", out var tree) && tree.ValueKind == JsonValueKind.Array)
    {
        foreach (var entry in tree.EnumerateArray())
        {
            // Yalnızca blob: "tree" girdileri dizin, içeriği okunamaz.
            if (entry.TryGetProperty("type", out var type) && type.GetString() == "blob" &&
                entry.TryGetProperty("path", out var path) && path.GetString() is { } p)
                paths.Add(p);
        }
    }

    var truncated = json.TryGetProperty("truncated", out var t) && t.ValueKind == JsonValueKind.True;
    return new RepositoryTree(paths, truncated);
}

/// <summary>
/// İsteği hazırlar; <paramref name="installationId"/> null ise ANONİM çıkar.
///
/// Public depo okuması App kimliği gerektirmiyor ve gerektirmemeli: F1'in
/// GitHub App'i beklememesinin tek sebebi bu yol.
/// </summary>
private async Task<HttpRequestMessage> RequestAsync(
    HttpMethod method, string path, long? installationId, CancellationToken ct)
{
    var request = new HttpRequestMessage(method, ApiBase + path);
    if (installationId is { } id)
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await InstallationTokenAsync(id, ct));
    return request;
}

/// <summary>
/// Okuma yolunun hata ayrımı.
///
/// Yazma yolundan ayrı, çünkü okumada iki durum kullanıcıya AYRI AYRI
/// söylenmeli: 404 "depo yok ya da private" (GitHub ikisini ayırt ettirmez,
/// biz de uydurmayız), 403+kalan kota 0 ise "saatlik sınır doldu, biraz sonra
/// ya da bir kurulumla dene". İkisini tek bir "GitHub hata verdi"ye indirgemek,
/// kullanıcının ne yapacağını bilememesi demek.
/// </summary>
private async Task EnsureReadSuccessAsync(
    HttpResponseMessage response, GithubRepository repository, CancellationToken ct)
{
    if (response.IsSuccessStatusCode) return;

    if (response.StatusCode == HttpStatusCode.NotFound)
        throw new GithubRepositoryUnavailableException(
            $"{repository} could not be read. It does not exist, or it is private — connecting a GitHub App would be needed for a private repository.");

    if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests &&
        response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) &&
        remaining.FirstOrDefault() == "0")
        throw new GithubRateLimitedException(
            "GitHub's hourly limit for anonymous requests is used up. Try again later, or connect a GitHub App.");

    await EnsureSuccessAsync(response, ct);
}
```

- [ ] **Adım 5: Testleri çalıştır**

Çalıştır: `dotnet test backend/Namines.Tests --filter "GithubClientReadTests|GithubClientTests|BotTests|GithubBotServiceTests"`
Beklenen: yeni 5 test PASS **ve** mevcut bot testleri hâlâ yeşil (imza değişikliği
onları kırmamalı).

- [ ] **Adım 6: Commit (onay sonrası)**

```bash
git add backend/Namines.Core/Github/IGithubClient.cs backend/Namines.Infrastructure/Services/GithubClient.cs backend/Namines.Tests/Github/GithubClientReadTests.cs
git commit -m "feat: read public repository trees without credentials"
```

---

### Görev 3: `RepositoryScanner` — zinciri birleştir

> ✅ **Bitmiştir.** `Infrastructure/Services/RepositoryScanner.cs` yazıldı; `Github/RepositoryScannerTests.cs` kapsıyor.

**Dosyalar:**
- Oluştur: `backend/Namines.Core/Interfaces/IRepositoryScanner.cs`
- Oluştur: `backend/Namines.Infrastructure/Services/RepositoryScanner.cs`
- Test: `backend/Namines.Tests/Github/RepositoryScannerTests.cs`

**Arayüzler:**
- Kullanır: Görev 1 ve 2'nin hepsi, `CodeSchemaExtractor.Extract`,
  `CodeSchemaExtractor.MaxFiles`.
- Üretir: `IRepositoryScanner.ScanAsync(GithubRepository repo, string? branch, long? installationId, CancellationToken ct)`
  → `RepositoryScanResult(DatabaseSchema Schema, string Format, string Branch, IReadOnlyList<string> ParsedFiles, IReadOnlyList<SkippedItem> Skipped, bool TreeTruncated)`

- [ ] **Adım 1: Başarısız testi yaz**

```csharp
using Namines.Core.Analysis;
using Namines.Core.Github;
using Namines.Core.Models;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Github;

public class RepositoryScannerTests
{
    /// <summary>Ağı taklit eden, çağrıları sayan sahte istemci.</summary>
    private sealed class FakeGithub : IGithubClient
    {
        public Dictionary<string, string> Files { get; init; } = new();
        public List<string> Paths { get; init; } = new();
        public bool Truncated { get; init; }
        public string DefaultBranch { get; init; } = "main";
        public List<string> Fetched { get; } = new();

        public bool IsConfigured => false;

        public Task<string?> GetDefaultBranchAsync(GithubRepository r, long? i, CancellationToken ct = default)
            => Task.FromResult<string?>(DefaultBranch);

        public Task<RepositoryTree> GetRepositoryTreeAsync(GithubRepository r, string reference, long? i, CancellationToken ct = default)
            => Task.FromResult(new RepositoryTree(Paths, Truncated));

        public Task<string?> GetFileContentAsync(GithubRepository r, long? i, string path, string reference, CancellationToken ct = default)
        {
            Fetched.Add(path);
            return Task.FromResult(Files.TryGetValue(path, out var c) ? c : null);
        }

        public Task PostCommentAsync(GithubRepository r, long i, int n, string b, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task CreateCheckRunAsync(GithubRepository r, long i, string s, string n, string c, string t, string su, string b, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_prisma_repository_becomes_a_schema()
    {
        var github = new FakeGithub
        {
            Paths = { "README.md", "prisma/schema.prisma" },
            Files = { ["prisma/schema.prisma"] = "model User {\n  id Int @id\n}" },
        };

        var result = await new RepositoryScanner(github).ScanAsync(
            new GithubRepository("acme", "shop"), branch: null, installationId: null);

        Assert.Equal("prisma", result.Format);
        Assert.Equal("main", result.Branch);
        Assert.Contains(result.Schema.Tables, t => t.Name == "User");
        Assert.Equal(new[] { "prisma/schema.prisma" }, github.Fetched);   // README hiç çekilmedi
    }

    [Fact]
    public async Task The_requested_branch_wins_over_the_default()
    {
        var github = new FakeGithub
        {
            Paths = { "prisma/schema.prisma" },
            Files = { ["prisma/schema.prisma"] = "model User {\n  id Int @id\n}" },
        };

        var result = await new RepositoryScanner(github).ScanAsync(
            new GithubRepository("acme", "shop"), branch: "feature/x", installationId: null);

        Assert.Equal("feature/x", result.Branch);
    }

    [Fact]
    public async Task A_truncated_tree_travels_all_the_way_to_the_caller()
    {
        var github = new FakeGithub
        {
            Truncated = true,
            Paths = { "prisma/schema.prisma" },
            Files = { ["prisma/schema.prisma"] = "model User {\n  id Int @id\n}" },
        };

        var result = await new RepositoryScanner(github).ScanAsync(
            new GithubRepository("acme", "shop"), branch: null, installationId: null);

        Assert.True(result.TreeTruncated);
    }

    [Fact]
    public async Task A_repository_with_nothing_parseable_says_so_instead_of_guessing()
    {
        var github = new FakeGithub { Paths = { "README.md", "src/app.ts" } };

        await Assert.ThrowsAsync<CodeSchemaExtractor.UnknownFormatException>(() =>
            new RepositoryScanner(github).ScanAsync(
                new GithubRepository("acme", "shop"), branch: null, installationId: null));
    }

    [Fact]
    public async Task A_file_that_vanished_between_listing_and_reading_is_reported()
    {
        // Ağaç ile içerik okuması arasında geçen sürede dal ilerleyebilir.
        var github = new FakeGithub
        {
            Paths = { "prisma/schema.prisma", "db/migrations/001.sql" },
            Files = { ["prisma/schema.prisma"] = "model User {\n  id Int @id\n}" },
        };

        var result = await new RepositoryScanner(github).ScanAsync(
            new GithubRepository("acme", "shop"), branch: null, installationId: null);

        Assert.Contains(result.Skipped, s => s.Name == "db/migrations/001.sql");
    }
}
```

- [ ] **Adım 2: Testi çalıştır, düştüğünü gör**

Çalıştır: `dotnet test backend/Namines.Tests --filter RepositoryScannerTests`
Beklenen: `RepositoryScanner` yok → derleme hatası.

- [ ] **Adım 3: Arayüz ve uygulamayı yaz**

`IRepositoryScanner.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Github;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <param name="ParsedFiles">İçeriği gerçekten okunan dosyalar.</param>
/// <param name="Skipped">Okunmayan her şey ve nedeni — kullanıcıya olduğu gibi gösterilir.</param>
/// <param name="TreeTruncated">GitHub ağacı kesti mi; kestiyse "bulamadım" demek yanlıştır.</param>
public sealed record RepositoryScanResult(
    DatabaseSchema Schema,
    string Format,
    string Branch,
    IReadOnlyList<string> ParsedFiles,
    IReadOnlyList<SkippedItem> Skipped,
    bool TreeTruncated);

/// <summary>
/// github/01-DEPO-TARAMA.md — bir depoyu okuyup şemayı çıkarır.
///
/// <b>Yeni bir ayrıştırıcı yok:</b> bu servis yalnızca dosyaları TOPLAR ve
/// var olan <see cref="CodeSchemaExtractor"/>'a verir. Depo, dosya seçicinin
/// yerini alan bir kaynaktan ibaret.
/// </summary>
public interface IRepositoryScanner
{
    Task<RepositoryScanResult> ScanAsync(
        GithubRepository repository, string? branch, long? installationId,
        CancellationToken cancellationToken = default);
}
```

`RepositoryScanner.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Github;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.Services;

public sealed class RepositoryScanner : IRepositoryScanner
{
    private readonly IGithubClient _github;

    public RepositoryScanner(IGithubClient github) => _github = github;

    public async Task<RepositoryScanResult> ScanAsync(
        GithubRepository repository, string? branch, long? installationId,
        CancellationToken cancellationToken = default)
    {
        var reference = branch;
        if (string.IsNullOrWhiteSpace(reference))
            reference = await _github.GetDefaultBranchAsync(repository, installationId, cancellationToken) ?? "main";

        var tree = await _github.GetRepositoryTreeAsync(repository, reference, installationId, cancellationToken);

        // Bütçe ağaçta uygulanıyor, indirdikten sonra değil: sınırın amacı
        // ayrıştırma maliyetinden önce AĞ maliyetini sınırlamak.
        var selection = RepositoryCandidateSelector.Select(tree.Paths, CodeSchemaExtractor.MaxFiles);

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = new List<SkippedItem>(selection.Skipped);

        foreach (var path in selection.Paths)
        {
            var content = await _github.GetFileContentAsync(repository, installationId, path, reference, cancellationToken);

            // null: dosya ağaç listelendikten sonra kaybolmuş (dal ilerlemiş)
            // ya da okunamamış. Sessizce atlamak, eksik şemayı tam gibi
            // göstermek olurdu.
            if (content is null) skipped.Add(new SkippedItem(path, "Could not be read at this ref."));
            else files[path] = content;
        }

        // Hiçbir dosya okunamadıysa da ayrıştırıcıya gidiliyor: "tanıyamadım"
        // mesajını üreten ve tek elden yöneten yer orası.
        var extraction = CodeSchemaExtractor.Extract(files);

        return new RepositoryScanResult(
            extraction.Schema,
            extraction.Format,
            reference,
            files.Keys.ToList(),
            skipped.Concat(extraction.Skipped).ToList(),
            tree.Truncated);
    }
}
```

- [ ] **Adım 4: Testleri çalıştır**

Çalıştır: `dotnet test backend/Namines.Tests --filter RepositoryScannerTests`
Beklenen: 5 test PASS.

- [ ] **Adım 5: Commit (onay sonrası)**

```bash
git add backend/Namines.Core/Interfaces/IRepositoryScanner.cs backend/Namines.Infrastructure/Services/RepositoryScanner.cs backend/Namines.Tests/Github/RepositoryScannerTests.cs
git commit -m "feat: scan a repository into a schema"
```

---

### Görev 4: `POST /api/github/scan` ucu

> ✅ **Bitmiştir.** `POST /api/github/scan` canlı; `Controllers/GithubScanControllerTests.cs` kapsıyor.

**Dosyalar:**
- Oluştur: `backend/Namines.API/Controllers/GithubScanController.cs`
- Değiştir: `backend/Namines.API/Extensions/ServiceCollectionExtensions.cs`
- Test: `backend/Namines.Tests/Controllers/GithubScanControllerTests.cs`

**Arayüzler:**
- Kullanır: `IRepositoryScanner`, `SchemaUuidAligner`, `SchemaImpactAnalyzer`.
- Üretir: `POST /api/github/scan` — gövde `{ repoUrl, branch?, compareWith? }`,
  yanıt `{ format, branch, schema, parsedFiles[], skipped[{name,reason}], treeTruncated, impact? }`.

- [ ] **Adım 1: Başarısız testi yaz**

```csharp
using Microsoft.AspNetCore.Mvc;
using Namines.API.Controllers;
using Namines.Core.Analysis;
using Namines.Core.Github;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Tests.Controllers;

public class GithubScanControllerTests
{
    private sealed class StubScanner : IRepositoryScanner
    {
        public GithubRepository? Seen { get; private set; }
        public Exception? Throw { get; init; }

        public Task<RepositoryScanResult> ScanAsync(
            GithubRepository repository, string? branch, long? installationId, CancellationToken ct = default)
        {
            Seen = repository;
            if (Throw is not null) throw Throw;

            return Task.FromResult(new RepositoryScanResult(
                new DatabaseSchema { Tables = new List<SchemaTable>() },
                "prisma", branch ?? "main",
                new[] { "prisma/schema.prisma" },
                new[] { new SkippedItem("node_modules/x.sql", "Build output or dependency directory.") },
                TreeTruncated: false));
        }
    }

    [Fact]
    public async Task A_valid_repository_url_is_scanned()
    {
        var scanner = new StubScanner();

        var result = await new GithubScanController(scanner).Scan(
            new ScanRepositoryRequest("https://github.com/acme/shop", null, null), default) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal("acme", scanner.Seen!.Owner);
        Assert.Equal("shop", scanner.Seen.Name);
    }

    [Fact]
    public async Task A_url_that_is_not_a_github_repository_is_rejected_before_any_request()
    {
        var scanner = new StubScanner();

        var result = await new GithubScanController(scanner).Scan(
            new ScanRepositoryRequest("https://gitlab.com/acme/shop", null, null), default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(scanner.Seen);   // ağa hiç çıkılmadı
    }

    [Fact]
    public async Task A_private_repository_is_answered_with_what_to_do_next()
    {
        var scanner = new StubScanner
        {
            Throw = new GithubRepositoryUnavailableException("acme/secret could not be read."),
        };

        var result = await new GithubScanController(scanner).Scan(
            new ScanRepositoryRequest("https://github.com/acme/secret", null, null), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("private", bad.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unrecognised_repository_says_so_instead_of_returning_an_empty_schema()
    {
        var scanner = new StubScanner
        {
            Throw = new CodeSchemaExtractor.UnknownFormatException("Could not recognise the format."),
        };

        var result = await new GithubScanController(scanner).Scan(
            new ScanRepositoryRequest("https://github.com/acme/shop", null, null), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
```

- [ ] **Adım 2: Testi çalıştır, düştüğünü gör**

Çalıştır: `dotnet test backend/Namines.Tests --filter GithubScanControllerTests`
Beklenen: derleme hatası.

- [ ] **Adım 3: Controller'ı yaz**

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Namines.Core.Analysis;
using Namines.Core.Github;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.API.Controllers;

/// <param name="RepoUrl">Kullanıcının yapıştırdığı adres.</param>
/// <param name="Branch">Boşsa deponun varsayılan dalı.</param>
/// <param name="CompareWith">Doluysa çıkarılan şema BUNA karşı karşılaştırılır.</param>
public sealed record ScanRepositoryRequest(string RepoUrl, string? Branch, DatabaseSchema? CompareWith);

/// <summary>
/// github/01-DEPO-TARAMA.md — depo linkinden şema.
///
/// <b>Giriş ZORUNLU</b> (<see cref="CodeSchemaController"/> ile aynı gerekçe ve
/// bir fazlası): bu uç sunucuyu dışarıya çıkarıyor. Kimliksiz bırakmak, onu
/// herkesin kullanabildiği bir istek aracına çevirirdi. Adres beyaz listesi
/// (<see cref="GithubRepositoryUrl"/>) nereye çıkılabileceğini de sınırlıyor.
/// </summary>
[Authorize]
[EnableRateLimiting("sensitive")]
[ApiController]
[Route("api/github")]
public class GithubScanController : ControllerBase
{
    private readonly IRepositoryScanner _scanner;

    public GithubScanController(IRepositoryScanner scanner) => _scanner = scanner;

    [HttpPost("scan")]
    public async Task<IActionResult> Scan(
        [FromBody] ScanRepositoryRequest request, CancellationToken cancellationToken)
    {
        if (!GithubRepositoryUrl.TryParse(request?.RepoUrl, out var repository))
            return BadRequest(new { message = "That is not a GitHub repository URL. Expected github.com/owner/name." });

        try
        {
            // installationId null: bu kademe yalnızca public depo okur.
            // Private depo F2'de GitHub App kurulumuyla geliyor.
            var scan = await _scanner.ScanAsync(repository!, request!.Branch, installationId: null, cancellationToken);

            // Koddan çıkan şemada StableUuid yok; hizalamadan karşılaştırmak
            // HER tabloyu "silindi + eklendi" gösterirdi (second-phase/11'de
            // yaşanmış ve testle kilitlenmiş bir hata).
            object? impact = null;
            if (request.CompareWith is not null)
            {
                var aligned = SchemaUuidAligner.Align(request.CompareWith, scan.Schema);
                impact = SchemaImpactAnalyzer.Analyze(request.CompareWith, aligned);
            }

            return Ok(new
            {
                format = scan.Format,
                branch = scan.Branch,
                schema = scan.Schema,
                parsedFiles = scan.ParsedFiles,
                skipped = scan.Skipped.Select(s => new { name = s.Name, reason = s.Reason }),
                treeTruncated = scan.TreeTruncated,
                impact,
            });
        }
        catch (GithubRepositoryUnavailableException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (GithubRateLimitedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (CodeSchemaExtractor.UnknownFormatException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
```

> **`SchemaUuidAligner.Align` ve `SchemaImpactAnalyzer.Analyze` imzalarını
> yazmadan önce doğrula:** `CodeSchemaController`'ın `compareWith` yolunu oku
> ve ORADAKİ çağrıyı birebir tekrarla. İki çağıranın aynı iki adımı farklı
> sırayla yapması, aynı depoda iki farklı drift cevabı demek olurdu.

- [ ] **Adım 4: DI kaydı**

```csharp
// Tarayıcı durumsuz; GithubClient zaten HttpClient fabrikasından geliyor.
services.AddScoped<IRepositoryScanner, RepositoryScanner>();
```

- [ ] **Adım 5: Testleri ve UYGULAMAYI çalıştır**

```bash
dotnet test backend/Namines.Tests --filter "GithubScanControllerTests|ApiControllerConventionTests"
dotnet run --project backend/Namines.API
```
Sonra gerçek bir public depoya karşı (giriş çerezi ile):
`POST /api/github/scan {"repoUrl":"https://github.com/prisma/prisma-examples"}`

Beklenen: gerçek bir şema, `parsedFiles` dolu, `skipped` gerekçeli.
**Bu adım atlanamaz:** sahte HTTP'ye karşı geçen testler, GitHub'ın gerçek
yanıt biçimini kanıtlamaz.

- [ ] **Adım 6: Commit (onay sonrası)**

```bash
git add backend/Namines.API/Controllers/GithubScanController.cs backend/Namines.API/Extensions/ServiceCollectionExtensions.cs backend/Namines.Tests/Controllers/GithubScanControllerTests.cs
git commit -m "feat: add the repository scan endpoint"
```

---

### Görev 5: Kataloğa GitHub kaynağı

> ✅ **Bitmiştir.** GitHub kaynağı kataloğa eklendi.

**Dosyalar:**
- Değiştir: `backend/Namines.Infrastructure/Services/StaticSchemaSourceCatalog.cs`
- Değiştir: `backend/Namines.Tests/Sources/SchemaSourceCatalogTests.cs`

- [ ] **Adım 1: Testi genişlet**

`Catalog_lists_every_source_the_product_has_today` içine `Assert.Contains("github", ids);`
ve yeni bir test:

```csharp
[Fact]
public void Github_promises_only_what_it_can_do_today()
{
    // F1 tek seferlik bir içe aktarma: drift takibi (Watch) ve geri yazma
    // (WriteBack) F3/F5'te geliyor. Bugün "Connect" grubuna koymak,
    // tutulmayacak bir söz vermek olurdu.
    var github = Catalog.All().Single(s => s.Id == "github");

    Assert.Equal(SchemaSourceKind.Import, github.Kind);
    Assert.False(github.Capabilities.HasFlag(SchemaSourceCapability.Watch));
    Assert.False(github.Capabilities.HasFlag(SchemaSourceCapability.WriteBack));
    Assert.False(github.ProducesGuess);   // ayrıştırıcılar deterministik
}
```

- [ ] **Adım 2: Çalıştır, düştüğünü gör** → `dotnet test backend/Namines.Tests --filter SchemaSourceCatalogTests`

- [ ] **Adım 3: Kaynağı ekle** (listenin BAŞINA — menüde ilk sırada dursun)

```csharp
new SchemaSourceDescriptor(
    "github", "GitHub repository",
    "Read the schema out of a public repository's code.",
    SchemaSourceKind.Import,
    SchemaSourceCapability.Import | SchemaSourceCapability.Compare,
    ProducesGuess: false),
```

- [ ] **Adım 4: Çalıştır** → 6 test PASS.

- [ ] **Adım 5: Commit (onay sonrası)**

```bash
git add backend/Namines.Infrastructure/Services/StaticSchemaSourceCatalog.cs backend/Namines.Tests/Sources/SchemaSourceCatalogTests.cs
git commit -m "feat: list GitHub as a schema source"
```

---

### Görev 6: Frontend — depo tarama modal'ı

> ✅ **Bitmiştir.** `components/prompt/GithubScanModal.tsx` + testi yazıldı.

**Dosyalar:**
- Oluştur: `frontend/components/prompt/GithubScanModal.tsx`
- Test: `frontend/components/prompt/GithubScanModal.test.tsx`
- Değiştir: `frontend/services/api.ts` (`sourceService.scanRepository`)
- Değiştir: `frontend/types/source.ts` (tarama yanıtı tipleri)
- Değiştir: `frontend/app/new/page.tsx` (menüde `github` seçimi modal'ı açar)

**Görsel karar öncesi (FRONTEND.md §0):**

```bash
python "C:/Users/PC/.claude/skills/ui-ux-pro-max/scripts/search.py" "modal form loading state error" --domain ux
```
Sonuç konuyla ilgisizse uydurma; projedeki mevcut modal kalıbını
(`VisionUploadModal`, `AuthModal`) taklit et ve bunu göreve not düş.

- [ ] **Adım 1: Başarısız testi yaz**

```tsx
// @vitest-environment jsdom
import { describe, expect, it, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, cleanup } from '@testing-library/react';
import { GithubScanModal } from './GithubScanModal';

describe('GithubScanModal', () => {
  afterEach(() => cleanup());

  it('does not scan until a repository is given', () => {
    const onScan = vi.fn();
    render(<GithubScanModal onScan={onScan} onImport={() => {}} onClose={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: /scan/i }));

    expect(onScan).not.toHaveBeenCalled();
  });

  it('shows what was read and what was skipped, with reasons', async () => {
    const onScan = vi.fn().mockResolvedValue({
      format: 'prisma', branch: 'main', schema: { tables: [] },
      parsedFiles: ['prisma/schema.prisma'],
      skipped: [{ name: 'node_modules/x.sql', reason: 'Build output or dependency directory.' }],
      treeTruncated: false,
    });

    render(<GithubScanModal onScan={onScan} onImport={() => {}} onClose={() => {}} />);
    fireEvent.change(screen.getByLabelText(/repository/i), { target: { value: 'github.com/acme/shop' } });
    fireEvent.click(screen.getByRole('button', { name: /scan/i }));

    // "Atlananlar" kapatılamaz: kullanıcının her şeyi gördüğünü sanması,
    // eksik şemayla ilerlemesinin tek sebebi olur (01-DEPO-TARAMA.md).
    await waitFor(() => expect(screen.getByText('prisma/schema.prisma')).toBeTruthy());
    expect(screen.getByText(/node_modules\/x\.sql/)).toBeTruthy();
    expect(screen.getByText(/Build output or dependency directory\./)).toBeTruthy();
  });

  it('warns when the repository tree was truncated', async () => {
    const onScan = vi.fn().mockResolvedValue({
      format: 'prisma', branch: 'main', schema: { tables: [] },
      parsedFiles: ['prisma/schema.prisma'], skipped: [], treeTruncated: true,
    });

    render(<GithubScanModal onScan={onScan} onImport={() => {}} onClose={() => {}} />);
    fireEvent.change(screen.getByLabelText(/repository/i), { target: { value: 'github.com/acme/shop' } });
    fireEvent.click(screen.getByRole('button', { name: /scan/i }));

    await waitFor(() => expect(screen.getByText(/too large to list in full/i)).toBeTruthy());
  });

  it('shows the server message when the scan fails', async () => {
    const onScan = vi.fn().mockRejectedValue(new Error('It does not exist, or it is private.'));

    render(<GithubScanModal onScan={onScan} onImport={() => {}} onClose={() => {}} />);
    fireEvent.change(screen.getByLabelText(/repository/i), { target: { value: 'github.com/acme/secret' } });
    fireEvent.click(screen.getByRole('button', { name: /scan/i }));

    await waitFor(() => expect(screen.getByText(/it is private/i)).toBeTruthy());
  });
});
```

- [ ] **Adım 2: Çalıştır, düştüğünü gör**

Çalıştır: `cd frontend && npx vitest run components/prompt/GithubScanModal.test.tsx`
Beklenen: modül bulunamadı. **"No test files found" görürsen bu kırmızı DEĞİL** —
dosya yolunun `vitest.config.mts`'teki `include` desenine (`components/**/*.test.tsx`)
girdiğini doğrula.

- [ ] **Adım 3: Servis ve tipleri ekle**

`frontend/types/source.ts`:

```ts
export interface RepositoryScanSkip { name: string; reason: string }

export interface RepositoryScanResult {
  format: string;
  branch: string;
  schema: unknown;          // DatabaseSchema — canvas store'una olduğu gibi gider
  parsedFiles: string[];
  skipped: RepositoryScanSkip[];
  treeTruncated: boolean;
  impact?: unknown;
}
```

`frontend/services/api.ts` → `sourceService` içine:

```ts
/** github/01-DEPO-TARAMA.md — depo linkinden şema. */
scanRepository: async (repoUrl: string, branch?: string): Promise<RepositoryScanResult> => {
  const response = await api.post<RepositoryScanResult>('/github/scan', { repoUrl, branch });
  return response.data;
},
```

- [ ] **Adım 4: Modal'ı yaz**

Sözleşme: `GithubScanModal({ onScan, onImport, onClose })`.
`onScan(repoUrl, branch?)` → `Promise<RepositoryScanResult>`;
`onImport(result)` → şemayı canvas'a yükler. İçerik:

1. Depo adresi alanı (`aria-label` içinde "repository").
2. İsteğe bağlı dal alanı.
3. `Scan` düğmesi — adres boşken hiçbir şey yapmaz.
4. Sonuç: okunan dosyalar listesi, **kapatılamaz** "Skipped" listesi
   (ad + gerekçe), `treeTruncated` ise uyarı satırı
   ("This repository was too large to list in full — some files were never seen.").
5. Hata: sunucudan gelen mesaj olduğu gibi.
6. `Import into canvas` düğmesi — yalnızca başarılı taramadan sonra.

`FRONTEND.md` token'ları; `glass-panel` / `glass-input` / `var(--radius-*)`;
odak tuzağı için `useFocusTrap` (projede var, bkz. `ConflictResolverModal`).

- [ ] **Adım 5: `/new` sayfasına bağla**

`handleSourceSelect` içine, `openapi` dalının önüne:

```tsx
if (id === 'github') { setGithubModalOpen(true); return; }
```

Modal'ın `onImport`'u: `loadFromSchema(result.schema as DatabaseSchema)` ve
`router.push('/canvas')` — üretim akışının bitişiyle aynı.

- [ ] **Adım 6: Testler ve kontroller**

```bash
cd frontend && npx vitest run && npm run check:design && npm run lint
```

- [ ] **Adım 7: CANLI doğrula**

`npm run dev`, giriş yap, `+ Add source → GitHub repository`:

1. `github.com/prisma/prisma-examples` → gerçek şema geliyor, atlananlar
   gerekçeli görünüyor.
2. Var olmayan bir depo → "does not exist, or it is private" mesajı.
3. `gitlab.com/...` → ağa hiç çıkmadan reddediliyor.
4. Şema dosyası olmayan bir depo → "could not recognise the format".
5. `Import into canvas` → canvas'ta tablolar duruyor.
6. 375px'de modal taşmıyor.

Ekran görüntüsü al; 1, 2 ve 5 kanıtlanmadan görev bitmiş sayılmaz.

- [ ] **Adım 8: Commit (onay sonrası)**

```bash
git add frontend/components/prompt/GithubScanModal.tsx frontend/components/prompt/GithubScanModal.test.tsx frontend/services/api.ts frontend/types/source.ts frontend/app/new/page.tsx
git commit -m "feat: import a schema from a public GitHub repository"
```

---

## Öz-denetim

- **Spec kapsamı:** 01'in istemci yüzeyi → Görev 2; `RepositoryScanner` ve
  öncelik/gürültü/bütçe kuralları → Görev 1+3; `POST /api/github/scan`
  sözleşmesi → Görev 4; UI ve "atlananlar kapatılamaz" kuralı → Görev 6;
  rate limit / private depo / truncated ağaç mesajları → Görev 2 (üretim) +
  4 (çeviri) + 6 (gösterim).
- **Yer tutucu taraması:** Görev 6 Adım 4 tek "tarif" adımı — bileşenin
  içeriği madde madde sabitlendi ve davranışı Adım 1'deki testler kilitliyor.
- **Tip tutarlılığı:** `RepositoryScanResult` alan adları C# kaydı → JSON →
  TS arayüzü boyunca aynı: `format`, `branch`, `schema`, `parsedFiles`,
  `skipped[].name/.reason`, `treeTruncated`, `impact`.
- **Bilinen risk:** Görev 4 `SchemaUuidAligner` / `SchemaImpactAnalyzer`
  imzalarını `CodeSchemaController`'dan doğrulamayı ŞART koşuyor; bu plan onları
  okumadan yazdı.
