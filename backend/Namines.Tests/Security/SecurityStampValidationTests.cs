using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Namines.API.Controllers;
using Namines.API.Extensions;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Security;

/// <summary>
/// <see cref="SecurityStampValidation"/> — jeton iptalinin GERÇEKTEN çalıştığı.
///
/// <b>Neden bu testler var:</b> Bu mekanizma ürünün "tüm oturumlarımı kapat" ve
/// "parolamı değiştirdim" vaatlerinin ARKASINDAKİ tek şey. Sessizce bozulursa
/// hiçbir şey kırılmaz — kullanıcılar giriş yapmaya devam eder, testler geçer,
/// ve çalınmış bir jeton 7 gün boyunca geçerli kalır. Arıza ancak bir güvenlik
/// olayında, en kötü anda görünür.
///
/// Bu oturumda HIBP doğrulayıcısının tam olarak böyle öldüğü (enjekte edilen
/// <c>HttpClient</c>'ın <c>BaseAddress</c>'i yoktu, her parola kabul ediliyordu)
/// görüldükten sonra yazıldılar. O hatayı da hiçbir test yakalamamıştı.
///
/// <b>Neden SQLite:</b> Test edilen şey akış — "damga eşleşmezse reddet" — ve
/// bu Postgres'e özgü hiçbir davranışa dayanmıyor. Docker gerektirmek, testin
/// çoğu makinede ATLANMASI demek olurdu; atlanan bir güvenlik testi yok
/// sayılabilir. Motor davranışı gereken testler (<c>AiQuotaServiceTests</c>
/// gibi) Testcontainers kullanmaya devam ediyor.
/// </summary>
public sealed class SecurityStampValidationTests : IAsyncLifetime
{
    private const string UserId = "user-1";
    private const string CurrentStamp = "STAMP-CURRENT";

    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        // Bağlantı AÇIK tutuluyor: SQLite'ın bellek içi veritabanı son bağlantı
        // kapandığında yok olur, yani `using` içine alınsa şema kaybolurdu.
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser
        {
            Id = UserId,
            UserName = "user1",
            SecurityStamp = CurrentStamp,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // ── Yardımcılar ─────────────────────────────────────────────────────────

    private ServiceProvider BuildServices() => new ServiceCollection()
        .AddDbContext<AuthDbContext>(o => o.UseSqlite(_connection))
        .AddMemoryCache()
        .BuildServiceProvider();

    /// <summary>
    /// Gerçek delegeyi gerçek bir <see cref="TokenValidatedContext"/> ile çalıştırır.
    /// Taklit edilen tek şey HTTP taşıması; doğrulama mantığı üretimdekinin aynısı.
    /// </summary>
    private static async Task<TokenValidatedContext> RunAsync(
        ServiceProvider services,
        IEnumerable<Claim> claims)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));

        var ctx = new TokenValidatedContext(http, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        };

        await new JwtBearerEvents().AddSecurityStampValidation().OnTokenValidated(ctx);
        return ctx;
    }

    private static Claim[] ClaimsFor(string userId, string? stamp) => stamp is null
        ? [new Claim(ClaimTypes.NameIdentifier, userId)]
        : [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(AuthController.SecurityStampClaimType, stamp),
        ];

    private static bool Rejected(TokenValidatedContext ctx) =>
        ctx.Result is { Succeeded: false, Failure: not null };

    // ── Testler ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guncel_damgali_jeton_kabul_edilir()
    {
        await using var services = BuildServices();

        var ctx = await RunAsync(services, ClaimsFor(UserId, CurrentStamp));

        Assert.False(Rejected(ctx));
    }

    [Fact]
    public async Task Eski_damgali_jeton_REDDEDILIR()
    {
        await using var services = BuildServices();

        var ctx = await RunAsync(services, ClaimsFor(UserId, "STAMP-OLD"));

        Assert.True(Rejected(ctx));
        Assert.Contains("revoked", ctx.Result!.Failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mekanizmanın var oluş sebebi: damga değişince ESKİ jeton ölmeli.
    /// "Tüm oturumlarımı kapat" ve "parolamı değiştirdim" tam olarak bunu yapıyor.
    /// </summary>
    [Fact]
    public async Task Damga_yenilenince_onceden_gecerli_jeton_olur()
    {
        await using var services = BuildServices();

        var before = await RunAsync(services, ClaimsFor(UserId, CurrentStamp));
        Assert.False(Rejected(before));

        await using (var db = new AuthDbContext(_options))
        {
            var user = await db.Users.SingleAsync(u => u.Id == UserId);
            user.SecurityStamp = "STAMP-ROTATED";
            await db.SaveChangesAsync();
        }

        // Önbellek geçmiş isteği hatırlıyor; iptalin gecikmesi bilinçli bir
        // takas (bkz. SecurityStampValidation.CacheDuration). Testin ölçtüğü
        // şey gecikme değil, gecikme BİTTİKTEN sonraki davranış — o yüzden
        // taze bir kapsayıcıyla (yeni önbellek) tekrar çalıştırılıyor.
        await using var freshServices = BuildServices();
        var after = await RunAsync(freshServices, ClaimsFor(UserId, CurrentStamp));

        Assert.True(Rejected(after));
    }

    /// <summary>
    /// Geriye uyumluluk. Bu davranış BİLEREK böyle: aksi hâlde dağıtım anında
    /// o sırada oturumu açık olan HERKES çıkışa zorlanırdı. Eski jetonlar
    /// süreleri dolunca (7 gün) kendiliğinden kaybolur.
    ///
    /// Test, bu tavizin bir gün <b>kazara</b> kaldırılmasını değil, <b>bilinçli</b>
    /// olarak kaldırılmasını zorluyor: silen kişi bu testi de silmek zorunda.
    /// </summary>
    [Fact]
    public async Task Damga_claimi_olmayan_ESKI_jeton_kabul_edilir()
    {
        await using var services = BuildServices();

        var ctx = await RunAsync(services, ClaimsFor(UserId, stamp: null));

        Assert.False(Rejected(ctx));
    }

    [Fact]
    public async Task Silinmis_kullanicinin_jetonu_REDDEDILIR()
    {
        await using var services = BuildServices();

        var ctx = await RunAsync(services, ClaimsFor("yok-boyle-bir-kullanici", CurrentStamp));

        Assert.True(Rejected(ctx));
    }

    /// <summary>
    /// Damgası olmayan (elle eklenmiş) bir kullanıcı KALICI olarak dışarıda
    /// bırakılmamalı: `null` ve boş dize eşdeğer sayılıyor, ikisi de "damga yok"
    /// demek. Bu bir güvenlik tavizi değil — jetondaki değer de boşsa zaten
    /// iptal edilecek bir şey yok.
    /// </summary>
    [Fact]
    public async Task Damgasiz_kullanici_bos_dizeli_jetonla_kabul_edilir()
    {
        await using var db = new AuthDbContext(_options);
        db.Users.Add(new ApplicationUser
        {
            Id = "damgasiz", UserName = "damgasiz", SecurityStamp = null,
        });
        await db.SaveChangesAsync();

        await using var services = BuildServices();

        var ctx = await RunAsync(services, ClaimsFor("damgasiz", string.Empty));

        Assert.False(Rejected(ctx));
    }

    /// <summary>
    /// Zincirdeki önceki <c>OnTokenValidated</c> işleyicisi ÇAĞRILMALI.
    /// Üzerine yazmak, ileride eklenecek başka bir doğrulamayı sessizce
    /// devre dışı bırakırdı — ve bunu hiçbir şey haber vermezdi.
    /// </summary>
    [Fact]
    public async Task Onceki_isleyici_ustune_yazilmaz()
    {
        await using var services = BuildServices();

        var calledFirst = false;
        var events = new JwtBearerEvents { OnTokenValidated = _ => { calledFirst = true; return Task.CompletedTask; } };
        events.AddSecurityStampValidation();

        var http = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var ctx = new TokenValidatedContext(http, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(ClaimsFor(UserId, CurrentStamp), "Test")),
        };

        await events.OnTokenValidated(ctx);

        Assert.True(calledFirst);
        Assert.False(Rejected(ctx));
    }
}
