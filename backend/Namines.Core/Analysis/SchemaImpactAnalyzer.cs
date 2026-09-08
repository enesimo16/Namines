using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// "Bu şema değişikliği sistemin geri kalanını nasıl etkiler?" sorusuna yapısal,
/// deterministik bir cevap üretir. <see cref="FkCascadeAnalyzer"/>'ın genellemesi —
/// bkz. new-phase/28-IMPACT-ANALYSIS-ENGINE.md.
///
/// Girdi iki <see cref="DatabaseSchema"/> sürümü (eski, yeni). Çıktı bir
/// <see cref="ImpactReport"/> — AI bunu SONRADAN insan diline çevirir, kendisi
/// bulgu üretmez. Motor canlı DB istatistiği kullanmaz (Faz 0); satır sayısı/süre
/// tahminleri null kalır, yalnızca risk SINIFI (<see cref="LockSeverity"/>) üretilir.
///
/// Eşleştirme <see cref="SchemaTable.StableUuid"/>/<see cref="SchemaColumn.StableUuid"/>
/// üzerinden yapılır — isim değişikliği (rename) ile add+remove birbirinden bu sayede
/// ayrılır. Eski kayıtlarda StableUuid rastgele üretilir (bkz. model varsayılanları),
/// yani eşleşme bulunamazsa güvenli tarafta kalınır: rename değil, add+remove sayılır.
/// </summary>
public static class SchemaImpactAnalyzer
{
    public static ImpactReport Analyze(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType engine)
    {
        var affectedTables = new List<AffectedTable>();
        var affectedRelations = new List<AffectedRelation>();
        var affectedIndexes = new List<AffectedIndex>();
        var breakingChanges = new List<BreakingChange>();
        var dataLossRisks = new List<DataLossRisk>();
        var lockRisks = new List<MigrationLockRisk>();
        var indexSuggestions = new List<MissingIndexSuggestion>();
        var irreversibleReasons = new List<string>();

        var oldTables = oldSchema.Tables ?? new List<SchemaTable>();
        var newTables = newSchema.Tables ?? new List<SchemaTable>();

        var oldById = oldTables.ToDictionary(t => t.StableUuid, t => t);
        var newById = newTables.ToDictionary(t => t.StableUuid, t => t);

        // ── 1. Tablo seviyesi yapısal diff ────────────────────────────────────
        foreach (var oldTable in oldTables)
        {
            if (newById.ContainsKey(oldTable.StableUuid)) continue;

            affectedTables.Add(new AffectedTable(oldTable.Name, ChangeKind.Removed, Array.Empty<string>()));
            breakingChanges.Add(new BreakingChange(
                $"Table '{oldTable.Name}' was removed.", BreakingChangeKind.TableRemoved,
                TableName: oldTable.Name,
                SuggestedMitigation: "Update the API and application code that depends on it before dropping the table."));
            dataLossRisks.Add(new DataLossRisk(oldTable.Name, null, "The table and every row in it are permanently deleted."));
            lockRisks.Add(new MigrationLockRisk("DROP TABLE", LockSeverity.Brief, oldTable.Name));
            irreversibleReasons.Add($"DROP TABLE on '{oldTable.Name}' cannot be undone — recovery means restoring from a backup.");
        }

        foreach (var newTable in newTables)
        {
            if (!oldById.TryGetValue(newTable.StableUuid, out var oldTable))
            {
                affectedTables.Add(new AffectedTable(newTable.Name, ChangeKind.Added, Array.Empty<string>()));
                continue;
            }

            var changedColumns = DiffTableColumns(oldTable, newTable, breakingChanges, dataLossRisks, lockRisks, irreversibleReasons);

            if (!string.Equals(oldTable.Name, newTable.Name, StringComparison.Ordinal))
            {
                affectedTables.Add(new AffectedTable(newTable.Name, ChangeKind.RenamedFrom, changedColumns, oldTable.Name));
                breakingChanges.Add(new BreakingChange(
                    $"Table '{oldTable.Name}' was renamed to '{newTable.Name}'.",
                    BreakingChangeKind.TableRenamed,
                    TableName: newTable.Name,
                    SuggestedMitigation: "Keep the old name alive as a view or synonym for a transition period."));
                lockRisks.Add(new MigrationLockRisk("RENAME TABLE", LockSeverity.Brief, newTable.Name));
            }
            else if (changedColumns.Count > 0)
            {
                affectedTables.Add(new AffectedTable(newTable.Name, ChangeKind.Modified, changedColumns));
            }
        }

        // ── 2. İlişki (FK) diff'i ──────────────────────────────────────────────
        var oldRelations = oldSchema.Relations ?? new List<SchemaRelation>();
        var newRelations = newSchema.Relations ?? new List<SchemaRelation>();
        var oldRelById = oldRelations.Where(r => !string.IsNullOrEmpty(r.Id)).ToDictionary(r => r.Id, r => r);
        var newRelById = newRelations.Where(r => !string.IsNullOrEmpty(r.Id)).ToDictionary(r => r.Id, r => r);
        var newTablesById2 = newTables.ToDictionary(t => t.Id, t => t);
        var oldTablesById2 = oldTables.ToDictionary(t => t.Id, t => t);

        foreach (var rel in oldRelations)
        {
            if (!string.IsNullOrEmpty(rel.Id) && newRelById.ContainsKey(rel.Id)) continue;
            var fromName = oldTablesById2.TryGetValue(rel.SourceTableId, out var f) ? f.Name : rel.SourceTableId;
            var toName = oldTablesById2.TryGetValue(rel.TargetTableId, out var t) ? t.Name : rel.TargetTableId;
            affectedRelations.Add(new AffectedRelation(rel.Id, ChangeKind.Removed, fromName, toName));
        }

        foreach (var rel in newRelations)
        {
            var isNew = string.IsNullOrEmpty(rel.Id) || !oldRelById.ContainsKey(rel.Id);
            if (!isNew) continue;

            var fromName = newTablesById2.TryGetValue(rel.SourceTableId, out var f) ? f.Name : rel.SourceTableId;
            var toName = newTablesById2.TryGetValue(rel.TargetTableId, out var t) ? t.Name : rel.TargetTableId;
            affectedRelations.Add(new AffectedRelation(rel.Id, ChangeKind.Added, fromName, toName));
            lockRisks.Add(new MigrationLockRisk("ADD FOREIGN KEY", LockSeverity.Blocking, fromName,
                "Where the engine allows it, add the key NOT VALID and validate it in a separate VALIDATE CONSTRAINT step — that path takes no lock."));

            // ── 5. Eksik index önerisi — yeni FK kolonunda kapsayan index var mı? ──
            if (newTablesById2.TryGetValue(rel.SourceTableId, out var sourceTable))
            {
                var sourceCol = sourceTable.Columns.FirstOrDefault(c => c.Id == rel.SourceColumnId);
                if (sourceCol is not null && !HasCoveringIndex(sourceTable, sourceCol.Id))
                {
                    indexSuggestions.Add(new MissingIndexSuggestion(
                        sourceTable.Name, sourceCol.Name,
                        "The foreign key column has no index, so JOINs and ON DELETE/UPDATE checks will scan the whole table."));
                }
            }
        }

        // ── 3. Cascade/FK geçerliliği — FkCascadeAnalyzer'ı doğrudan devral ────
        foreach (var issue in FkCascadeAnalyzer.Analyze(newSchema, engine))
        {
            var kind = issue.Kind switch
            {
                CascadeIssueKind.MultipleCascadePaths => BreakingChangeKind.MultipleCascadePaths,
                CascadeIssueKind.CascadeCycle => BreakingChangeKind.CascadeCycle,
                CascadeIssueKind.SetNullOnNotNullColumn => BreakingChangeKind.InvalidSetNullTarget,
                CascadeIssueKind.SetDefaultWithoutDefaultValue => BreakingChangeKind.InvalidSetDefaultTarget,
                _ => BreakingChangeKind.MultipleCascadePaths
            };
            breakingChanges.Add(new BreakingChange(issue.Message, kind, TableName: issue.FromTable));
        }

        // ── 4. Index diff (yapısal — kolon diff'inden bağımsız yeni/silinen index) ─
        DiffIndexes(oldTables, newTables, affectedIndexes, lockRisks);

        var rollback = irreversibleReasons.Count == 0
            ? new RollbackAssessment(true, null)
            : new RollbackAssessment(false, string.Join(" ", irreversibleReasons));

        var overallRisk = ComputeOverallRisk(breakingChanges, dataLossRisks, lockRisks);

        return new ImpactReport(
            affectedTables, affectedRelations, affectedIndexes,
            breakingChanges, dataLossRisks, lockRisks, indexSuggestions,
            rollback, overallRisk);
    }

