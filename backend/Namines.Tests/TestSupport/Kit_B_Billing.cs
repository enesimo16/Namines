using Microsoft.EntityFrameworkCore;
using Namines.Billing.Application;
using Namines.Billing.Domain;
using Namines.BuildingBlocks.Contracts;
using Namines.Identity.Infrastructure.Persistence;

namespace Namines.Tests.TestSupport;

/// <summary>
/// Faturalama testlerinin ortak tohumu. Stripe alanları artık kullanıcı satırında değil: eski testlerin
/// <c>new ApplicationUser { PlanCode = ... }</c> kurulumu burada abonelik/müşteri satırına çevrilir.
/// </summary>
internal static class KitBBilling
{
    /// <summary>Saklama temizliğinin kimlik ayağı: kit bunu sunmuyor, servis doğrudan kurulur.</summary>
    public static IIdentityMaintenance IdentityMaintenance(this ModuleKit kit) =>
        new Namines.Identity.Application.IdentityMaintenanceService(kit.Db);

    /// <summary>Faturalama hesap servisi, dış iptal çağrısı ve denetim dinleyicisiyle: hesap silme akışı bunu ister.</summary>
    internal static BillingAccountService BillingAccountsWith(this ModuleKit kit, ISubscriptionCanceller? canceller) =>
        new(kit.Billing, kit.Identity, kit.AiQuota(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BillingAccountService>.Instance, canceller,
            new ISubscriptionCancellationObserver[] { kit.GovernanceAudit });

    /// <summary>Hesap silme planının tüm ayakları sözleşmelerden: kimlik, faturalama ve denetim modülleri gerçek servislerle.</summary>
    internal static Namines.Infrastructure.Services.AccountDeletionService AccountDeletion(
        this ModuleKit kit,
        Microsoft.AspNetCore.Identity.UserManager<Namines.Core.Models.Auth.ApplicationUser> users,
        Namines.Infrastructure.Services.ProjectDeletionService projects,
        ISubscriptionCanceller? canceller = null)
    {
        var billing = kit.BillingAccountsWith(canceller);
        return new Namines.Infrastructure.Services.AccountDeletionService(
            new Namines.Identity.Application.AccountOrganizationsService(kit.Db, users), billing, billing,
            kit.GovernanceAudit, kit.GovernanceAudit, projects, kit.HubService, kit.Transaction,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Namines.Infrastructure.Services.AccountDeletionService>.Instance,
            kit.Feedback, kit.Projects, kit.GatewayMaintenance);
    }

    /// <summary>Kullanıcının abonelik satırını yazar (varsa üzerine yazar); isteğe bağlı müşteri eşlemesi ekler.</summary>
    public static async Task SeedSubscriptionAsync(
        this AuthDbContext db,
        string userId,
        string? status,
        string? planCode,
        string? stripeSubscriptionId = null,
        string? stripeCustomerId = null,
        DateTime? currentPeriodEnd = null)
    {
        var billing = db.Billing();
        var existing = await billing.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
        if (existing is null)
        {
            billing.Subscriptions.Add(new BillingSubscription
            {
                UserId = userId,
                Status = status,
                PlanCode = planCode,
                StripeSubscriptionId = stripeSubscriptionId,
                CurrentPeriodEnd = currentPeriodEnd,
            });
        }
        else
        {
            existing.Status = status;
            existing.PlanCode = planCode;
            existing.StripeSubscriptionId = stripeSubscriptionId;
            existing.CurrentPeriodEnd = currentPeriodEnd;
        }

        if (stripeCustomerId is not null && !await billing.Customers.AnyAsync(c => c.UserId == userId))
            billing.Customers.Add(new BillingCustomer { UserId = userId, StripeCustomerId = stripeCustomerId });

        await billing.SaveChangesAsync();
        billing.ChangeTracker.Clear();
    }
}
