using Namines.Core.Enums;
using Namines.Core.Analysis;
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
                var sqlType = TypeSql.Map(col.Type, col.Length, DatabaseType.MariaDB);
                var nullStr = col.IsNullable ? "NULL" : "NOT NULL";
                var defaultValue = DefaultValueSql.Translate(col.DefaultValue, DatabaseType.MariaDB);
                var defaultStr = !string.IsNullOrWhiteSpace(defaultValue)
                    ? $" DEFAULT {defaultValue}"
                    : "";

                // AUTO_INCREMENT: yalnızca TEK KOLONLU INT/BIGINT PK için. MariaDB (MySQL
                // gibi) bir tabloda yalnızca bir AUTO_INCREMENT kolonuna izin verir;
                // bileşik PK'nın iki kolonuna birden vermek geçersiz DDL üretir.
                var autoIncStr = IdentityPolicy.IsGenerated(col, pkColumns.Count)
                    ? " AUTO_INCREMENT"
                    : "";

                lines.Add($"    `{col.Name}` {sqlType} {nullStr}{defaultStr}{autoIncStr}");
            }

            // Primary Key
            if (pkColumns.Any())
            {
                var pkCols = string.Join(", ", pkColumns.Select(c => $"`{c.Name}`"));
                lines.Add($"    PRIMARY KEY ({pkCols})");
            }

            lines.AddRange(ConstraintSql.InlineConstraints(table, DatabaseType.MariaDB, Quote));

            sb.AppendLine(string.Join(",\n", lines));
            sb.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
            sb.AppendLine();

            var indexes = ConstraintSql.CreateIndexes(table, DatabaseType.MariaDB, Quote);
            if (!string.IsNullOrEmpty(indexes))
            {
                sb.Append(indexes);
                sb.AppendLine();
            }
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

    private static string Quote(string identifier) => $"`{identifier}`";
}
