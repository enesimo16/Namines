using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Application;

/// <summary>
/// Studio Bounded Context Uygulama Servis Sözleşmesi (Phase 14.2B).
/// <c>studio.projects</c> ve <c>studio.project_shares</c> tabloları üzerindeki tüm
/// iş kurallarını, optimistik eşzamanlılığı (<c>RowVersion</c>) ve kriptografik paylaşım özetlerini yönetir.
/// </summary>
public interface IStudioApplicationService
{
    Task<CloudProject?> GetProjectAsync(string projectId, CancellationToken ct = default);

    Task<IReadOnlyList<CloudProject>> GetProjectsForUserOrOrgAsync(
        string userId,
        IReadOnlyCollection<string> orgIds,
        CancellationToken ct = default);

    Task<CloudProject> CreateOrUpdateProjectAsync(
        CloudProject project,
        CancellationToken ct = default);

    Task<bool> DeleteProjectAsync(
        string projectId,
        CancellationToken ct = default);

    /// <summary>
    /// Kriptografik rastgele 192-bit URL-safe token üretir, SHA-256 özetini
    /// <c>studio.project_shares</c> tablosuna yazar ve ham token'ı tek seferlik döndürür.
    /// Düz metin token hiçbir yerde saklanmaz.
    /// </summary>
    Task<string> CreateShareLinkAsync(
        string projectId,
        CancellationToken ct = default);

    /// <summary>
    /// Projenin salt-okunur paylaşım bağlantısını derhal iptal eder (<c>studio.project_shares</c> kaydını siler).
    /// </summary>
    Task<bool> RevokeShareLinkAsync(
        string projectId,
        CancellationToken ct = default);

    /// <summary>
    /// SHA-256 token özeti ile paylaşım kaydını sorgular.
    /// </summary>
    Task<ProjectShare?> GetShareByTokenHashAsync(
        string tokenHash,
        CancellationToken ct = default);

    /// <summary>
    /// Paylaşım jetonunun SHA-256 özeti üzerinden projeyi salt-okunur getirir.
    /// </summary>
    Task<CloudProject?> GetSharedProjectByTokenHashAsync(
        string tokenHash,
        CancellationToken ct = default);

    Task<bool> SetAutoApproveSafeChangesAsync(
        string projectId,
        bool enabled,
        CancellationToken ct = default);
}
