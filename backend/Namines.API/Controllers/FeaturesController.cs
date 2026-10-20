using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Interfaces;

namespace Namines.API.Controllers;

/// <summary>
/// İstemcilerin (Frontend tuval, menü, araç çubuğu) sistemdeki özelliklerin
/// durumunu ve kill-switch durumlarını sorgulamasını sağlayan genel uç.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FeaturesController : ControllerBase
{
    private readonly IFeatureFlagService _featureFlags;

    public FeaturesController(IFeatureFlagService featureFlags)
    {
        _featureFlags = featureFlags;
    }

    /// <summary>
    /// Aktif özellik bayraklarını döner.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetFlags()
    {
        var flags = await _featureFlags.GetAllFlagsAsync();
        return Ok(flags.Select(f => new
        {
            f.FeatureKey,
            f.DisplayName,
            f.Description,
            f.Category,
            f.IsEnabled,
            f.Mode,
            f.NoticeMessage,
            f.UpdatedAt
        }));
    }
}
