using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <summary>
/// Sistemdeki araç ve özelliklerin dinamik olarak açılıp kapatılmasını (kill-switch) yöneten servis.
/// </summary>
public interface IFeatureFlagService
{
    Task<IReadOnlyList<AdminFeatureFlag>> GetAllFlagsAsync(CancellationToken ct = default);

    Task<AdminFeatureFlag?> GetFlagAsync(string featureKey, CancellationToken ct = default);

    Task UpdateFlagAsync(
        string featureKey,
        bool isEnabled,
        string mode,
        string? noticeMessage = null,
        CancellationToken ct = default);

    /// <summary>
    /// Yüksek performanslı bellek içi denetim.
    /// </summary>
    bool IsFeatureEnabled(string featureKey, out string displayMode, out string noticeMessage);
}
