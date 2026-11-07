using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
using Namines.Desk.Abstractions;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Persistence;
using Namines.Flow.Abstractions;
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

        private readonly Namines.Identity.Application.IIdentityAccounts _accounts;
        private readonly Namines.BuildingBlocks.Contracts.IBillingAccounts _billing;
        private readonly Namines.BuildingBlocks.Contracts.IAiPolicies _policies;
        private readonly IConfiguration _configuration;
        private readonly IMigrationService _migrationService;
        private readonly IAutomationJobQueue _automationQueue;
        private readonly ILogger<AuthController> _logger;
        private readonly IWebHostEnvironment? _environment;
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly AccountDeletionService? _accountDeletion;
        private readonly IFlowModule? _flowModule;
        private readonly IDeskModule? _deskModule;
        private readonly IStudioApplicationService _studio;
        private readonly IProjectLifecycleService _lifecycle;

        private readonly IProjectWorkspaceService _workspace;

        public AuthController(
            Namines.Identity.Application.IIdentityAccounts accounts,
            Namines.BuildingBlocks.Contracts.IBillingAccounts billing,
            Namines.BuildingBlocks.Contracts.IAiPolicies policies,
            IConfiguration configuration,
            IMigrationService migrationService,
            IAutomationJobQueue automationQueue,
            ILogger<AuthController> logger,
            IStudioApplicationService studio,
            IProjectLifecycleService lifecycle,
            IProjectWorkspaceService workspace,
            IWebHostEnvironment? environment = null,
            IHttpClientFactory? httpClientFactory = null,


            AccountDeletionService? accountDeletion = null,
            IFlowModule? flowModule = null,
            IDeskModule? deskModule = null)
        {
            _lifecycle = lifecycle;
            _workspace = workspace;
            _accounts = accounts;
            _billing = billing;
            _policies = policies;
            _configuration = configuration;
            _migrationService = migrationService;
            _automationQueue = automationQueue;
            _logger = logger;
            _studio = studio;
            _environment = environment;
            _httpClientFactory = httpClientFactory;
            _httpClientFactory = httpClientFactory;
            _accountDeletion = accountDeletion;
            _flowModule = flowModule;
            _deskModule = deskModule;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var registration = await _accounts.RegisterAsync(model.Username, model.Email, model.Password, model.CompanyName);
            if (registration.Status == Namines.Identity.Application.RegistrationStatus.ReservedEmail)
                return BadRequest(new { Message = "This email address is reserved and cannot be registered publicly." });
            if (registration.Status == Namines.Identity.Application.RegistrationStatus.EmailTaken)
                return BadRequest(new { Message = "An account with this email already exists." });
            if (registration.Status != Namines.Identity.Application.RegistrationStatus.Created)
                return BadRequest(new { Message = $"Registration failed: {registration.Errors}" });
            var user = registration.User!;

            // Varsayilan AI politikasi ve gunluk kota satiri; varsayilanlar ilgili modullerde tanimli.
            await _policies.EnsureAsync(user.Id);
            var defaultQuota = await _billing.ProvisionDefaultQuotaAsync(user.Id);

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
            // Kilit, parola, MFA kapıları ve başarısızlık sayacı Identity modülünde; burada yalnız HTTP eşlemesi.
            // Kullanıcı yoksa da aynı mesaj: farklı cevap, hangi e-postaların kayıtlı olduğunu sızdırırdı.
            var signIn = await _accounts.PasswordSignInAsync(model.Email, model.Password, model.TwoFactorCode);
            switch (signIn.Status)
            {
                case Namines.Identity.Application.PasswordSignInStatus.LockedOut:
                    return Unauthorized(new
                    {
                        Message = "This account is temporarily locked after too many failed sign-in attempts. Try again later.",
                    });
                case Namines.Identity.Application.PasswordSignInStatus.TwoFactorRequired:
                    return Unauthorized(new
                    {
                        requiresTwoFactor = true,
                        Message = "Enter the 6-digit code from your authenticator app.",
                    });
                case Namines.Identity.Application.PasswordSignInStatus.Succeeded:
                    break;
                default:
                    return Unauthorized(new { Message = "Incorrect email or password." });
            }
            var user = signIn.User!;

            var plan = await _billing.GetPlanSnapshotAsync(user.Id);
            var tier = Namines.Core.Analysis.PlanQuotas.Resolve(plan?.SubscriptionStatus, plan?.PlanCode, user.IsDev);

            var quota = await _billing.SyncQuotaOnLoginAsync(user.Id, user.IsDev);

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
            if (IsDeskHandoffSession()) return DeskHandoffForbidden();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (_deskModule is null) throw new InvalidOperationException("IDeskModule is required for Desk handoff token operations.");
            var raw = await _deskModule.Application.CreateHandoffTokenAsync(userId, ct);
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

            if (_deskModule is null) throw new InvalidOperationException("IDeskModule is required for Desk handoff token operations.");
            var userId = await _deskModule.Application.ExchangeHandoffTokenAsync(request.Token, ct);
            if (userId is null)
                return Unauthorized(new { Message = "The token is invalid, expired, or already used." });

            var user = await _accounts.FindByIdAsync(userId);
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

        [Authorize]
        [HttpPost("sync")]
        [RequestSizeLimit(SchemaRequestLimits.BulkSyncBytes)]
        public async Task<IActionResult> SyncProjects([FromBody] List<SyncProjectDto> projects)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _accounts.FindByIdAsync(userId);
            if (user == null)
                return Unauthorized();

            // Yetki, sürüm çakışması, kimlik üretimi ve işlem Studio'da (denetim M-2); burada yalnız HTTP eşlemesi.
            var outcome = await _lifecycle.SyncAsync(userId, user.UserName ?? "Personal",
                projects.Select(p => new ProjectSyncItem(p.Id, p.Name, p.DbType, p.SchemaJson, p.NodePositionsJson, p.RowVersion)).ToList());

            if (outcome.Status == ProjectSyncStatus.VersionConflict)
            {
                var conflict = outcome.Conflict!;
                return Conflict(new
                {
                    message = "This project was changed by someone else while you were working on it. " +
                              "Reload it to get the latest version, then re-apply your changes.",
                    projectId = conflict.ProjectId,
                    projectName = conflict.ProjectName,
                    yourVersion = conflict.YourVersion,
                    currentVersion = conflict.CurrentVersion,
                    changedAt = conflict.ChangedAt,
                });
            }
            if (outcome.Status == ProjectSyncStatus.ConcurrentModification)
                // İstemcinin sürümü yoksa yukarıdaki kontrol yakalamaz; xmin okuma-yazma yarışını yakalar: 500 değil 409.
                return ConcurrencyConflict.Result();

            var savedCount = outcome.SavedCount;
            var pendingDiffs = outcome.Changes.Select(c => (c.ProjectId, c.DbType, OldJson: c.OldSchemaJson, NewJson: c.NewSchemaJson)).ToList();

            // Kaydetme başarılı OLDUKTAN SONRA: sunucu istemcinin "şunu değiştirdim" iddiasına değil kendi hesapladığı
            // diff'e güvenir. Kuyruk dolarsa iş loglanıp atlanır, senkron isteğin başarısını ASLA etkilemez. Kuralı
            // olmayan projede deserialize + diff + enqueue boşa gitmesin diye önce kuralı olanlar tek sorguda alınır.
            var diffProjectIds = pendingDiffs.Select(d => d.ProjectId).Distinct().ToList();
            IReadOnlySet<string> projectIdsWithRules = diffProjectIds.Count == 0 || _flowModule is null

                ? new HashSet<string>()
                : await _flowModule.Application.GetProjectIdsWithEnabledRulesAsync(diffProjectIds);

            // Flow modulu uzerinden sorgulaniyor (dogrudan AutomationRules erisimi yok).
            // projectIdsWithRules yukarida Flow modulu ile cozuldu.
            // .Select(r => r.ProjectId)
            // .Distinct()
            // .ToListAsync())
            // .ToHashSet();

            foreach (var (pid, dbType, oldJson, newJson) in pendingDiffs)
            {
                if (!projectIdsWithRules.Contains(pid)) continue;

                try
                {
                    var oldSchema = string.IsNullOrWhiteSpace(oldJson)
                        ? new DatabaseSchema()
                        : JsonSerializer.Deserialize<DatabaseSchema>(oldJson, Namines.Core.Models.SchemaJsonOptions.Default) ?? new DatabaseSchema();
                    var newSchema = JsonSerializer.Deserialize<DatabaseSchema>(newJson, Namines.Core.Models.SchemaJsonOptions.Default) ?? new DatabaseSchema();

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

            // ORTAK WORKSPACE: üyesi olunan organizasyonların tüm projeleri, kişisel org dahil.
            // Üyelik sınırı Studio'da uygulanıyor (IProjectWorkspaceService).
            var workspace = await _workspace.ListForUserAsync(userId);
            var projects = workspace.Select(w => new
            {
                w.Project.Id,
                w.Project.Name,
                w.Project.DbType,
                w.Project.SchemaJson,
                w.Project.NodePositionsJson,
                w.Project.CreatedAt,
                w.Project.UpdatedAt,
                w.Project.AutoApproveSafeChanges,
                // Ekip görünümünde kimin projesi olduğu belli olmalı.
                ownerUserId = w.Project.UserId,
                ownerName = w.OwnerName,
                isMine = w.Project.UserId == userId,
                w.Project.OrganizationId,
                w.IsOwner,
                // İstemci bunu saklayıp `sync`'te geri göndermek zorunda (B-32).
                w.Project.RowVersion,
            }).ToList();

            // Namines Desk (namines_desk/02-PROJECTS.md §2): kart üzerindeki "bağlı/bağlı değil"
            // rozeti gerçek bir alana dayanıyor; bağlantı dizesinin KENDİSİ asla dönmez, yalnızca
            // var/yok bilgisi. Tek toplu sorgu: proje başına ayrı sorgu (N+1) değil.
            var connections = await _studio.GetConnectionMetadataAsync(projects.Select(p => p.Id).ToList());

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
                hasConnection = connections.GetValueOrDefault(p.Id)?.HasConnection ?? false,
                connectionDbType = connections.GetValueOrDefault(p.Id)?.ConnectionDbType,
                tableCount = TryCountTables(p.SchemaJson),
                isOwner = p.IsOwner,
                allowDeskSql = connections.GetValueOrDefault(p.Id)?.AllowDeskSql ?? false,
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
            var user = await _accounts.FindByIdAsync(userId!);
            if (user == null) return Unauthorized();

            // Parse extra profile data stored as JSON in ProfileJson field (if it exists)
            ProfileData profile = new();
            if (!string.IsNullOrEmpty(user.ProfileJson))
            {
                try { profile = System.Text.Json.JsonSerializer.Deserialize<ProfileData>(user.ProfileJson) ?? new(); }
                catch { }
            }

            var subscription = await _billing.GetSubscriptionAsync(user.Id);

            return Ok(new
            {
                username    = user.UserName,
                email       = user.Email,
                type        = user.Type.ToString().ToLowerInvariant(),
                companyName = user.CompanyName,
                subscriptionStatus = subscription?.Status ?? "none",
                currentPeriodEnd   = subscription?.CurrentPeriodEnd,
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
            var user = await _accounts.FindByIdAsync(userId!);
            if (user == null) return Unauthorized();

            // Update basic fields
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

            var result = await _accounts.UpdateProfileAsync(user, dto.CompanyName, System.Text.Json.JsonSerializer.Serialize(profile));
            if (!result.Succeeded)
                return BadRequest(new { message = "Profile update failed.", errors = result.Errors });

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
            if (IsDeskHandoffSession()) return DeskHandoffForbidden();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var user = await _accounts.FindByIdAsync(userId);
            if (user is null) return Unauthorized();

            if (!await _accounts.RevokeAllSessionsAsync(user))
                return StatusCode(500, new { Message = "Sessions could not be revoked. Try again." });

            // Bu tarayıcının cookie'si de siliniyor: kullanıcı "tüm oturumları
            // kapat" dediğinde burada oturumda kalması kafa karıştırıcı olurdu.
            Response.Cookies.Delete(AuthCookieName, BuildAuthCookieOptions(forExpiry: true));

            return Ok(new { Message = "All sessions revoked. Sign in again on every device." });
        }

        /// <summary>
        /// Taze oturum penceresi: parolası olmayan (yalnız OAuth) hesaplar için "yeniden doğrulama",
        /// jetonun <c>iat</c> değerinin bu süreden yeni olması (yani yakın zamanda giriş yapılmış olması).
        /// </summary>
        private static readonly TimeSpan FreshLoginWindow = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Hesabı KALICI olarak siler (KVKK/GDPR silme hakkı).
        ///
        /// <b>Yeniden doğrulama zorunlu:</b> yalnızca oturum yetmez, çalınmış bir jeton hesabı
        /// yok edebilirdi (MFA kapatmayla aynı mantık). Parolası olan hesap parolayı (MFA açıksa
        /// ayrıca kodu) gönderir; yalnız OAuth ile giren hesap son <see cref="FreshLoginWindow"/>
        /// içinde giriş yapmış olmalıdır. Ayrıca e-posta adresi yazılarak onay istenir.
        /// Desk devir teslim jetonu bu işi YAPAMAZ.
        /// </summary>
        [Authorize]
        [HttpDelete("me")]
        [EnableRateLimiting("sensitive")]
        public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request, CancellationToken ct)
        {
            if (IsDeskHandoffSession()) return DeskHandoffForbidden();
            if (_accountDeletion is null) return StatusCode(500, new { Message = "Account deletion is not available." });

            var user = await GetCurrentUserAsync();
            if (user is null) return Unauthorized();

            if (!string.Equals(request?.Confirmation?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { Message = "Type your account e-mail address exactly to confirm.", code = "CONFIRMATION_MISMATCH" });

            var reauth = await _accounts.ReauthenticateAsync(user, request?.Password, request?.TwoFactorCode);
            switch (reauth)
            {
                case Namines.Identity.Application.ReauthStatus.LockedOut:
                    return StatusCode(StatusCodes.Status429TooManyRequests, new { Message = "This account is temporarily locked. Try again later." });
                case Namines.Identity.Application.ReauthStatus.BadPassword:
                    return StatusCode(StatusCodes.Status403Forbidden, new { Message = "Password is incorrect.", code = "REAUTH_FAILED" });
                case Namines.Identity.Application.ReauthStatus.BadTwoFactorCode:
                    return StatusCode(StatusCodes.Status403Forbidden, new { Message = "That code is not valid.", code = "REAUTH_FAILED" });
                case Namines.Identity.Application.ReauthStatus.NoPassword when !HasFreshLogin():
                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        Message = "Sign in again, then retry within a few minutes to confirm it is you.",
                        code = "REAUTH_REQUIRED",
                    });
            }

            var result = await _accountDeletion.DeleteAsync(user.Id, ct);
            if (!result.Ok)
                return StatusCode(result.StatusCode, new
                {
                    Message = result.Error,
                    code = result.Code,
                    organizations = result.Organizations?.Select(o => new { o.Id, o.Name }),
                });

            Response.Cookies.Delete(AuthCookieName, BuildAuthCookieOptions(forExpiry: true));
            if (HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) is IMemoryCache cache)
                SecurityStampValidation.ForgetStamp(cache, user.Id);

            return Ok(new { Message = "Your account has been deleted." });
        }

        /// <summary>Jetonun verilme anı (<c>iat</c>) taze pencerede mi? Eski jetonlarda claim yoksa taze SAYILMAZ.</summary>
        private bool HasFreshLogin()
        {
            var iat = User.FindFirstValue(JwtRegisteredClaimNames.Iat);
            return long.TryParse(iat, out var seconds)
                   && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds) <= FreshLoginWindow;
        }

        // -- Cok faktorlu dogrulama (TOTP) -------------------------------------
        //
        // Saglayici ASP.NET Identity'nin kendi authenticator'i
        // (`AddDefaultTokenProviders` ile zaten kayitli). Ne yeni bir paket ne
        // de yeni bir tablo gerekti: `TwoFactorEnabled` ve authenticator
        // anahtari IdentityUser'da hazir duruyordu.

        /// <summary>Kurtarma kodu sayisi -- telefon kaybedildiginde tek cikis yolu.</summary>

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
            if (IsDeskHandoffSession()) return DeskHandoffForbidden();

            var user = await GetCurrentUserAsync();
            if (user is null) return Unauthorized();

            if (user.TwoFactorEnabled)
                return BadRequest(new { Message = "Two-factor authentication is already enabled." });

            // Her kurulum denemesinde YENI anahtar: yarim kalmis bir kurulumun
            // anahtari, kullanicinin telefonunda kalip sonraki kurulumla
            // cakisabilirdi.
            var key = await _accounts.BeginMfaSetupAsync(user);
            if (key is null)
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
            if (IsDeskHandoffSession()) return DeskHandoffForbidden();

            var user = await GetCurrentUserAsync();
            if (user is null) return Unauthorized();

            if (user.TwoFactorEnabled)
                return BadRequest(new { Message = "Two-factor authentication is already enabled." });

            var recoveryCodes = await _accounts.EnableMfaAsync(user, request.Code);
            if (recoveryCodes is null)
                return BadRequest(new { Message = "That code is not valid. Check your device clock and try again." });

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
            if (IsDeskHandoffSession()) return DeskHandoffForbidden();

            var user = await GetCurrentUserAsync();
            if (user is null) return Unauthorized();

            if (!user.TwoFactorEnabled)
                return BadRequest(new { Message = "Two-factor authentication is not enabled." });

            if (!await _accounts.DisableMfaAsync(user, request.Code))
                return BadRequest(new { Message = "That code is not valid." });

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
                    ? await _accounts.CountRecoveryCodesAsync(user)
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
        private void RenewCurrentSession(ApplicationUser user)
        {
            SetAuthCookie(GenerateJwtToken(user));
            if (HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) is IMemoryCache cache)
                SecurityStampValidation.ForgetStamp(cache, user.Id);
        }

        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return userId is null ? null : await _accounts.FindByIdAsync(userId);
        }

        /// <summary>
        /// Kimlik dogrulayici uygulamalari kodu bosluklu gosterir ("123 456");
        /// kullanici kopyalayip yapistirdiginda bosluk gelir. Temizlenmezse
        /// dogru kod yanlis sayilirdi.
        /// </summary>
        internal static string NormalizeCode(string? code) =>
            Namines.Identity.Application.IdentityAccountService.NormalizeCode(code);

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

        private const string DeskHandoffPurpose = "desk-handoff";

        /// <summary>
        /// Çağıran, Desk devir teslimiyle üretilmiş KISITLI bir jetonla mı geliyor?
        /// Böyle bir jeton kendi başına yeni devir jetonu üretebilseydi 12 saatlik
        /// pencere sonsuza uzar, çalınan bir Desk jetonu kalıcı erişime dönüşürdü;
        /// parola/MFA/oturum yönetimi de ana uygulamanın (tam oturum) işidir.
        /// </summary>
        private bool IsDeskHandoffSession() =>
            User.HasClaim(TokenPurposeClaimType, DeskHandoffPurpose);

        private IActionResult DeskHandoffForbidden() =>
            StatusCode(StatusCodes.Status403Forbidden, new
            {
                Message = "This session was issued for Namines Desk and cannot do this. Sign in to the main app instead.",
            });

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

                // Verilme anı: hesap silme gibi hassas işlemlerde "yakın zamanda giriş yapıldı mı" kanıtı.
                new Claim(JwtRegisteredClaimNames.Iat,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64),
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

        private sealed record OAuthIdentity(string Email, string Name, string? Avatar);

        private static readonly HttpClient FallbackOAuthHttp = new() { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>Fabrika varsa onun istemcisi (zaman aşımlı), yoksa paylaşılan statik istemci.</summary>
        private HttpClient CreateOAuthHttpClient()
        {
            if (_httpClientFactory is null) return FallbackOAuthHttp;
            var client = _httpClientFactory.CreateClient("oauth");
            client.Timeout = TimeSpan.FromSeconds(10);
            return client;
        }

        private bool IsOAuthDevSimulationAllowed() =>
            _environment?.IsDevelopment() == true
            && _configuration.GetValue<bool>("Auth:AllowOAuthDevSimulation");

        private string? GoogleClientId() =>
            _configuration["Authentication:Google:ClientId"]
            ?? _configuration["Google:ClientId"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");

        private static bool JsonTrue(JsonElement root, string prop) =>
            root.TryGetProperty(prop, out var v) &&
            (v.ValueKind == JsonValueKind.True ||
             (v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), "true", StringComparison.OrdinalIgnoreCase)));

        private async Task<(OAuthIdentity? Identity, IActionResult? Error)> ResolveGoogleIdentityAsync(OAuthCallbackRequestDto model)
        {
            if (string.IsNullOrWhiteSpace(model.IdToken) && string.IsNullOrWhiteSpace(model.Code))
                return (null, BadRequest(new { Message = "Google idToken or code is required." }));

            var clientId = GoogleClientId();
            if (string.IsNullOrEmpty(clientId))
                return (null, BadRequest(new { Message = "Google OAuth is not configured on server." }));

            var http = CreateOAuthHttpClient();
            var idToken = model.IdToken;

            if (string.IsNullOrWhiteSpace(idToken))
            {
                var clientSecret = _configuration["Authentication:Google:ClientSecret"] ?? _configuration["Google:ClientSecret"];
                if (string.IsNullOrEmpty(clientSecret))
                    return (null, BadRequest(new { Message = "Google OAuth credentials not configured on server." }));

                var tokenResp = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "code", model.Code! },
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "redirect_uri", model.RedirectUri ?? "http://localhost:3000/auth/callback" },
                    { "grant_type", "authorization_code" }
                }));

                if (!tokenResp.IsSuccessStatusCode)
                    return (null, BadRequest(new { Message = "Failed to exchange Google auth code." }));

                using var tokenDoc = await JsonDocument.ParseAsync(await tokenResp.Content.ReadAsStreamAsync());
                if (!tokenDoc.RootElement.TryGetProperty("id_token", out var idTokenProp) ||
                    string.IsNullOrWhiteSpace(idTokenProp.GetString()))
                    return (null, BadRequest(new { Message = "Google did not return an ID token." }));
                idToken = idTokenProp.GetString();
            }

            // Jeton URL'e KAÇIRILARAK gömülüyor: ham birleştirme sorgu parametresi enjeksiyonuna açıktı.
            var resp = await http.GetAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(idToken!)}");
            if (!resp.IsSuccessStatusCode)
                return (null, BadRequest(new { Message = "Invalid Google ID token." }));

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            var root = doc.RootElement;

            // `aud` bizim istemci kimliğimiz olmalı: aksi hâlde başka bir uygulama için
            // verilmiş geçerli bir Google jetonu burada kimlik kanıtı sayılırdı.
            var aud = root.TryGetProperty("aud", out var audProp) ? audProp.GetString() : null;
            if (!string.Equals(aud, clientId, StringComparison.Ordinal))
                return (null, BadRequest(new { Message = "Google ID token was not issued for this application." }));

            var iss = root.TryGetProperty("iss", out var issProp) ? issProp.GetString() : null;
            if (iss != "accounts.google.com" && iss != "https://accounts.google.com")
                return (null, BadRequest(new { Message = "Invalid Google ID token issuer." }));

            // Doğrulanmamış e-posta ile hesap bağlanırsa, başkasının adresini "sahiplenen" biri
            // o kişinin hesabına girebilirdi.
            if (!JsonTrue(root, "email_verified"))
                return (null, BadRequest(new { Message = "Your Google email address is not verified." }));

            var email = root.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var avatar = root.TryGetProperty("picture", out var p) ? p.GetString() : null;
            return (new OAuthIdentity(email, name, avatar), null);
        }

        private async Task<(OAuthIdentity? Identity, IActionResult? Error)> ResolveGitHubIdentityAsync(OAuthCallbackRequestDto model)
        {
            if (string.IsNullOrWhiteSpace(model.Code))
                return (null, BadRequest(new { Message = "GitHub authorization code is required." }));

            var clientId = _configuration["Authentication:GitHub:ClientId"] ?? _configuration["GitHub:ClientId"];
            var clientSecret = _configuration["Authentication:GitHub:ClientSecret"] ?? _configuration["GitHub:ClientSecret"];
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                return (null, BadRequest(new { Message = "GitHub OAuth credentials not configured on server." }));

            var http = CreateOAuthHttpClient();

            using var tokenReq = GitHubRequest(HttpMethod.Post, "https://github.com/login/oauth/access_token", null);
            tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "code", model.Code! },
                { "redirect_uri", model.RedirectUri ?? "http://localhost:3000/auth/callback" }
            });
            using var tokenResp = await http.SendAsync(tokenReq);
            if (!tokenResp.IsSuccessStatusCode)
                return (null, BadRequest(new { Message = "Failed to exchange GitHub auth code." }));

            using var tokenDoc = await JsonDocument.ParseAsync(await tokenResp.Content.ReadAsStreamAsync());
            if (!tokenDoc.RootElement.TryGetProperty("access_token", out var accessTokenProp) ||
                string.IsNullOrEmpty(accessTokenProp.GetString()))
                return (null, BadRequest(new { Message = "GitHub did not return an access token." }));
            var accessToken = accessTokenProp.GetString();

            using var userReq = GitHubRequest(HttpMethod.Get, "https://api.github.com/user", accessToken);
            using var userResp = await http.SendAsync(userReq);
            if (!userResp.IsSuccessStatusCode)
                return (null, BadRequest(new { Message = "Failed to fetch GitHub user profile." }));

            using var userDoc = await JsonDocument.ParseAsync(await userResp.Content.ReadAsStreamAsync());
            var userRoot = userDoc.RootElement;
            var name = userRoot.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
            var avatar = userRoot.TryGetProperty("avatar_url", out var av) ? av.GetString() : null;

            // /user'daki `email` alanı herkese açık profil e-postasıdır ve DOĞRULANMIŞ olmak
            // zorunda değil; yalnızca primary + verified adres kimlik kanıtı sayılır.
            var email = "";
            using var emailsReq = GitHubRequest(HttpMethod.Get, "https://api.github.com/user/emails", accessToken);
            using var emailsResp = await http.SendAsync(emailsReq);
            if (emailsResp.IsSuccessStatusCode)
            {
                using var emailsDoc = await JsonDocument.ParseAsync(await emailsResp.Content.ReadAsStreamAsync());
                if (emailsDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in emailsDoc.RootElement.EnumerateArray())
                    {
                        if (JsonTrue(item, "primary") && JsonTrue(item, "verified") &&
                            item.TryGetProperty("email", out var e))
                        {
                            email = e.GetString() ?? "";
                            break;
                        }
                    }
                }
            }

            return (new OAuthIdentity(email, name, avatar), null);
        }

        private static HttpRequestMessage GitHubRequest(HttpMethod method, string url, string? bearer)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Namines-OAuth");
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (bearer is not null)
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            return req;
        }

        /// <summary>
        /// Namines Hub & Auth (Faz 4): Google veya GitHub OAuth callback'ini işler,
        /// kullanıcıyı bulur veya otomatik oluşturur, oturum cookie'sini yazar.
        ///
        /// <b>Simülasyon modu</b> yalnızca Development ortamında VE
        /// <c>Auth:AllowOAuthDevSimulation=true</c> iken çalışır. Önceden istemcinin
        /// gönderdiği <c>IsDevSimulation</c> bayrağı her ortamda geçerliydi: anonim biri
        /// sabit "google.dev@/github.dev@" hesaplarına oturum açabiliyordu.
        /// </summary>
        [HttpPost("oauth/callback")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("sensitive")]
        public async Task<IActionResult> OAuthCallback([FromBody] OAuthCallbackRequestDto model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Provider))
                return BadRequest(new { Message = "Provider is required (google or github)." });

            var provider = model.Provider.Trim().ToLowerInvariant();
            if (provider != "google" && provider != "github")
                return BadRequest(new { Message = $"Unsupported OAuth provider '{provider}'." });

            string email;
            string name;
            string? avatar = null;

            if (model.IsDevSimulation)
            {
                if (!IsOAuthDevSimulationAllowed())
                    return BadRequest(new { Message = "OAuth dev simulation is disabled." });

                email = provider == "google" ? "google.dev@namines.app" : "github.dev@namines.app";
                name = provider == "google" ? "Google Developer" : "github-developer";
            }
            else
            {
                OAuthIdentity identity;
                try
                {
                    var (resolved, error) = provider == "google"
                        ? await ResolveGoogleIdentityAsync(model)
                        : await ResolveGitHubIdentityAsync(model);
                    if (resolved is null) return error!;
                    identity = resolved;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    _logger.LogWarning(ex, "OAuth provider {Provider} istegi basarisiz.", provider);
                    return StatusCode(StatusCodes.Status502BadGateway,
                        new { Message = "Could not reach the OAuth provider. Try again." });
                }

                email = identity.Email;
                name = identity.Name;
                avatar = identity.Avatar;
            }

            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { Message = "Could not obtain verified email from OAuth provider." });

            var external = await _accounts.ResolveExternalUserAsync(email, name, avatar);
            switch (external.Status)
            {
                case Namines.Identity.Application.ExternalSignInStatus.CreateFailed:
                    return BadRequest(new { Message = $"Could not create user account: {external.Errors}" });
                case Namines.Identity.Application.ExternalSignInStatus.LockedOut:
                    return Unauthorized(new
                    {
                        Message = "This account is temporarily locked after too many failed sign-in attempts. Try again later.",
                    });
                case Namines.Identity.Application.ExternalSignInStatus.TwoFactorRequired:
                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        requiresTwoFactor = true,
                        Message = "This account has two-factor authentication enabled. Sign in with your email, password and authenticator code instead.",
                    });
                case Namines.Identity.Application.ExternalSignInStatus.SecureFailed:
                    return StatusCode(500, new { Message = "Could not secure the existing account. Try again." });
                case Namines.Identity.Application.ExternalSignInStatus.LinkFailed:
                    return StatusCode(500, new { Message = "Could not link the existing account. Try again." });
            }
            var user = external.User!;
            if (external.Status == Namines.Identity.Application.ExternalSignInStatus.Created)
            {
                await _policies.EnsureAsync(user.Id);
                await _billing.ProvisionDefaultQuotaAsync(user.Id);
            }

            var token = GenerateJwtToken(user);
            SetAuthCookie(token);

            var quota = await _billing.GetQuotaRowAsync(user.Id);
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

    /// <param name="Password">Parolası olan hesaplar için zorunlu.</param>
    /// <param name="TwoFactorCode">MFA açıksa zorunlu.</param>
    /// <param name="Confirmation">Hesabın e-posta adresi, yazılarak onay.</param>
    public sealed record DeleteAccountRequest(string? Password, string? TwoFactorCode, string? Confirmation);

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
