using Namines.Identity.Infrastructure.Persistence;
using Namines.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Namines.API.Controllers;
using Namines.Billing.Domain;
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
    private const string CustomerId = "cus_test_1";

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
        // Stripe alanlari artik kullanici satirinda degil, faturalama tablolarinda.
        await db.SeedSubscriptionAsync("u1", "active", "pro", stripeSubscriptionId: "sub_1", stripeCustomerId: CustomerId);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private AuthDbContext NewContext() => new(_options);

    private static string SubscriptionEventJson(string eventId, string type, string status, DateTime created) =>
        KitBStripe.SubscriptionEventJson(eventId, type, status, created, CustomerId);

    private async Task<IActionResult> PostAsync(string payload)
    {
        // İmza zaman damgası ŞİMDİ: Stripe'ın tolerans penceresi olayın kendi
        // `created` alanına değil, imzanın zamanına bakıyor. Eski bir olayı
        // "şimdi" imzalamak, sıra dışı teslimatın gerçek hâli.
        await using var db = NewContext();
        var service = KitBStripe.Service(db, KitBStripe.Config());
        var controller = KitBStripe.Controller(service, payload, KitBStripe.SignatureHeader(payload, DateTime.UtcNow));
        return await controller.HandleStripeWebhook(default);
    }

    /// <summary>Kullanici kimligi ile abonelik artik ayri modulde: test ikisini tek gorunumde toplar.</summary>
    private sealed record UserView(string? SubscriptionStatus, UserType Type);

    private async Task<UserView> ReloadUserAsync()
    {
        await using var db = NewContext();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == "u1");
        var subscription = await db.Billing().Subscriptions.AsNoTracking().SingleAsync(s => s.UserId == "u1");
        return new UserView(subscription.Status, user.Type);
    }

    [Fact]
    public async Task Ayni_olay_IKINCI_kez_islenmiyor()
    {
        var created = DateTime.UtcNow.AddMinutes(-5);
        var cancelEvent = SubscriptionEventJson(
            "evt_cancel", "customer.subscription.deleted", "canceled", created);

        Assert.Equal(200, KitBStripe.StatusOf(await PostAsync(cancelEvent)));
        Assert.Equal("canceled", (await ReloadUserAsync()).SubscriptionStatus);

        // Kullanici arada YENIDEN abone oldu.
        await using (var db = NewContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == "u1");
            user.Type = UserType.Corporate;
            await db.SaveChangesAsync();
            await db.SeedSubscriptionAsync("u1", "active", "pro", stripeSubscriptionId: "sub_2", stripeCustomerId: CustomerId);
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

        Assert.Equal(200, KitBStripe.StatusOf(result));
        Assert.Contains("stale", KitBStripe.BodyOf(result));

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

        Assert.Equal(200, KitBStripe.StatusOf(await PostAsync(cancelEvent)));

        var reloaded = await ReloadUserAsync();
        Assert.Equal("canceled", reloaded.SubscriptionStatus);
        Assert.Equal(UserType.Individual, reloaded.Type);

        await using var db = NewContext();
        var recorded = Assert.Single(await db.Billing().StripeWebhookEvents.ToListAsync());
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
        var service = KitBStripe.Service(db, KitBStripe.Config());
        var controller = KitBStripe.Controller(service, payload, "t=1,v1=deadbeef");

        Assert.Equal(400, KitBStripe.StatusOf(await controller.HandleStripeWebhook(default)));

        await using var verify = NewContext();
        Assert.Empty(await verify.Billing().StripeWebhookEvents.ToListAsync());
        Assert.Equal("active", (await ReloadUserAsync()).SubscriptionStatus);
    }
}
