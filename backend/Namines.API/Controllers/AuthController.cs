using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        // BranchController ile aynı desen: frontend enum'ları (ör. ReferentialAction)
        // JSON'a string yazıyor ("NoAction", "Cascade"...) — dönüştürücü olmadan
        // Deserialize<DatabaseSchema> bu alanlarda JsonException fırlatır.
        private static readonly JsonSerializerOptions SchemaJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

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
                return BadRequest(new { Message = "An account with this email already exists." });

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
                return BadRequest(new { Message = $"Registration failed: {errors}" });
            }

            // Automatically provision a default UserAIPolicy for this user
            var defaultPolicy = new UserAIPolicy
            {
                UserId = user.Id,
                // Varsayilanlar UserAIPolicy'de; burada tekrar yazilmiyor ki
                // iki yer birbirinden ayrismasin.
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
                    Type = user.Type.ToString().ToLowerInvariant(),
                    CompanyName = user.CompanyName
                },
                Quota = quotaInfo
            });
        }

        /// <summary>
        /// Giriş.
        ///
        /// <b>Hesap kilitleme AÇIK.</b> Önceden yalnızca
        /// <c>CheckPasswordAsync</c> çağrılıyordu; o metot başarısız denemeyi
        /// SAYMAZ, dolayısıyla Identity'nin kilitleme mekanizması hiç devreye
        /// girmiyordu ve parola deneme sayısı sınırsızdı. IP başına rate limit
        /// tek başına yetmiyor: dağıtık bir saldırı IP değiştirerek onu geçer,
        /// ama hesap kilidi hesabın kendisini korur.
        /// </summary>
        [HttpPost("login")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("sensitive")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);

            // Kullanıcı yoksa da aynı mesaj: farklı cevap vermek, hangi
            // e-postaların kayıtlı olduğunu sızdıran bir numaralandırma
            // aracına dönerdi.
            if (user is null)
                return Unauthorized(new { Message = "Incorrect email or password." });

            if (await _userManager.IsLockedOutAsync(user))
                return Unauthorized(new
                {
                    Message = "This account is temporarily locked after too many failed sign-in attempts. Try again later.",
                });

            if (!await _userManager.CheckPasswordAsync(user, model.Password))
            {
                // Başarısızlığı KAYDET — kilitleme buna dayanıyor.
                await _userManager.AccessFailedAsync(user);
                return Unauthorized(new { Message = "Incorrect email or password." });
            }

            // ── MFA kapısı ────────────────────────────────────────────────
            //
            // Parola doğrulandıktan SONRA. Önce sorulsaydı, parolayı bilmeyen
            // biri bile bir hesapta MFA açık olup olmadığını öğrenirdi — hedef
            // seçmesine yarayan bir bilgi.
            if (user.TwoFactorEnabled)
            {
                if (string.IsNullOrWhiteSpace(model.TwoFactorCode))
                {
                    // Sayaç SIFIRLANMIYOR: parola doğru ama giriş tamamlanmadı.
                    return Unauthorized(new
                    {
                        requiresTwoFactor = true,
                        Message = "Enter the 6-digit code from your authenticator app.",
                    });
                }

                if (!await VerifyTwoFactorAsync(user, model.TwoFactorCode))
                {
                    // Yanlış MFA kodu da kilitleme sayacına yazılıyor. Aksi
                    // hâlde parolası sızmış bir hesapta saldırgan altı haneyi
                    // sınırsız deneyebilirdi — bir milyon olasılık, sınırsız
                    // deneme demek kırılmış MFA demek.
                    await _userManager.AccessFailedAsync(user);
                    return Unauthorized(new { Message = "Incorrect email or password." });
                }
            }

            // Başarılı girişte sayaç sıfırlanır; aksi hâlde aylar içinde biriken
            // yanlış denemeler doğru parolayla giren kullanıcıyı kilitlerdi.
            await _userManager.ResetAccessFailedCountAsync(user);

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
                    Type = user.Type.ToString().ToLowerInvariant(),
                    CompanyName = user.CompanyName
                },
                Quota = quotaInfo
            });
        }

        /// <summary>
        /// Namines Desk v2 §E1.2, adım 1 — ana uygulamada zaten girişli kullanıcı için
        /// tek kullanımlık, 30 saniye ömürlü bir devir jetonu üretir. Jetonun kendisi
        /// yalnızca burada, bu yanıtın gövdesinde görünür — asla bir URL'e YAZILMAZ
        /// (çağıran taraf, `frontend`'in "Namines Desk" düğmesi, bunu bir form POST
        /// gövdesinde Desk'e taşır; bkz. `DeskHandoffToken` sınıf yorumu).
        ///
        /// Asıl mantık <c>DeskHandoff.CreateDeskHandoffTokenAsync</c>'te (Infrastructure/
        /// Data) — GatewayAudit/OrgAccess ile aynı desen, DB'ye dokunan mantık
        /// controller'da yaşamaz; ayrıca yarış korumasının Testcontainers'la gerçek
        /// Postgres'e karşı sınanabilmesi için de gerekliydi.
        /// </summary>
        [Authorize]
        [HttpPost("desk-handoff-token")]
        public async Task<IActionResult> CreateDeskHandoffToken(CancellationToken ct)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var raw = await _context.CreateDeskHandoffTokenAsync(userId, ct);
            return Ok(new { token = raw, expiresInSeconds = 30 });
        }

        /// <summary>
        /// Namines Desk v2 §E1.2, adım 2 — Desk'in sunucu tarafı (`app/handoff/route.ts`)
        /// jetonu burada tam bir JWT'ye çevirir. <c>[AllowAnonymous]</c>: çağıranın henüz
        /// hiçbir kimliği yok, kimliği jetonun KENDİSİ taşıyor.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("desk-handoff-exchange")]
        public async Task<IActionResult> ExchangeDeskHandoffToken([FromBody] DeskHandoffExchangeRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return BadRequest(new { Message = "A token is required." });

            var userId = await _context.ExchangeDeskHandoffTokenAsync(request.Token, ct);
            if (userId is null)
                return Unauthorized(new { Message = "The token is invalid, expired, or already used." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return Unauthorized(new { Message = "User not found." });

            var jwt = GenerateJwtToken(user);
            return Ok(new { token = jwt });
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

            // Yeni projeler kullanıcının kişisel organizasyonuna bağlanıyor.
            //
            // <b>Bu satır olmadan ortak workspace çalışmıyor:</b> OrganizationId boş
            // kalan bir proje hiçbir ekibe ait olmuyor, dolayısıyla ekip arkadaşının
            // listesinde hiç görünmüyordu. Canlı denemede tam olarak bu yaşandı —
            // proje kaydediliyor, "başarılı" dönüyor ama diğer üye sıfır proje
            // görüyordu.
            var personalOrg = await _context.GetOrCreatePersonalOrgAsync(
                userId, user.UserName ?? "Personal");

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
                    // Org'a hiç bağlanmamış eski satırlar burada tembel olarak
                    // bağlanıyor; aksi hâlde kullanıcının Team'e geçmeden önce
                    // oluşturduğu projeler ekibe hiç görünmezdi.
                    existing.OrganizationId ??= personalOrg.Id;
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
                        OrganizationId = personalOrg.Id,
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

            // ORTAK WORKSPACE: proje listesi artık yalnızca "benim oluşturduklarım"
            // değil, ÜYESİ OLDUĞUM organizasyonların tamamı.
            //
            // Önceden `p.UserId == userId` yazıyordu; bu, Team planının satın
            // aldığı şeyi imkânsız kılıyordu — ekip arkadaşı projeye eklense bile
            // listesinde göremiyor, yalnızca doğrudan bağlantıyla açabiliyordu.
            //
            // Kişisel org da bu sorguya dahil (her kullanıcının bir tane var),
            // yani tek kişilik hesaplarda sonuç aynı kalıyor.
            var myOrgIds = await _context.OrganizationMembers.AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => m.OrganizationId)
                .ToListAsync();

            // Namines Desk v2 §E4.2: hangi projede Owner olduğumu bilmem gerekiyor
            // (yalnızca Owner ham SQL çalıştırabilir/açabilir) — yukarıdaki
            // `myOrgIds` yalnızca ÜYELİĞİ biliyordu, ROLÜ değil.
            var myRoles = await _context.OrganizationMembers.AsNoTracking()
                .Where(m => m.UserId == userId)
                .ToDictionaryAsync(m => m.OrganizationId, m => m.Role);

            var projects = await _context.CloudProjects
                .Where(p => p.UserId == userId ||
                            (p.OrganizationId != null && myOrgIds.Contains(p.OrganizationId)))
                .OrderByDescending(p => p.UpdatedAt)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.DbType,
                    p.SchemaJson,
                    p.NodePositionsJson,
                    p.CreatedAt,
                    p.UpdatedAt,
                    p.AutoApproveSafeChanges,
                    // Ekip görünümünde kimin projesi olduğu belli olmalı; aksi
                    // hâlde ortak listede kimin ne eklediği anlaşılmaz.
                    ownerUserId = p.UserId,
                    ownerName = p.User.UserName,
                    isMine = p.UserId == userId,
                    // Namines Desk (namines_desk/02-PROJECTS.md §2): kart üzerindeki
                    // "bağlı/bağlı değil" rozeti gerçek bir alana dayanıyor —
                    // bağlantı dizesinin KENDİSİ asla dönmez, yalnızca var/yok bilgisi.
                    hasConnection = p.EncryptedConnectionString != null,
                    connectionDbType = p.ConnectionDbType,
                    p.OrganizationId,
                    p.AllowDeskSql,
                })
                .ToListAsync();

            // Tablo sayısı SchemaJson'dan sayılır (§2 "Tablo sayısı" alanı) — ayrı
            // bir sorgu değil, zaten dönen alanın üstüne bir hesap. Ayrıştırılamayan
            // bir SchemaJson sessizce 0 GÖSTERMEZ: null döner, yani arayüz "?" yazar,
            // uydurma bir sayı değil.
            // `schemaJson` ANA UYGULAMA TARAFINDAN OKUNUYOR (frontend/store/
            // useProjectHistoryStore.ts) — buradan çıkarmak Studio'nun bulut
            // senkronundan şema hidrasyonunu kırardı. Bu yüzden alan KORUNUYOR;
            // yeni alanlar (tableCount, hasConnection, connectionDbType) üstüne
            // ekleniyor, onun yerine geçmiyor.
            var result = projects.Select(p => new
            {
                p.Id, p.Name, p.DbType, p.SchemaJson, p.NodePositionsJson, p.CreatedAt, p.UpdatedAt,
                p.AutoApproveSafeChanges, p.ownerUserId, p.ownerName, p.isMine,
                p.hasConnection, p.connectionDbType,
                tableCount = TryCountTables(p.SchemaJson),
                // Legacy (org'a taşınmamış) satırlarda GetRoleAsync'in kendi kuralıyla
                // aynı: sahip == Owner sayılır (OrgAccess.GetRoleAsync'teki fallback).
                isOwner = p.OrganizationId == null
                    ? p.isMine
                    : myRoles.TryGetValue(p.OrganizationId, out var role) && role == OrgRole.Owner,
                allowDeskSql = p.AllowDeskSql,
            });

            return Ok(result);
        }

        /// <summary>
        /// Namines Desk'in proje kartındaki "N tablo" alanı için (02-PROJECTS.md §2).
        ///
        /// `BranchController`'ın zaten kurduğu <c>Deserialize&lt;DatabaseSchema&gt;(json,
        /// SchemaJsonOptions)</c> desenini yeniden kullanıyor — bu depoda `SchemaJson`
        /// okuyan her yer aynı yoldan geçsin diye (bkz. o dosyadaki aynı yorum).
        ///
        /// Ayrıştırılamayan/beklenmeyen biçimdeki bir SchemaJson SESSİZCE 0 SAYILMAZ —
        /// null döner, arayüz "bilinmiyor" gösterir; uydurma bir sayı vermektense.
        /// </summary>
        private static int? TryCountTables(string schemaJson)
        {
            if (string.IsNullOrWhiteSpace(schemaJson)) return null;
            try
            {
                return JsonSerializer.Deserialize<DatabaseSchema>(schemaJson, SchemaJsonOptions)?.Tables.Count;
            }
            catch (JsonException)
            {
                return null;
            }
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
                type        = user.Type.ToString().ToLowerInvariant(),
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

        /// <summary>
        /// Jetondaki SecurityStamp kopyasının claim adı. Doğrulama tarafı
        /// (<c>SecurityStampValidation</c>) aynı sabiti kullanıyor.
        /// </summary>
        internal const string SecurityStampClaimType = "sstamp";

        /// <summary>
        /// Yalnızca Development'ta devreye giren imzalama anahtarı. Program.cs
        /// diğer ortamlarda anahtar tanımsızsa uygulamayı başlatmıyor.
        /// </summary>
        internal const string DevFallbackJwtKey = "NaminesDevFallbackKey_Change_In_Production_Min32Chars!";

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

        /// <summary>
        /// Bu tarayıcıdaki oturumu kapatır.
        ///
        /// <b>Yalnızca cookie'yi siler.</b> Jetonun kendisi süresi dolana kadar
        /// geçerli kalır — bu, JWT'nin doğası. Bir jetonun ÇALINDIĞINDAN
        /// şüpheleniyorsanız <see cref="RevokeAllSessions"/> kullanın.
        /// </summary>
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            // Silme isteği cookie'nin yazıldığı attribute'larla birebir eşleşmezse
            // tarayıcı cookie'yi silmez — bu yüzden aynı options üzerinden gidiyoruz.
            Response.Cookies.Delete(AuthCookieName, BuildAuthCookieOptions(forExpiry: true));
            return Ok(new { Message = "Logged out." });
        }

        /// <summary>
        /// TÜM oturumları geçersiz kılar — bu cihaz dahil.
        ///
        /// Kullanıcının <c>SecurityStamp</c>'ini yeniliyor. Daha önce çıkarılmış
        /// her jetonun içindeki damga kopyası artık eşleşmeyeceği için
        /// (<see cref="Namines.API.Extensions.SecurityStampValidation"/>) hepsi
        /// süresi dolmadan reddedilecek.
        ///
        /// <b>Bu, jeton hırsızlığına verilebilecek tek gerçek cevap.</b> Önceden
        /// böyle bir cevap yoktu: tek seçenek imzalama anahtarını değiştirip
        /// bütün kullanıcıları çıkışa zorlamaktı.
        ///
        /// İptalin devreye girmesi damga önbelleği kadar (60 sn) gecikebilir.
        /// </summary>
        [Authorize]
        [HttpPost("revoke-all-sessions")]
        public async Task<IActionResult> RevokeAllSessions()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return Unauthorized();

            var result = await _userManager.UpdateSecurityStampAsync(user);
            if (!result.Succeeded)
                return StatusCode(500, new { Message = "Sessions could not be revoked. Try again." });

            // Bu tarayıcının cookie'si de siliniyor: kullanıcı "tüm oturumları
            // kapat" dediğinde burada oturumda kalması kafa karıştırıcı olurdu.
            Response.Cookies.Delete(AuthCookieName, BuildAuthCookieOptions(forExpiry: true));

            return Ok(new { Message = "All sessions revoked. Sign in again on every device." });
        }

        // -- Cok faktorlu dogrulama (TOTP) -------------------------------------
        //
        // Saglayici ASP.NET Identity'nin kendi authenticator'i
        // (`AddDefaultTokenProviders` ile zaten kayitli). Ne yeni bir paket ne
        // de yeni bir tablo gerekti: `TwoFactorEnabled` ve authenticator
        // anahtari IdentityUser'da hazir duruyordu.

        /// <summary>Kurtarma kodu sayisi -- telefon kaybedildiginde tek cikis yolu.</summary>
        private const int RecoveryCodeCount = 8;

        /// <summary>
        /// MFA kurulumunu baslatir: paylasilan anahtari ve QR icin `otpauth://`
        /// URI'sini dondurur. <b>Henuz ETKINLESTIRMEZ</b> -- kullanici bir kod
        /// uretip dogrulayana kadar hesap eskisi gibi calisir.
        ///
        /// Bu ayrim onemli: dogrulamadan etkinlestirmek, kullaniciyi kuramadigi
        /// bir uygulamaya bagimli hale getirip hesabindan kilitleyebilirdi.
        /// </summary>
        [Authorize]
        [HttpPost("mfa/setup")]
        public async Task<IActionResult> StartMfaSetup()
        {
            var user = await GetCurrentUserAsync();
            if (user is null) return Unauthorized();

            if (user.TwoFactorEnabled)
                return BadRequest(new { Message = "Two-factor authentication is already enabled." });

            // Her kurulum denemesinde YENI anahtar: yarim kalmis bir kurulumun
            // anahtari, kullanicinin telefonunda kalip sonraki kurulumla
            // cakisabilirdi.
            await _userManager.ResetAuthenticatorKeyAsync(user);
            var key = await _userManager.GetAuthenticatorKeyAsync(user);

            if (string.IsNullOrEmpty(key))
                return StatusCode(500, new { Message = "Could not generate an authenticator key." });

            return Ok(new
            {
                sharedKey = FormatAuthenticatorKey(key),
                // otpauth:// bicimi tum kimlik dogrulayici uygulamalarinin ortak dili.
                otpauthUri = BuildOtpauthUri(user.Email ?? user.UserName ?? "user", key),
            });
        }

        /// <summary>
        /// Kurulumu tamamlar: kullanicinin urettigi kod dogrulanirsa MFA acilir
        /// ve kurtarma kodlari <b>bir kez</b> gosterilir.
        /// </summary>
        [Authorize]
        [HttpPost("mfa/enable")]
        public async Task<IActionResult> EnableMfa([FromBody] MfaCodeRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user is null) return Unauthorized();

            if (user.TwoFactorEnabled)
                return BadRequest(new { Message = "Two-factor authentication is already enabled." });

            var code = NormalizeCode(request.Code);
            var valid = await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

            if (!valid)
                return BadRequest(new { Message = "That code is not valid. Check your device clock and try again." });

            await _userManager.SetTwoFactorEnabledAsync(user, true);

            // Kurtarma kodlari OLMADAN MFA acmak, telefonunu kaybeden
            // kullaniciyi hesabindan kalici olarak kilitlemek demek.
            var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);

            return Ok(new
            {
                Message = "Two-factor authentication is on. Save these recovery codes -- they are shown only once.",
                recoveryCodes,
            });
        }

        /// <summary>
        /// MFA'yi kapatir. <b>Gecerli bir kod ZORUNLU</b> -- yalnizca oturum
        /// yeterli olsaydi, calinmis bir jeton MFA'yi kapatip korumayi ortadan
        /// kaldirirdi; yani MFA kendi kendini savunamazdi.
        /// </summary>
        [Authorize]
        [HttpPost("mfa/disable")]
        public async Task<IActionResult> DisableMfa([FromBody] MfaCodeRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user is null) return Unauthorized();

            if (!user.TwoFactorEnabled)
                return BadRequest(new { Message = "Two-factor authentication is not enabled." });

            if (!await VerifyTwoFactorAsync(user, request.Code))
            {
                await _userManager.AccessFailedAsync(user);
                return BadRequest(new { Message = "That code is not valid." });
            }

            await _userManager.SetTwoFactorEnabledAsync(user, false);
            await _userManager.ResetAuthenticatorKeyAsync(user);

            return Ok(new { Message = "Two-factor authentication is off." });
        }

        /// <summary>MFA durumu -- arayuzun ne gosterecegine karar vermesi icin.</summary>
        [Authorize]
        [HttpGet("mfa/status")]
        public async Task<IActionResult> MfaStatus()
        {
            var user = await GetCurrentUserAsync();
            if (user is null) return Unauthorized();

            return Ok(new
            {
                enabled = user.TwoFactorEnabled,
                recoveryCodesLeft = user.TwoFactorEnabled
                    ? await _userManager.CountRecoveryCodesAsync(user)
                    : 0,
            });
        }

        /// <summary>
        /// Kodu dogrular. Once TOTP, olmazsa kurtarma kodu denenir.
        ///
        /// Kurtarma kodu <b>tek kullanimlik</b>: Identity onu dogrular dogrulamaz
        /// listeden siliyor. Bu yuzden siralama onemli -- TOTP once denenmezse,
        /// normal bir giris kurtarma kodlarini tuketebilirdi.
        /// </summary>
        private async Task<bool> VerifyTwoFactorAsync(ApplicationUser user, string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            // TOTP icin bosluk VE tire temizleniyor: kimlik dogrulayici
            // uygulamalari kodu "123 456" diye gosteriyor.
            var totpCode = NormalizeCode(code);
            if (totpCode.Length > 0 && await _userManager.VerifyTwoFactorTokenAsync(
                    user, _userManager.Options.Tokens.AuthenticatorTokenProvider, totpCode))
                return true;

            // Kurtarma kodu icin YALNIZCA bosluk temizleniyor -- TIRE KORUNUYOR.
            //
            // Identity kurtarma kodlarini "xxxxx-xxxxx" bicimde uretip AYNEN
            // sakliyor. Tireyi silmek, dogru kodu yanlis gosteriyordu: canli
            // denemede kullanici kurtarma koduyla giris YAPAMIYORDU -- yani
            // telefonunu kaybedeni hesabindan kilitleyen, tam da kurtarma
            // kodlarinin onlemek icin var oldugu durum.
            var recoveryCode = code.Trim();
            var recovery = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode);
            return recovery.Succeeded;
        }

        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return userId is null ? null : await _userManager.FindByIdAsync(userId);
        }

        /// <summary>
        /// Kimlik dogrulayici uygulamalari kodu bosluklu gosterir ("123 456");
        /// kullanici kopyalayip yapistirdiginda bosluk gelir. Temizlenmezse
        /// dogru kod yanlis sayilirdi.
        /// </summary>
        internal static string NormalizeCode(string? code) =>
            code?.Replace(" ", string.Empty).Replace("-", string.Empty).Trim() ?? string.Empty;

        /// <summary>Anahtari elle girilebilir hale getirir: dorderli gruplar.</summary>
        internal static string FormatAuthenticatorKey(string key)
        {
            var result = new StringBuilder();
            for (var i = 0; i < key.Length; i += 4)
                result.Append(key, i, Math.Min(4, key.Length - i)).Append(' ');

            return result.ToString().TrimEnd().ToLowerInvariant();
        }

        internal static string BuildOtpauthUri(string account, string key) =>
            $"otpauth://totp/Namines:{Uri.EscapeDataString(account)}" +
            $"?secret={key}&issuer=Namines&digits=6";

        private string GenerateJwtToken(ApplicationUser user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? ""),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim("type", user.Type.ToString().ToLowerInvariant()),

                // Jeton iptalinin dayanagi. ASP.NET Identity, parola degisimi ve
                // "tum oturumlari kapat" gibi islemlerde SecurityStamp'i
                // yeniliyor; jetondaki kopya artik eslesmeyecegi icin o jeton
                // suresi DOLMADAN gecersiz oluyor.
                //
                // Bu olmadan calinmis bir jetonu iptal etmenin tek yolu imzalama
                // anahtarini degistirmekti -- yani HERKESI cikisa zorlamak.
                new Claim(SecurityStampClaimType, user.SecurityStamp ?? string.Empty),
            };

            if (!string.IsNullOrEmpty(user.CompanyName))
            {
                claims.Add(new Claim("companyName", user.CompanyName));
            }

            // Fallback anahtar burada TEKRARLANMIYOR: Program.cs, Development
            // disinda Jwt:Key tanimsizsa uygulamayi hic baslatmiyor. Ikinci bir
            // kopya, o kapinin ilerideki bir degisiklikte sessizce atlanmasini
            // mumkun kilardi.
            var secretKey = _configuration["Jwt:Key"] ?? DevFallbackJwtKey;
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

        /// <summary>
        /// Kimlik doğrulayıcı uygulamasından gelen 6 haneli kod ya da bir
        /// kurtarma kodu. Yalnızca hesapta MFA açıksa gerekli.
        ///
        /// <b>Ayrı bir "MFA jetonu" adımı BİLEREK yok.</b> İki aşamalı akışta
        /// sunucu, parolası doğrulanmış ama MFA'sı tamamlanmamış kullanıcı için
        /// ikinci bir jeton türü üretmek zorunda kalır — ve o jeton da
        /// çalınabilir, saklanmalı, süresi yönetilmeli. Kodu aynı istekte
        /// almak o yüzeyi tamamen ortadan kaldırıyor: istemci ilk denemede
        /// `requiresTwoFactor` cevabını alıp kodu ekleyerek tekrar deniyor.
        /// </summary>
        public string? TwoFactorCode { get; set; }
    }

    public sealed record DeskHandoffExchangeRequest(string Token);

    /// <param name="Code">6 haneli TOTP kodu ya da bir kurtarma kodu.</param>
    public sealed record MfaCodeRequest(string? Code);

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
