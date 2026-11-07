using Namines.Billing.Application;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Interfaces;
using Namines.Governance.Application;

namespace Namines.Tests.TestSupport;

/// <summary>Governance testlerinin ortak kurulumu: değişiklik isteği servisi gerçek yetki kuralıyla kurulur.</summary>
internal static class KitBGovernance
{
    /// <summary>
    /// Dış bağımlılıklar (göç üretimi, test koşucusu, AI, kota) yalnızca ilgili uçlar için gerekli: oylama ve
    /// listeleme yolları bunlara dokunmaz, o yüzden varsayılan null (dokunulursa yüksek sesle patlar).
    /// </summary>
    public static ChangeRequestService ChangeRequests(
        this ModuleKit kit,
        IMigrationService? migration = null,
        IBranchTestRunner? testRunner = null,
        IAIService? ai = null,
        IAiQuota? quota = null) =>
        new(kit.Governance, kit.DefaultAuthority, kit.Projects, kit.Branches, kit.Identity,
            migration!, testRunner!, ai!, quota!);
}
