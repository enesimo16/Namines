using System.Text;
using System.Linq;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// SQLite DDL generator.
/// - INT/BIGINT PK → INTEGER PRIMARY KEY AUTOINCREMENT (SQLite affinity)
/// - NVARCHAR/VARCHAR → TEXT
/// - Foreign keys via PRAGMA + separate constraint block
/// </summary>
public class SqliteDdlGenerator : IDdlGenerator
{
    public string Generate(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        // SQLite foreign key desteğini aktifleştir
        sb.AppendLine("PRAGMA foreign_keys = ON;");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"CREATE TABLE IF NOT EXISTS \"{table.Name}\" (");

            var pkColumns = table.Columns.Where(c => c.IsPK).ToList();
            bool singleIntPk = pkColumns.Count == 1 &&
                               IsIntegerType(pkColumns[0].Type);

            var lines = new List<string>();

            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];
                var sqliteType = MapToSqliteType(col.Type);
                var nullStr = col.IsNullable ? "NULL" : "NOT NULL";
                var defaultStr = !string.IsNullOrWhiteSpace(col.DefaultValue)
                    ? $" DEFAULT {col.DefaultValue}"
                    : "";

                // SQLite'ta tek INT PK → AUTOINCREMENT için özel syntax
                if (col.IsPK && singleIntPk)
                {
                    lines.Add($"    \"{col.Name}\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT");
                }
                else
                {
                    lines.Add($"    \"{col.Name}\" {sqliteType} {nullStr}{defaultStr}");
                }
            }

            // Bileşik PK (tek INT PK değilse)
            if (pkColumns.Count > 1 || (pkColumns.Count == 1 && !singleIntPk))
            {
                var pkCols = string.Join(", ", pkColumns.Select(c => $"\"{c.Name}\""));
                lines.Add($"    PRIMARY KEY ({pkCols})");
            }

            // FK kısıtlamaları inline
            if (schema.Relations != null && schema.Relations.Any())
            {
                foreach (var relation in schema.Relations)
                {
                    var sourceTable = schema.Tables.FirstOrDefault(t => t.Id == relation.SourceTableId);
                    var targetTable = schema.Tables.FirstOrDefault(t => t.Id == relation.TargetTableId);
                    if (sourceTable?.Name != table.Name || targetTable == null) continue;

                    var sourceCol = sourceTable.Columns.FirstOrDefault(c => c.Id == relation.SourceColumnId);
                    var targetCol = targetTable.Columns.FirstOrDefault(c => c.Id == relation.TargetColumnId);
                    if (sourceCol == null || targetCol == null) continue;

                    lines.Add($"    FOREIGN KEY (\"{sourceCol.Name}\") REFERENCES \"{targetTable.Name}\" (\"{targetCol.Name}\") ON DELETE CASCADE");
                }
            }

            sb.AppendLine(string.Join(",\n", lines));
            sb.AppendLine(");");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static bool IsIntegerType(string type) =>
        type.ToUpperInvariant() is "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT";

    private static string MapToSqliteType(string type)
    {
        return type.ToUpperInvariant() switch
        {
            "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT" => "INTEGER",
            "NVARCHAR" or "VARCHAR" or "CHAR" or "TEXT" or "NTEXT" => "TEXT",
            "REAL" or "FLOAT" or "DOUBLE" or "NUMERIC" or "DECIMAL" => "REAL",
            "BLOB" or "BINARY" or "VARBINARY" => "BLOB",
            "BOOLEAN" or "BIT" => "INTEGER",  // SQLite boolean → INTEGER (0/1)
            "DATE" or "DATETIME" or "TIMESTAMP" or "TIME" => "TEXT",  // ISO 8601
            _ => "TEXT"
        };
    }
}
