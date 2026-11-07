using Namines.Identity.Infrastructure.Persistence;
using Namines.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    private const string CustomerId = "cus_atomic_1";

    private SqliteConnection _connection = null!;
    private DbContextOptions<AuthDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_connection).Options;

        await using var db = new AuthDbContext(_options);
        await db.EnsureModulesCreatedAsync();

        db.Users.Add(new ApplicationUser
        {
            Id = "u1", UserName = "u1", NormalizedUserName = "U1",
            Type = UserType.Corporate,
        });
        await db.SaveChangesAsync();
        await db.SeedSubscriptionAsync("u1", "active", "pro", stripeSubscriptionId: "sub_1", stripeCustomerId: CustomerId);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static string CancelEventJson(string eventId, DateTime created) =>
        KitBStripe.SubscriptionEventJson(eventId, "customer.subscription.deleted", "canceled", created, CustomerId);

    private async Task<IActionResult> PostAsync(string payload, bool failingHandler)
    {
        await using var db = new AuthDbContext(_options);
        // Kimlik yazimi patlarsa islem geri alinmali: kayit ile etki birlikte gider.
        var identity = failingHandler
            ? FailingIdentityProxy.Wrap(TestModules.For(db).Identity, db, nameof(Namines.BuildingBlocks.Contracts.IIdentityDirectory.SetUserTypeAsync))
            : null;
        var service = KitBStripe.Service(db, KitBStripe.Config(), identity: identity);
        var controller = KitBStripe.Controller(service, payload, KitBStripe.SignatureHeader(payload, DateTime.UtcNow));

        return await controller.HandleStripeWebhook(default);
    }

    [Fact]
    public async Task Handler_throws_event_is_not_recorded_and_the_retry_is_processed()
    {
        var payload = CancelEventJson("evt_retry", DateTime.UtcNow.AddMinutes(-1));

        // 1) İşleyici başarısız → 500 (Stripe yeniden dener), kayıt YOK, kullanıcı değişmedi.
        var first = await PostAsync(payload, failingHandler: true);
        Assert.Equal(500, KitBStripe.StatusOf(first));

        await using (var db = new AuthDbContext(_options))
        {
            Assert.Empty(await db.Billing().StripeWebhookEvents.ToListAsync());
            var user = await db.Users.SingleAsync(u => u.Id == "u1");
            Assert.Equal("active", (await db.Billing().Subscriptions.SingleAsync(s => s.UserId == "u1")).Status);
            Assert.Equal(UserType.Corporate, user.Type);
        }

        // 2) Aynı olayın yeniden iletimi → "duplicate" DEĞİL, gerçekten işleniyor.
        var retry = await PostAsync(payload, failingHandler: false);
        Assert.Equal(200, KitBStripe.StatusOf(retry));
        Assert.DoesNotContain("duplicate", KitBStripe.BodyOf(retry));

        await using (var db = new AuthDbContext(_options))
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == "u1");
            Assert.Equal("canceled", (await db.Billing().Subscriptions.SingleAsync(s => s.UserId == "u1")).Status);
            Assert.Equal(UserType.Individual, user.Type);
            Assert.Single(await db.Billing().StripeWebhookEvents.ToListAsync());
        }

        // 3) Başarıdan sonraki tekrar iletim artık duplicate.
        var again = await PostAsync(payload, failingHandler: false);
        Assert.Equal(200, KitBStripe.StatusOf(again));
        Assert.Contains("duplicate", KitBStripe.BodyOf(again));
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
