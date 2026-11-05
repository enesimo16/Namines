using System.Threading;
using System.Threading.Tasks;

namespace Namines.BuildingBlocks.Security;

/// <summary>
/// "Bu kullanıcı platform yöneticisi mi?" sorusunun modüllere açık tek yanıtı. Kimlik tablolarına
/// dokunmadan Application katmanında da yetki denetlenebilsin: denetim yalnızca controller'da
/// kalırsa servisi çağıran başka bir yol (yeni uç, arka plan işi) onu atlardı.
/// </summary>
public interface IPlatformAdminAuthority
{
    Task<bool> IsPlatformAdminAsync(string? userId, CancellationToken ct = default);
}
