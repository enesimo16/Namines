using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Namines.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuthDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            AuthDbContext context,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
                return BadRequest(new { Message = "Bu e-posta adresiyle kayıtlı bir kullanıcı zaten mevcut." });

            // All new users start as Free (Individual) tier.
            // Pro (Corporate) status is ONLY granted by the Stripe payment webhook
            // after a successful subscription — it can never be self-assigned at registration.
            var userType = UserType.Individual;

            var user = new ApplicationUser
            {
                UserName = model.Username ?? model.Email,
                Email = model.Email,
                Type = userType,
                CompanyName = model.CompanyName,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return BadRequest(new { Message = $"Kayıt başarısız: {errors}" });
            }

            // Automatically provision a default UserAIPolicy for this user
            var defaultPolicy = new UserAIPolicy
            {
                UserId = user.Id,
                SmartSeed = AIMode.HighMixtral,
                Documentation = AIMode.HighMixtral,
                Scaffolding = AIMode.HighMixtral,
                SchemaGeneration = AIMode.HighMixtral,
                SchemaRevision = AIMode.HighMixtral,
                DbaAnalysis = AIMode.HighMixtral,
                Migration = AIMode.HighMixtral,
                Voice = AIMode.HighMixtral,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.UserAIPolicies.AddAsync(defaultPolicy);

            // Automatically provision a default UserAIQuota for this user
            var defaultQuota = new UserAIQuota
            {
                UserId = user.Id,
                DailyLimit = 100, // 100% percentage-based credits
                DailyUsageCount = 0,
                LastResetDate = DateTime.UtcNow
            };
            await _context.UserAIQuotas.AddAsync(defaultQuota);
            await _context.SaveChangesAsync();

            var quotaInfo = new
            {
                dailyLimit = defaultQuota.DailyLimit,
                used = defaultQuota.DailyUsageCount,
                remaining = Math.Max(0, defaultQuota.DailyLimit - defaultQuota.DailyUsageCount),
                resetAt = defaultQuota.LastResetDate.AddHours(3).Date.AddDays(1).AddHours(-3).ToString("o")
            };

            var token = GenerateJwtToken(user);
            SetAuthCookie(token);
            return Ok(new
            {
                Token = token,
                User = new
                {
                    Username = user.UserName,
                    Email = user.Email,
                    Type = user.Type.ToString().ToLower(),
                    CompanyName = user.CompanyName
                },
                Quota = quotaInfo
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null || !await _userManager.CheckPasswordAsync(user, model.Password))
            {
                return Unauthorized(new { Message = "E-posta veya şifre hatalı." });
            }

            var quota = await _context.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == user.Id);
            if (quota == null)
            {
                quota = new UserAIQuota
                {
                    UserId = user.Id,
                    DailyLimit = 100,
                    DailyUsageCount = 0,
                    LastResetDate = DateTime.UtcNow
                };
                await _context.UserAIQuotas.AddAsync(quota);
                await _context.SaveChangesAsync();
            }
            else
            {
                if (quota.DailyLimit < 100)
                {
                    quota.DailyLimit = 100;
                }

                if (quota.LastResetDate.AddHours(3).Date < DateTime.UtcNow.AddHours(3).Date)
                {
                    quota.DailyUsageCount = 0;
                    quota.LastResetDate = DateTime.UtcNow;
                }
                _context.UserAIQuotas.Update(quota);
                await _context.SaveChangesAsync();
            }

            var quotaInfo = new
            {
                dailyLimit = quota.DailyLimit,
                used = quota.DailyUsageCount,
                remaining = Math.Max(0, quota.DailyLimit - quota.DailyUsageCount),
                resetAt = quota.LastResetDate.Date.AddDays(1).ToString("o")
            };

            var token = GenerateJwtToken(user);
            SetAuthCookie(token);
            return Ok(new
            {
                Token = token,
                User = new
                {
                    Username = user.UserName,
                    Email = user.Email,
                    Type = user.Type.ToString().ToLower(),
                    CompanyName = user.CompanyName
                },
                Quota = quotaInfo
            });
        }

        [Authorize]
        [HttpPost("sync")]
        public async Task<IActionResult> SyncProjects([FromBody] List<SyncProjectDto> projects)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Unauthorized();

            int savedCount = 0;
            foreach (var projDto in projects)
            {
                // Check if this project ID belongs to the current user
                var existing = await _context.CloudProjects
                    .FirstOrDefaultAsync(p => p.Id == projDto.Id && p.UserId == userId);

                if (existing != null)
                {
                    // Update existing record owned by this user
                    existing.Name = projDto.Name;
                    existing.DbType = projDto.DbType;
                    existing.SchemaJson = projDto.SchemaJson;
                    existing.NodePositionsJson = projDto.NodePositionsJson;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _context.CloudProjects.Update(existing);
                }
                else
                {
                    // Check if this ID is already taken by another user — assign new UUID if so
                    var idTakenByOther = await _context.CloudProjects
                        .AnyAsync(p => p.Id == projDto.Id);

                    var newCloudProj = new CloudProject
                    {
                        Id = idTakenByOther ? Guid.NewGuid().ToString() : projDto.Id,
                        Name = projDto.Name,
                        DbType = projDto.DbType,
                        SchemaJson = projDto.SchemaJson,
                        NodePositionsJson = projDto.NodePositionsJson,
                        UserId = userId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await _context.CloudProjects.AddAsync(newCloudProj);
                }
                savedCount++;
            }

            await _context.SaveChangesAsync();
            return Ok(new { Message = "Sync successful.", SavedCount = savedCount });
        }

        [Authorize]
        [HttpGet("projects")]
        public async Task<IActionResult> GetProjects()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var projects = await _context.CloudProjects
                .Where(p => p.UserId == userId)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.DbType,
                    p.SchemaJson,
                    p.NodePositionsJson,
                    p.CreatedAt,
                    p.UpdatedAt
                })
                .ToListAsync();

            return Ok(projects);
        }

        [Authorize]
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId!);
            if (user == null) return Unauthorized();

            // Parse extra profile data stored as JSON in ProfileJson field (if it exists)
            ProfileData profile = new();
            if (!string.IsNullOrEmpty(user.ProfileJson))
            {
                try { profile = System.Text.Json.JsonSerializer.Deserialize<ProfileData>(user.ProfileJson) ?? new(); }
                catch { }
            }

            return Ok(new
            {
                username    = user.UserName,
                email       = user.Email,
                type        = user.Type.ToString().ToLower(),
                companyName = user.CompanyName,
                subscriptionStatus = user.SubscriptionStatus ?? "none",
                currentPeriodEnd   = user.CurrentPeriodEnd,
                // Extended profile
                profile.FullName,
                profile.GithubUrl,
                profile.LinkedinUrl,
                profile.WebsiteUrl,
                profile.TwitterUrl,
                profile.Bio,
                profile.Location
            });
        }

        [Authorize]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId!);
            if (user == null) return Unauthorized();

            // Update basic fields
            if (!string.IsNullOrWhiteSpace(dto.CompanyName))
                user.CompanyName = dto.CompanyName;

            // Serialize extended profile data into ProfileJson
            var profile = new ProfileData
            {
                FullName    = dto.FullName ?? "",
                GithubUrl   = dto.GithubUrl ?? "",
                LinkedinUrl = dto.LinkedinUrl ?? "",
                WebsiteUrl  = dto.WebsiteUrl ?? "",
                TwitterUrl  = dto.TwitterUrl ?? "",
                Bio         = dto.Bio ?? "",
                Location    = dto.Location ?? ""
            };
            user.ProfileJson = System.Text.Json.JsonSerializer.Serialize(profile);

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return BadRequest(new { message = "Profile update failed.", errors = result.Errors.Select(e => e.Description) });

            return Ok(new { message = "Profile updated successfully." });
        }

        // JWT'yi httpOnly cookie olarak yazar → token JS'e (localStorage) sızmaz, XSS ile çalınamaz.
        private const string AuthCookieName = "namines_token";
        private const int AuthCookieDays = 7;

        /// <summary>
        /// Auth cookie'sinin ortam-bağımlı ayarları.
        /// Frontend ile API farklı site'lardaysa (ör. Vercel + Railway) SameSite=Lax cookie
        /// XHR isteklerinde tarayıcı tarafından GÖNDERİLMEZ → login başarılı görünür ama
        /// sonraki her istek anonim kalır. Bu durumda SameSite=None + Secure zorunludur.
        /// Auth:CrossSiteCookie=true ile açılır (aynı domain/subdomain kullanılıyorsa gerekmez).
        /// </summary>
        private CookieOptions BuildAuthCookieOptions(bool forExpiry = false)
        {
            var crossSite = _configuration.GetValue<bool>("Auth:CrossSiteCookie");

            var options = new CookieOptions
            {
                HttpOnly = true,
                // SameSite=None kullanan cookie'ler tarayıcı kuralı gereği Secure OLMAK ZORUNDA.
                Secure = crossSite || Request.IsHttps,
                SameSite = crossSite ? SameSiteMode.None : SameSiteMode.Lax,
                Path = "/"
            };

            if (!forExpiry)
                options.Expires = DateTimeOffset.UtcNow.AddDays(AuthCookieDays);

            return options;
        }

        private void SetAuthCookie(string token)
            => Response.Cookies.Append(AuthCookieName, token, BuildAuthCookieOptions());

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            // Silme isteği cookie'nin yazıldığı attribute'larla birebir eşleşmezse
            // tarayıcı cookie'yi silmez — bu yüzden aynı options üzerinden gidiyoruz.
            Response.Cookies.Delete(AuthCookieName, BuildAuthCookieOptions(forExpiry: true));
            return Ok(new { Message = "Logged out." });
        }

        private string GenerateJwtToken(ApplicationUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? ""),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim("type", user.Type.ToString().ToLower()),
            };

            if (!string.IsNullOrEmpty(user.CompanyName))
            {
                claims.Add(new Claim("companyName", user.CompanyName));
            }

            var secretKey = _configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(secretKey))
                secretKey = "NaminesDevFallbackKey_Change_In_Production_Min32Chars!";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddDays(7);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"] ?? "NaminesServer",
                audience: _configuration["Jwt:Audience"] ?? "NaminesClient",
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    public class RegisterRequestDto
    {
        public string Email { get; set; } = null!;
        public string? Username { get; set; }
        public string Password { get; set; } = null!;
        public string? Type { get; set; }        // "individual" or "corporate"
        public string? CompanyName { get; set; }
    }

    public class LoginRequestDto
    {
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
    }

    public class SyncProjectDto
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string DbType { get; set; } = null!;
        public string SchemaJson { get; set; } = null!;
        public string NodePositionsJson { get; set; } = null!;
    }

    public class UpdateProfileDto
    {
        public string? FullName    { get; set; }
        public string? GithubUrl   { get; set; }
        public string? LinkedinUrl { get; set; }
        public string? WebsiteUrl  { get; set; }
        public string? TwitterUrl  { get; set; }
        public string? Bio         { get; set; }
        public string? Location    { get; set; }
        public string? CompanyName { get; set; }
    }

    public class ProfileData
    {
        public string FullName    { get; set; } = "";
        public string GithubUrl   { get; set; } = "";
        public string LinkedinUrl { get; set; } = "";
        public string WebsiteUrl  { get; set; } = "";
        public string TwitterUrl  { get; set; } = "";
        public string Bio         { get; set; } = "";
        public string Location    { get; set; } = "";
    }
}
