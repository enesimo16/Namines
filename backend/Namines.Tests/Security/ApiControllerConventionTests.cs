using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Namines.Tests.Security;

/// <summary>
/// Controller sözleşmesi.
///
/// <b>Bu testler ÜÇÜNCÜ kez tekrarlanan bir hata yüzünden var.</b> Bir
/// <c>[ApiController]</c> özniteliği yanlışlıkla bir <c>record</c>'un üstüne
/// düştüğünde ASP.NET o record'u controller sanıyor, <c>Deconstruct</c> metodunu
/// bir action olarak okuyor ve <b>uygulama hiç BAŞLAMIYOR</b> — 857 test yeşilken.
/// Birim testleri bunu göremez, çünkü hata tip yükleme sırasında değil, MVC'nin
/// keşif aşamasında çıkıyor.
///
/// Aynı şekilde <c>[ApiController]</c>/<c>[Route]</c>'u UNUTMAK da sessiz: uygulama
/// başlar, ama o controller'ın uçları hiçbir adrese bağlanmaz ve her çağrı 404
/// döner. İkisi de "derlenir, testler geçer, üretimde çalışmaz" sınıfında.
/// </summary>
public class ApiControllerConventionTests
{
    private static readonly Assembly ApiAssembly = typeof(Namines.API.Controllers.CompileController).Assembly;

    private static bool IsController(Type t) =>
        typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract;

