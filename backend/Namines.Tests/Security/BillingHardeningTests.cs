using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
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

namespace Namines.Tests.Security;

/// <summary>
/// Stripe webhook'unun ATOMİKLİĞİ (Oct-2 güvenlik turu A-1).
///
/// Eski davranış: tekrar-iletim kaydı işleyiciden ÖNCE kalıcı yazılıyordu. İşleyici
/// patlarsa Stripe'ın yeniden denemesi "zaten işlenmiş" diye 200 alıyor, ödeyen
/// müşteri hiç yükseltilmiyordu. Testler GERÇEK imzalı gövde kullanıyor.
/// </summary>
public sealed class BillingHardeningTests : IAsyncLifetime
{
    private const string Secret = "whsec_test_secret_for_unit_tests";
    private const string CustomerId = "cus_atomic_1";

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

    /// <summary>Her kullanıcı güncellemesini reddeden doğrulayıcı: işleyiciyi başarısız yapar.</summary>
    private sealed class RejectingValidator : IUserValidator<ApplicationUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "forced failure" }));
    }

    private static UserManager<ApplicationUser> NewUserManager(AuthDbContext db, bool failing) =>
        new(new UserStore<ApplicationUser>(db),
            optionsAccessor: null!,
            passwordHasher: new PasswordHasher<ApplicationUser>(),
            userValidators: failing
                ? new List<IUserValidator<ApplicationUser>> { new RejectingValidator() }
                : new List<IUserValidator<ApplicationUser>>(),
            passwordValidators: new List<IPasswordValidator<ApplicationUser>>(),
            keyNormalizer: new UpperInvariantLookupNormalizer(),
            errors: new IdentityErrorDescriber(),
            services: null!,
            logger: NullLogger<UserManager<ApplicationUser>>.Instance);

    private static string SignatureHeader(string payload, DateTime signedAt)
    {
        var timestamp = ((long)(signedAt - DateTime.UnixEpoch).TotalSeconds)
            .ToString(CultureInfo.InvariantCulture);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var signature = Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();

        return $"t={timestamp},v1={signature}";
    }

    private static string CancelEventJson(string eventId, DateTime created) =>
        $@"{{
  ""id"": ""{eventId}"",
  ""object"": ""event"",
  ""api_version"": ""2024-06-20"",
  ""livemode"": false,
  ""pending_webhooks"": 1,
  ""request"": null,
  ""type"": ""customer.subscription.deleted"",
  ""created"": {(long)(created - DateTime.UnixEpoch).TotalSeconds},
  ""data"": {{
    ""object"": {{
      ""id"": ""sub_1"",
      ""object"": ""subscription"",
      ""customer"": ""{CustomerId}"",
      ""status"": ""canceled"",
      ""current_period_end"": {(long)(created.AddDays(30) - DateTime.UnixEpoch).TotalSeconds},
      ""items"": {{ ""object"": ""list"", ""data"": [] }}
    }}
  }}
}}";

    private async Task<IActionResult> PostAsync(string payload, bool failingHandler)
    {
        await using var db = new AuthDbContext(_options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:WebhookSecret"] = Secret })
            .Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.Headers["Stripe-Signature"] = SignatureHeader(payload, DateTime.UtcNow);

        var controller = new StripeWebhookController(
            NewUserManager(db, failingHandler), db, config, NullLogger<StripeWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        return await controller.HandleStripeWebhook();
    }

    [Fact]
    public async Task Handler_throws_event_is_not_recorded_and_the_retry_is_processed()
    {
        var payload = CancelEventJson("evt_retry", DateTime.UtcNow.AddMinutes(-1));

        // 1) İşleyici başarısız → 500 (Stripe yeniden dener), kayıt YOK, kullanıcı değişmedi.
        var first = await PostAsync(payload, failingHandler: true);
        Assert.Equal(500, Assert.IsType<ObjectResult>(first).StatusCode);

        await using (var db = new AuthDbContext(_options))
        {
            Assert.Empty(await db.StripeWebhookEvents.ToListAsync());
            var user = await db.Users.SingleAsync(u => u.Id == "u1");
            Assert.Equal("active", user.SubscriptionStatus);
            Assert.Equal(UserType.Corporate, user.Type);
        }

        // 2) Aynı olayın yeniden iletimi → "duplicate" DEĞİL, gerçekten işleniyor.
        var retry = await PostAsync(payload, failingHandler: false);
        var ok = Assert.IsType<OkObjectResult>(retry);
        Assert.DoesNotContain("duplicate", ok.Value!.ToString()!);

        await using (var db = new AuthDbContext(_options))
        {
            var user = await db.Users.SingleAsync(u => u.Id == "u1");
            Assert.Equal("canceled", user.SubscriptionStatus);
            Assert.Equal(UserType.Individual, user.Type);
            Assert.Single(await db.StripeWebhookEvents.ToListAsync());
        }

        // 3) Başarıdan sonraki tekrar iletim artık duplicate.
        var again = Assert.IsType<OkObjectResult>(await PostAsync(payload, failingHandler: false));
        Assert.Contains("duplicate", again.Value!.ToString()!);
    }

    private sealed class FakeDbException(string? sqlState) : DbException("fake")
    {
        public override string? SqlState => sqlState;
    }

    [Fact]
    public void Only_a_real_unique_violation_is_treated_as_a_duplicate()
    {
        Assert.True(DbUniqueViolation.IsUniqueViolation(
            new DbUpdateException("x", new FakeDbException("23505"))));

        // Başka bir kısıt / bağlantı hatası "duplicate" sayılmamalı.
        Assert.False(DbUniqueViolation.IsUniqueViolation(
            new DbUpdateException("x", new FakeDbException("23503"))));
        Assert.False(DbUniqueViolation.IsUniqueViolation(
            new DbUpdateException("x", new FakeDbException("08006"))));
        Assert.False(DbUniqueViolation.IsUniqueViolation(new DbUpdateException("x")));
        Assert.False(DbUniqueViolation.IsUniqueViolation(new InvalidOperationException("x")));
    }
}
