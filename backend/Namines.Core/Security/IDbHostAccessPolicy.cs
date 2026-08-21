namespace Namines.Core.Security;

/// <summary>
/// Kullanıcının verdiği bir veritabanı host'una bağlanmaya izin verilip verilmediğine
/// karar verir. Varsayılan kural <see cref="SsrfGuard"/>: özel/ayrılmış adresler
/// reddedilir (SSRF).
///
/// Neden ayrı bir soyutlama: yerel geliştirmede özelliği hiç test edememek (kendi
/// Postgres'in localhost'ta) gerçek bir sorun. Ama bu gevşetme ASLA production'a
/// sızmamalı — bu yüzden karar statik bir bayrağa değil, ortamı bilen enjekte
/// edilmiş bir servise bırakıldı. Global mutable state yok, test edilebilir.
/// </summary>
public interface IDbHostAccessPolicy
{
    /// <summary>true ise bağlantı kurulabilir. false ise <paramref name="denyReason"/> doldurulur.</summary>
    bool IsHostAllowed(string? host, out string denyReason);
}
