using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Persistence;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models.Auth;

namespace Namines.Studio.Application;

/// <summary>İstemcinin kaydettiği proje: kimlik istemci seçimli olabilir, sürüm jetonu son gördüğü.</summary>
public sealed record ProjectSyncItem(string Id, string Name, string DbType, string SchemaJson, string NodePositionsJson, uint? RowVersion);

/// <summary>İstemcinin gördüğü sürüm eskimiş: kim, neyi, ne zaman değiştirdi.</summary>
public sealed record ProjectSyncConflict(string ProjectId, string ProjectName, uint YourVersion, uint CurrentVersion, DateTime ChangedAt);

/// <summary>Kaydedilen var olan projenin önceki ve yeni şeması: otomasyon tetikleyicisi bunu kullanır.</summary>
public sealed record ProjectSchemaChange(string ProjectId, string DbType, string? OldSchemaJson, string NewSchemaJson);

public enum ProjectSyncStatus { Saved, VersionConflict, ConcurrentModification }

public sealed record ProjectSyncOutcome(
    ProjectSyncStatus Status,
    int SavedCount,
    ProjectSyncConflict? Conflict,
    IReadOnlyList<ProjectSchemaChange> Changes);

/// <summary>
/// Proje yaşam döngüsü: oluşturma, güncelleme, org'a taşıma. Yetki kararı burada; controller yalnızca sonucu
/// HTTP'ye çevirir (denetim M-2). Aktör her çağrıda verilir, yetkisiz çağrı NotFound döner (varlık sızdırmaz).
/// </summary>
public interface IProjectLifecycleService
{
    /// <summary>
    /// Toplu kaydetme. Düzenleme yetkisi olan projeyi günceller, salt okunur olanı atlar, erişimi olmayan ya
    /// da kimliği güvensiz olanı yeni kimlikle aktörün kişisel org'unda oluşturur.
    /// </summary>
    Task<ProjectSyncOutcome> SyncAsync(string actorUserId, string actorDisplayName, IReadOnlyList<ProjectSyncItem> items, CancellationToken ct = default);

    /// <summary>Projeyi hedef org'a taşır: projede üye yönetimi ve hedef org'da üyelik gerekir.</summary>
    Task<ModuleOutcome<string>> ShareToOrganizationAsync(string actorUserId, string projectId, string organizationId, CancellationToken ct = default);

    /// <summary>Projeyi kurucunun kişisel org'una geri çeker; yalnız mevcut org'un Owner'ı.</summary>
    Task<ModuleOutcome<string>> UnshareAsync(string actorUserId, string projectId, CancellationToken ct = default);

    /// <summary>
    /// Projenin org'u; org'suz eski satırı kurucunun kişisel org'una taşır. Görme yetkisi yoksa null:
    /// tembel taşıma yetkisiz bir isteğin yan etkisi olmamalı.
    /// </summary>
    Task<string?> ResolveOrganizationAsync(string actorUserId, string projectId, CancellationToken ct = default);

    /// <summary>Onay zorunluluğunu gevşeten ayar: Editor'a bırakılmaz, üye yönetimi (Admin/Owner) ister.</summary>
    Task<ModuleOutcome<bool>> SetAutoApproveAsync(string actorUserId, string projectId, bool enabled, CancellationToken ct = default);
}

