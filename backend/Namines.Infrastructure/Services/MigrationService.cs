using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    public Task<SchemaDiffResult> CalculateDiffAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema)
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
                result.HasBreakingChanges = true; // Table deletion is breaking
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

                    // Adding a non-nullable column without a default value is a breaking change!
                    if (!newCol.IsNullable && string.IsNullOrEmpty(newCol.DefaultValue))
                    {
                        result.HasBreakingChanges = true;
                    }
                }
            }

            // 2.3 Removed Columns
            foreach (var oldCol in oldTable.Columns)
            {
                if (!newTable.Columns.Any(n => AreColumnsMatching(oldCol, n)))
                {
                    tableDiff.RemovedColumns.Add(oldCol.Name);
                    tableModified = true;
                    result.HasBreakingChanges = true; // Column deletion is breaking
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

                    // Breaking change conditions for modified columns
                    if (isTypeChanged || isPkChanged || (oldCol.IsNullable && !newCol.IsNullable))
                    {
                        result.HasBreakingChanges = true;
                    }
                }
            }

            if (tableModified)
            {
                result.ModifiedTables.Add(tableDiff);
            }
        }

        return Task.FromResult(result);
    }

    public async Task<MigrationResult> GenerateMigrationAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType dbType)
    {
        if (oldSchema == null) oldSchema = new DatabaseSchema();
        if (newSchema == null) newSchema = new DatabaseSchema();

        return await _groqService.GenerateMigrationCodeAsync(oldSchema, newSchema, dbType);
    }
}
