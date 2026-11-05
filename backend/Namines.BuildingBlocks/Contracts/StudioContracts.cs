using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.BuildingBlocks.Contracts;

/// <summary>Modül dışına açılan proje görünümü; Studio varlığı değil, kopya.</summary>
public sealed record StudioProject(
    string Id,
    string Name,
    string DbType,
    string SchemaJson,
    string NodePositionsJson,
    string UserId,
    string? OrganizationId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool AutoApproveSafeChanges,
    uint RowVersion);

/// <summary>Şema taşımayan hafif proje üst verisi: liste ve yetki kararları büyük JSON çekmesin.</summary>
public sealed record StudioProjectHeader(
    string Id,
    string Name,
    string DbType,
    string UserId,
    string? OrganizationId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool AutoApproveSafeChanges,
    uint RowVersion);

/// <summary>
/// Oluştur ya da güncelle. <see cref="ExpectedRowVersion"/> doluysa satır o sürümde olmalı, değilse
/// <c>DbUpdateConcurrencyException</c>: iki kullanıcının birbirinin üstüne yazması sessiz kalmaz.
/// </summary>
public sealed record ProjectWrite(
    string Id,
    string Name,
    string DbType,
    string SchemaJson,
    string NodePositionsJson,
    string UserId,
    string? OrganizationId,
    uint? ExpectedRowVersion = null,
    DateTime? CreatedAt = null,
    DateTime? UpdatedAt = null);

public sealed record StudioBranch(
    string Id,
    string ProjectId,
    string Name,
    string? ParentBranchId,
    int? ForkedFromVersion,
    bool IsDefault,
    string? CreatedByUserId,
    DateTime CreatedAt,
    DateTime? ClosedAt);

/// <summary>Şema JSON'u olmayan sürüm satırı: liste görünümleri büyük blob taşımaz.</summary>
public sealed record StudioVersionHeader(
    string Id,
    string ProjectId,
    string BranchId,
    int Version,
    string Checksum,
    string? Message,
    short TableCount,
    string? AuthorUserId,
    DateTime CreatedAt);

public sealed record StudioSchemaVersion(
    string Id,
    string ProjectId,
    string BranchId,
    int Version,
    string Checksum,
    string SchemaJson,
    string? Message,
    short TableCount,
    string? AuthorUserId,
    DateTime CreatedAt);

/// <summary>
/// Studio projelerinin modüller arası sözleşmesi. Yöntemler yetki denetlemez: çağıran zaten yetkiyi
/// doğrulamış güvenilir koddur (başka modülün Application'ı, arka plan işi). Son kullanıcı istekleri
/// için Studio'nun kendi aktörlü servisleri var.
/// </summary>
public interface IStudioProjects
{
    Task<StudioProject?> GetAsync(string projectId, CancellationToken ct = default);

