using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Namines.API.Middleware;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Denetimde bulunan güvenlik açıklarının kapatıldığını sabitleyen testler.
///
/// <b>Neden ayrı bir dosya:</b> Bunların her biri bir kez gerçekten açıktı.
/// Bir daha açılırsa bunu bir denetimin değil, bir testin yakalamasını
/// istiyoruz — ve testin yanında hangi açığın kapatıldığı yazılı olsun.
/// </summary>
public class SecurityHardeningTests
{
    // ── CSRF (AUTHZ-004) ──────────────────────────────────────────────────────

    private static async Task<int> RunCsrfAsync(
        string method, bool withCookie, bool withAuthHeader, bool withCsrfHeader)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();

        if (withCookie) context.Request.Headers["Cookie"] = "namines_token=abc";
        if (withAuthHeader) context.Request.Headers["Authorization"] = "Bearer abc";
        if (withCsrfHeader) context.Request.Headers[CsrfProtectionMiddleware.HeaderName] = "1";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        // Zincir devam ettiyse istek KABUL edilmiş demektir.
        return nextCalled ? StatusCodes.Status200OK : context.Response.StatusCode;
    }

    /// <summary>
    /// Asıl açık: cookie ile kimlik doğrulanan bir yazma isteği, başka bir
    /// siteden tetiklenebiliyordu. Özel başlık olmadan artık reddediliyor.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Cookie_ile_yazma_istegi_csrf_basligi_olmadan_reddedilir(string method)
    {
        Assert.Equal(StatusCodes.Status403Forbidden,
            await RunCsrfAsync(method, withCookie: true, withAuthHeader: false, withCsrfHeader: false));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Cookie_ile_yazma_istegi_csrf_basligiyla_gecer(string method)
    {
        Assert.Equal(StatusCodes.Status200OK,
            await RunCsrfAsync(method, withCookie: true, withAuthHeader: false, withCsrfHeader: true));
    }

    /// <summary>
    /// Okuma istekleri engellenmemeli: yan etkileri yok ve engellemek
    /// paylaşılan şema sayfaları gibi anonim akışları kırardı.
    /// </summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Guvenli_metotlar_basliksiz_gecer(string method)
    {
        Assert.Equal(StatusCodes.Status200OK,
            await RunCsrfAsync(method, withCookie: true, withAuthHeader: false, withCsrfHeader: false));
    }

    /// <summary>
    /// Bearer jetonu tarayıcı tarafından KENDILIGINDEN eklenmez, dolayısıyla
    /// CSRF yüzeyi yoktur. Bu istisna olmasaydı SDK, CLI, MCP ve Desk kırılırdı.
    /// </summary>
    [Fact]
    public async Task Authorization_basligiyla_gelen_istek_csrf_basligi_istemez()
    {
        Assert.Equal(StatusCodes.Status200OK,
            await RunCsrfAsync("POST", withCookie: false, withAuthHeader: true, withCsrfHeader: false));
    }

    /// <summary>
    /// Cookie yoksa korunacak bir oturum da yok. Webhook'lar (Stripe, GitHub)
    /// ve giriş/kayıt uçları bu yoldan geçiyor.
    /// </summary>
    [Fact]
    public async Task Cookiesiz_istek_csrf_basligi_istemez()
    {
        Assert.Equal(StatusCodes.Status200OK,
            await RunCsrfAsync("POST", withCookie: false, withAuthHeader: false, withCsrfHeader: false));
    }

    // ── Rol sıralaması tuzağı (AUTHZ-001) ─────────────────────────────────────

    /// <summary>
    /// <c>Billing</c> sayısal olarak 4, <c>Owner</c> ise 3. Ham bir
    /// <c>role &gt;= OrgRole.Admin</c> karşılaştırması Billing'e Owner'dan
    /// fazla yetki verirdi. Yardımcı bunu imkânsız kılıyor.
    /// </summary>
    [Fact]
    public void Billing_rolu_hicbir_yetki_esigini_gecmez()
    {
        Assert.False(OrgRole.Billing.IsAtLeast(OrgRole.Viewer));
        Assert.False(OrgRole.Billing.IsAtLeast(OrgRole.Editor));
        Assert.False(OrgRole.Billing.IsAtLeast(OrgRole.Admin));
        Assert.False(OrgRole.Billing.IsAtLeast(OrgRole.Owner));

        // Sayısal karşılaştırmanın neden yanlış olduğunun kanıtı:
        Assert.True((int)OrgRole.Billing > (int)OrgRole.Owner);
    }

    [Theory]
    [InlineData(OrgRole.Owner, OrgRole.Admin, true)]
    [InlineData(OrgRole.Admin, OrgRole.Editor, true)]
    [InlineData(OrgRole.Editor, OrgRole.Editor, true)]
    [InlineData(OrgRole.Viewer, OrgRole.Editor, false)]
    [InlineData(OrgRole.Editor, OrgRole.Admin, false)]
    public void Hiyerarsi_icindeki_roller_dogru_siralaniyor(OrgRole role, OrgRole minimum, bool expected)
    {
        Assert.Equal(expected, role.IsAtLeast(minimum));
    }

    // ── Tanımlayıcı sınırlayıcı kaçırma (derinlemesine savunma) ───────────────

    /// <summary>
    /// <see cref="GatewayService.Quote"/> bugün yalnızca katı regex'ten geçmiş
    /// değerler alıyor, yani kaçırma gereksiz. Test, o regex bir gün
    /// gevşetildiğinde kaçırmanın hâlâ orada olduğunu garanti ediyor —
    /// korumanın tek bir satıra bağlı kalmaması için.
    /// </summary>
    [Theory]
    [InlineData("MSSQL", "a]b", "[a]]b]")]
    [InlineData("MYSQL", "a`b", "`a``b`")]
    [InlineData("POSTGRESQL", "a\"b", "\"a\"\"b\"")]
    public void Sinirlayici_kacirilir(string dbType, string identifier, string expected)
    {
        Assert.Equal(expected, GatewayService.Quote(dbType, identifier));
    }

    [Fact]
    public void Normal_tanimlayici_degismeden_sarilir()
    {
        Assert.Equal("[users]", GatewayService.Quote("MSSQL", "users"));
        Assert.Equal("`users`", GatewayService.Quote("MYSQL", "users"));
        Assert.Equal("\"users\"", GatewayService.Quote("POSTGRESQL", "users"));
    }

    // ── Hassas alanların serileştirilmemesi (ARCH-004) ────────────────────────

    /// <summary>
    /// Bugün her uç yanıtını elle projekte ediyor, yani sızıntı yok. Ama tek bir
    /// <c>return Ok(project)</c> yeterdi. <c>[JsonIgnore]</c> kazayı
    /// serileştirici seviyesinde imkânsız kılıyor; bu test onu sabitliyor.
    /// </summary>
    [Fact]
    public void Sifreli_baglanti_dizesi_json_a_yazilmaz()
    {
        var project = new CloudProject
        {
            Id = "p1",
            Name = "Proje",
            EncryptedConnectionString = "COK-GIZLI-SIFRELI-DEGER",
        };

        var json = JsonSerializer.Serialize(project);

        Assert.DoesNotContain("COK-GIZLI-SIFRELI-DEGER", json);
        Assert.DoesNotContain("EncryptedConnectionString", json);
    }

    [Fact]
    public void Anahtar_ve_jeton_hashleri_json_a_yazilmaz()
    {
        var key = new GatewayApiKey { ProjectId = "p1", KeyHash = "HASH-GIZLI" };
        var invite = new TeamInvite { TokenHash = "DAVET-HASH" };
        var handoff = new DeskHandoffToken { TokenHash = "DEVIR-HASH" };

        Assert.DoesNotContain("HASH-GIZLI", JsonSerializer.Serialize(key));
        Assert.DoesNotContain("DAVET-HASH", JsonSerializer.Serialize(invite));
        Assert.DoesNotContain("DEVIR-HASH", JsonSerializer.Serialize(handoff));
    }
}
