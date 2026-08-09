using Namines.Core.Enums;
using System.Text;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using System.Linq;

namespace Namines.Infrastructure.Generators.DdlGenerator;

public class MySqlDdlGenerator : IDdlGenerator
{
    public string Generate(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"CREATE TABLE `{table.Name}` (");

            var pkColumns = table.Columns.Where(c => c.IsPK).ToList();
            
            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];
                var lengthStr = col.Length.HasValue ? $"({col.Length})" : "";
                var nullStr = col.IsNullable ? "NULL" : "NOT NULL";
                var defaultStr = !string.IsNullOrWhiteSpace(col.DefaultValue) ? $" DEFAULT {col.DefaultValue}" : "";

                var aiStr = (col.IsPK && (col.Type.ToUpper() == "INT" || col.Type.ToUpper() == "BIGINT")) ? " AUTO_INCREMENT" : "";

                sb.Append($"    `{col.Name}` {col.Type.ToUpper()}{lengthStr} {nullStr}{defaultStr}{aiStr}");
                
                if (i < table.Columns.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            if (pkColumns.Any())
            {
                sb.AppendLine($"    , PRIMARY KEY ({string.Join(", ", pkColumns.Select(c => $"`{c.Name}`"))})");
            }

            foreach (var constraint in ConstraintSql.InlineConstraints(table, DatabaseType.MySQL, Quote))
            {
                sb.AppendLine($"    , {constraint.TrimStart()}");
            }

            sb.AppendLine(") ENGINE=InnoDB;");
            sb.AppendLine();

            var indexes = ConstraintSql.CreateIndexes(table, DatabaseType.MySQL, Quote);
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

                var actions = ReferentialActionSql.Clauses(relation.OnDelete, relation.OnUpdate, DatabaseType.MySQL);

                sb.AppendLine($"ALTER TABLE `{sourceTable.Name}` ADD CONSTRAINT `FK_{sourceTable.Name}_{targetTable.Name}_{sourceCol.Name}` FOREIGN KEY(`{sourceCol.Name}`)");
                sb.AppendLine($"REFERENCES `{targetTable.Name}` (`{targetCol.Name}`){actions};");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string Quote(string identifier) => $"`{identifier}`";
}
