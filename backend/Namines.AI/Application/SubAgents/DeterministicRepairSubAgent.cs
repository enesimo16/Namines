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
/// Sıfır LLM token maliyetiyle yapısal, tip, index, cascade ve motor tutarsızlıklarını
/// şema üzerinde deterministik olarak onaran alt-ajan.
/// </summary>
public sealed class DeterministicRepairSubAgent : ISubAgent<OrchestrationWorkspace, IReadOnlyList<string>>
{
    public AgentKind Kind => AgentKind.SchemaMutator;

    public Task<IReadOnlyList<string>> ExecuteAsync(
        AgentInstance self,
        OrchestrationWorkspace workspace,
        CancellationToken ct = default)
    {
        var schema = workspace.CurrentSchema;
        var engine = workspace.TaskInput.Engine;
        var fixes = new List<string>();

        var tableById = schema.Tables.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        // 1. Eksik PK ve boyutsuz VARCHAR/DECIMAL onarımı + kimlik güvenliği
        foreach (var table in schema.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.Id))
                table.Id = SchemaIdConvention.TableId(table.Name);

            if (string.IsNullOrWhiteSpace(table.StableUuid))
                table.StableUuid = SchemaIdentity.ForTable(table.Name);

            if (!table.Columns.Any(c => c.IsPK))
            {
                var pkCol = new SchemaColumn
                {
                    Id = $"{table.Id}_id",
                    Name = "id",
                    Type = "UUID",
                    IsPK = true,
                    IsNullable = false,
                };
                table.Columns.Insert(0, pkCol);
                fixes.Add($"{table.Name}: eksik primary key için 'id UUID PK' eklendi.");
            }

