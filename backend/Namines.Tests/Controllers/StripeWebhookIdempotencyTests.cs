using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// Stripe webhook'unun TEKRAR İLETİM ve SIRA DIŞI teslimat davranışı —
/// docs/last-review M-2.
///
/// <b>Bulunan açık:</b> işleyici olay kimliğine hiç bakmıyordu. Stripe aynı olayı
/// başarılı bir 200'den sonra bile tekrar gönderebiliyor (ağ zaman aşımı, kendi
/// yeniden deneme kuyruğu, panelden elle "resend"). Tekrar iletilen bir
/// <c>customer.subscription.updated / active</c> olayı, arada iptal edilmiş bir
/// aboneliği <b>sessizce geri getiriyordu</b>: kullanıcı ödemediği hâlde ücretli
/// plana dönüyordu.
///
/// Testler GERÇEK imzalı gövdeler üretiyor — imza doğrulaması atlanmıyor, çünkü
/// atlanırsa test ettiğimiz şey uçun kendisi olmaz.
/// </summary>
public sealed class StripeWebhookIdempotencyTests : IAsyncLifetime
{
    private const string Secret = "whsec_test_secret_for_unit_tests";
    private const string CustomerId = "cus_test_1";

    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new ApplicationUser
        {
            Id = "u1", UserName = "u1", NormalizedUserName = "U1",
            StripeCustomerId = CustomerId,
            StripeSubscriptionId = "sub_1",
            SubscriptionStatus = "active",
            PlanCode = "pro",
            Type = UserType.Corporate,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AuthDbContext NewContext() => new(_options);

    private static UserManager<ApplicationUser> NewUserManager(AuthDbContext db) =>
        new(new UserStore<ApplicationUser>(db),
            optionsAccessor: null!,
            passwordHasher: new PasswordHasher<ApplicationUser>(),
            userValidators: new List<IUserValidator<ApplicationUser>>(),
            passwordValidators: new List<IPasswordValidator<ApplicationUser>>(),
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: null!,
            logger: NullLogger<UserManager<ApplicationUser>>.Instance);

    /// <summary>
    /// Stripe'ın imza başlığı: <c>t=&lt;unix&gt;,v1=&lt;HMAC-SHA256(t + "." + gövde)&gt;</c>.
    /// Elle hesaplanıyor, çünkü doğrulamayı SDK yapıyor ve onu atlatmak testin
    /// kanıt değerini yok ederdi.
    /// </summary>
    private static string SignatureHeader(string payload, DateTime signedAt)
    {
        var timestamp = ((long)(signedAt - DateTime.UnixEpoch).TotalSeconds)
            .ToString(CultureInfo.InvariantCulture);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var signature = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();

        return $"t={timestamp},v1={signature}";
    }

    /// <summary>Bir <c>customer.subscription.updated|deleted</c> olayının JSON gövdesi.</summary>
    private static string SubscriptionEventJson(
        string eventId, string type, string status, DateTime created) =>
        $@"{{
  ""id"": ""{eventId}"",
  ""object"": ""event"",
  ""api_version"": ""2024-06-20"",
  ""livemode"": false,
  ""pending_webhooks"": 1,
  ""request"": null,
  ""type"": ""{type}"",
  ""created"": {(long)(created - DateTime.UnixEpoch).TotalSeconds},
  ""data"": {{
    ""object"": {{
      ""id"": ""sub_1"",
      ""object"": ""subscription"",
      ""customer"": ""{CustomerId}"",
      ""status"": ""{status}"",
      ""current_period_end"": {(long)(created.AddDays(30) - DateTime.UnixEpoch).TotalSeconds},
      ""items"": {{ ""object"": ""list"", ""data"": [] }}
    }}
  }}
}}";

    private static StripeWebhookController NewController(
        AuthDbContext db, UserManager<ApplicationUser> userManager, string payload, DateTime signedAt)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:WebhookSecret"] = Secret })
            .Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Headers["Stripe-Signature"] = SignatureHeader(payload, signedAt);

        return new StripeWebhookController(
            userManager, db, config, NullLogger<StripeWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private async Task<IActionResult> PostAsync(string payload)
    {
        // İmza zaman damgası ŞİMDİ: Stripe'ın tolerans penceresi olayın kendi
        // `created` alanına değil, imzanın zamanına bakıyor. Eski bir olayı
        // "şimdi" imzalamak, sıra dışı teslimatın gerçek hâli.
        await using var db = NewContext();
        var userManager = NewUserManager(db);
        var controller = NewController(db, userManager, payload, DateTime.UtcNow);
        return await controller.HandleStripeWebhook();
    }

    private async Task<ApplicationUser> ReloadUserAsync()
    {
        await using var db = NewContext();
        return await db.Users.SingleAsync(u => u.Id == "u1");
    }

    [Fact]
    public async Task Ayni_olay_IKINCI_kez_islenmiyor()
    {
        var created = DateTime.UtcNow.AddMinutes(-5);
        var cancelEvent = SubscriptionEventJson(
            "evt_cancel", "customer.subscription.deleted", "canceled", created);

        Assert.IsType<OkObjectResult>(await PostAsync(cancelEvent));
        Assert.Equal("canceled", (await ReloadUserAsync()).SubscriptionStatus);

        // Kullanici arada YENIDEN abone oldu.
        await using (var db = NewContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == "u1");
            user.SubscriptionStatus = "active";
            user.Type = UserType.Corporate;
            await db.SaveChangesAsync();
        }

        // Stripe ayni iptal olayini TEKRAR gonderiyor.
        await PostAsync(cancelEvent);

        // Yeniden abonelik BOZULMAMALI.
        var reloaded = await ReloadUserAsync();
        Assert.Equal("active", reloaded.SubscriptionStatus);
        Assert.Equal(UserType.Corporate, reloaded.Type);
    }

    [Fact]
    public async Task SIRA_DISI_gelen_ESKI_active_olayi_iptali_geri_ALMIYOR()
    {
        // Asıl açık: idempotency tek başına bunu çözmüyor, çünkü olay kimliği
        // FARKLI. Stripe teslimat sırasını garanti etmiyor.
        var oldActive = SubscriptionEventJson(
            "evt_old_active", "customer.subscription.updated", "active",
            DateTime.UtcNow.AddMinutes(-30));

        var newerCancel = SubscriptionEventJson(
            "evt_cancel", "customer.subscription.deleted", "canceled",
            DateTime.UtcNow.AddMinutes(-5));

        // Once YENI iptal olayi geliyor.
        await PostAsync(newerCancel);
        Assert.Equal("canceled", (await ReloadUserAsync()).SubscriptionStatus);

        // Sonra ESKI "active" olayi geliyor (gecikmis teslimat).
        var result = await PostAsync(oldActive);

        Assert.IsType<OkObjectResult>(result);

        var reloaded = await ReloadUserAsync();
        Assert.Equal("canceled", reloaded.SubscriptionStatus);
        Assert.Equal(UserType.Individual, reloaded.Type);
    }

    [Fact]
    public async Task Ilk_olay_NORMAL_isleniyor()
    {
        // Düzeltme meşru akışı kırmamalı.
        var cancelEvent = SubscriptionEventJson(
            "evt_first", "customer.subscription.deleted", "canceled", DateTime.UtcNow);

        Assert.IsType<OkObjectResult>(await PostAsync(cancelEvent));

        var reloaded = await ReloadUserAsync();
        Assert.Equal("canceled", reloaded.SubscriptionStatus);
        Assert.Equal(UserType.Individual, reloaded.Type);

        await using var db = NewContext();
        var recorded = Assert.Single(await db.StripeWebhookEvents.ToListAsync());
        Assert.Equal("evt_first", recorded.Id);
        Assert.True(recorded.LatestProcessedForCustomer);
    }

    [Fact]
    public async Task Gecersiz_imza_HICBIR_sey_kaydetmiyor()
    {
        // Kayıt yazma imza doğrulamasından SONRA olmak zorunda: önce olsaydı,
        // kimliği doğrulanmamış biri uydurma olay kimlikleriyle tabloyu
        // doldurup gerçek olayları "zaten işlenmiş" saydırabilirdi.
        var payload = SubscriptionEventJson(
            "evt_forged", "customer.subscription.deleted", "canceled", DateTime.UtcNow);

        await using var db = NewContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        httpContext.Request.Headers["Stripe-Signature"] = "t=1,v1=deadbeef";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:WebhookSecret"] = Secret })
            .Build();

        var controller = new StripeWebhookController(
            NewUserManager(db), db, config, NullLogger<StripeWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        Assert.IsType<BadRequestObjectResult>(await controller.HandleStripeWebhook());

        await using var verify = NewContext();
        Assert.Empty(await verify.StripeWebhookEvents.ToListAsync());
        Assert.Equal("active", (await ReloadUserAsync()).SubscriptionStatus);
    }
}
