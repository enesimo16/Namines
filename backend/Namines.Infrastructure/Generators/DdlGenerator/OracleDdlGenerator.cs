using Namines.Core.Enums;
using System.Text;
using System.Linq;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// Oracle DDL generator.
/// - INT → NUMBER(10), BIGINT → NUMBER(19), VARCHAR/NVARCHAR → NVARCHAR2
/// - Auto-increment: Oracle 12c+ GENERATED ALWAYS AS IDENTITY
/// - Foreign keys via ALTER TABLE (ayrı blok)
/// - Her statement sonrasında noktalı virgül; PL/SQL bloğu gerekmez.
/// </summary>
public class OracleDdlGenerator : IDdlGenerator
{
    public string Generate(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"CREATE TABLE \"{table.Name}\" (");

            var pkColumns = table.Columns.Where(c => c.IsPK).ToList();
            var lines = new List<string>();

            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];
                var oracleType = MapToOracleType(col.Type, col.Length);
                var nullStr = col.IsNullable ? "NULL" : "NOT NULL";
                var defaultStr = !string.IsNullOrWhiteSpace(col.DefaultValue)
                    ? $" DEFAULT {col.DefaultValue}"
                    : "";

                // Oracle 12c+ IDENTITY (auto-increment)
                var identityStr = (col.IsPK && IsNumericType(col.Type))
                    ? " GENERATED ALWAYS AS IDENTITY"
                    : "";

                lines.Add($"    \"{col.Name}\" {oracleType}{identityStr} {nullStr}{defaultStr}");
            }

            // Inline PK constraint
            if (pkColumns.Any())
            {
                var pkCols = string.Join(", ", pkColumns.Select(c => $"\"{c.Name}\""));
                lines.Add($"    CONSTRAINT \"PK_{table.Name}\" PRIMARY KEY ({pkCols})");
            }

            sb.AppendLine(string.Join(",\n", lines));
            sb.AppendLine(");");
            sb.AppendLine();
        }

        // Foreign key constraints (ayrı ALTER TABLE blokları)
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

                // FK adı 30 karakterle sınırlandır (Oracle 12c- uyumluluğu)
                var fkName = $"FK_{sourceTable.Name}_{targetTable.Name}_{sourceCol.Name}";
                if (fkName.Length > 30) fkName = fkName[..30];

                var actions = ReferentialActionSql.Clauses(relation.OnDelete, relation.OnUpdate, DatabaseType.Oracle);

                sb.AppendLine($"ALTER TABLE \"{sourceTable.Name}\"");
                sb.AppendLine($"    ADD CONSTRAINT \"{fkName}\"");
                sb.AppendLine($"    FOREIGN KEY (\"{sourceCol.Name}\")");
                sb.AppendLine($"    REFERENCES \"{targetTable.Name}\" (\"{targetCol.Name}\"){actions};");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static bool IsNumericType(string type) =>
        type.ToUpperInvariant() is "INT" or "INTEGER" or "NUMBER" or "BIGINT" or "SMALLINT";

    private static string MapToOracleType(string type, int? length)
    {
        return type.ToUpperInvariant() switch
        {
            "INT" or "INTEGER" or "SMALLINT" or "TINYINT" => "NUMBER(10)",
            "BIGINT"                                        => "NUMBER(19)",
            "DECIMAL" or "NUMERIC"                          => length.HasValue ? $"NUMBER({length})" : "NUMBER(18,4)",
            "FLOAT" or "REAL"                               => "BINARY_FLOAT",
            "DOUBLE"                                        => "BINARY_DOUBLE",
            "VARCHAR"                                       => $"VARCHAR2({length ?? 255})",
            "NVARCHAR"                                      => $"NVARCHAR2({length ?? 255})",
            "CHAR"                                          => $"CHAR({length ?? 1})",
            "TEXT" or "NTEXT"                               => "CLOB",
            "DATETIME" or "TIMESTAMP"                       => "TIMESTAMP",
            "DATE"                                          => "DATE",
            "TIME"                                          => "INTERVAL DAY TO SECOND",
            "BIT" or "BOOLEAN"                              => "NUMBER(1)",
            "UNIQUEIDENTIFIER" or "UUID"                    => "RAW(16)",
            "BLOB" or "BINARY" or "VARBINARY"               => "BLOB",
            "JSON"                                          => "CLOB",
            _                                               => "NVARCHAR2(255)"
        };
    }
}
