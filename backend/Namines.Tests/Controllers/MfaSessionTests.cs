using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// MFA'yı açıp kapatmak, işlemi yapan oturumu DÜŞÜRMEMELİ.
///
/// <b>Bulunan hata (canlı, 2026-09-27):</b> ASP.NET Identity'nin
/// <c>ResetAuthenticatorKeyAsync</c> ve <c>SetTwoFactorEnabledAsync</c>
/// çağrıları kullanıcının <c>SecurityStamp</c>'ini yeniliyor. Damga her istekte
/// JWT'deki kopyayla karşılaştırıldığı için (bkz. <c>SecurityStampValidation</c>),
/// "Turn on"a basan kullanıcının jetonu geçersizleşiyordu: 60 saniyelik damga
/// önbelleği dolunca gelen ilk istek — QR'ı okutup kodu girdiği
/// <c>mfa/enable</c> — 401 alıyor, istemci oturumu kapatıyordu. Yani 2FA
/// arayüzden fiilen açılamıyordu. Diğer oturumların düşmesi istenen bir şey;
/// işlemi yapan oturum ise yeni damgayla devam etmeli.
/// </summary>
public sealed class MfaSessionTests : IAsyncLifetime
{
    private const string UserId = "mfa-user";
    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();
        using var userManager = NewUserManager(db);
        var user = new ApplicationUser { Id = UserId, UserName = "mfa", Email = "mfa@example.test" };
        Assert.True((await userManager.CreateAsync(user)).Succeeded);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static UserManager<ApplicationUser> NewUserManager(AuthDbContext db)
    {
        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            optionsAccessor: null!,
            passwordHasher: new PasswordHasher<ApplicationUser>(),
            userValidators: new List<IUserValidator<ApplicationUser>>(),
            passwordValidators: new List<IPasswordValidator<ApplicationUser>>(),
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: null!,
            logger: NullLogger<UserManager<ApplicationUser>>.Instance);
        userManager.RegisterTokenProvider(
            TokenOptions.DefaultAuthenticatorProvider, new AuthenticatorTokenProvider<ApplicationUser>());
        return userManager;
    }

    private static (AuthController Controller, IMemoryCache Cache) NewController(
        AuthDbContext db, UserManager<ApplicationUser> userManager)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new AuthController(
            userManager, db, new ConfigurationBuilder().Build(), new MigrationService(null!),
            new AutomationJobQueue(), NullLogger<AuthController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, UserId) }, "TestAuth")),
                RequestServices = new ServiceCollection().AddSingleton<IMemoryCache>(cache).BuildServiceProvider(),
            },
        };
        return (controller, cache);
    }

    /// <summary>Yanıtın yazdığı oturum çerezindeki damga claim'i; çerez yoksa null.</summary>
    private static string? RenewedStamp(AuthController controller)
    {
        var header = controller.Response.Headers.SetCookie.LastOrDefault(c => c!.StartsWith("namines_token="));
        if (header is null) return null;
        var jwt = header["namines_token=".Length..].Split(';')[0];
        return new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims
            .Single(c => c.Type == AuthController.SecurityStampClaimType).Value;
    }

    private async Task<string> CurrentStampAsync()
    {
        await using var db = new AuthDbContext(_options);
        return await db.Users.Where(u => u.Id == UserId).Select(u => u.SecurityStamp!).SingleAsync();
    }

    /// <summary>RFC 6238 TOTP — kimlik doğrulayıcı uygulamanın göstereceği kod.</summary>
    private static string Totp(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32Key.TrimEnd('=').ToUpperInvariant()
            .Select(ch => Convert.ToString(alphabet.IndexOf(ch), 2).PadLeft(5, '0')));
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();

        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    [Fact]
    public async Task Kurulum_baslatmak_mevcut_oturumu_yeni_damgayla_yeniliyor()
    {
        await using var db = new AuthDbContext(_options);
        using var userManager = NewUserManager(db);
        var (controller, cache) = NewController(db, userManager);
        var stampBefore = await CurrentStampAsync();
        cache.Set($"sstamp:{UserId}", stampBefore);

        Assert.IsType<OkObjectResult>(await controller.StartMfaSetup());

        var stampAfter = await CurrentStampAsync();
        Assert.NotEqual(stampBefore, stampAfter);
        // Yeni çerez GÜNCEL damgayı taşımalı; taşımazsa sonraki istek 401 alır.
        Assert.Equal(stampAfter, RenewedStamp(controller));
        // Önbellekte kalan ESKİ damga, 60 sn boyunca yeni çerezi reddederdi.
        Assert.False(cache.TryGetValue($"sstamp:{UserId}", out _));
    }

    [Fact]
    public async Task Etkinlestirme_ve_kapatma_da_oturumu_dusurmuyor()
    {
        await using var db = new AuthDbContext(_options);
        using var userManager = NewUserManager(db);

        var (setupController, _) = NewController(db, userManager);
        Assert.IsType<OkObjectResult>(await setupController.StartMfaSetup());
        var user = await userManager.FindByIdAsync(UserId);
        var key = await userManager.GetAuthenticatorKeyAsync(user!);

        var (enableController, enableCache) = NewController(db, userManager);
        enableCache.Set($"sstamp:{UserId}", "stale");
        Assert.IsType<OkObjectResult>(await enableController.EnableMfa(new MfaCodeRequest(Totp(key!))));
        Assert.Equal(await CurrentStampAsync(), RenewedStamp(enableController));
        Assert.False(enableCache.TryGetValue($"sstamp:{UserId}", out _));

        var (disableController, _) = NewController(db, userManager);
        Assert.IsType<OkObjectResult>(await disableController.DisableMfa(new MfaCodeRequest(Totp(key!))));
        Assert.Equal(await CurrentStampAsync(), RenewedStamp(disableController));
    }
}
