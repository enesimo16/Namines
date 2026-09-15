using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Tests.Services;

/// <summary>
/// Hattı GERÇEK AI olmadan sürmek için. Her çağrıyı kaydeder; parça
/// çağrılarında istenen tabloları birebir üretir — hattın parçaları doğru
/// dağıtıp doğru birleştirdiğini bu sayede ölçebiliyoruz.
/// </summary>
public sealed class FakeSchemaDraftSource : ISchemaDraftSource
{
    public string? PlanResponse { get; set; }
    public List<SchemaChunk> ChunkCalls { get; } = new();
    public List<IReadOnlyList<string>> ChunkContexts { get; } = new();
    public int DraftCalls { get; private set; }
    public int RepairCalls { get; private set; }

    /// <summary>Etiketi burada olan parça çağrısı istisna fırlatır.</summary>
    public HashSet<string> FailingChunkLabels { get; } = new();

    public Task<string?> PlanAsync(string prompt, DatabaseType engine, CancellationToken ct = default)
        => Task.FromResult(PlanResponse);

    public Task<DatabaseSchema> DraftAsync(
        string prompt, DatabaseType engine, string? plan, CancellationToken ct = default)
    {
        DraftCalls++;
        return Task.FromResult(SchemaOf("fallback_table"));
    }

    public Task<DatabaseSchema> DraftChunkAsync(
        string prompt, DatabaseType engine, SchemaChunk chunk,
        IReadOnlyList<string> allTableNames, CancellationToken ct = default)
    {
        ChunkCalls.Add(chunk);
        ChunkContexts.Add(allTableNames);

        if (FailingChunkLabels.Contains(chunk.Label))
            throw new InvalidOperationException($"chunk '{chunk.Label}' failed");

        return Task.FromResult(SchemaOf(chunk.OwnedTables.ToArray()));
    }

    public Task<DatabaseSchema> RepairAsync(
        DatabaseSchema schema, IReadOnlyList<string> findings,
        DatabaseType engine, CancellationToken ct = default)
    {
        RepairCalls++;
        return Task.FromResult(schema);
    }

    /// <summary>
    /// Her tabloya bir birincil anahtar veriliyor: aksi hâlde NslValidator
    /// bulgu üretir, hat onarım döngüsüne girer ve test ölçmek istediği
    /// şeyi değil, döngüyü ölçer.
    /// </summary>
    private static DatabaseSchema SchemaOf(params string[] tableNames)
    {
        var schema = new DatabaseSchema { Name = "Fake" };
        foreach (var name in tableNames)
        {
            schema.Tables.Add(new SchemaTable
            {
                Id = SchemaIdConvention.TableId(name),
                Name = name,
                Columns = new List<SchemaColumn>
                {
                    new()
                    {
                        Id = SchemaIdConvention.ColumnId(name, "id"),
                        Name = "id", Type = "uuid", IsPK = true, IsNullable = false,
                    },
                },
            });
        }
        return schema;
    }
}
