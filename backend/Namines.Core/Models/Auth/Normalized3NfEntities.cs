using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace Namines.Core.Models.Auth;

/// <summary>
/// 3NF Normalizasyonu: <see cref="CloudProject"/> içindeki canlı veritabanı bağlantı sırlarının
/// (<c>EncryptedConnectionString</c>, <c>ConnectionDbType</c>, <c>AllowDeskSql</c>) ayrıştırıldığı
/// 1:1 tablo (<c>studio.project_connections</c>, Kritik Kontrol #3: Secret Management).
/// </summary>
public sealed class ProjectConnection
{
    [Key]
    [MaxLength(64)]
    public string ProjectId { get; set; } = string.Empty;

    [Required]
    public string EncryptedConnectionString { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string ConnectionDbType { get; set; } = "PostgreSQL";

    public bool AllowDeskSql { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 3NF Normalizasyonu: <see cref="CloudProject"/> içindeki GitHub entegrasyon alanlarının
/// ayrıştırıldığı 1:1 tablo (<c>studio.project_git_repositories</c>).
/// </summary>
public sealed class ProjectGitRepository
{
    [Key]
    [MaxLength(64)]
    public string ProjectId { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string GithubRepoFullName { get; set; } = string.Empty;

    public long? GithubInstallationId { get; set; }

    [MaxLength(120)]
    public string? GithubDefaultBranch { get; set; }

    [MaxLength(40)]
    public string? GithubSchemaSourceKind { get; set; }

    [MaxLength(400)]
    public string? GithubSchemaFilePath { get; set; }

    [MaxLength(400)]
    public string? GithubMigrationsPath { get; set; }

    public bool GithubAutoPrOnApprove { get; set; }

    /// <summary>AES-256-GCM ile şifrelenmiş GitHub webhook sırrı.</summary>
    public string? EncryptedGithubWebhookSecret { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 3NF Normalizasyonu: <see cref="CloudProject"/> içindeki Namines Hub vitrin ve
/// topluluk sayaç alanlarının ayrıştırıldığı 1:1 tablo (<c>hub.project_publications</c>).
/// </summary>
public sealed class ProjectPublication
{
    [Key]
    [MaxLength(64)]
    public string ProjectId { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string OwnerUserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    public bool IsPublic { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(60)]
    public string? Category { get; set; }

    [MaxLength(400)]
    public string? Tags { get; set; }

    public int StarsCount { get; set; }
    public int ForksCount { get; set; }
    public int ViewsCount { get; set; }
    public int IssuesCount { get; set; }

    [MaxLength(64)]
    public string? ForkedFromProjectId { get; set; }

    public DateTime? PublishedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 3NF + Güvenlik Normalizasyonu: <see cref="Branch"/> içindeki düz metin <c>ShareToken</c>
/// alanının SHA-256 özetiyle (<c>TokenHash</c>) saklandığı tablo (<c>studio.schema_share_links</c>,
/// Kritik Kontrol #3: Secret Management).
/// </summary>
public sealed class SchemaShareLink
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(64)]
    public string BranchId { get; set; } = string.Empty;

    /// <summary>Ham paylaşım jetonunun 64 karakterlik küçük harf hex SHA-256 özeti.</summary>
    [Required]
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    [MaxLength(64)]
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static string ComputeTokenHash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken.Trim()))).ToLowerInvariant();
    }

    public bool IsUsable(DateTime utcNow) =>
        IsActive &&
        RevokedAt is null &&
        (ExpiresAt is null || ExpiresAt > utcNow);
}

/// <summary>
/// 3NF Normalizasyonu: <see cref="CloudProject"/> içindeki salt-okunur anonim paylaşım
/// özetinin saklandığı 1:1 tablo (<c>studio.project_shares</c>).
/// Düz metin token hiçbir zaman veritabanında saklanmaz; yalnızca SHA-256 özeti tutulur.
/// </summary>
public sealed class ProjectShare
{
    [Key]
    [MaxLength(64)]
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Ham 192-bit URL-safe token'ın küçük harf hex SHA-256 özeti (UNIQUE).</summary>
    [Required]
    [MaxLength(64)]
    public string ShareTokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 3NF Normalizasyonu: <see cref="CloudProject"/> içindeki Namines Hub topluluk vitrini,
/// arama indeksleri ve sosyal sayaç alanlarının ayrıştırıldığı 1:1 tablo (<c>hub.repositories</c>).
/// </summary>
public sealed class ProjectRepository
{
    [Key]
    [MaxLength(64)]
    public string ProjectId { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public ProjectVisibility Visibility { get; set; } = ProjectVisibility.Private;

    [Required]
    [MaxLength(64)]
    public string Category { get; set; } = "General";

    public int StarCount { get; set; } = 0;
    public int ForkCount { get; set; } = 0;

    [MaxLength(64)]
    public string? ForkedFromProjectId { get; set; }

    [MaxLength(140)]
    public string? LinkedGithubRepo { get; set; }

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 3NF Normalizasyonu: Virgülle ayrılmış CSV <c>CloudProjects.Tags</c> alanının
/// ilişkisel ve B-tree indeksli 1:N tablosu (<c>hub.project_tags</c>).
/// <c>LIKE '%tag%'</c> full table scan maliyetini ortadan kaldırır.
/// </summary>
public sealed class ProjectTag
{
    [MaxLength(64)]
    public string ProjectId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Tag { get; set; } = string.Empty;
}
