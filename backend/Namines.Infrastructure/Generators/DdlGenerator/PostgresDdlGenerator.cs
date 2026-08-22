using Namines.Core.Enums;
using Namines.Core.Analysis;
using System.Text;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using System.Linq;

namespace Namines.Infrastructure.Generators.DdlGenerator;

public class PostgresDdlGenerator : IDdlGenerator
{
    public string Generate(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"CREATE TABLE \"{table.Name}\" (");

            var pkColumns = table.Columns.Where(c => c.IsPK).ToList();
            
            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];
                var rawType = col.Type.ToUpper();

                // SERIAL/BIGSERIAL kısayolu tip eşlemesinden ÖNCE, ham kanonik tip
                // üzerinden karar verilir — böylece yalnızca gerçek PK INT/BIGINT
                // kolonları otomatik artan olur.
                string type;
                var generated = IdentityPolicy.IsGenerated(col, pkColumns.Count);
                if (generated && rawType == "INT") type = "SERIAL";
                else if (generated && rawType == "BIGINT") type = "BIGSERIAL";
                else type = TypeSql.Map(col.Type, col.Length, DatabaseType.PostgreSQL);

                var nullStr = col.IsNullable ? "NULL" : "NOT NULL";
                var defaultValue = DefaultValueSql.Translate(col.DefaultValue, DatabaseType.PostgreSQL);
                var defaultStr = !string.IsNullOrWhiteSpace(defaultValue) ? $" DEFAULT {defaultValue}" : "";

                sb.Append($"    \"{col.Name}\" {type} {nullStr}{defaultStr}");
                
                if (i < table.Columns.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            if (pkColumns.Any())
            {
                sb.AppendLine($"    , CONSTRAINT \"PK_{table.Name}\" PRIMARY KEY ({string.Join(", ", pkColumns.Select(c => $"\"{c.Name}\""))})");
            }

            foreach (var constraint in ConstraintSql.InlineConstraints(table, DatabaseType.PostgreSQL, Quote))
            {
                sb.AppendLine($"    , {constraint.TrimStart()}");
            }

            sb.AppendLine(");");
            sb.AppendLine();

            var indexes = ConstraintSql.CreateIndexes(table, DatabaseType.PostgreSQL, Quote);
            if (!string.IsNullOrEmpty(indexes))
            {
                sb.Append(indexes);
                sb.AppendLine();
            }
        }

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

                var actions = ReferentialActionSql.Clauses(relation.OnDelete, relation.OnUpdate, DatabaseType.PostgreSQL);

                sb.AppendLine($"ALTER TABLE \"{sourceTable.Name}\" ADD CONSTRAINT \"FK_{sourceTable.Name}_{targetTable.Name}_{sourceCol.Name}\" FOREIGN KEY(\"{sourceCol.Name}\")");
                sb.AppendLine($"REFERENCES \"{targetTable.Name}\" (\"{targetCol.Name}\"){actions};");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string Quote(string identifier) => $"\"{identifier}\"";
}
