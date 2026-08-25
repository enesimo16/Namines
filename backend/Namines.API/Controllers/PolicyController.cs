using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
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
        private readonly AuthDbContext _context;

        public PolicyController(AuthDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetPolicy()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // Check if user actually exists in the database to prevent FOREIGN KEY crashes for legacy sessions
            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
                return Unauthorized(new { Message = "User session is invalid. Please log in again." });

            var policy = await _context.UserAIPolicies.FirstOrDefaultAsync(p => p.UserId == userId);
            if (policy == null)
            {
                // Fallback / Auto-provision for pre-existing accounts
                // Varsayilanlar UserAIPolicy'de tanimli (Flash agirlikli). Burada
                // tekrar yazmak, iki yerin ayrismasi demekti: bir tarafi
                // degistirip digerini unutmak, "ayarlara hic dokunmadim ama
                // farkli davraniyor" seklinde ortaya cikardi.
                policy = new UserAIPolicy
                {
                    UserId = userId,
                    UpdatedAt = DateTime.UtcNow
                };
                await _context.UserAIPolicies.AddAsync(policy);
                await _context.SaveChangesAsync();
            }

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
                // Kayitli deger yoksa VARSAYILANLAR donuyor, null degil: arayuz
                // bos bir formla acilsaydi, kullanici kaydetmedigi surece hangi
                // ayarin gecerli oldugunu goremezdi.
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

            // Check if user actually exists in the database to prevent FOREIGN KEY crashes for legacy sessions
            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
                return Unauthorized(new { Message = "User session is invalid. Please log in again." });

            var policy = await _context.UserAIPolicies.FirstOrDefaultAsync(p => p.UserId == userId);
            if (policy == null)
            {
                policy = new UserAIPolicy { UserId = userId };
                await _context.UserAIPolicies.AddAsync(policy);
            }

            policy.SmartSeed = (AIMode)model.SmartSeed;
            policy.Documentation = (AIMode)model.Documentation;
            policy.Scaffolding = (AIMode)model.Scaffolding;
            policy.SchemaGeneration = (AIMode)model.SchemaGeneration;
            policy.SchemaRevision = (AIMode)model.SchemaRevision;
            policy.DbaAnalysis = (AIMode)model.DbaAnalysis;
            policy.Migration = (AIMode)model.Migration;
            policy.Voice = (AIMode)model.Voice;
            // Advanced null ise DOKUNULMUYOR: model secimlerini kaydeden eski bir
            // istemci, farkinda olmadan gelismis ayarlari varsayilana dondururdu.
            if (model.Advanced is not null)
                policy.AdvancedJson = model.Advanced.ToJson();
            policy.UpdatedAt = DateTime.UtcNow;

            _context.UserAIPolicies.Update(policy);
            await _context.SaveChangesAsync();

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

        /// <summary>Gelismis AI tercihleri. Null ise mevcut kayit korunur.</summary>
        public AiAdvancedSettings? Advanced { get; set; }
    }
}