internal sealed class ProjectLifecycleService : IProjectLifecycleService
{
    private static readonly Regex SafeNewId = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant);

    private readonly IStudioProjects _projects;
    private readonly ITenantProjectAuthority _authority;
    private readonly IIdentityDirectory _identity;
    private readonly IProjectReadModelWriter _readModel;
    private readonly ICrossModuleTransaction _transaction;

    public ProjectLifecycleService(
        IStudioProjects projects,
        ITenantProjectAuthority authority,
        IIdentityDirectory identity,
        IProjectReadModelWriter readModel,
        ICrossModuleTransaction transaction)
    {
        _projects = projects;
        _authority = authority;
        _identity = identity;
        _readModel = readModel;
        _transaction = transaction;
    }

    /// <summary>
    /// Yeni projenin istemci seçimli kimliği: gerçek istemciler <c>crypto.randomUUID()</c> üretir. Yol ayırıcı,
    /// nokta, alt çizgi dışarıda: kimlik yola ve tanımlayıcıya güvenle gömülebilsin.
    /// </summary>
    internal static bool IsSafeNewProjectId(string? id) => !string.IsNullOrEmpty(id) && SafeNewId.IsMatch(id);

    public async Task<ProjectSyncOutcome> SyncAsync(string actorUserId, string actorDisplayName, IReadOnlyList<ProjectSyncItem> items, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentNullException.ThrowIfNull(items);

        // Yeni projeler HER ZAMAN kişisel org'a: ilk ekip org'una bağlamak projeyi istemeden tüm ekibe açıyordu.
        var personalOrgId = await _identity.GetOrCreatePersonalOrganizationAsync(actorUserId, actorDisplayName, ct);

        var saved = 0;
        var writes = new List<ProjectWrite>();
        var readModel = new List<ProjectReadModelWrite>();
        var changes = new List<ProjectSchemaChange>();

        foreach (var item in items)
        {
            var existing = await _projects.GetAsync(item.Id, ct);
            var canEdit = existing != null && await _authority.CanEditAsync(existing.Id, actorUserId, ct);
            var canView = existing != null && (canEdit || await _authority.CanViewAsync(existing.Id, actorUserId, ct));

            if (existing != null && canEdit)
            {
                // Çakışma kontrolü (REL-003a / B-32): istemcinin gördüğü sürüm eskiyse kimse ezilmez.
                if (item.RowVersion is { } clientVersion && clientVersion != existing.RowVersion)
                    return new ProjectSyncOutcome(ProjectSyncStatus.VersionConflict, 0,
                        new ProjectSyncConflict(existing.Id, existing.Name, clientVersion, existing.RowVersion, existing.UpdatedAt),
                        Array.Empty<ProjectSchemaChange>());

                // Okunan sürüm jetonu beklenen değer: okuma ile yazma arasındaki yarış da 409'a düşer.
                writes.Add(new ProjectWrite(existing.Id, item.Name, item.DbType, item.SchemaJson, item.NodePositionsJson,
                    existing.UserId, existing.OrganizationId ?? personalOrgId, existing.RowVersion));
                readModel.Add(new ProjectReadModelWrite(existing.Id, existing.UserId, item.Name, item.DbType));
                changes.Add(new ProjectSchemaChange(existing.Id, existing.DbType, existing.SchemaJson, item.SchemaJson));
            }
            else if (existing != null && canView)
            {
                // Ortak projede salt okunur üye: üzerine yazma da kopya oluşturma da yok.
                continue;
            }
            else
            {
                // Başkasının kimliğiyle gelen proje yeni kimlik alır: varlığı sızdırılmaz, üzerine yazılmaz.
                var id = existing == null && IsSafeNewProjectId(item.Id) ? item.Id : Guid.NewGuid().ToString();
                writes.Add(new ProjectWrite(id, item.Name, item.DbType, item.SchemaJson, item.NodePositionsJson, actorUserId, personalOrgId));
                readModel.Add(new ProjectReadModelWrite(id, actorUserId, item.Name, item.DbType));
            }
            saved++;
        }

        try
        {
            // Proje ve repo satırı tek işlemde: repo satırı olmayan proje oluşmaz.
            await _transaction.ExecuteAsync(async c =>
            {
                await _projects.SaveAsync(writes, c);
                await _readModel.WriteAsync(readModel, c);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ProjectSyncOutcome(ProjectSyncStatus.ConcurrentModification, 0, null, Array.Empty<ProjectSchemaChange>());
        }

        return new ProjectSyncOutcome(ProjectSyncStatus.Saved, saved, null, changes);
    }

    public async Task<ModuleOutcome<string>> ShareToOrganizationAsync(string actorUserId, string projectId, string organizationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || string.IsNullOrWhiteSpace(organizationId))
            return ModuleOutcome<string>.NotFound();

        // "Kurucu" kısayolu yok: org'dan çıkarılan kurucu projeyi başka org'a taşıyıp ekibin erişimini koparamaz.
        if (!await _authority.CanManageMembersAsync(projectId, actorUserId, ct))
            return ModuleOutcome<string>.NotFound();
        // Hedef org'da üye olmayan, projeyi yabancı bir org'a (ve onun sahiplerine) veremez.
        if (await _identity.GetRoleAsync(organizationId, actorUserId, ct) is null)
            return ModuleOutcome<string>.NotFound();

        return await _projects.SetOrganizationAsync(projectId, organizationId, ct)
            ? ModuleOutcome<string>.Ok(organizationId)
            : ModuleOutcome<string>.NotFound();
    }

    public async Task<ModuleOutcome<string>> UnshareAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId)) return ModuleOutcome<string>.NotFound();

        // Paylaşımı geri çekmek ekibin erişimini keser: yalnız mevcut org'un Owner'ı (Admin değil).
        if (await _authority.GetRoleAsync(projectId, actorUserId, ct) != OrgRole.Owner)
            return ModuleOutcome<string>.NotFound();

        var project = await _projects.GetHeaderAsync(projectId, ct);
        if (project is null) return ModuleOutcome<string>.NotFound();

        // Proje çağıranın değil KURUCUNUN kişisel org'una döner: ekip sahibi başkasının projesini kendine çekemez.
        var founderOrgId = await PersonalOrganizationOfAsync(project.UserId, ct);
        await _projects.SetOrganizationAsync(projectId, founderOrgId, ct);
        return ModuleOutcome<string>.Ok(founderOrgId);
    }

    public async Task<string?> ResolveOrganizationAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || !await _authority.CanViewAsync(projectId, actorUserId, ct))
            return null;

        var project = await _projects.GetHeaderAsync(projectId, ct);
        if (project is null) return null;
        if (!string.IsNullOrEmpty(project.OrganizationId)) return project.OrganizationId;

        // Org'suz eski satır: tembel taşıma kurucunun kişisel org'una.
        var orgId = await PersonalOrganizationOfAsync(project.UserId, ct);
        await _projects.SetOrganizationAsync(projectId, orgId, ct);
        return orgId;
    }

    public async Task<ModuleOutcome<bool>> SetAutoApproveAsync(string actorUserId, string projectId, bool enabled, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || !await _authority.CanManageMembersAsync(projectId, actorUserId, ct))
            return ModuleOutcome<bool>.NotFound();
        return await _projects.SetAutoApproveSafeChangesAsync(projectId, enabled, ct)
            ? ModuleOutcome<bool>.Ok(enabled)
            : ModuleOutcome<bool>.NotFound();
    }

    private async Task<string> PersonalOrganizationOfAsync(string userId, CancellationToken ct)
    {
        var names = await _identity.GetUserNamesAsync(new[] { userId }, ct);
        return await _identity.GetOrCreatePersonalOrganizationAsync(userId, names.GetValueOrDefault(userId) ?? "Personal", ct);
    }
}
