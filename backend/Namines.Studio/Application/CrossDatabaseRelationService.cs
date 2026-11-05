using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Core.Models;
using Namines.Studio.Domain;

namespace Namines.Studio.Application;

public sealed record CreateRelationCommand(
    string SourceProjectId, string SourceTableId, string SourceColumnId,
    string TargetProjectId, string TargetTableId, string TargetColumnId,
    string? Note);

public sealed record RelationView(
    string Id, string Direction, string LocalColumn, string OtherProjectId, string OtherProjectName, string OtherColumn, string? Note, DateTime CreatedAt);

public sealed record RelationImpactView(string RelationId, string Direction, string OtherProjectId, string OtherProjectName, string? Note);

/// <summary>Veritabanları arası mantıksal ilişki. İki projenin de sınırını aştığı için ikisine de yetki gerekir.</summary>
public interface ICrossDatabaseRelationService
{
    /// <param name="planLimit">Negatifse sınırsız; plan bilgisi faturalama modülünde, karar burada.</param>
    Task<ModuleOutcome<string>> CreateAsync(string actorUserId, CreateRelationCommand command, int planLimit, CancellationToken ct = default);

    Task<ModuleOutcome<IReadOnlyList<RelationView>>> ListAsync(string actorUserId, string projectId, CancellationToken ct = default);

    Task<ModuleOutcome<bool>> DeleteAsync(string actorUserId, string relationId, CancellationToken ct = default);

    Task<ModuleOutcome<IReadOnlyList<RelationImpactView>>> ImpactAsync(string actorUserId, string projectId, string tableId, string? columnId, CancellationToken ct = default);
}

internal sealed class CrossDatabaseRelationService : ICrossDatabaseRelationService, IStudioRelations
{
    private const string NotAccessible = "Project not found or you don't have access to it.";

    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly IStudioData _db;
    private readonly ITenantProjectAuthority _authority;

    public CrossDatabaseRelationService(IStudioData db, ITenantProjectAuthority authority)
    {
        _db = db;
        _authority = authority;
    }

    public Task<int> CountForProjectAsync(string projectId, CancellationToken ct = default) =>
        _db.CrossDatabaseRelations.AsNoTracking().CountAsync(r => r.SourceProjectId == projectId || r.TargetProjectId == projectId, ct);

    public Task<int> RemoveForProjectAsync(string projectId, CancellationToken ct = default) =>
        _db.CrossDatabaseRelations.Where(r => r.SourceProjectId == projectId || r.TargetProjectId == projectId).ExecuteDeleteAsync(ct);

    public async Task<ModuleOutcome<string>> CreateAsync(string actorUserId, CreateRelationCommand command, int planLimit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SourceProjectId == command.TargetProjectId)
            return ModuleOutcome<string>.Invalid("A cross-database relation must connect two different projects.");

        // Erişimi olmayan bir projenin varlığı/adı sızmasın.
        if (!await _authority.CanViewAsync(command.SourceProjectId, actorUserId, ct) ||
            !await _authority.CanViewAsync(command.TargetProjectId, actorUserId, ct))
            return ModuleOutcome<string>.NotFound("One of the projects was not found or you don't have access to it.");

        if (planLimit >= 0)
        {
            // Kullanıcının KENDİ kurduğu ilişkiler sayılır: ekip arkadaşının kurduğu bağ senin hakkını yememeli.
            var used = await _db.CrossDatabaseRelations.AsNoTracking().CountAsync(r => r.CreatedByUserId == actorUserId, ct);
            if (used >= planLimit)
                return ModuleOutcome<string>.TooMany($"Your plan allows {planLimit} cross-database relation(s). Remove one, or upgrade for more.");
        }