            foreach (var col in table.Columns)
            {
                if (string.IsNullOrWhiteSpace(col.Id))
                    col.Id = $"{table.Id}_{SchemaIdConvention.Normalize(col.Name)}";

                if (col.IsPK && col.IsNullable)
                {
                    col.IsNullable = false;
                    fixes.Add($"{table.Name}.{col.Name}: PK kolonu NOT NULL yapıldı.");
                }

                var upperType = (col.Type ?? string.Empty).Trim().ToUpperInvariant();
                if (upperType is "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" && col.Length is null or <= 0)
                {
                    col.Length = col.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                                 col.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                                 col.Name.Contains("email", StringComparison.OrdinalIgnoreCase)
                        ? 255
                        : 150;
                    fixes.Add($"{table.Name}.{col.Name}: boyutsuz {upperType} -> {upperType}({col.Length}) sabitlendi.");
                }

                // Finansal doğruluk: para/tutar/bakiye alanlarında kayan nokta yerine DECIMAL(19,4) kullan.
                if (upperType is "FLOAT" or "DOUBLE" or "REAL" &&
                    (col.Name.Contains("price", StringComparison.OrdinalIgnoreCase) ||
                     col.Name.Contains("amount", StringComparison.OrdinalIgnoreCase) ||
                     col.Name.Contains("balance", StringComparison.OrdinalIgnoreCase) ||
                     col.Name.Contains("total", StringComparison.OrdinalIgnoreCase) ||
                     col.Name.Contains("salary", StringComparison.OrdinalIgnoreCase)))
                {
                    col.Type = "DECIMAL";
                    col.Length = 19;
                    col.Scale = 4;
                    fixes.Add($"{table.Name}.{col.Name}: finansal kayan nokta tipi DECIMAL(19,4)'e yükseltildi.");
                }

                // Güvenlik: düz "password" alanı varsa hash saklandığını netleştir ve uzunluğu garantiye al.
                if (string.Equals(col.Name, "password", StringComparison.OrdinalIgnoreCase))
                {
                    col.Name = "password_hash";
                    col.Length = Math.Max(col.Length ?? 0, 255);
                    if (string.Equals(col.Type, "TEXT", StringComparison.OrdinalIgnoreCase))
                        col.Type = "VARCHAR";
                    fixes.Add($"{table.Name}: ham 'password' kolonu 'password_hash' VARCHAR(255) olarak sertleştirildi.");
                }
            }
        }

        // 2. FK tip hizalama (NSL004) ve eksik FK index ekleme (NSL010)
        foreach (var rel in schema.Relations)
        {
            if (!tableById.TryGetValue(rel.SourceTableId, out var srcTable) ||
                !tableById.TryGetValue(rel.TargetTableId, out var dstTable))
                continue;

            var srcCol = srcTable.Columns.FirstOrDefault(c => c.Id == rel.SourceColumnId);
            var dstCol = dstTable.Columns.FirstOrDefault(c => c.Id == rel.TargetColumnId);
            if (srcCol is null || dstCol is null)
                continue;

            srcCol.IsFK = true;

            if (!string.Equals(srcCol.Type, dstCol.Type, StringComparison.OrdinalIgnoreCase) ||
                srcCol.Length != dstCol.Length ||
                srcCol.Scale != dstCol.Scale)
            {
                srcCol.Type = dstCol.Type;
                srcCol.Length = dstCol.Length;
                srcCol.Scale = dstCol.Scale;
                fixes.Add($"{srcTable.Name}.{srcCol.Name}: FK tipi hedef {dstTable.Name}.{dstCol.Name} ile eşitlendi.");
            }

            var hasIndex = srcTable.Indexes.Any(idx =>
                idx.Columns.Count > 0 &&
                string.Equals(idx.Columns[0].ColumnId, srcCol.Id, StringComparison.OrdinalIgnoreCase));

            if (!hasIndex && !srcCol.IsPK)
            {
                srcTable.Indexes.Add(new SchemaIndex
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"idx_{SchemaIdConvention.Normalize(srcTable.Name)}_{SchemaIdConvention.Normalize(srcCol.Name)}",
                    Columns = new List<SchemaIndexColumn>
                    {
                        new() { ColumnId = srcCol.Id, Descending = false },
                    },
                });
                fixes.Add($"{srcTable.Name}.{srcCol.Name}: eksik FK indeksi eklendi.");
            }
        }

        // 3. Çoklu cascade yolları veya cascade döngülerini MSSQL / motor güvenliği için NO ACTION'a düşür.
        var cascadeIssues = FkCascadeAnalyzer.Analyze(schema, engine);
        if (cascadeIssues.Count > 0)
        {
            var relById = schema.Relations.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var issue in cascadeIssues)
            {
                if (!string.IsNullOrWhiteSpace(issue.RelationId) &&
                    relById.TryGetValue(issue.RelationId, out var rel))
                {
                    rel.OnDelete = ReferentialAction.NoAction;
                    rel.OnUpdate = ReferentialAction.NoAction;
                    fixes.Add($"{issue.FromTable}->{issue.ToTable}: cascade ({issue.Kind}) veri güvenliği için NO ACTION'a çevrildi.");
                }
            }

            // İlişki Id eşleşmezse ilgili kaynak/hedef eşleşmesini düşür.
            foreach (var issue in cascadeIssues.Where(i => string.IsNullOrWhiteSpace(i.RelationId)))
            {
                foreach (var rel in schema.Relations)
                {
                    if (tableById.TryGetValue(rel.SourceTableId, out var s) &&
                        tableById.TryGetValue(rel.TargetTableId, out var d) &&
                        string.Equals(s.Name, issue.FromTable, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(d.Name, issue.ToTable, StringComparison.OrdinalIgnoreCase))
                    {
                        rel.OnDelete = ReferentialAction.NoAction;
                        rel.OnUpdate = ReferentialAction.NoAction;
                        fixes.Add($"{s.Name}->{d.Name}: cascade NO ACTION'a çekildi.");
                    }
                }
            }
        }

        // 4. Köprü tablo tekil kısıt, polimorfik indeks ve zaman aralığı CHECK kısıtlarını 0-token ekle.
        RepairCompositeIndexesAndChecks(schema, fixes);

        // 5. Motor uyumsuz trigger/SP etiketlerini ve TableId referanslarını hedef motora hizala.
        var tableByName = schema.Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var trig in schema.Triggers)
        {
            if (trig.TargetEngine != engine)
            {
                trig.TargetEngine = engine;
                fixes.Add($"Trigger {trig.Id}: TargetEngine {engine} ile hizalandı.");
            }

            if (!tableById.ContainsKey(trig.TableId) &&
                !string.IsNullOrWhiteSpace(trig.TableId) &&
                tableByName.TryGetValue(trig.TableId, out var matchedTable))
            {
                trig.TableId = matchedTable.Id;
                fixes.Add($"Trigger {trig.Id}: TableId '{matchedTable.Name}' -> '{matchedTable.Id}' olarak hizalandı.");
            }

            if (!string.Equals(trig.Timing, "Before", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trig.Timing, "After", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trig.Timing, "InsteadOf", StringComparison.OrdinalIgnoreCase))
            {
                trig.Timing = "After";
                fixes.Add($"Trigger {trig.Id}: Timing varsayılan 'After' olarak normalize edildi.");
            }

            if (!string.Equals(trig.Event, "Insert", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trig.Event, "Update", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trig.Event, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                trig.Event = "Insert";
                fixes.Add($"Trigger {trig.Id}: Event varsayılan 'Insert' olarak normalize edildi.");
            }
        }

        foreach (var sp in schema.StoredProcedures.Where(p => p.TargetEngine != engine))
        {
            sp.TargetEngine = engine;
            fixes.Add($"StoredProcedure {sp.Name}: TargetEngine {engine} ile hizalandı.");
        }

        if (fixes.Count > 0)
        {
            workspace.Swarm.SendMessage(
                self.AgentId,
                workspace.MasterAgentId,
                "repair.deterministic",
                $"0-token onarımcı {fixes.Count} iyileştirme uyguladı.");
        }

        return Task.FromResult<IReadOnlyList<string>>(fixes);
    }

    private static void RepairCompositeIndexesAndChecks(DatabaseSchema schema, List<string> fixes)
    {
        foreach (var table in schema.Tables)
        {
            // Köprü tablo bileşik tekil kısıtı
            var fkCols = table.Columns.Where(c => c.IsFK).ToList();
            var pkCols = table.Columns.Where(c => c.IsPK).ToList();
            if (fkCols.Count >= 2 && table.Columns.Count <= 5 && pkCols.Count < 2)
            {
                var fkIds = fkCols.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var covered = table.Uniques.Any(u => fkIds.All(id => u.ColumnIds.Contains(id, StringComparer.OrdinalIgnoreCase))) ||
                              table.Indexes.Any(ix => ix.IsUnique && fkIds.All(id => ix.Columns.Any(c => string.Equals(c.ColumnId, id, StringComparison.OrdinalIgnoreCase))));
                if (!covered)
                {
                    table.Indexes.Add(new SchemaIndex
                    {
                        Id = $"uq_{SchemaIdConvention.Normalize(table.Name)}_pair",
                        Name = $"uq_{SchemaIdConvention.Normalize(table.Name)}_pair",
                        IsUnique = true,
                        Columns = fkCols.Select(c => new SchemaIndexColumn { ColumnId = c.Id }).ToList(),
                    });
                    fixes.Add($"{table.Name}: köprü tablo için bileşik UNIQUE indeks eklendi.");
                }
            }

            // Polimorfik (_type, _id) bileşik indeksi
            foreach (var typeCol in table.Columns.Where(c => c.Name.EndsWith("_type", StringComparison.OrdinalIgnoreCase)))
            {
                var prefix = typeCol.Name[..^5];
                var idCol = table.Columns.FirstOrDefault(c => string.Equals(c.Name, $"{prefix}_id", StringComparison.OrdinalIgnoreCase));
                if (idCol is null) continue;

                var hasComposite = table.Indexes.Any(ix =>
                    ix.Columns.Count >= 2 &&
                    string.Equals(ix.Columns[0].ColumnId, typeCol.Id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ix.Columns[1].ColumnId, idCol.Id, StringComparison.OrdinalIgnoreCase));
                if (!hasComposite)
                {
                    table.Indexes.Add(new SchemaIndex
                    {
                        Id = $"idx_{SchemaIdConvention.Normalize(table.Name)}_{SchemaIdConvention.Normalize(prefix)}_poly",
                        Name = $"idx_{SchemaIdConvention.Normalize(table.Name)}_{SchemaIdConvention.Normalize(prefix)}_poly",
                        Columns = new List<SchemaIndexColumn>
                        {
                            new() { ColumnId = typeCol.Id },
                            new() { ColumnId = idCol.Id },
                        },
                    });
                    fixes.Add($"{table.Name}: polimorfik ({typeCol.Name}, {idCol.Name}) bileşik indeksi eklendi.");
                }
            }

            // Tarih aralığı CHECK kısıtı
            var startCol = table.Columns.FirstOrDefault(c =>
                c.Name.Equals("start_date", StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals("starts_at", StringComparison.OrdinalIgnoreCase));
            var endCol = table.Columns.FirstOrDefault(c =>
                c.Name.Equals("end_date", StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals("ends_at", StringComparison.OrdinalIgnoreCase));
            if (startCol is not null && endCol is not null)
            {
                var hasCheck = table.Checks.Any(ch =>
                    ch.Expression.Contains(startCol.Name, StringComparison.OrdinalIgnoreCase) &&
                    ch.Expression.Contains(endCol.Name, StringComparison.OrdinalIgnoreCase));
                if (!hasCheck)
                {
                    table.Checks.Add(new SchemaCheck
                    {
                        Id = $"chk_{SchemaIdConvention.Normalize(table.Name)}_date_order",
                        Name = $"chk_{SchemaIdConvention.Normalize(table.Name)}_date_order",
                        Expression = $"{endCol.Name} >= {startCol.Name}",
                    });
                    fixes.Add($"{table.Name}: CHECK ({endCol.Name} >= {startCol.Name}) eklendi.");
                }
            }

            // Kiracı izolasyon indeksi
            var tenantCol = table.Columns.FirstOrDefault(c =>
                c.Name.Equals("tenant_id", StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals("organization_id", StringComparison.OrdinalIgnoreCase) ||
                c.Name.Equals("workspace_id", StringComparison.OrdinalIgnoreCase));
            if (tenantCol is not null)
            {
                var hasTenantIdx = table.Indexes.Any(ix =>
                    ix.Columns.Count > 0 &&
                    string.Equals(ix.Columns[0].ColumnId, tenantCol.Id, StringComparison.OrdinalIgnoreCase));
                if (!hasTenantIdx)
                {
                    table.Indexes.Add(new SchemaIndex
                    {
                        Id = $"idx_{SchemaIdConvention.Normalize(table.Name)}_{SchemaIdConvention.Normalize(tenantCol.Name)}",
                        Name = $"idx_{SchemaIdConvention.Normalize(table.Name)}_{SchemaIdConvention.Normalize(tenantCol.Name)}",
                        Columns = new List<SchemaIndexColumn> { new() { ColumnId = tenantCol.Id } },
                    });
                    fixes.Add($"{table.Name}.{tenantCol.Name}: çok kiracılı kiracı indeksi eklendi.");
                }
            }
        }
    }
}
