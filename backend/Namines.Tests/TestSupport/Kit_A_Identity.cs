using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Billing.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Models.Auth;
using Namines.Identity.Application;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Infrastructure.Services;

namespace Namines.Tests.TestSupport;

/// <summary>
/// Kimlik/oturum/yönetim test kümesinin ortak kurulumu: üretimdeki DI bağlantısının aynısı, tek yerde.
/// </summary>
internal static class Kit_A_Identity
{
    public static ProjectMembershipService Members(AuthDbContext db)
    {
        var kit = TestModules.For(db);
        return new ProjectMembershipService(kit.DefaultAuthority, kit.Lifecycle, new OrganizationMembersService(db), kit.Identity, kit.GatewayMaintenance, kit.Transaction);
    }

    public static TeamService Team(AuthDbContext db) => new(db, TestModules.For(db).BillingAccounts);

    /// <summary>Yonetim denetleyicisinin kimlik/faturalama/yonetisim bagimliliklari, uretimdeki sozlesmelerle.</summary>
    public static (IIdentityAdmin Users, IBillingAccounts Billing, IGovernanceAdminQueries Governance) AdminContracts(AuthDbContext db)
    {
        var kit = TestModules.For(db);
        return (new IdentityAdminService(db), kit.BillingAccounts, kit.GovernanceAudit);
    }

    /// <summary>
    /// Hesap silme servisi, sözleşmelerle. İptal edici Billing'in içine verilir: üretimde Stripe çağrısı
    /// BillingAccountService'in outbox işleyicisinden geçer, hesap silme ona sözleşmeden ulaşır.
    /// </summary>
    public static AccountDeletionService AccountDeletion(
        AuthDbContext db, UserManager<ApplicationUser> users, ProjectDeletionService projects, ISubscriptionCanceller? canceller = null)
    {
        var kit = TestModules.For(db);
        var billing = new BillingAccountService(kit.Billing, kit.Identity, kit.AiQuota(),
            NullLogger<BillingAccountService>.Instance, canceller, new ISubscriptionCancellationObserver[] { kit.GovernanceAudit });
        return new AccountDeletionService(
            new AccountOrganizationsService(db, users), billing, billing, kit.GovernanceAudit, kit.GovernanceAudit,
            projects, kit.HubService, kit.Transaction, NullLogger<AccountDeletionService>.Instance,
            kit.Feedback, kit.Projects, kit.GatewayMaintenance);
    }
}
