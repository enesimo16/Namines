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

            var userType = UserType.Individual;
            if (model.Type?.ToLower() == "corporate")
            {
                userType = UserType.Corporate;
                
                // Simple corporate validation: make sure they provided a company name
                if (string.IsNullOrWhiteSpace(model.CompanyName))
                {
                    return BadRequest(new { Message = "Kurumsal üyelik için şirket adı girmek zorunludur." });
                }

                // Check for business email domains - reject standard public mail hosts for corporate tier (optional guidance)
                var emailDomain = model.Email.Split('@')[1].ToLower();
                var freeDomains = new List<string> { "gmail.com", "hotmail.com", "yahoo.com", "outlook.com", "icloud.com" };
                if (freeDomains.Contains(emailDomain))
                {
                    return BadRequest(new { Message = "Kurumsal hesaplar için lütfen kurumsal e-posta adresinizi kullanın." });
                }
            }

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

            var token = GenerateJwtToken(user);
            return Ok(new
            {
                Token = token,
                User = new
                {
                    Username = user.UserName,
                    Email = user.Email,
                    Type = user.Type.ToString().ToLower(),
                    CompanyName = user.CompanyName
                }
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

            var token = GenerateJwtToken(user);
            return Ok(new
            {
                Token = token,
                User = new
                {
                    Username = user.UserName,
                    Email = user.Email,
                    Type = user.Type.ToString().ToLower(),
                    CompanyName = user.CompanyName
                }
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
                var existing = await _context.CloudProjects
                    .FirstOrDefaultAsync(p => p.Id == projDto.Id && p.UserId == userId);

                if (existing != null)
                {
                    existing.Name = projDto.Name;
                    existing.DbType = projDto.DbType;
                    existing.SchemaJson = projDto.SchemaJson;
                    existing.NodePositionsJson = projDto.NodePositionsJson;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _context.CloudProjects.Update(existing);
                }
                else
                {
                    var newCloudProj = new CloudProject
                    {
                        Id = projDto.Id,
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
            return Ok(new { Message = "Senkronizasyon başarılı.", SavedCount = savedCount });
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
}
