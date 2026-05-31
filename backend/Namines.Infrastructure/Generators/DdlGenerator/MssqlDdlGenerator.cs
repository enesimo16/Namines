using System.Text;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using System.Linq;

namespace Namines.Infrastructure.Generators.DdlGenerator;

public class MssqlDdlGenerator : IDdlGenerator
{
    public string Generate(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"CREATE TABLE [{table.Name}] (");

            var pkColumns = table.Columns.Where(c => c.IsPK).ToList();
            
            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];
                var lengthStr = col.Length.HasValue ? $"({col.Length})" : "";
                var nullStr = col.IsNullable ? "NULL" : "NOT NULL";
                var defaultStr = !string.IsNullOrWhiteSpace(col.DefaultValue) ? $" DEFAULT {col.DefaultValue}" : "";

                // Identity for INT/BIGINT PK
                var identityStr = (col.IsPK && (col.Type.ToUpper() == "INT" || col.Type.ToUpper() == "BIGINT")) ? " IDENTITY(1,1)" : "";

                sb.Append($"    [{col.Name}] {col.Type.ToUpper()}{lengthStr}{identityStr} {nullStr}{defaultStr}");
                
                if (i < table.Columns.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            if (pkColumns.Any())
            {
                sb.AppendLine($"    , CONSTRAINT [PK_{table.Name}] PRIMARY KEY CLUSTERED ({string.Join(", ", pkColumns.Select(c => $"[{c.Name}]"))})");
            }

            sb.AppendLine(");");
            sb.AppendLine();
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

                sb.AppendLine($"ALTER TABLE [{sourceTable.Name}] WITH CHECK ADD CONSTRAINT [FK_{sourceTable.Name}_{targetTable.Name}_{sourceCol.Name}] FOREIGN KEY([{sourceCol.Name}])");
                sb.AppendLine($"REFERENCES [{targetTable.Name}] ([{targetCol.Name}])");
                sb.AppendLine("ON DELETE CASCADE;");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
