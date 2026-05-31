using System.Linq;
using System.Text.RegularExpressions;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure;

public class LinterService : ILinterService
{
    public LintResult Lint(DatabaseSchema schema)
    {
        var result = new LintResult();

        if (schema == null || schema.Tables == null)
            return result;

        foreach (var table in schema.Tables)
        {
            // Rule: Table name must be PascalCase
            if (!string.IsNullOrWhiteSpace(table.Name) && !Regex.IsMatch(table.Name, @"^[A-Z][a-zA-Z0-9]*$"))
            {
                result.Messages.Add(new LintMessage
                {
                    Severity = LintSeverity.Info,
                    Message = $"Table '{table.Name}' should ideally be PascalCase.",
                    TableId = table.Id
                });
            }

            var pkColumns = table.Columns.Where(c => c.IsPK).ToList();

            // Rule: Multiple PKs
            if (pkColumns.Count > 1)
            {
                result.Messages.Add(new LintMessage
                {
                    Severity = LintSeverity.Error,
                    Message = $"Table '{table.Name}' has multiple primary keys.",
                    TableId = table.Id
                });
            }

            // Rule: No PK
            if (pkColumns.Count == 0)
            {
                result.Messages.Add(new LintMessage
                {
                    Severity = LintSeverity.Warning,
                    Message = $"Table '{table.Name}' has no primary key.",
                    TableId = table.Id
                });
            }
        }

        if (schema.Relations != null)
        {
            foreach (var relation in schema.Relations)
            {
                var sourceTable = schema.Tables.FirstOrDefault(t => t.Id == relation.SourceTableId);
                var targetTable = schema.Tables.FirstOrDefault(t => t.Id == relation.TargetTableId);

                if (sourceTable == null || targetTable == null) continue;

                var sourceCol = sourceTable.Columns.FirstOrDefault(c => c.Id == relation.SourceColumnId);
                var targetCol = targetTable.Columns.FirstOrDefault(c => c.Id == relation.TargetColumnId);

                if (sourceCol == null || targetCol == null) continue;

                // Rule: FK type matches PK type
                if (sourceCol.Type.ToLower() != targetCol.Type.ToLower())
                {
                    result.Messages.Add(new LintMessage
                    {
                        Severity = LintSeverity.Error,
                        Message = $"Type mismatch in relation: '{sourceTable.Name}.{sourceCol.Name}' ({sourceCol.Type}) -> '{targetTable.Name}.{targetCol.Name}' ({targetCol.Type})",
                        TableId = sourceTable.Id,
                        ColumnId = sourceCol.Id
                    });
                }
            }
            
            // Basic Circular FK check (A -> B -> A)
            foreach (var r1 in schema.Relations)
            {
                var returnPath = schema.Relations.FirstOrDefault(r2 => 
                    r2.SourceTableId == r1.TargetTableId && 
                    r2.TargetTableId == r1.SourceTableId);

                if (returnPath != null)
                {
                    // Adding warning to source table of r1
                    // Only add once per pair to avoid duplicate warnings
                    if (string.Compare(r1.SourceTableId, r1.TargetTableId) < 0)
                    {
                        result.Messages.Add(new LintMessage
                        {
                            Severity = LintSeverity.Warning,
                            Message = $"Circular foreign key detected between '{schema.Tables.FirstOrDefault(t=>t.Id==r1.SourceTableId)?.Name}' and '{schema.Tables.FirstOrDefault(t=>t.Id==r1.TargetTableId)?.Name}'.",
                            TableId = r1.SourceTableId
                        });
                    }
                }
            }
        }

        return result;
    }
}
