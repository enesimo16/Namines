using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Namines.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/user/policy")]
    public class PolicyController : ControllerBase
    {
        private readonly IAiPolicies _policies;
        private readonly IIdentityDirectory _identity;

        public PolicyController(IAiPolicies policies, IIdentityDirectory identity)
        {
            _policies = policies;
            _identity = identity;
        }

        [HttpGet]
        public async Task<IActionResult> GetPolicy()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Eski oturumlar silinmiş kullanıcıya ait olabilir: politika satırı kullanıcıya FK ile bağlı, çökmesin.
            if (!(await _identity.GetSessionStateAsync(userId)).Exists)
                return Unauthorized(new { Message = "User session is invalid. Please log in again." });

            // Kayıt yoksa (önceden var olan hesaplar) varsayılanlarla açılır. Varsayılanlar UserAIPolicy'de tanımlı.
            var policy = await _policies.EnsureAsync(userId);

            return Ok(new UserAIPolicyDto
            {
                SmartSeed = (int)policy.SmartSeed,
                Documentation = (int)policy.Documentation,
                Scaffolding = (int)policy.Scaffolding,
                SchemaGeneration = (int)policy.SchemaGeneration,
                SchemaRevision = (int)policy.SchemaRevision,
                DbaAnalysis = (int)policy.DbaAnalysis,
                Migration = (int)policy.Migration,
                Voice = (int)policy.Voice,
                // Kayıtlı değer yoksa VARSAYILANLAR döner, null değil: arayüz boş formla açılmasın.
                Advanced = AiAdvancedSettings.Parse(policy.AdvancedJson)
            });
        }

        [HttpPut]
        public async Task<IActionResult> UpdatePolicy([FromBody] UserAIPolicyDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            if (!(await _identity.GetSessionStateAsync(userId)).Exists)
                return Unauthorized(new { Message = "User session is invalid. Please log in again." });

            await _policies.SaveAsync(userId,
                new AiPolicySnapshot(
                    (AIMode)model.SmartSeed, (AIMode)model.Documentation, (AIMode)model.Scaffolding,
                    (AIMode)model.SchemaGeneration, (AIMode)model.SchemaRevision, (AIMode)model.DbaAnalysis,
                    (AIMode)model.Migration, (AIMode)model.Voice, null),
                // Advanced null ise DOKUNULMUYOR: model seçimlerini kaydeden eski istemci gelişmiş ayarları sıfırlamasın.
                model.Advanced?.ToJson());

            return Ok(new { Message = "AI Policy updated successfully." });
        }
    }

    public class UserAIPolicyDto
    {
        public int SmartSeed { get; set; }
        public int Documentation { get; set; }
        public int Scaffolding { get; set; }
        public int SchemaGeneration { get; set; }
        public int SchemaRevision { get; set; }
        public int DbaAnalysis { get; set; }
        public int Migration { get; set; }
        public int Voice { get; set; }

        public AiAdvancedSettings? Advanced { get; set; }
    }
}