    [Fact]
    public void The_api_controller_attribute_is_only_on_actual_controllers()
    {
        var strays = ApiAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<ApiControllerAttribute>(inherit: false) is not null)
            .Where(t => !IsController(t))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(strays.Count == 0,
            "These types carry [ApiController] but are not controllers, which stops the app from starting: " +
            string.Join(", ", strays));
    }

    [Fact]
    public void Every_controller_is_routable()
    {
        var unroutable = ApiAssembly.GetTypes()
            .Where(IsController)
            .Where(t => t.GetCustomAttribute<RouteAttribute>(inherit: true) is null &&
                        t.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any() == false)
            .Select(t => t.FullName!)
            .ToList();

        // Rota olmadan controller derlenir ve uygulama başlar; yalnızca her çağrı
        // 404 döner — hatanın en pahalı biçimi.
        Assert.True(unroutable.Count == 0,
            "These controllers have no [Route], so every call to them returns 404: " +
            string.Join(", ", unroutable));
    }

    [Fact]
    public void Every_controller_declares_the_api_controller_attribute()
    {
        var missing = ApiAssembly.GetTypes()
            .Where(IsController)
            .Where(t => t.GetCustomAttribute<ApiControllerAttribute>(inherit: true) is null)
            .Select(t => t.FullName!)
            .ToList();

        // [ApiController] olmadan model doğrulama otomatik 400 üretmez; geçersiz
        // gövde sessizce varsayılan değerlerle işlenir.
        Assert.True(missing.Count == 0,
            "These controllers are missing [ApiController]: " + string.Join(", ", missing));
    }

    // ── Yetkilendirme sözleşmesi ──────────────────────────────────────────────

    /// <summary>
    /// Gövdesinde yetki kontrolü YAPTIĞI kabul edilen yardımcılar.
    ///
    /// <c>GatewayController</c> sınıf düzeyinde <c>[AllowAnonymous]</c>: kimlik
    /// bir API anahtarından çözülüyor, öznitelikten değil. O yüzden orada
    /// kontrol elle yapılıyor ve bu liste "elle ama DOĞRU yapılmış"ın tanımı.
    /// </summary>
    private static readonly string[] AuthorizationHelpers =
    {
        "AuthorizeAsync(",
        "AuthorizeForSqlAsync(",
        "ResolveKeyAsync(",
        "CurrentUserId",
        "GetRoleAsync(",
    };

    /// <summary>
    /// BİLEREK herkese açık uçlar. Her satırın gerekçesi yazılı olmak zorunda.
    ///
    /// <b>Bu liste testin can damarı:</b> yeni bir uç kimlik doğrulaması
    /// olmadan eklendiğinde test kırılıyor ve geliştirici iki şeyden birini
    /// yapmak zorunda kalıyor — ya <c>[Authorize]</c> eklemek ya da buraya
    /// gerekçesiyle bir satır yazmak. İkisi de bilinçli bir karar; unutmak
    /// artık sessiz değil.
    /// </summary>
    private static readonly Dictionary<string, string> IntentionallyPublic = new()
    {
        // Kimlik daha kurulmadan çağrılan uçlar.
        ["AuthController.Register"] = "Hesap oluşturma — tanımı gereği kimliksiz.",
        ["AuthController.Login"] = "Giriş — tanımı gereği kimliksiz. Rate limit + hesap kilitleme var.",
        ["AuthController.Logout"] = "Cookie silme; kimliksiz çağrı zararsız.",
        ["AuthController.ExchangeDeskHandoffToken"] =
            "Tek kullanımlık devir jetonunu JWT'ye çevirir; jetonun kendisi kimlik yerine geçiyor.",

        // Dış sistemlerin çağırdığı uçlar: JWT gönderemezler, imzayla korunuyorlar.
        ["StripeWebhookController.HandleStripeWebhook"] = "Stripe imza doğrulaması ile korunuyor.",
        ["GithubWebhookController.Receive"] = "GitHub imza doğrulaması ile korunuyor.",

        // ⚠️ BİLİNEN ÖDÜNLEŞİM — kapatılabilir, bkz. docs/audit/29.
        //
        // SSE akışı `EventSource` ile tüketiliyor ve EventSource `Authorization`
        // başlığı gönderemiyor. Bunun yerine sunucu üretimi GUID `jobId` bir
        // "capability URL" görevi görüyor: bilmeyen erişemez.
        //
        // Ama capability URL'leri sızabilir (proxy logu, tarayıcı geçmişi,
        // Referer) ve akış kullanıcının DDL'ini içerebilen derleme loglarını
        // yayıyor.
        //
        // ÇÖZÜM HAZIR ama bu oturumda DOĞRULANAMADI (Docker sandbox akışı
        // uçtan uca çalıştırılamadı): EventSource cookie GÖNDEREBİLİYOR ve
        // CORS politikası zaten `AllowCredentials()` taşıyor. Yani
        // `new EventSource(url, { withCredentials: true })` + uca `[Authorize]`
        // yeterli.
        ["DockerController.StreamLogs"] =
            "SSE + EventSource; kimlik GUID jobId capability'siyle. Kapatılabilir (bkz. yorum).",

        // Saf dönüşüm: girdiden çıktı, kalıcı durum yok, veritabanına bağlanmıyor.
        // Rate limit var (BACK-002).
        ["CompileController"] = "Saf dönüşüm (.nsl → DDL, şema → kod). Kalıcı durum yok, rate limit var.",
        ["SchemaController"] = "AI şema üretimi: girdiden çıktı. Kota ve rate limit var.",
        ["MigrationController"] = "Saf dönüşüm: iki şemadan migration metni.",
        ["LintController"] = "Saf dönüşüm: şemadan uyarı listesi.",
        ["ScaffolderController"] = "Saf dönüşüm: şemadan proje iskeleti.",
        ["ReverseEngineerController"] = "Saf dönüşüm: SQL metninden şema.",
        ["CodeSchemaController"] = "Saf dönüşüm: kaynak koddan şema.",
        ["DocumentationController"] = "Saf dönüşüm: şemadan belge.",
        ["AIDbaController"] = "Saf dönüşüm: şemadan DBA bulguları.",
        ["CoderAIController"] = "Saf dönüşüm: şemadan kod.",
        ["SmartSeedController"] = "Saf dönüşüm: şemadan örnek veri.",
        ["VoiceController"] = "Saf dönüşüm: sesten metne.",
        ["FeedbackController"] = "Geri bildirim toplama; kimliksiz kabul ediliyor.",

        // Herkese açık olması ÜRÜN kararı olan uçlar.
        ["ShareController"] = "Paylaşım linkleri jetonla korunuyor; alıcının hesabı olmayabilir.",
        ["SubscriptionController.GetPlans"] = "Fiyat listesi giriş öncesi görünmeli.",
        ["QuotaController"] = "Anonim kullanıcının kalan kotasını göstermek için.",
        ["TeamController.PreviewInvite"] = "Davet linkine tıklayan kişinin henüz hesabı olmayabilir.",
        ["VaultController.Health"] = "Sağlık bilgisi; sır taşımıyor.",
        ["GatewayController"] = "Kimlik API anahtarından çözülüyor; her uç elle kontrol ediyor (aşağıdaki test).",
    };

    private static IEnumerable<(Type Controller, MethodInfo Action)> AllActions() =>
        ApiAssembly.GetTypes()
            .Where(IsController)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
                .Select(m => (t, m)));

    private static bool IsCoveredByAttribute(Type controller, MethodInfo action)
    {
        // Metot düzeyindeki [AllowAnonymous] sınıf düzeyindeki [Authorize]'ı EZER.
        if (action.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false) is not null)
            return false;

        return action.GetCustomAttribute<AuthorizeAttribute>(inherit: false) is not null
            || controller.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is not null;
    }

    private static bool IsAllowlisted(Type controller, MethodInfo action) =>
        IntentionallyPublic.ContainsKey($"{controller.Name}.{action.Name}")
        || IntentionallyPublic.ContainsKey(controller.Name);

    /// <summary>
    /// <b>Varsayılan KAPALI — test seviyesinde.</b>
    ///
    /// Denetim (ARCH-002) şunu buldu: <c>GatewayController</c> sınıf düzeyinde
    /// <c>[AllowAnonymous]</c> ve yetki 15 uçta ELLE tekrarlanıyor. Bugün
    /// hiçbiri eksik değil — tarandı ve doğrulandı — ama yapı, unutulmayı
    /// sessiz bırakıyor: yeni bir uç eklerken kontrolü atlayan kişiyi hiçbir
    /// şey uyarmaz.
    /// </summary>
    [Fact]
    public void Every_endpoint_is_authorized_or_explicitly_public()
    {
        var unprotected = AllActions()
            .Where(x => !IsCoveredByAttribute(x.Controller, x.Action))
            .Where(x => !IsAllowlisted(x.Controller, x.Action))
            .Select(x => $"{x.Controller.Name}.{x.Action.Name}")
            .OrderBy(x => x)
            .ToList();

        Assert.True(unprotected.Count == 0,
            "Bu uclar ne [Authorize] tasiyor ne de bilerek-acik listesinde:\n  " +
            string.Join("\n  ", unprotected) +
            "\n\nYa [Authorize] ekleyin ya da IntentionallyPublic listesine GEREKCESIYLE yazin.");
    }

    /// <summary>
    /// <c>GatewayController</c>'ın her ucu gövdesinde bir yetki kontrolü
    /// çağırmalı. Sınıf <c>[AllowAnonymous]</c> olduğu için öznitelik
    /// koruması yok — tek savunma bu çağrı.
    ///
    /// <b>Kaynak metni okunuyor, IL değil:</b> yansıma metot gövdesini
    /// göremiyor. Kaynak tarama kırılgan görünse de burada doğru araç, çünkü
    /// aradığımız şey tam olarak "bu satır yazılmış mı".
    /// </summary>
    [Fact]
    public void Every_gateway_endpoint_checks_authorization_in_its_body()
    {
        var source = File.ReadAllText(GatewayControllerSourcePath());

        var actions = typeof(Namines.API.Controllers.GatewayController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        Assert.NotEmpty(actions);

        var missing = new List<string>();
        foreach (var action in actions)
        {
            var body = ExtractMethodBody(source, action);
            if (body is null)
            {
                missing.Add($"{action} (kaynak icinde bulunamadi)");
                continue;
            }

            if (!AuthorizationHelpers.Any(body.Contains))
                missing.Add(action);
        }

        Assert.True(missing.Count == 0,
            "GatewayController'in su uclari govdesinde yetki kontrolu CAGIRMIYOR:\n  " +
            string.Join("\n  ", missing) +
            "\n\nSinif [AllowAnonymous]; oznitelik korumasi YOK. Kontrol elle yapilmak zorunda.");
    }

    /// <summary>
    /// Kabaca: metot imzasından başlayıp bir sonraki metot imzasına kadar olan
    /// metin. Tam bir ayrıştırıcı değil ve olması da gerekmiyor — aradığımız
    /// tek şey, o aralıkta bir yetki çağrısının geçip geçmediği.
    /// </summary>
    private static string? ExtractMethodBody(string source, string actionName)
    {
        var start = source.IndexOf($" {actionName}(", StringComparison.Ordinal);
        if (start < 0) return null;

        // Bir sonraki action imzasi ya da dosya sonu.
        var next = source.IndexOf("\n    [Http", start, StringComparison.Ordinal);
        var end = next < 0 ? source.Length : next;

        return source[start..end];
    }

    /// <summary>
    /// Kaynak dosyanın yolu, BU dosyanın derleme zamanındaki yolundan
    /// türetiliyor. Çalışma dizinine bağlı olsaydı test, nereden koşturulduğuna
    /// göre farklı davranırdı.
    /// </summary>
    private static string GatewayControllerSourcePath([CallerFilePath] string thisFile = "")
    {
        // .../backend/Namines.Tests/Security/ApiControllerConventionTests.cs
        var testsRoot = Directory.GetParent(thisFile)!.Parent!;          // Namines.Tests
        var backend = testsRoot.Parent!;                                  // backend
        return Path.Combine(backend.FullName, "Namines.API", "Controllers", "GatewayController.cs");
    }
}
