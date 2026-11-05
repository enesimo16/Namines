using System;
using System.Text.Json.Serialization;

namespace Namines.Studio.Domain;

/// <summary>
/// <c>studio.project_connections</c> (projeyle 1:0..1). Satır, üç alandan biri anlamlıysa vardır:
/// bağlantı kopsa da <see cref="AllowDeskSql"/> onayı korunur, bu yüzden şifreli metin nullable.
/// Anahtar sürümü ayrı kolon değil, şifreli metnin kendi önekinde durur (v1 / kid formatı).
/// </summary>
public sealed class ProjectConnection
{
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// AES-256-GCM şifreli metin (AAD = proje kimliği). Düz metin buraya asla yazılmaz.
    /// Tek çözme kapısı <c>IProjectConnectionSecretProvider</c>.
    /// </summary>
    [JsonIgnore]
    public string? EncryptedConnectionString { get; set; }

    /// <summary>
    /// Gerçekten BAĞLANILAN motor; <see cref="CloudProject.DbType"/>'tan ayrı çünkü o, şemanın
    /// derlendiği hedef motor ve ikisi farklı olabilir.
    /// </summary>
    public string? ConnectionDbType { get; set; }

    /// <summary>
    /// Namines Desk v2 §E4.2: SQL konsolu opt-in'i. Varsayılan kapalı, yalnızca Owner değiştirir.
    /// </summary>
    public bool AllowDeskSql { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// <c>studio.project_shares</c>: anonim salt-okunur paylaşım bağlantısı. Düz metin token
/// saklanmaz; ham değer yalnızca oluşturulurken bir kez döner.
///
/// Proje başına BİRDEN ÇOK etkin satır olabilir: eski API ikinci çağrıda aynı linki geri
/// veriyordu, özet saklayınca bu imkânsız. Tek satırda döndürmek daha önce kopyalanmış URL'leri
/// sessizce öldürürdü; o yüzden her çağrı yeni bağlantı üretir ve iptal hepsini kapatır.
/// </summary>
public sealed class ProjectShare
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Ham token'ın küçük harf hex SHA-256 özeti (UNIQUE), bkz. <see cref="ShareTokenHasher"/>.</summary>
    [JsonIgnore]
    public string ShareTokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Dolu ise bağlantı geçersiz; satır "ne zaman kapatıldı" izi için kalır.</summary>
    public DateTime? RevokedAt { get; set; }
}
