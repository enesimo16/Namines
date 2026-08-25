using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

public sealed record CreateInviteRequest(OrgRole Role = OrgRole.Editor, int ExpiresInDays = 7);

/// <summary>
/// Team planının ekip yönetimi: koltuklar, tek kullanımlık davet bağlantıları
/// ve ekip etkinliği.
///
/// <b>Neden ProjectMemberController'dan ayrı:</b> o controller PROJE bazında
/// çalışıyor ve üyeyi e-posta ile doğrudan ekliyor — karşı tarafın önceden
/// kayıtlı olmasını zorunlu kılıyor. Burası ORGANİZASYON bazında çalışıyor ve
/// bağlantı üretiyor: kişi bağlantıyı alıp önce kaydolabilir.
/// </summary>
[ApiController]
[Route("api/team")]
[Authorize]
public class TeamController : ControllerBase
{
    private readonly AuthDbContext _context;

    public TeamController(AuthDbContext context)
    {
        _context = context;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>Kullanıcının planı — koltuk sayısı buradan geliyor.</summary>
    private async Task<PlanTier> TierAsync(string userId, CancellationToken ct)
    {
        var account = await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SubscriptionStatus, u.PlanCode, u.IsDev })
            .FirstOrDefaultAsync(ct);

        return PlanQuotas.Resolve(account?.SubscriptionStatus, account?.PlanCode, account?.IsDev ?? false);
    }

    /// <summary>
    /// Kullanıcının kendi (kişisel) organizasyonu — ekip buranın üstüne kuruluyor.
    ///
    /// Ayrı bir "team org" açmıyoruz: kullanıcının projeleri zaten kişisel org'unda
    /// ve ekibi ayrı bir org'a koymak, var olan projeleri taşımayı gerektirirdi.
    /// Taşıma sırasında bir proje geride kalırsa kimse fark etmez.
    /// </summary>
    private async Task<Organization> MyOrgAsync(string userId, CancellationToken ct)
    {
        var name = await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct);

        return await _context.GetOrCreatePersonalOrgAsync(userId, name ?? "Personal", ct);
    }

    /// <summary>
    /// Kullanıcının EKİP organizasyonu — davetle katıldığı org varsa o, yoksa kendi
    /// kişisel org'u.
    ///
    /// <b>Neden gerekli:</b> davetle katılan biri için "kendi kişisel org'u" yanlış
    /// cevap. Önceden burada doğrudan kişisel org dönüyordu ve sonuç şuydu: birisi
    /// davet bağlantısıyla ekibe katılıyor, ekip ekranını açıyor ve kendisinden
    /// başka kimseyi göremiyordu — katıldığı ekip görünmez kalıyordu.
    ///
    /// Birden fazla ekipte olan kişide EN ESKİ üyelik seçiliyor: rastgele bir
    /// tanesini seçmek, ekranın her açılışta farklı ekip göstermesi demek olurdu.
    /// </summary>
    private async Task<Organization> ActiveOrgAsync(string userId, CancellationToken ct)
    {
        var personal = await MyOrgAsync(userId, ct);

        var joined = await _context.OrganizationMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.OrganizationId != personal.Id)
            .OrderBy(m => m.JoinedAt)
            .Select(m => m.Organization)
            .FirstOrDefaultAsync(ct);

        return joined ?? personal;
    }

    /// <summary>
    /// Organizasyonun plan katmanı — <b>onu KURAN kişinin planı.</b>
    ///
    /// Çağıranın planına bakmak yanlış olurdu: davetle katılan üye Free olabilir
    /// ve koltuğu o satın almadı. Kendi planına bakılsaydı, ekibe katılmış bir
    /// Free kullanıcı ekip ekranını "planınız tek kişilik" diye görürdü.
    /// </summary>
    private async Task<PlanTier> OrgTierAsync(Organization org, CancellationToken ct)
        => await TierAsync(org.CreatedByUserId, ct);

    // ── Ekip görünümü ────────────────────────────────────────────────────────

    /// <summary>Ekip üyeleri, koltuk durumu ve bekleyen davetler.</summary>
    [HttpGet]
    public async Task<IActionResult> GetTeam(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var org = await ActiveOrgAsync(userId, ct);
        var tier = await OrgTierAsync(org, ct);
        var seats = PlanQuotas.For(tier).TeamSeats;

        var members = await _context.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == org.Id)
            .Include(m => m.User)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new
            {
                m.UserId,
                username = m.User.UserName,
                email = m.User.Email,
                role = m.Role.ToString(),
                m.JoinedAt,
                isYou = m.UserId == userId,
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        // Bekleyen davetler de koltuk sayıyor: saymasaydı, üç davet birden
        // gönderilip hepsi kabul edildiğinde sınır aşılırdı ve bunu geri almanın
        // yolu birini ekipten atmak olurdu.
        var pending = await _context.TeamInvites.AsNoTracking()
            .Where(i => i.OrganizationId == org.Id &&
                        i.AcceptedByUserId == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new { i.Id, role = i.Role.ToString(), i.CreatedAt, i.ExpiresAt })
            .ToListAsync(ct);

        return Ok(new
        {
            plan = tier.ToString(),
            // Team'e özel bir ekran: Free/Pro tek kişilik olduğu için orada
            // ekip arayüzü göstermek, satın alınmamış bir özelliği varmış gibi
            // sunmak olurdu.
            teamEnabled = seats < 0 || seats > 1,
            seats = new
            {
                total = seats,
                used = members.Count + pending.Count,
                available = seats < 0 ? -1 : Math.Max(0, seats - members.Count - pending.Count),
            },
            organizationId = org.Id,
            members,
            pendingInvites = pending,
        });
    }

    // ── Davet bağlantısı ─────────────────────────────────────────────────────

    /// <summary>
    /// Tek kullanımlık davet bağlantısı üretir.
    ///
    /// Ham token YALNIZCA burada, bir kez döndürülüyor; veritabanında özeti
    /// duruyor. Sonradan "bağlantıyı bir daha göster" mümkün değil — mümkün
    /// olsaydı, veritabanına okuma erişimi olan herkes her ekibe katılabilirdi.
    /// </summary>
    [HttpPost("invites")]
    public async Task<IActionResult> CreateInvite([FromBody] CreateInviteRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var org = await ActiveOrgAsync(userId, ct);
        var tier = await OrgTierAsync(org, ct);
        var seats = PlanQuotas.For(tier).TeamSeats;

        if (seats >= 0 && seats <= 1)
            return StatusCode(402, new { error = "Inviting teammates requires the Team plan." });

        // Davet etmek üye yönetimi yetkisi istiyor. Bu kontrol olmadan, Editor
        // olarak katılmış biri ekibe kendi seçtiği kişileri ekleyebilir ve
        // koltukları sahibin haberi olmadan doldurabilirdi.
        var myRole = await _context.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == org.Id && m.UserId == userId)
            .Select(m => (OrgRole?)m.Role)
            .FirstOrDefaultAsync(ct);

        if (myRole is not (OrgRole.Owner or OrgRole.Admin))
            return StatusCode(403, new { error = "Only team owners and admins can invite people." });

        // Owner davetle verilmiyor: bağlantıyı ele geçiren biri faturalama ve org
        // silme yetkisi kazanırdı. Yükseltme ancak var olan bir Owner tarafından
        // ProjectMemberController üzerinden yapılabilir.
        if (request.Role is OrgRole.Owner)
            return BadRequest(new { error = "Owner role cannot be granted through an invite link." });

        var now = DateTime.UtcNow;

        var memberCount = await _context.OrganizationMembers
            .CountAsync(m => m.OrganizationId == org.Id, ct);
        var pendingCount = await _context.TeamInvites
            .CountAsync(i => i.OrganizationId == org.Id &&
                             i.AcceptedByUserId == null && i.RevokedAt == null && i.ExpiresAt > now, ct);

        if (seats >= 0 && memberCount + pendingCount >= seats)
            return Conflict(new
            {
                error = $"No seats left. The {tier} plan allows {seats} people in total " +
                        "(including you). Revoke a pending invite or remove a member first.",
            });

        // Süre sınırı kullanıcıya bırakılıyor ama sınırsız değil: süresiz bir
        // bağlantı, ekipten ayrılan birinin elindeki eski linkle aylar sonra geri
        // dönebilmesi demek olurdu.
        var days = Math.Clamp(request.ExpiresInDays, 1, 30);

        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        var invite = new TeamInvite
        {
            OrganizationId = org.Id,
            TokenHash = Hash(raw),
            CreatedByUserId = userId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(days),
            Role = request.Role,
        };

        await _context.TeamInvites.AddAsync(invite, ct);
        await _context.SaveChangesAsync(ct);

        return Ok(new
        {
            invite.Id,
            token = raw,
            role = invite.Role.ToString(),
            invite.ExpiresAt,
        });
    }

    /// <summary>Bekleyen bir daveti iptal eder ve koltuğu geri verir.</summary>
    [HttpDelete("invites/{inviteId}")]
    public async Task<IActionResult> RevokeInvite(string inviteId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var org = await ActiveOrgAsync(userId, ct);

        var invite = await _context.TeamInvites
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.OrganizationId == org.Id, ct);
        if (invite is null) return NotFound(new { error = "Invite not found." });

        // Kullanılmış bir daveti iptal etmek anlamsız: kişi zaten ekipte ve onu
        // çıkarmanın yolu üyeliği silmek. Sessizce "başarılı" demek, kullanıcıya
        // birini çıkardığını sandırırdı.
        if (invite.AcceptedByUserId is not null)
            return Conflict(new { error = "This invite was already used. Remove the member instead." });

        invite.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(new { revoked = inviteId });
    }

    // ── Katılım ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Bağlantı geçerli mi — katılmadan önce gösterilecek önizleme.
    ///
    /// <b>Giriş GEREKTİRMİYOR ve bu zorunlu:</b> davet edilen kişinin çoğu zaman
    /// henüz hesabı yok. Kimlik istenseydi bağlantı 401 döner, arayüz de bunu
    /// "geçersiz bağlantı" diye gösterirdi — kişi hiç kaydolmadan vazgeçerdi.
    ///
    /// Sızan bilgi, davet edenin zaten paylaştığı şeyle sınırlı: organizasyon adı
    /// ve rol. Katılım hâlâ kimlik istiyor (bkz. AcceptInvite).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("invites/{token}/preview")]
    public async Task<IActionResult> PreviewInvite(string token, CancellationToken ct)
    {
        var invite = await _context.TeamInvites.AsNoTracking()
            .Include(i => i.Organization)
            .FirstOrDefaultAsync(i => i.TokenHash == Hash(token), ct);

        if (invite is null) return NotFound(new { error = "This invite link is not valid." });

        var now = DateTime.UtcNow;
        if (invite.AcceptedByUserId is not null)
            return Conflict(new { error = "This invite link has already been used." });
        if (invite.RevokedAt is not null)
            return Conflict(new { error = "This invite link was revoked." });
        if (invite.ExpiresAt <= now)
            return Conflict(new { error = "This invite link has expired." });

        return Ok(new
        {
            organization = invite.Organization.Name,
            role = invite.Role.ToString(),
            invite.ExpiresAt,
        });
    }

    /// <summary>
    /// Bağlantıyla ekibe katılır. Bağlantı bu çağrıda TÜKENİYOR.
    /// </summary>
    [HttpPost("invites/{token}/accept")]
    public async Task<IActionResult> AcceptInvite(string token, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var invite = await _context.TeamInvites
            .FirstOrDefaultAsync(i => i.TokenHash == Hash(token), ct);
        if (invite is null) return NotFound(new { error = "This invite link is not valid." });

        var now = DateTime.UtcNow;
        if (!invite.IsUsable(now))
            return Conflict(new { error = "This invite link is no longer usable." });

        var already = await _context.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == invite.OrganizationId && m.UserId == userId, ct);
        if (already)
        {
            // Zaten üye: daveti yakmıyoruz. Yaksaydık, yanlışlıkla kendi
            // bağlantısına tıklayan bir sahip, başkasına ayrılmış koltuğu
            // sessizce harcamış olurdu.
            return Conflict(new { error = "You are already a member of this team." });
        }

        await _context.OrganizationMembers.AddAsync(new OrganizationMember
        {
            OrganizationId = invite.OrganizationId,
            UserId = userId,
            Role = invite.Role,
            JoinedAt = now,
        }, ct);

        invite.AcceptedByUserId = userId;
        invite.AcceptedAt = now;

        await _context.SaveChangesAsync(ct);

        return Ok(new { joined = invite.OrganizationId, role = invite.Role.ToString() });
    }

    // ── Ekip etkinliği ───────────────────────────────────────────────────────

    /// <summary>
    /// Ekibin ortak projeleri ve kimin ne zaman dokunduğu.
    ///
    /// Ayrı bir etkinlik tablosu açmıyoruz: projelerin <c>UpdatedAt</c> ve
    /// change request kayıtları zaten "kim ne yaptı" sorusunu cevaplıyor.
    /// İkinci bir kayıt tutmak, ikisinin ayrışabileceği bir yer daha yaratırdı.
    /// </summary>
    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var org = await ActiveOrgAsync(userId, ct);

        var projects = await _context.CloudProjects.AsNoTracking()
            .Where(p => p.OrganizationId == org.Id)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(50)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.DbType,
                p.UpdatedAt,
                ownerUserId = p.UserId,
                ownerName = p.User.UserName,
            })
            .ToListAsync(ct);

        return Ok(new { projects });
    }
}
