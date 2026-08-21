using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.AI;

namespace Namines.Infrastructure.Services;

public class MigrationService : IMigrationService
{
    private readonly GroqAIService _groqService;

    public MigrationService(GroqAIService groqService)
    {
        _groqService = groqService;
    }

    public async Task<DatabaseSchema> ParseDbContextAsync(string dbContextCode, DatabaseType dbType)
    {
        if (string.IsNullOrWhiteSpace(dbContextCode))
        {
            return new DatabaseSchema
            {
                Name = "EmptySchema"
            };
        }

        return await _groqService.ParseDbContextToSchemaAsync(dbContextCode, dbType);
    }

    private static bool AreTablesMatching(SchemaTable oldTable, SchemaTable newTable)
    {
        if (oldTable == null || newTable == null) return false;
        if (!string.IsNullOrEmpty(oldTable.StableUuid) && !string.IsNullOrEmpty(newTable.StableUuid))
        {
            return string.Equals(oldTable.StableUuid, newTable.StableUuid, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(oldTable.Name, newTable.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreColumnsMatching(SchemaColumn oldCol, SchemaColumn newCol)
    {
        if (oldCol == null || newCol == null) return false;
        if (!string.IsNullOrEmpty(oldCol.StableUuid) && !string.IsNullOrEmpty(newCol.StableUuid))
        {
            return string.Equals(oldCol.StableUuid, newCol.StableUuid, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(oldCol.Name, newCol.Name, StringComparison.OrdinalIgnoreCase);
    }

    public Task<SchemaDiffResult> CalculateDiffAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType engine = DatabaseType.PostgreSQL)
    {
        var result = new SchemaDiffResult();

        if (oldSchema == null) oldSchema = new DatabaseSchema();
        if (newSchema == null) newSchema = new DatabaseSchema();

        var oldTables = oldSchema.Tables ?? new List<SchemaTable>();
        var newTables = newSchema.Tables ?? new List<SchemaTable>();

        // 1. Identify Renamed, Added, and Removed Tables
        
        // 1.1 Renamed Tables
        foreach (var newTable in newTables)
        {
            var oldTable = oldTables.FirstOrDefault(o => AreTablesMatching(o, newTable));
            if (oldTable != null && !string.Equals(oldTable.Name, newTable.Name, StringComparison.OrdinalIgnoreCase))
            {
                result.RenamedTables.Add(new TableRenameDetail
                {
                    OldName = oldTable.Name,
                    NewName = newTable.Name
                });
            }
        }

        // 1.2 Added Tables
        foreach (var newTable in newTables)
        {
            if (!oldTables.Any(o => AreTablesMatching(o, newTable)))
            {
                result.AddedTables.Add(newTable.Name);
            }
        }

        // 1.3 Removed Tables
        foreach (var oldTable in oldTables)
        {
            if (!newTables.Any(n => AreTablesMatching(oldTable, n)))
            {
                result.RemovedTables.Add(oldTable.Name);
            }
        }

        // 2. Modified Tables & Column Changes (Common Tables)
        foreach (var oldTable in oldTables)
        {
            var newTable = newTables.FirstOrDefault(n => AreTablesMatching(oldTable, n));
            if (newTable == null) continue; // This table was removed

            var tableDiff = new TableDiffDetail { TableName = newTable.Name }; // Use new name for reference
            bool tableModified = false;

            // 2.1 Renamed Columns
            foreach (var newCol in newTable.Columns)
            {
                var oldCol = oldTable.Columns.FirstOrDefault(o => AreColumnsMatching(o, newCol));
                if (oldCol != null && !string.Equals(oldCol.Name, newCol.Name, StringComparison.OrdinalIgnoreCase))
                {
                    tableDiff.RenamedColumns.Add(new ColumnRenameDetail
                    {
                        OldName = oldCol.Name,
                        NewName = newCol.Name
                    });
                    tableModified = true;
                }
            }

            // 2.2 Added Columns
            foreach (var newCol in newTable.Columns)
            {
                if (!oldTable.Columns.Any(o => AreColumnsMatching(o, newCol)))
                {
                    tableDiff.AddedColumns.Add(newCol.Name);
                    tableModified = true;
                }
            }

            // 2.3 Removed Columns
            foreach (var oldCol in oldTable.Columns)
            {
                if (!newTable.Columns.Any(n => AreColumnsMatching(oldCol, n)))
                {
                    tableDiff.RemovedColumns.Add(oldCol.Name);
                    tableModified = true;
                }
            }

            // 2.4 Modified Columns (matching by UUID/Name)
            foreach (var oldCol in oldTable.Columns)
            {
                var newCol = newTable.Columns.FirstOrDefault(n => AreColumnsMatching(oldCol, n));
                if (newCol == null) continue; // Column was removed or renamed

                bool isTypeChanged = !string.Equals(oldCol.Type, newCol.Type, StringComparison.OrdinalIgnoreCase);
                bool isLengthChanged = oldCol.Length != newCol.Length;
                bool isNullableChanged = oldCol.IsNullable != newCol.IsNullable;
                bool isDefaultChanged = !string.Equals(oldCol.DefaultValue, newCol.DefaultValue, StringComparison.OrdinalIgnoreCase);
                bool isPkChanged = oldCol.IsPK != newCol.IsPK;

                if (isTypeChanged || isLengthChanged || isNullableChanged || isDefaultChanged || isPkChanged)
                {
                    tableDiff.ModifiedColumns.Add(newCol.Name);
                    tableModified = true;
                }
            }

            if (tableModified)
            {
                result.ModifiedTables.Add(tableDiff);
            }
        }

        // 3. Identify Added and Removed Relations
        var oldRelations = oldSchema.Relations ?? new List<SchemaRelation>();
        var newRelations = newSchema.Relations ?? new List<SchemaRelation>();

        foreach (var newRel in newRelations)
        {
            if (!oldRelations.Any(o => AreRelationsMatching(o, oldSchema, newRel, newSchema)))
            {
                result.AddedRelations.Add(newRel);
            }
        }

        foreach (var oldRel in oldRelations)
        {
            if (!newRelations.Any(n => AreRelationsMatching(oldRel, oldSchema, n, newSchema)))
            {
                result.RemovedRelations.Add(oldRel);
            }
        }

        // ── Risk sınıflandırması — SchemaImpactAnalyzer tek doğruluk kaynağı ────
        // Daha önce burada dağınık `HasBreakingChanges = true` atamaları vardı (tablo
        // silindi mi, kolon silindi mi, tip değişti mi...) — her biri ayrı ayrı, birbirini
        // görmeden karar veriyordu. G9: bunun yerine SchemaImpactAnalyzer'ın deterministik,
        // StableUuid-farkındalı, motora özgü (Msg 1785 vb.) analizi kullanılıyor —
        // bkz. new-phase/11-MIGRATIONS-BRANCHING.md §2 risk tablosu.
        var impact = SchemaImpactAnalyzer.Analyze(oldSchema, newSchema, engine);
        result.Impact = impact;
        result.OverallRisk = impact.OverallRisk;
        result.HasBreakingChanges = impact.BreakingChanges.Count > 0;

        return Task.FromResult(result);
    }

    private static string? GetTableName(DatabaseSchema schema, string tableId)
    {
        if (schema.Tables == null) return null;
        var table = schema.Tables.FirstOrDefault(t => string.Equals(t.Id, tableId, StringComparison.OrdinalIgnoreCase));
        if (table != null) return table.Name;
        if (schema.Tables.Any(t => string.Equals(t.Name, tableId, StringComparison.OrdinalIgnoreCase)))
            return tableId;
        return null;
    }

    private static string? GetColumnName(DatabaseSchema schema, string tableId, string columnId)
    {
        if (schema.Tables == null) return null;
        var table = schema.Tables.FirstOrDefault(t => string.Equals(t.Id, tableId, StringComparison.OrdinalIgnoreCase) 
                                                   || string.Equals(t.Name, tableId, StringComparison.OrdinalIgnoreCase));
        if (table == null) return null;
        
        var col = table.Columns?.FirstOrDefault(c => string.Equals(c.Id, columnId, StringComparison.OrdinalIgnoreCase));
        if (col != null) return col.Name;
        
        if (table.Columns != null && table.Columns.Any(c => string.Equals(c.Name, columnId, StringComparison.OrdinalIgnoreCase)))
            return columnId;
        return null;
    }

    private static bool AreRelationsMatching(SchemaRelation r1, DatabaseSchema s1, SchemaRelation r2, DatabaseSchema s2)
    {
        if (r1 == null || r2 == null) return false;

        var sourceTable1 = GetTableName(s1, r1.SourceTableId);
        var sourceColumn1 = GetColumnName(s1, r1.SourceTableId, r1.SourceColumnId);
        var targetTable1 = GetTableName(s1, r1.TargetTableId);
        var targetColumn1 = GetColumnName(s1, r1.TargetTableId, r1.TargetColumnId);

        var sourceTable2 = GetTableName(s2, r2.SourceTableId);
        var sourceColumn2 = GetColumnName(s2, r2.SourceTableId, r2.SourceColumnId);
        var targetTable2 = GetTableName(s2, r2.TargetTableId);
        var targetColumn2 = GetColumnName(s2, r2.TargetTableId, r2.TargetColumnId);

        if (sourceTable1 == null || sourceColumn1 == null || targetTable1 == null || targetColumn1 == null) return false;
        if (sourceTable2 == null || sourceColumn2 == null || targetTable2 == null || targetColumn2 == null) return false;

        return string.Equals(sourceTable1, sourceTable2, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(sourceColumn1, sourceColumn2, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(targetTable1, targetTable2, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(targetColumn1, targetColumn2, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(r1.Type, r2.Type, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<MigrationResult> GenerateMigrationAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType dbType)
    {
        if (oldSchema == null) oldSchema = new DatabaseSchema();
        if (newSchema == null) newSchema = new DatabaseSchema();

        return await _groqService.GenerateMigrationCodeAsync(oldSchema, newSchema, dbType);
    }
}