    private static List<string> DiffTableColumns(
        SchemaTable oldTable, SchemaTable newTable,
        List<BreakingChange> breakingChanges, List<DataLossRisk> dataLossRisks,
        List<MigrationLockRisk> lockRisks, List<string> irreversibleReasons)
    {
        var changed = new List<string>();
        var oldColsById = oldTable.Columns.ToDictionary(c => c.StableUuid, c => c);
        var newColsById = newTable.Columns.ToDictionary(c => c.StableUuid, c => c);

        foreach (var oldCol in oldTable.Columns)
        {
            if (newColsById.ContainsKey(oldCol.StableUuid)) continue;

            changed.Add(oldCol.Name);
            breakingChanges.Add(new BreakingChange(
                $"Column '{newTable.Name}.{oldCol.Name}' was removed.", BreakingChangeKind.ColumnRemoved,
                TableName: newTable.Name, ColumnName: oldCol.Name,
                SuggestedMitigation: "Rename it with a _deprecated_ prefix first, leave it in place for a while, then drop it."));
            dataLossRisks.Add(new DataLossRisk(newTable.Name, oldCol.Name, "The column and every value in it are permanently deleted."));
            lockRisks.Add(new MigrationLockRisk("DROP COLUMN", LockSeverity.Brief, newTable.Name));
            irreversibleReasons.Add($"DROP COLUMN on '{newTable.Name}.{oldCol.Name}' cannot be undone — the data goes with it.");
        }

        foreach (var newCol in newTable.Columns)
        {
            if (!oldColsById.TryGetValue(newCol.StableUuid, out var oldCol))
            {
                changed.Add(newCol.Name);
                if (!newCol.IsNullable && string.IsNullOrWhiteSpace(newCol.DefaultValue))
                {
                    breakingChanges.Add(new BreakingChange(
                        $"'{newTable.Name}.{newCol.Name}' was added NOT NULL with no DEFAULT.",
                        BreakingChangeKind.NotNullWithoutDefault,
                        TableName: newTable.Name, ColumnName: newCol.Name,
                        SuggestedMitigation: "Give it a DEFAULT, or add the column nullable first and backfill it before tightening the constraint."));
                    lockRisks.Add(new MigrationLockRisk("ADD COLUMN NOT NULL (no default)", LockSeverity.Blocking, newTable.Name));
                }
                else
                {
                    lockRisks.Add(new MigrationLockRisk("ADD COLUMN", LockSeverity.Brief, newTable.Name));
                }
                continue;
            }

            var colChanged = false;

            if (!string.Equals(oldCol.Name, newCol.Name, StringComparison.Ordinal))
            {
                colChanged = true;
                breakingChanges.Add(new BreakingChange(
                    $"Column '{newTable.Name}.{oldCol.Name}' was renamed to '{newCol.Name}'.",
                    BreakingChangeKind.ColumnRenamed,
                    TableName: newTable.Name, ColumnName: newCol.Name,
                    SuggestedMitigation: "Use the expand-contract pattern: add the new column, write to both for a while, then drop the old one."));
                lockRisks.Add(new MigrationLockRisk("RENAME COLUMN", LockSeverity.Brief, newTable.Name));
            }

            if (!string.Equals(oldCol.Type, newCol.Type, StringComparison.OrdinalIgnoreCase)
                || IsNarrowing(oldCol.Length, newCol.Length))
            {
                colChanged = true;
                var narrowing = IsNarrowing(oldCol.Length, newCol.Length)
                    || !string.Equals(oldCol.Type, newCol.Type, StringComparison.OrdinalIgnoreCase);

                if (narrowing)
                {
                    breakingChanges.Add(new BreakingChange(
                        $"'{newTable.Name}.{newCol.Name}' was narrowed ({oldCol.Type}{FormatLength(oldCol.Length)} → {newCol.Type}{FormatLength(newCol.Length)}).",
                        BreakingChangeKind.TypeNarrowed,
                        TableName: newTable.Name, ColumnName: newCol.Name,
                        SuggestedMitigation: "Check that the existing data fits the new type and length before running this."));
                    dataLossRisks.Add(new DataLossRisk(newTable.Name, newCol.Name, "Values that do not fit the new type or length are truncated, or the conversion fails outright."));
                    irreversibleReasons.Add($"Narrowing '{newTable.Name}.{newCol.Name}' can lose data, so a safe rollback cannot be guaranteed.");
                }
                lockRisks.Add(new MigrationLockRisk("ALTER TYPE", narrowing ? LockSeverity.Blocking : LockSeverity.Blocking, newTable.Name));
            }

            if (oldCol.IsNullable && !newCol.IsNullable)
            {
                colChanged = true;
                lockRisks.Add(new MigrationLockRisk("SET NOT NULL", LockSeverity.Blocking, newTable.Name,
                    "Confirm no row holds NULL first — that check scans the whole table."));
            }
            else if (!oldCol.IsNullable && newCol.IsNullable)
            {
                colChanged = true;
                lockRisks.Add(new MigrationLockRisk("DROP NOT NULL", LockSeverity.Brief, newTable.Name));
            }

            if (colChanged) changed.Add(newCol.Name);
        }

        return changed.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void DiffIndexes(
        List<SchemaTable> oldTables, List<SchemaTable> newTables,
        List<AffectedIndex> affectedIndexes, List<MigrationLockRisk> lockRisks)
    {
        var oldByUuid = oldTables.ToDictionary(t => t.StableUuid, t => t);

        foreach (var newTable in newTables)
        {
            var oldIndexIds = oldByUuid.TryGetValue(newTable.StableUuid, out var oldTable)
                ? oldTable.Indexes.Select(i => i.StableUuid).ToHashSet()
                : new HashSet<string>();

            foreach (var idx in newTable.Indexes)
            {
                if (oldIndexIds.Contains(idx.StableUuid)) continue;
                affectedIndexes.Add(new AffectedIndex(newTable.Name, idx.Name, ChangeKind.Added));
                lockRisks.Add(new MigrationLockRisk("CREATE INDEX", LockSeverity.Blocking, newTable.Name,
                    "Use CONCURRENTLY on PostgreSQL, or WITH (ONLINE=ON) on SQL Server Enterprise."));
            }
        }

        foreach (var oldTable in oldTables)
        {
            var newIndexIds = oldByUuid.ContainsKey(oldTable.StableUuid)
                ? newTables.FirstOrDefault(t => t.StableUuid == oldTable.StableUuid)?.Indexes.Select(i => i.StableUuid).ToHashSet() ?? new HashSet<string>()
                : new HashSet<string>();

            var stillExists = newTables.Any(t => t.StableUuid == oldTable.StableUuid);
            if (!stillExists) continue; // tablo zaten DROP TABLE olarak raporlandı

            foreach (var idx in oldTable.Indexes)
            {
                if (newIndexIds.Contains(idx.StableUuid)) continue;
                affectedIndexes.Add(new AffectedIndex(oldTable.Name, idx.Name, ChangeKind.Removed));
                lockRisks.Add(new MigrationLockRisk("DROP INDEX", LockSeverity.Brief, oldTable.Name,
                    "Review the query plans before dropping the index; removing it can cause a performance regression."));
            }
        }
    }

    private static bool HasCoveringIndex(SchemaTable table, string columnId)
    {
        if (table.Indexes.Any(idx => idx.Columns.Count > 0 && idx.Columns[0].ColumnId == columnId)) return true;
        if (table.Uniques.Any(u => u.ColumnIds.Count > 0 && u.ColumnIds[0] == columnId)) return true;
        return table.Columns.Any(c => c.Id == columnId && c.IsPK);
    }

    private static bool IsNarrowing(int? oldLength, int? newLength) =>
        oldLength.HasValue && newLength.HasValue && newLength.Value < oldLength.Value;

    private static string FormatLength(int? length) => length.HasValue ? $"({length.Value})" : string.Empty;

    private static RiskLevel ComputeOverallRisk(
        List<BreakingChange> breakingChanges, List<DataLossRisk> dataLossRisks, List<MigrationLockRisk> lockRisks)
    {
        var level = RiskLevel.Safe;
        if (lockRisks.Any(l => l.Severity == LockSeverity.Blocking)) level = Max(level, RiskLevel.Risky);
        if (dataLossRisks.Count > 0) level = Max(level, RiskLevel.Destructive);
        if (breakingChanges.Count > 0) level = Max(level, RiskLevel.Breaking);
        return level;
    }

    private static RiskLevel Max(RiskLevel a, RiskLevel b) => a > b ? a : b;
}
