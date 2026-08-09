using Namines.Core.Enums;
using System.Text;
using System.Linq;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// MariaDB DDL generator.
/// MySQL'den türetilmiştir; ENGINE=InnoDB ve AUTO_INCREMENT syntax'ı aynıdır.
/// MariaDB'ye özel: backtick quoting, explicit ENGINE=InnoDB DEFAULT CHARSET=utf8mb4.
/// </summary>
public class MariaDbDdlGenerator : IDdlGenerator
{
    public string Generate(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"CREATE TABLE IF NOT EXISTS `{table.Name}` (");

            var pkColumns = table.Columns.Where(c => c.IsPK).ToList();
            var lines = new List<string>();

            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];
                var lengthStr = col.Length.HasValue ? $"({col.Length})" : "";
                var nullStr = col.IsNullable ? "NULL" : "NOT NULL";
                var defaultStr = !string.IsNullOrWhiteSpace(col.DefaultValue)
                    ? $" DEFAULT {col.DefaultValue}"
                    : "";

                // AUTO_INCREMENT: INT/BIGINT PK için
                var autoIncStr = (col.IsPK && IsIntegerType(col.Type))
                    ? " AUTO_INCREMENT"
                    : "";

                lines.Add($"    `{col.Name}` {col.Type.ToUpper()}{lengthStr} {nullStr}{defaultStr}{autoIncStr}");
            }

            // Primary Key
            if (pkColumns.Any())
            {
                var pkCols = string.Join(", ", pkColumns.Select(c => $"`{c.Name}`"));
                lines.Add($"    PRIMARY KEY ({pkCols})");
            }

            sb.AppendLine(string.Join(",\n", lines));
            sb.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
            sb.AppendLine();
        }

        // Foreign key constraints (ayrı ALTER TABLE)
        if (schema.Relations != null && schema.Relations.Any())
        {
            foreach (var relation in schema.Relations)
            {
                var sourceTable = schema.Tables.FirstOrDefault(t => t.Id == relation.SourceTableId);
                var targetTable = schema.Tables.FirstOrDefault(t => t.Id == relation.TargetTableId);
                if (sourceTable == null || targetTable == null) continue;

                var sourceCol = sourceTable.Columns.FirstOrDefault(c => c.Id == relation.SourceColumnId);
                var targetCol = targetTable.Columns.FirstOrDefault(c => c.Id == relation.TargetColumnId);
                if (sourceCol == null || targetCol == null) continue;

                var actions = ReferentialActionSql.Clauses(relation.OnDelete, relation.OnUpdate, DatabaseType.MariaDB);

                sb.AppendLine($"ALTER TABLE `{sourceTable.Name}` ADD CONSTRAINT `FK_{sourceTable.Name}_{targetTable.Name}_{sourceCol.Name}`");
                sb.AppendLine($"    FOREIGN KEY (`{sourceCol.Name}`) REFERENCES `{targetTable.Name}` (`{targetCol.Name}`){actions};");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static bool IsIntegerType(string type) =>
        type.ToUpperInvariant() is "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT";
}
