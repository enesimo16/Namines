using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DocumentationGenerator;

public class ReadmeGenerator
{
    public string Generate(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {schema.Name ?? "Veritabanı Şeması"}");
        sb.AppendLine();
        sb.AppendLine("Bu doküman Namines AI tarafından otomatik olarak oluşturulmuştur.");
        sb.AppendLine();
        sb.AppendLine("## İstatistikler");
        sb.AppendLine($"- **Tablo Sayısı**: {schema.Tables.Count}");
        sb.AppendLine($"- **İlişki Sayısı**: {schema.Relations.Count}");
        sb.AppendLine();
        sb.AppendLine("## Tablolar");
        
        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"### `{table.Name}`");
            sb.AppendLine("| Kolon | Tip | Boş Olabilir | Özellikler |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");
            foreach (var col in table.Columns)
            {
                var nullStr = col.IsNullable ? "Evet" : "Hayır";
                var typeStr = $"{col.Type}{(col.Length.HasValue ? $"({col.Length})" : "")}";
                var props = col.IsPK ? "PK" : (col.IsFK ? "FK" : "");
                sb.AppendLine($"| {col.Name} | {typeStr} | {nullStr} | {props} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## İlişkiler");
        foreach (var rel in schema.Relations)
        {
            var sourceTable = schema.Tables.FirstOrDefault(t => t.Id == rel.SourceTableId);
            var targetTable = schema.Tables.FirstOrDefault(t => t.Id == rel.TargetTableId);
            if (sourceTable != null && targetTable != null)
            {
                sb.AppendLine($"- `{sourceTable.Name}` -> `{targetTable.Name}` ({rel.Type})");
            }
        }

        return sb.ToString();
    }
}
