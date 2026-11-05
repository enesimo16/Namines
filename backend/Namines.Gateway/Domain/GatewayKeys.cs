using System.Security.Cryptography;
using System.Text;

namespace Namines.Gateway.Domain;

/// <summary>Anahtar üretimi ve özeti: ham anahtar yalnızca üretim anında var, kayıtta yalnızca özeti durur.</summary>
public static class GatewayKeys
{
    public const string KeyPrefix = "nmn_";

    /// <summary>Aday bulmak için saklanan görünür önek; gizli değil, tek başına hiçbir şey kanıtlamaz.</summary>
    public const int PrefixLength = 12;

    public static (GatewayApiKey Entity, string RawKey) CreateKey(
        string projectId, string name, string createdByUserId, bool canWrite, DateTime? expiresAt)
    {
        // 32 bayt = 256 bit entropi. Tahmin edilemezlik anahtarın tek savunması.
        var raw = KeyPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "").Replace("/", "").Replace("=", "");

        var entity = new GatewayApiKey
        {
            ProjectId = projectId,
            Name = name,
            Prefix = raw[..PrefixLength],
            KeyHash = Hash(raw),
            CanWrite = canWrite,
            CreatedByUserId = createdByUserId,
            ExpiresAt = expiresAt,
        };

        return (entity, raw);
    }

    public static string Hash(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes);
    }
}
