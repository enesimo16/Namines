using System.Threading;
using System.Threading.Tasks;

namespace Namines.BuildingBlocks.Security;

/// <summary>
/// Bir projenin çözülmüş (decrypted) canlı veritabanı bağlantı bilgisi.
/// </summary>
public sealed record ResolvedProjectConnection(
    string ProjectId,
    string ProjectName,
    string ConnectionString,
    string ConnectionDbType,
    bool AllowDeskSql,
    string? HostPolicyDenyReason = null);

/// <summary>
/// Canlı veritabanı bağlantı sırlarının modüller arası güvenli erişim sözleşmesi
/// (Kritik Kontrol #3: Secret Management).
///
/// <b>Neden gerekli:</b> `Vault`, `Ground`, `Desk` ve `Gateway` modüllerinin şifreli bağlantı
/// dizesini okumak veya güncellemek için `CloudProject` varlığına doğrudan erişmesi modül
/// sınırını deler. Bu sözleşme şifre çözme/şifreleme (`IConnectionSecretProtector`) ve
/// SSRF host politikası doğrulamasını tek bir güvenli kapıda toplar.
/// </summary>
public interface IProjectConnectionSecretProvider
{
    Task<ResolvedProjectConnection?> ResolveAsync(string projectId, CancellationToken ct = default);

    /// <summary>
    /// Kiracı ve kullanıcı yetkisini (<see cref="ITenantProjectAuthority.CanViewAsync"/>)
    /// doğruladıktan sonra bağlantıyı çözer. Yetkisiz erişimde <c>null</c> döner.
    /// </summary>
    Task<ResolvedProjectConnection?> ResolveForUserAsync(string projectId, string userId, CancellationToken ct = default);

    string? FindDisallowedHost(string connectionString, string connectionDbType);

    Task<bool> SetConnectionAsync(
        string projectId,
        string rawConnectionString,
        string connectionDbType,
        CancellationToken ct = default);

    Task<bool> ClearConnectionAsync(string projectId, CancellationToken ct = default);

    Task<bool> SetDeskSqlEnabledAsync(string projectId, bool enabled, CancellationToken ct = default);
}
