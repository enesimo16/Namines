using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Core.Interfaces;

/// <summary>
/// Bağlanılan kullanıcının veritabanı üzerindeki yetkilerini raporlar
/// (new-phase/06-DATA-PLANE.md §5: "bağlanınca kullanıcının yetkileri raporlanır —
/// 'bu kullanıcı DROP TABLE yapabiliyor, daha dar bir rol öneriyoruz'").
///
/// Amaç engellemek DEĞİL, göstermek. Kullanıcının kendi veritabanı ve kendi
/// kimlik bilgisi; bir aracın onu kilitlemesi yanlış olurdu. Ama insanlar
/// alışkanlıkla süper kullanıcıyla bağlanır ve bunun ne anlama geldiğini o an
/// düşünmez — riski göz hizasına getirmek, sessizce kabullenmekten iyidir.
/// </summary>
public interface IDbPrivilegeInspector
{
    Task<DbPrivilegeReport> InspectAsync(
        string connectionString, string dbType, CancellationToken cancellationToken = default);
}

/// <param name="Username">Bağlantının fiilen kullandığı kullanıcı.</param>
/// <param name="IsSuperuser">
/// Süper kullanıcı/yönetici. Doğruysa aşağıdaki tekil yetkilerin hepsi zaten var
/// demektir; ayrı ayrı sorgulamak yanıltıcı olurdu.
/// </param>
/// <param name="Findings">İnsan diline çevrilmiş bulgular, en riskliden başlayarak.</param>
/// <param name="Recommendation">Öneri; risk yoksa null.</param>
public sealed record DbPrivilegeReport(
    string? Username,
    bool IsSuperuser,
    bool CanWrite,
    bool CanDropObjects,
    IReadOnlyList<DbPrivilegeFinding> Findings,
    string? Recommendation);

/// <param name="Severity">"high" | "medium" | "info".</param>
public sealed record DbPrivilegeFinding(string Severity, string Message);