        var relation = new CrossDatabaseRelation
        {
            SourceProjectId = command.SourceProjectId,
            SourceTableId = command.SourceTableId,
            SourceColumnId = command.SourceColumnId,
            TargetProjectId = command.TargetProjectId,
            TargetTableId = command.TargetTableId,
            TargetColumnId = command.TargetColumnId,
            Note = command.Note,
            CreatedByUserId = actorUserId,
        };
        _db.CrossDatabaseRelations.Add(relation);
        await _db.SaveChangesAsync(ct);
        _db.Entry(relation).State = EntityState.Detached;
        return ModuleOutcome<string>.Ok(relation.Id);
    }

    public async Task<ModuleOutcome<IReadOnlyList<RelationView>>> ListAsync(string actorUserId, string projectId, CancellationToken ct = default)
    {
        if (!await _authority.CanViewAsync(projectId, actorUserId, ct))
            return ModuleOutcome<IReadOnlyList<RelationView>>.NotFound(NotAccessible);

        var relations = await _db.CrossDatabaseRelations.AsNoTracking()
            .Where(r => r.SourceProjectId == projectId || r.TargetProjectId == projectId)
            .ToListAsync(ct);
        if (relations.Count == 0)
            return ModuleOutcome<IReadOnlyList<RelationView>>.Ok(Array.Empty<RelationView>());

        // N ilişki için N sorgu değil: taraf projelerin adı ve şeması tek seferde.
        var ids = relations.SelectMany(r => new[] { r.SourceProjectId, r.TargetProjectId }).Append(projectId).Distinct().ToList();
        var projects = await _db.Projects.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.SchemaJson })
            .ToDictionaryAsync(p => p.Id, ct);

        var schemaCache = new Dictionary<string, DatabaseSchema?>();
        DatabaseSchema? SchemaOf(string id)
        {
            if (schemaCache.TryGetValue(id, out var cached)) return cached;
            DatabaseSchema? parsed = null;
            if (projects.TryGetValue(id, out var project))
            {
                try { parsed = JsonSerializer.Deserialize<DatabaseSchema>(project.SchemaJson, SchemaJsonOptions); }
                catch (JsonException) { parsed = null; }
            }
            schemaCache[id] = parsed;
            return parsed;
        }

        static string? NameOf(DatabaseSchema? schema, string tableId, string? columnId)
        {
            var table = schema?.Tables.FirstOrDefault(t => t.Id == tableId);
            if (table is null) return null;
            if (columnId is null) return table.Name;
            var column = table.Columns.FirstOrDefault(c => c.Id == columnId);
            return column is null ? table.Name : $"{table.Name}.{column.Name}";
        }

        IReadOnlyList<RelationView> views = relations.Select(r =>
        {
            var isSource = r.SourceProjectId == projectId;
            var otherId = isSource ? r.TargetProjectId : r.SourceProjectId;
            var localTableId = isSource ? r.SourceTableId : r.TargetTableId;
            var localColumnId = isSource ? r.SourceColumnId : r.TargetColumnId;
            var otherTableId = isSource ? r.TargetTableId : r.SourceTableId;
            var otherColumnId = isSource ? r.TargetColumnId : r.SourceColumnId;

            return new RelationView(
                r.Id,
                isSource ? "outgoing" : "incoming",
                NameOf(SchemaOf(projectId), localTableId, localColumnId) ?? $"{localTableId}.{localColumnId}",
                otherId,
                projects.TryGetValue(otherId, out var other) ? other.Name : "(deleted project)",
                NameOf(SchemaOf(otherId), otherTableId, otherColumnId) ?? $"{otherTableId}.{otherColumnId}",
                r.Note,
                r.CreatedAt);
        }).ToList();

        return ModuleOutcome<IReadOnlyList<RelationView>>.Ok(views);
    }

    public async Task<ModuleOutcome<bool>> DeleteAsync(string actorUserId, string relationId, CancellationToken ct = default)
    {
        var relation = await _db.CrossDatabaseRelations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == relationId, ct);
        if (relation is null) return ModuleOutcome<bool>.NotFound();

        // İki projeden BİRİNDE düzenleme yetkisi yeterli: diğer tarafa erişimi olmayan biri de kendi
        // tarafındaki kaydı temizleyebilmeli.
        var canEditEither = await _authority.CanEditAsync(relation.SourceProjectId, actorUserId, ct) ||
                            await _authority.CanEditAsync(relation.TargetProjectId, actorUserId, ct);
        if (!canEditEither) return ModuleOutcome<bool>.Forbidden();

        await _db.CrossDatabaseRelations.Where(r => r.Id == relationId).ExecuteDeleteAsync(ct);
        return ModuleOutcome<bool>.Ok(true);
    }

    public async Task<ModuleOutcome<IReadOnlyList<RelationImpactView>>> ImpactAsync(string actorUserId, string projectId, string tableId, string? columnId, CancellationToken ct = default)
    {
        if (!await _authority.CanViewAsync(projectId, actorUserId, ct))
            return ModuleOutcome<IReadOnlyList<RelationImpactView>>.NotFound(NotAccessible);

        var relations = await _db.CrossDatabaseRelations.AsNoTracking()
            .Where(r => r.SourceProjectId == projectId || r.TargetProjectId == projectId)
            .ToListAsync(ct);

        var impacts = CrossDatabaseImpactAnalyzer.FindAffected(relations, projectId, tableId, columnId);
        if (impacts.Count == 0)
            return ModuleOutcome<IReadOnlyList<RelationImpactView>>.Ok(Array.Empty<RelationImpactView>());

        var otherIds = impacts.Select(i => i.OtherProjectId).Distinct().ToList();
        var names = await _db.Projects.AsNoTracking()
            .Where(p => otherIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        IReadOnlyList<RelationImpactView> views = impacts
            .Select(i => new RelationImpactView(i.RelationId, i.Direction, i.OtherProjectId, names.GetValueOrDefault(i.OtherProjectId, "(deleted project)"), i.Note))
            .ToList();
        return ModuleOutcome<IReadOnlyList<RelationImpactView>>.Ok(views);
    }
}
