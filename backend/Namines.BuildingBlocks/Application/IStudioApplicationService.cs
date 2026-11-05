using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.BuildingBlocks.Application;

/// <summary>Bir projenin bağlantı üst verisi. Şifreli metin bilerek YOK: "var/yok" yeterli.</summary>
public sealed record ProjectConnectionMetadata(
    string ProjectId,
    bool HasConnection,
    string? ConnectionDbType,
    bool AllowDeskSql);

/// <summary>Toplu okumalar için hafif proje özeti; SchemaJson taşımaz (çok büyük olabilir).</summary>
public sealed record StudioProjectSummary(
    string Id,
    string Name,
    string DbType,
    string UserId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Anonim paylaşım uçlarının görebileceği alanların tamamı.</summary>
public sealed record SharedProjectView(
    string Name,
    string DbType,
    string SchemaJson,
    string NodePositionsJson);

/// <summary>
/// Studio modülünün dış sözleşmesi: proje özetleri, bağlantı üst verisi, anonim paylaşım.
/// Bağlantı sırrını çözmek burada değil, <c>IProjectConnectionSecretProvider</c>'da.
/// Yetki denetimi ÇAĞIRANIN işi: bu servis kiracı bilgisini görmez.
/// </summary>
public interface IStudioApplicationService
{
    /// <summary>N+1 olmasın diye toplu; bilinmeyen kimlikler sonuçta yer almaz.</summary>
    Task<IReadOnlyDictionary<string, StudioProjectSummary>> GetProjectSummariesAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> GetSchemaJsonAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default);

    /// <summary>Satır yoksa bağlantısız ve SQL konsolu kapalı kabul edilir (null dönmez).</summary>
    Task<ProjectConnectionMetadata> GetConnectionMetadataAsync(
        string projectId,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, ProjectConnectionMetadata>> GetConnectionMetadataAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default);

    /// <summary>
    /// Yeni bir bağlantı üretir, yalnızca özetini yazar, ham token'ı bu tek seferlik döndürür.
    /// Önceki bağlantılar geçerli kalır (bkz. <see cref="Namines.Core.Models.Auth.ProjectShare"/>).
    /// Proje yoksa <see cref="KeyNotFoundException"/>.
    /// </summary>
    Task<string> CreateShareLinkAsync(
        string projectId,
        CancellationToken ct = default);

    /// <summary>Projenin TÜM etkin paylaşım bağlantılarını kapatır; kapatılan sayısını döner.</summary>
    Task<int> RevokeShareLinksAsync(
        string projectId,
        CancellationToken ct = default);

    /// <summary>Ham token değil özet alır; iptal edilmiş bağlantı için null.</summary>
    Task<SharedProjectView?> GetSharedProjectByTokenHashAsync(
        string tokenHash,
        CancellationToken ct = default);
}
