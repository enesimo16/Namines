using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Projenin varsayılan ("main") branch'ini bul-yoksa-oluştur — TEK kopya.
///
/// Neden burada: bu mantık daha önce hem <c>BranchController.GetOrCreateDefaultBranch</c>
/// hem de <c>ChangeRequestController.CreateQuick</c> içinde ayrı ayrı yazılıydı. Yarış
/// durumuna karşı sertleştirme yalnızca birine uygulanınca diğeri sessizce savunmasız
/// kaldı (code review bulgusu) — kopyalanmış mantığın klasik bedeli. Artık iki çağıran
/// da buraya gider.
/// </summary>
public static class BranchProvisioning
{
    /// <summary>
    /// Kısmi unique index (ProjectId WHERE IsDefault) sayesinde eşzamanlı iki çağrıdan
    /// yalnızca biri INSERT edebilir. Kaybeden taraf 500 vermek yerine kazananın
    /// oluşturduğu satırı okuyup döner — böylece aynı anda katılan iki kullanıcı yine
    /// aynı branch'te (ve G17 sayesinde aynı realtime odasında) buluşur.
    /// </summary>
    public static async Task<Branch> GetOrCreateDefaultBranchAsync(
        this AuthDbContext context,
        string projectId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var branch = await context.Branches
            .FirstOrDefaultAsync(b => b.ProjectId == projectId && b.IsDefault, cancellationToken);
        if (branch is not null) return branch;

        var created = new Branch
        {
            ProjectId = projectId,
            Name = "main",
            IsDefault = true,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        await context.Branches.AddAsync(created, cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı bir istek bizden önce oluşturdu — kendi izlenen kopyamızı bırak,
            // kazananın satırını oku.
            context.Entry(created).State = EntityState.Detached;

            var winner = await context.Branches
                .FirstOrDefaultAsync(b => b.ProjectId == projectId && b.IsDefault, cancellationToken);

            // Gerçekten yarış idiyse winner dolu olur. Değilse (başka bir kısıt ihlali)
            // hatayı yutmak yanlış olur — yukarı fırlat.
            if (winner is null) throw;
            return winner;
        }
    }
}