    Task<StudioProjectHeader?> GetHeaderAsync(string projectId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, StudioProject>> GetManyAsync(IReadOnlyCollection<string> projectIds, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, StudioProjectHeader>> GetHeadersAsync(IReadOnlyCollection<string> projectIds, CancellationToken ct = default);

    Task<bool> ExistsAsync(string projectId, CancellationToken ct = default);

    /// <summary>
    /// Tümünü aynı <c>SaveChanges</c>'te yazar. <see cref="Namines.BuildingBlocks.Persistence.ICrossModuleTransaction"/>
    /// içinde çağrılınca işleme katılır; yeniden denemede temiz başlasın diye izleyiciyi kendisi temizler.
    /// Var olan projeye yazımda <see cref="ProjectWrite.ExpectedRowVersion"/> zorunlu: bayat okumadan yapılan
    /// tam satır yazımı araya giren şema kaydını sessizce eziyordu (denetim B-1).
    /// </summary>
    Task SaveAsync(IReadOnlyList<ProjectWrite> writes, CancellationToken ct = default);

    /// <summary>
    /// Yalnızca ad ve motor (ve <c>UpdatedAt</c>). Hub ayarları bunun dışında Studio alanı yazmaz; şema ve
    /// org sütunlarına hiç dokunmadığı için eşzamanlı şema kaydıyla yarışamaz. Proje yoksa false.
    /// </summary>
    Task<bool> RenameAsync(string projectId, string name, string dbType, CancellationToken ct = default);

    /// <summary>Yalnızca organizasyon değişir; proje yoksa false. Sürüm jetonu ilerler.</summary>
    Task<bool> SetOrganizationAsync(string projectId, string organizationId, CancellationToken ct = default);

    Task<bool> SetAutoApproveSafeChangesAsync(string projectId, bool enabled, CancellationToken ct = default);

    /// <summary>Hesap silmede kurucu devri.</summary>
    Task ReassignCreatorAsync(string projectId, string newUserId, CancellationToken ct = default);

    /// <summary>Şemalar arası FK'ler bağlı satırları temizler. Satır yoksa false.</summary>
    Task<bool> DeleteAsync(string projectId, CancellationToken ct = default);

    Task<IReadOnlyList<StudioProjectHeader>> ListCreatedByAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListIdsInOrganizationsAsync(IReadOnlyCollection<string> organizationIds, CancellationToken ct = default);

    Task<IReadOnlyList<StudioProjectHeader>> ListInOrganizationAsync(string organizationId, int take, CancellationToken ct = default);
}

/// <summary>Projenin Hub'daki ad/motor kopyası ve eksik slug'ı için tek yazım.</summary>
public sealed record ProjectReadModelWrite(string ProjectId, string OwnerUserId, string Name, string DbType);

/// <summary>
/// Studio proje kaydederken okuma modelinin aynı işlemde güncel kalması için çağırdığı kapı. Hub uygular;
/// Studio Hub'ı tanımaz. Slug üretimi Hub'ın işi: aynı çağrıdaki projeler birbirinin slug'ını da sayar.
/// </summary>
public interface IProjectReadModelWriter
{
    Task WriteAsync(IReadOnlyList<ProjectReadModelWrite> writes, CancellationToken ct = default);
}

/// <summary>Studio branch ve şema sürümlerinin modüller arası sözleşmesi. Yetki çağıranda.</summary>
public interface IStudioBranches
{
    Task<StudioBranch?> GetAsync(string branchId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, StudioBranch>> GetManyAsync(IReadOnlyCollection<string> branchIds, CancellationToken ct = default);

    Task<IReadOnlyList<StudioBranch>> ListAsync(string projectId, CancellationToken ct = default);

    /// <summary>Eşzamanlı iki çağrı aynı branch'te buluşur: kısmi unique index kaybedeni kazanana yönlendirir.</summary>
    Task<StudioBranch> GetOrCreateDefaultAsync(string projectId, string userId, CancellationToken ct = default);

    /// <summary>Canlı oda kimliği: "room-" önekli ya da çıplak branch kimliğinden proje.</summary>
    Task<string?> ResolveProjectIdAsync(string branchIdOrRoom, CancellationToken ct = default);

    Task<StudioSchemaVersion?> GetVersionAsync(string versionId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, StudioVersionHeader>> GetVersionHeadersAsync(IReadOnlyCollection<string> versionIds, CancellationToken ct = default);

    Task<StudioSchemaVersion?> GetLatestVersionAsync(string branchId, CancellationToken ct = default);

    Task<StudioSchemaVersion?> GetVersionAtAsync(string branchId, int version, CancellationToken ct = default);

    /// <summary>Aynı branch'te sürüm numarası çakışırsa (eşzamanlı commit) yeni numarayla yeniden dener.</summary>
    Task<StudioSchemaVersion> AddVersionAsync(string branchId, string schemaJson, string? message, short tableCount, string? authorUserId, CancellationToken ct = default);

    Task<int> CountVersionsAsync(string projectId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    Task<int> CountBranchesAsync(string projectId, CancellationToken ct = default);

    Task<int> CountVersionsAsync(string projectId, CancellationToken ct = default);
}

/// <summary>Kaynak ve hedef kapsamıyla proje ilişkilerinin yaşam döngüsü; silme akışı buradan geçer.</summary>
public interface IStudioRelations
{
    Task<int> CountForProjectAsync(string projectId, CancellationToken ct = default);

    /// <summary>İki FK de Restrict: proje silinmeden önce ilişkiler açıkça kaldırılmalı.</summary>
    Task<int> RemoveForProjectAsync(string projectId, CancellationToken ct = default);
}

/// <summary>Yönetim paneli okumaları: platform geneli sayımlar. Yetki AdminController + IPlatformAdminAuthority.</summary>
public interface IStudioAdminQueries
{
    Task<int> CountProjectsAsync(CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, int>> CountByOwnerAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default);

    Task<IReadOnlyList<StudioProjectHeader>> ListByOwnerAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListDbTypesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<StudioProject>> PageAsync(int skip, int take, CancellationToken ct = default);

    /// <summary>Şemalar tek tek akar; hepsi belleğe alınmaz.</summary>
    IAsyncEnumerable<string> StreamSchemasAsync(CancellationToken ct = default);

    Task<long> SumSchemaCharsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<StudioProject>> ExportAsync(int take, CancellationToken ct = default);
}

/// <summary>Kimlik tablolarının Studio'ya gereken dar görünümü. D-2'de Identity modülü uygular.</summary>
public sealed record OrgMembership(string OrganizationId, Namines.Core.Models.Auth.OrgRole Role);

public interface IIdentityDirectory
{
    Task<Namines.Core.Models.Auth.OrgRole?> GetRoleAsync(string organizationId, string userId, CancellationToken ct = default);

    Task<IReadOnlyList<OrgMembership>> GetMembershipsAsync(string userId, CancellationToken ct = default);

    /// <summary>Editor ve üstü: oy verebilen üye sayısı.</summary>
    Task<int> CountVotingMembersAsync(string organizationId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string?>> GetUserNamesAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string?>> GetUserEmailsAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default);

    Task<Namines.BuildingBlocks.Security.UserPlanSnapshot?> GetUserPlanSnapshotAsync(string userId, CancellationToken ct = default);

    /// <summary>Kullanıcı adı ya da kimlikten kullanıcı kimliği; yoksa null.</summary>
    Task<string?> ResolveUserIdAsync(string userNameOrId, CancellationToken ct = default);

    /// <summary>Yarış güvenli: eşzamanlı iki istek aynı kişisel organizasyonda buluşur.</summary>
    Task<string> GetOrCreatePersonalOrganizationAsync(string userId, string displayName, CancellationToken ct = default);
}

/// <summary>Proje silmenin sonucu. <see cref="StatusCode"/> HTTP karşılığı (200 = silindi).</summary>
public sealed record ProjectDeletionResult(bool Ok, int StatusCode, string? Code = null, string? Error = null)
{
    public static ProjectDeletionResult Success() => new(true, 200);
}

/// <summary>Bir projeyi dış kaynakları ve tüm modüllerdeki satırlarıyla birlikte silen tek akış. Yetki çağıranda.</summary>
public interface IProjectDeletion
{
    Task<ProjectDeletionResult> DeleteAsync(string projectId, string? actorUserId, CancellationToken ct, bool recordName = true);
}

/// <summary>
/// Kullanım ölçümü: faturalama tabloları Billing'in (D-2). Modüller ve controller'lar DbContext görmeden
/// tüketimi yazar ve kota kararını alır.
/// </summary>
public interface IUsageMeter
{
    Task RecordAsync(string userId, Namines.Core.Models.Auth.UsageResource resource, decimal quantity = 1, string? contextLabel = null, CancellationToken ct = default);

    Task<Namines.Core.Analysis.UsageDecision> EvaluateAsync(string userId, Namines.Core.Models.Auth.UsageResource resource, decimal requested = 1, CancellationToken ct = default);
}

/// <summary>Hub vitrininin değişiklik isteği sayacı; Governance verisine başka kapı yok.</summary>
public interface IChangeRequestStats
{
    Task<(int Open, int Closed)> CountAsync(string projectId, CancellationToken ct = default);
}

/// <summary>Bağlantı dizesinin SSRF politikasına uyup uymadığını söyleyen bağlantı noktası.</summary>
public interface IConnectionHostInspector
{
    string? FindDisallowedHost(string connectionString, string connectionDbType);
}
