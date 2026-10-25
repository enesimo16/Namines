using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.API.Extensions;
using Namines.Infrastructure.Services;
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
        private readonly IMigrationService _migrationService;
        private readonly IAutomationJobQueue _automationQueue;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            AuthDbContext context,
            IConfiguration configuration,
            IMigrationService migrationService,
            IAutomationJobQueue automationQueue,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _context = context;
            _configuration = configuration;
            _migrationService = migrationService;
            _automationQueue = automationQueue;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var devConfigEmail = _configuration["Dev:Email"];
            bool isReservedEmail = !string.IsNullOrWhiteSpace(model.Email) && (
                string.Equals(model.Email, "namines@namines.com", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model.Email, "admin@namines.com", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(devConfigEmail) && string.Equals(model.Email, devConfigEmail, StringComparison.OrdinalIgnoreCase)));

            if (isReservedEmail)
                return BadRequest(new { Message = "This email address is reserved and cannot be registered publicly." });

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

            var devConfigEmail = _configuration["Dev:Email"];
            var isConfigDev = !string.IsNullOrWhiteSpace(devConfigEmail) && string.Equals(user.Email, devConfigEmail, StringComparison.OrdinalIgnoreCase);
            if (!user.IsDev && isConfigDev)
            {
                user.IsDev = true;
                user.EmailConfirmed = true;
                user.Type = UserType.Corporate;
                await _userManager.UpdateAsync(user);
            }

            var tier = Namines.Core.Analysis.PlanQuotas.Resolve(user.SubscriptionStatus, user.PlanCode, user.IsDev);
            var defaultLimit = user.IsDev ? int.MaxValue : 100;

            var quota = await _context.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == user.Id);
            if (quota == null)
            {
                quota = new UserAIQuota
                {
                    UserId = user.Id,
                    DailyLimit = defaultLimit,
                    DailyUsageCount = 0,
                    LastResetDate = DateTime.UtcNow
                };
                await _context.UserAIQuotas.AddAsync(quota);
                await _context.SaveChangesAsync();
            }
            else
            {
                if (user.IsDev)
                {
                    quota.DailyLimit = int.MaxValue;
                }
                else if (quota.DailyLimit < 100)
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
                plan = tier.ToString(),
                dailyLimit = quota.DailyLimit,
                used = quota.DailyUsageCount,
                remaining = user.IsDev ? int.MaxValue : Math.Max(0, quota.DailyLimit - quota.DailyUsageCount),
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
                    IsDev = user.IsDev,
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

            // NORMAL oturum jetonu DEĞİL: kısa ömürlü ve amacı işaretli
            // (bkz. DeskHandoffSessionLifetime'ın notu, docs/last-review M-15).
            var jwt = GenerateJwtToken(user, DeskHandoffSessionLifetime, purpose: "desk-handoff");
            return Ok(new
            {
                token = jwt,
                expiresInSeconds = (int)DeskHandoffSessionLifetime.TotalSeconds,
            });
        }

        /// <summary>
        /// Yeni bir projenin istemci-secimli kimligi icin izin verilen bicim. Gercek
        /// istemciler crypto.randomUUID() uretir; yol ayiricilari, nokta ve alt cizgi
        /// disarida kaliyor, boylece kimlik yola ya da tanimlayiciya guvenle gomulebilir.
        /// </summary>
        internal static bool IsSafeNewProjectId(string? id) =>
            !string.IsNullOrEmpty(id) && System.Text.RegularExpressions.Regex.IsMatch(
                id, "^[A-Za-z0-9-]{1,64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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

            // Yeni projeler kullanıcının aktif ekip organizasyonuna (yoksa kişisel
            // organizasyonuna) bağlanıyor.
            var personalOrg = await _context.GetOrCreatePersonalOrgAsync(
                userId, user.UserName ?? "Personal");
            var activeOrgId = await _context.OrganizationMembers.AsNoTracking()
                .Where(m => m.UserId == userId && m.OrganizationId != personalOrg.Id)
                .OrderBy(m => m.JoinedAt)
                .Select(m => m.OrganizationId)
                .FirstOrDefaultAsync() ?? personalOrg.Id;

            int savedCount = 0;
            var pendingDiffs = new List<(string ProjectId, string DbType, string? OldJson, string NewJson)>();
            foreach (var projDto in projects)
            {
                var existing = await _context.CloudProjects
                    .FirstOrDefaultAsync(p => p.Id == projDto.Id);
                var canEditExisting = existing != null &&
                    (existing.UserId == userId || await _context.CanEditAsync(existing.Id, userId));
                var canViewExisting = existing != null &&
                    (existing.UserId == userId || canEditExisting || await _context.CanViewAsync(existing.Id, userId));

                if (existing != null && canEditExisting)
                {
                    // ÇAKIŞMA KONTROLÜ (REL-003a / B-32).
                    if (projDto.RowVersion is { } clientVersion &&
                        clientVersion != existing.RowVersion)
                    {
                        return Conflict(new
                        {
                            message = "This project was changed by someone else while you were working on it. " +
                                      "Reload it to get the latest version, then re-apply your changes.",
                            projectId = existing.Id,
                            projectName = existing.Name,
                            yourVersion = clientVersion,
                            currentVersion = existing.RowVersion,
                            changedAt = existing.UpdatedAt,
                        });
                    }

                    string? oldSchemaJsonForDiff = existing.SchemaJson;
                    string newSchemaJsonForDiff = projDto.SchemaJson;
                    string dbTypeForDiff = existing.DbType;
                    string projectIdForDiff = existing.Id;

                    existing.Name = projDto.Name;
                    existing.DbType = projDto.DbType;
                    existing.SchemaJson = projDto.SchemaJson;
                    existing.NodePositionsJson = projDto.NodePositionsJson;
                    existing.UpdatedAt = DateTime.UtcNow;
                    if (string.IsNullOrWhiteSpace(existing.Slug))
                    {
                        var clean = System.Text.RegularExpressions.Regex.Replace(existing.Name.ToLowerInvariant(), @"[^a-z0-9\-]", "-").Trim('-');
                        existing.Slug = string.IsNullOrWhiteSpace(clean) ? $"project-{existing.Id[..Math.Min(6, existing.Id.Length)]}" : clean;
                    }
                    existing.OrganizationId ??= activeOrgId;
                    _context.CloudProjects.Update(existing);

                    pendingDiffs.Add((projectIdForDiff, dbTypeForDiff, oldSchemaJsonForDiff, newSchemaJsonForDiff));
                }
                else if (existing != null && canViewExisting)
                {
                    // Ortak projede salt-okunur (Viewer) üye: üzerine yazma veya kopya oluşturma.
                    continue;
                }
                else
                {
                    var idTakenByOther = !IsSafeNewProjectId(projDto.Id) || existing != null;

                    var newCloudProj = new CloudProject
                    {
                        Id = idTakenByOther ? Guid.NewGuid().ToString() : projDto.Id,
                        Name = projDto.Name,
                        Slug = System.Text.RegularExpressions.Regex.Replace(projDto.Name.ToLowerInvariant(), @"[^a-z0-9\-]", "-").Trim('-'),
                        DbType = projDto.DbType,
                        SchemaJson = projDto.SchemaJson,
                        NodePositionsJson = projDto.NodePositionsJson,
                        UserId = userId,
                        OrganizationId = activeOrgId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await _context.CloudProjects.AddAsync(newCloudProj);
                }
                savedCount++;
            }

            await _context.SaveChangesAsync();

            // Kaydetme başarılı OLDUKTAN SONRA — sunucu, istemcinin "şunu değiştirdim"
            // iddiasına değil, KENDİ hesapladığı diff'e güveniyor (bkz. spec "Kritik
            // bulgu"). Kuyruk dolarsa iş sessizce loglanıp atlanır — bu, senkron
            // isteğin (proje kaydetme) başarısını ASLA etkilemez.
            // ÖNCE: bu partide hangi projelerin AÇIK bir otomasyon kuralı var?
            // Kullanıcıların büyük çoğunluğunun hiç kuralı yok; kural yoksa
            // deserialize + diff + enqueue işinin TAMAMI boşa gidiyordu (her
            // sync'te, her proje için). Tek sorgu + HashSet: Task 1'in
            // (ProjectId, Enabled) indeksi bunu zaten karşılıyor.
            var diffProjectIds = pendingDiffs.Select(d => d.ProjectId).Distinct().ToList();
            var projectIdsWithRules = diffProjectIds.Count == 0
                ? new HashSet<string>()
                : (await _context.AutomationRules.AsNoTracking()
                        .Where(r => diffProjectIds.Contains(r.ProjectId) && r.Enabled)
                        .Select(r => r.ProjectId)
                        .Distinct()
                        .ToListAsync())
                    .ToHashSet();

            foreach (var (pid, dbType, oldJson, newJson) in pendingDiffs)
            {
                if (!projectIdsWithRules.Contains(pid)) continue;

                try
                {
                    var oldSchema = string.IsNullOrWhiteSpace(oldJson)
                        ? new DatabaseSchema()
                        : JsonSerializer.Deserialize<DatabaseSchema>(oldJson, Namines.Infrastructure.Services.SchemaJsonOptions.Default) ?? new DatabaseSchema();
                    var newSchema = JsonSerializer.Deserialize<DatabaseSchema>(newJson, Namines.Infrastructure.Services.SchemaJsonOptions.Default) ?? new DatabaseSchema();

                    var engine = Enum.TryParse<DatabaseType>(dbType, ignoreCase: true, out var parsedEngine)
                        ? parsedEngine : DatabaseType.PostgreSQL;
                    var diff = await _migrationService.CalculateDiffAsync(oldSchema, newSchema, engine);

                    if (!_automationQueue.TryEnqueue(new AutomationJob(pid, diff, oldSchema, newSchema)))
                        _logger.LogWarning("Namines Flow: {ProjectId} icin otomasyon kuyrugu dolu, tetikleme atlandi.", pid);
                }
                catch (Exception ex)
                {
                    // Diff hesaplama patlarsa (örn. bozuk JSON) senkron isteği ASLA
                    // düşürmemeli — kullanıcı projesini kaydedebilmeli, otomasyon
                    // tetiklenmemesi ikincil bir kayıp.
                    _logger.LogError(ex, "Namines Flow: {ProjectId} icin diff hesaplanamadi.", pid);
                }
            }

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
                    // İstemci bunu saklayıp `sync`'te geri göndermek zorunda,
                    // yoksa çakışma koruması devreye girmez (B-32).
                    p.RowVersion,
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
                p.RowVersion,
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

            RenewCurrentSession(user);
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

            RenewCurrentSession(user);
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

            RenewCurrentSession(user);
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

        /// <summary>
        /// Güvenlik damgası yenilendikten sonra İŞLEMİ YAPAN oturumu yeni
        /// damgalı bir jetonla sürdürür.
        ///
        /// Identity, authenticator anahtarını sıfırlarken ve 2FA'yı açıp
        /// kapatırken damgayı yeniliyor. Bu doğru: o hesaba ait DİĞER oturumlar
        /// düşmeli. Ama işlemi yapan tarayıcının jetonu da eski damgayı taşıdığı
        /// için sonraki isteği 401 alıyordu — "Turn on"dan sonra kodu doğrulamak
        /// oturumu kapatıyor, 2FA fiilen açılamıyordu (bkz. MfaSessionTests).
        /// </summary>
        private void RenewCurrentSession(ApplicationUser user)
        {
            SetAuthCookie(GenerateJwtToken(user));
            if (HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) is IMemoryCache cache)
                SecurityStampValidation.ForgetStamp(cache, user.Id);
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

        /// <summary>Desk devir teslimiyle verilen jetonu işaretleyen claim.</summary>
        public const string TokenPurposeClaimType = "namines:purpose";

        /// <summary>
        /// Desk devir teslim jetonunun ömrü.
        ///
        /// <b>Bulunan açık (docs/last-review M-15):</b> 30 SANİYELİK, tek kullanımlık
        /// bir devir teslim jetonu, 7 GÜNLÜK ve tam yetkili normal bir oturum
        /// jetonuna çevriliyordu. Yani el sıkışmanın dar penceresi, elde edilen şeyin
        /// dar olduğu anlamına gelmiyordu: jeton Desk'e bağlı değil, projeye bağlı
        /// değil ve `sessionStorage`'da tutuluyor. Ömür 7 günden 12 saate çekildi —
        /// çalınan bir Desk jetonunun işe yarama süresi 14 kat kısaldı.
        /// </summary>
        private static readonly TimeSpan DeskHandoffSessionLifetime = TimeSpan.FromHours(12);

        private string GenerateJwtToken(ApplicationUser user) =>
            GenerateJwtToken(user, TimeSpan.FromDays(7), purpose: null);

        /// <param name="lifetime">Jetonun ömrü.</param>
        /// <param name="purpose">
        /// Doluysa <see cref="TokenPurposeClaimType"/> claim'i olarak ekleniyor.
        /// Şu an yetki KISITLAMIYOR; jetonun nereden geldiğini denetlenebilir kılıyor
        /// ve uç-bazlı kapsam kontrolü eklendiğinde dayanağı hazır olsun diye var
        /// (bkz. docs/last-review M-15'in "kapsam dışı" notu).
        /// </param>
        private string GenerateJwtToken(ApplicationUser user, TimeSpan lifetime, string? purpose)
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

            if (!string.IsNullOrEmpty(purpose))
            {
                claims.Add(new Claim(TokenPurposeClaimType, purpose));
            }

            // Fallback anahtar burada TEKRARLANMIYOR: Program.cs, Development
            // disinda Jwt:Key tanimsizsa uygulamayi hic baslatmiyor. Ikinci bir
            // kopya, o kapinin ilerideki bir degisiklikte sessizce atlanmasini
            // mumkun kilardi.
            var secretKey = _configuration["Jwt:Key"] ?? DevFallbackJwtKey;
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.Add(lifetime);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"] ?? "NaminesServer",
                audience: _configuration["Jwt:Audience"] ?? "NaminesClient",
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Namines Hub & Auth (Faz 4): OAuth sağlayıcılarının (Google, GitHub)
        /// yapılandırma durumunu ve istemci kimliklerini döner.
        /// </summary>
        [HttpGet("oauth/providers")]
        [AllowAnonymous]
        public IActionResult GetOAuthProviders()
        {
            var googleClientId = _configuration["Authentication:Google:ClientId"]
                                 ?? _configuration["Google:ClientId"]
                                 ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");

            var githubClientId = _configuration["Authentication:GitHub:ClientId"]
                                 ?? _configuration["GitHub:ClientId"]
                                 ?? Environment.GetEnvironmentVariable("GITHUB_CLIENT_ID");

            return Ok(new
            {
                google = new
                {
                    enabled = !string.IsNullOrWhiteSpace(googleClientId),
                    clientId = googleClientId
                },
                github = new
                {
                    enabled = !string.IsNullOrWhiteSpace(githubClientId),
                    clientId = githubClientId
                }
            });
        }

        /// <summary>
        /// Namines Hub & Auth (Faz 4): Google veya GitHub OAuth callback'ini işler,
        /// kullanıcıyı bulur veya otomatik oluşturur, oturum cookie'sini yazar.
        /// Geliştirme ortamında (Development) sağlayıcı anahtarı yoksa güvenli simülasyon sunar.
        /// </summary>
        [HttpPost("oauth/callback")]
        [AllowAnonymous]
        public async Task<IActionResult> OAuthCallback([FromBody] OAuthCallbackRequestDto model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Provider))
                return BadRequest(new { Message = "Provider is required (google or github)." });

            var provider = model.Provider.Trim().ToLowerInvariant();
            string email = "";
            string name = "";
            string? avatar = null;

            // 1. Geliştirme ortamı / Simülasyon modu
            if (model.IsDevSimulation)
            {
                if (provider == "google")
                {
                    email = "google.dev@namines.app";
                    name = "Google Developer";
                }
                else if (provider == "github")
                {
                    email = "github.dev@namines.app";
                    name = "github-developer";
                }
                else
                {
                    return BadRequest(new { Message = $"Unknown provider '{provider}'." });
                }
            }
            else
            {
                // Canlı OAuth akışı (Google ID Token veya GitHub Auth Code)
                if (provider == "google")
                {
                    if (string.IsNullOrWhiteSpace(model.IdToken) && string.IsNullOrWhiteSpace(model.Code))
                        return BadRequest(new { Message = "Google idToken or code is required." });

                    using var httpClient = new HttpClient();
                    if (!string.IsNullOrWhiteSpace(model.IdToken))
                    {
                        var resp = await httpClient.GetAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={model.IdToken}");
                        if (!resp.IsSuccessStatusCode)
                            return BadRequest(new { Message = "Invalid Google ID token." });

                        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
                        var root = doc.RootElement;
                        email = root.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
                        name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        avatar = root.TryGetProperty("picture", out var p) ? p.GetString() : null;
                    }
                    else
                    {
                        var clientId = _configuration["Authentication:Google:ClientId"] ?? _configuration["Google:ClientId"];
                        var clientSecret = _configuration["Authentication:Google:ClientSecret"] ?? _configuration["Google:ClientSecret"];
                        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                            return BadRequest(new { Message = "Google OAuth credentials not configured on server." });

                        var tokenResp = await httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            { "code", model.Code! },
                            { "client_id", clientId },
                            { "client_secret", clientSecret },
                            { "redirect_uri", model.RedirectUri ?? "http://localhost:3000/auth/callback" },
                            { "grant_type", "authorization_code" }
                        }));

                        if (!tokenResp.IsSuccessStatusCode)
                            return BadRequest(new { Message = "Failed to exchange Google auth code." });

                        using var tokenDoc = await JsonDocument.ParseAsync(await tokenResp.Content.ReadAsStreamAsync());
                        var idToken = tokenDoc.RootElement.GetProperty("id_token").GetString();
                        var verifyResp = await httpClient.GetAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={idToken}");
                        using var doc = await JsonDocument.ParseAsync(await verifyResp.Content.ReadAsStreamAsync());
                        var root = doc.RootElement;
                        email = root.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
                        name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    }
                }
                else if (provider == "github")
                {
                    if (string.IsNullOrWhiteSpace(model.Code))
                        return BadRequest(new { Message = "GitHub authorization code is required." });

                    var clientId = _configuration["Authentication:GitHub:ClientId"] ?? _configuration["GitHub:ClientId"];
                    var clientSecret = _configuration["Authentication:GitHub:ClientSecret"] ?? _configuration["GitHub:ClientSecret"];
                    if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                        return BadRequest(new { Message = "GitHub OAuth credentials not configured on server." });

                    using var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Namines-OAuth");
                    httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                    var tokenResp = await httpClient.PostAsync("https://github.com/login/oauth/access_token", new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "client_id", clientId },
                        { "client_secret", clientSecret },
                        { "code", model.Code! },
                        { "redirect_uri", model.RedirectUri ?? "http://localhost:3000/auth/callback" }
                    }));

                    if (!tokenResp.IsSuccessStatusCode)
                        return BadRequest(new { Message = "Failed to exchange GitHub auth code." });

                    using var tokenDoc = await JsonDocument.ParseAsync(await tokenResp.Content.ReadAsStreamAsync());
                    if (!tokenDoc.RootElement.TryGetProperty("access_token", out var accessTokenProp))
                        return BadRequest(new { Message = "GitHub did not return an access token." });

                    var accessToken = accessTokenProp.GetString();
                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                    var userResp = await httpClient.GetAsync("https://api.github.com/user");
                    if (!userResp.IsSuccessStatusCode)
                        return BadRequest(new { Message = "Failed to fetch GitHub user profile." });

                    using var userDoc = await JsonDocument.ParseAsync(await userResp.Content.ReadAsStreamAsync());
                    var userRoot = userDoc.RootElement;
                    name = userRoot.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
                    avatar = userRoot.TryGetProperty("avatar_url", out var av) ? av.GetString() : null;

                    if (userRoot.TryGetProperty("email", out var em) && !string.IsNullOrEmpty(em.GetString()))
                    {
                        email = em.GetString()!;
                    }
                    else
                    {
                        var emailsResp = await httpClient.GetAsync("https://api.github.com/user/emails");
                        if (emailsResp.IsSuccessStatusCode)
                        {
                            using var emailsDoc = await JsonDocument.ParseAsync(await emailsResp.Content.ReadAsStreamAsync());
                            foreach (var item in emailsDoc.RootElement.EnumerateArray())
                            {
                                if (item.TryGetProperty("primary", out var prim) && prim.GetBoolean() &&
                                    item.TryGetProperty("verified", out var ver) && ver.GetBoolean() &&
                                    item.TryGetProperty("email", out var e))
                                {
                                    email = e.GetString() ?? "";
                                    break;
                                }
                            }
                        }
                    }
                }
                else
                {
                    return BadRequest(new { Message = $"Unsupported OAuth provider '{provider}'." });
                }
            }

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { Message = "Could not obtain verified email from OAuth provider." });

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                var baseUsername = !string.IsNullOrWhiteSpace(name)
                    ? System.Text.RegularExpressions.Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9\-]", "")
                    : email.Split('@')[0];
                if (string.IsNullOrWhiteSpace(baseUsername)) baseUsername = "user";
                var uniqueUsername = baseUsername;
                int suffix = 1;
                while (await _userManager.FindByNameAsync(uniqueUsername) != null)
                {
                    uniqueUsername = $"{baseUsername}{suffix++}";
                }

                user = new ApplicationUser
                {
                    UserName = uniqueUsername,
                    Email = email,
                    EmailConfirmed = true,
                    Type = UserType.Individual,
                    CreatedAt = DateTime.UtcNow,
                    ProfileJson = !string.IsNullOrEmpty(avatar)
                        ? JsonSerializer.Serialize(new { avatarUrl = avatar })
                        : null
                };

                var createRes = await _userManager.CreateAsync(user);
                if (!createRes.Succeeded)
                {
                    var errors = string.Join(", ", createRes.Errors.Select(e => e.Description));
                    return BadRequest(new { Message = $"Could not create user account: {errors}" });
                }

                var defaultPolicy = new UserAIPolicy
                {
                    UserId = user.Id,
                    UpdatedAt = DateTime.UtcNow
                };
                await _context.UserAIPolicies.AddAsync(defaultPolicy);

                var defaultQuota = new UserAIQuota
                {
                    UserId = user.Id,
                    DailyLimit = 100,
                    DailyUsageCount = 0,
                    LastResetDate = DateTime.UtcNow
                };
                await _context.UserAIQuotas.AddAsync(defaultQuota);
                await _context.SaveChangesAsync();
            }

            var token = GenerateJwtToken(user);
            SetAuthCookie(token);

            var quota = await _context.UserAIQuotas.AsNoTracking().FirstOrDefaultAsync(q => q.UserId == user.Id);
            var quotaInfo = quota != null ? new
            {
                dailyLimit = quota.DailyLimit,
                used = quota.DailyUsageCount,
                remaining = Math.Max(0, quota.DailyLimit - quota.DailyUsageCount),
                resetAt = quota.LastResetDate.AddHours(3).Date.AddDays(1).AddHours(-3).ToString("o")
            } : null;

            return Ok(new
            {
                Token = token,
                User = new
                {
                    Username = user.UserName,
                    Email = user.Email,
                    Type = user.Type.ToString().ToLowerInvariant(),
                    IsDev = user.IsDev,
                    CompanyName = user.CompanyName
                },
                Quota = quotaInfo
            });
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

        /// <summary>
        /// İstemcinin bu projeyi en son GÖRDÜĞÜ sürüm (REL-003a / B-32).
        ///
        /// <b>Null ise kontrol atlanır ve bu bilinçli bir taviz.</b> Alanı
        /// zorunlu yapmak, bugün çalışan her istemciyi (ve kaydedilmiş her
        /// yerel projeyi) anında kırardı — kullanıcı kendi işini kaydedemez
        /// hâle gelirdi. Değer gönderen istemci korunuyor; göndermeyen,
        /// değişiklikten önceki davranışı görüyor.
        ///
        /// Sunucu bu değeri <c>GET /auth/projects</c> yanıtında
        /// <c>rowVersion</c> olarak veriyor.
        /// </summary>
        public uint? RowVersion { get; set; }
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

    public class OAuthCallbackRequestDto
    {
        public string Provider { get; set; } = null!;
        public string? Code { get; set; }
        public string? IdToken { get; set; }
        public string? RedirectUri { get; set; }
        public bool IsDevSimulation { get; set; }
    }
}
