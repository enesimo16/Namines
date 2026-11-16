using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// Denetim zaman damgalarını (<c>created_at</c>/<c>updated_at</c>) ve yumuşak silme (<c>deleted_at</c>)
/// kısmi indekslerini 0-token ile motor kurallarına göre hizalayan yapıcı alt-ajan.
/// </summary>
public sealed class AuditAndTemporalWeaverSubAgent : ISubAgent<OrchestrationWorkspace, IReadOnlyList<string>>
{
    public AgentKind Kind => AgentKind.SchemaMutator;

    public Task<IReadOnlyList<string>> ExecuteAsync(
        AgentInstance self,
        OrchestrationWorkspace workspace,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var schema = workspace.CurrentSchema;
        var engine = workspace.TaskInput.Engine;
        var changes = new List<string>();
        var currentTsExpr = ResolveCurrentTimestampDefault(engine);

        foreach (var table in schema.Tables)
        {
            foreach (var col in table.Columns)
            {
                if (IsCreatedAtColumn(col.Name) && string.IsNullOrWhiteSpace(col.DefaultValue))
                {
                    col.DefaultValue = currentTsExpr;
                    changes.Add($"{table.Name}.{col.Name}: varsayılan '{currentTsExpr}' eklendi.");
                }

                if (IsDeletedAtColumn(col.Name))
                {
                    if (!col.IsNullable)
                    {
                        col.IsNullable = true;
                        changes.Add($"{table.Name}.{col.Name}: soft-delete alanı nullable yapıldı.");
                    }

                    EnsureSoftDeletePartialIndex(table, col, changes);
                }
            }
        }

        if (changes.Count > 0)
        {
            workspace.Swarm.SendMessage(
                self.AgentId,
                workspace.PositiveAgentId,
                "temporal.woven",
                $"{changes.Count} denetim/zaman damgası standardizasyonu uygulandı.");
        }

        return Task.FromResult<IReadOnlyList<string>>(changes);
    }

    private static void EnsureSoftDeletePartialIndex(
        SchemaTable table,
        SchemaColumn deletedCol,
        List<string> changes)
    {
        var hasIdx = table.Indexes.Any(ix =>
            ix.Columns.Any(c => string.Equals(c.ColumnId, deletedCol.Id, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(ix.Where) && ix.Where.Contains(deletedCol.Name, StringComparison.OrdinalIgnoreCase)));
        if (hasIdx) return;

        var pkCol = table.Columns.FirstOrDefault(c => c.IsPK) ?? table.Columns.FirstOrDefault();
        if (pkCol is null) return;

        table.Indexes.Add(new SchemaIndex
        {
            Id = $"ix_{SchemaIdConvention.Normalize(table.Name)}_active",
            Name = $"idx_{SchemaIdConvention.Normalize(table.Name)}_active",
            IsUnique = false,
            Where = $"{deletedCol.Name} IS NULL",
            Columns = new List<SchemaIndexColumn> { new() { ColumnId = pkCol.Id, Descending = false } },
        });
        changes.Add($"{table.Name}: aktif kayıtlar için partial index ({deletedCol.Name} IS NULL) eklendi.");
    }

    private static string ResolveCurrentTimestampDefault(DatabaseType engine) => engine switch
    {
        DatabaseType.MSSQL => "GETUTCDATE()",
        DatabaseType.Oracle => "CURRENT_TIMESTAMP",
        _ => "CURRENT_TIMESTAMP",
    };

    private static bool IsCreatedAtColumn(string name)
    {
        var norm = SchemaIdConvention.Normalize(name);
        return norm is "created_at" or "createdat" or "created_on";
    }

    private static bool IsDeletedAtColumn(string name)
    {
        var norm = SchemaIdConvention.Normalize(name);
        return norm is "deleted_at" or "deletedat" or "archived_at";
    }
}
