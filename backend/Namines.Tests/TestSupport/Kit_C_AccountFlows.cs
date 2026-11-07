using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Billing.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Models.Auth;
using Namines.Identity.Application;
using Namines.Infrastructure.Services;

namespace Namines.Tests.TestSupport;

/// <summary>
/// Hesap silme / abonelik iptali akışlarını gerçek PostgreSQL'de sınayan testlerin kurulumu:
/// iptal istemcisi Billing servisine, kullanıcı silme Identity'ye ait; ikisini aynı kitten kuruyoruz.
/// </summary>
internal static class Kit_C_AccountFlows
{
    /// <summary>Abonelik iptalcisi verilebilen Billing servisi; iptal tamamlanınca denetim dinleyicisi çağrılır.</summary>
    public static BillingAccountService BillingWithCanceller(this ModuleKit kit, ISubscriptionCanceller? canceller) =>
        new(kit.Billing, kit.Identity, kit.AiQuota(), NullLogger<BillingAccountService>.Instance, canceller,
            new ISubscriptionCancellationObserver[] { kit.GovernanceAudit });

    public static AccountDeletionService AccountDeletion(
        this ModuleKit kit, UserManager<ApplicationUser> users, ProjectDeletionService projects, ISubscriptionCanceller? canceller)
    {
        var billing = kit.BillingWithCanceller(canceller);
        return new AccountDeletionService(
            new AccountOrganizationsService(kit.Db, users), billing, billing, kit.GovernanceAudit, kit.GovernanceAudit,
            projects, kit.HubService, kit.Transaction, NullLogger<AccountDeletionService>.Instance,
            kit.Feedback, kit.Projects, kit.GatewayMaintenance);
    }

    public static IIdentityMaintenance IdentityMaintenance(this ModuleKit kit) => new IdentityMaintenanceService(kit.Db);
}
