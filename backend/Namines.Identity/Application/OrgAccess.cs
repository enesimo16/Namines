using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;

namespace Namines.Identity.Application;

/// <summary>Kullanıcının kişisel organizasyonu. Proje rolü artık ITenantProjectAuthority'de (Studio).</summary>
internal static class OrgAccess
{
    /// <summary>
    /// Kullanıcının kişisel organizasyonunu bul-yoksa-oluştur. Kayıt sırasında ve
    /// eski kullanıcılar için tembel (lazy) çağrılır.
    /// BranchProvisioning ile aynı yarış-güvenli desen.
    /// </summary>
    public static async Task<Organization> GetOrCreatePersonalOrgAsync(
        this IIdentityData context, string userId, string displayName, CancellationToken ct = default)
    {
        var existing = await context.Organizations
            .FirstOrDefaultAsync(o => o.IsPersonal && o.CreatedByUserId == userId, ct);
        if (existing is not null) return existing;

        var org = new Organization
        {
            Name = string.IsNullOrWhiteSpace(displayName) ? "Personal" : $"{displayName}'s workspace",
            IsPersonal = true,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        await context.Organizations.AddAsync(org, ct);
        await context.OrganizationMembers.AddAsync(new OrganizationMember
        {
            OrganizationId = org.Id,
            UserId = userId,
            Role = OrgRole.Owner
        }, ct);

        try
        {
            await context.SaveChangesAsync(ct);
            return org;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // UX_Organizations_PersonalOwner (kullanıcı başına tek kişisel org) eşzamanlı ikinci
            // insert'i düşürdü: kazananı oku. Bağlantı kopması gibi başka hatalar yutulmaz.
            context.Entry(org).State = EntityState.Detached;
            var winner = await context.Organizations
                .FirstOrDefaultAsync(o => o.IsPersonal && o.CreatedByUserId == userId, ct);
            if (winner is null) throw;
            return winner;
        }
    }

    /// <summary>
    /// Benzersizlik ihlali mi? PostgreSQL SQLSTATE 23505; SQLite (yalnızca testler) SQLITE_CONSTRAINT=19.
    /// API katmanındaki DbUniqueViolation ile aynı mantık (Infrastructure API'ye referans veremez).
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        System.Data.Common.DbException { SqlState: "23505" } => true,
        Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 } => true,
        _ => false,
    };
}
