using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DocumentationGenerator;

public class MermaidErGenerator
{
    private string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "unknown";
        return System.Text.RegularExpressions.Regex.Replace(input, @"[\s\""\'\(\)]", "_");
    }

    public string Generate(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");

        foreach (var table in schema.Tables)
        {
            var tableName = Sanitize(table.Name);
            sb.AppendLine($"    {tableName} {{");
            foreach (var col in table.Columns)
            {
                var keyStr = col.IsPK ? " PK" : (col.IsFK ? " FK" : "");
                var rawType = $"{col.Type}{(col.Length.HasValue ? $"({col.Length})" : "")}";
                var typeStr = Sanitize(rawType);
                var nameStr = Sanitize(col.Name);
                sb.AppendLine($"        {typeStr} {nameStr}{keyStr}");
            }
            sb.AppendLine("    }");
        }

        if (schema.Relations != null)
        {
            foreach (var rel in schema.Relations)
            {
                var sourceTable = Sanitize(schema.Tables.FirstOrDefault(t => t.Id == rel.SourceTableId)?.Name);
                var targetTable = Sanitize(schema.Tables.FirstOrDefault(t => t.Id == rel.TargetTableId)?.Name);

                if (sourceTable != "unknown" && targetTable != "unknown")
                {
                    string relationSymbol = rel.Type.ToLower() switch
                    {
                        "onetoone" => "||--||",
                        "onetomany" => "||--o{",
                        "manytomany" => "}o--o{",
                        _ => "||--o{"
                    };

                    sb.AppendLine($"    {targetTable} {relationSymbol} {sourceTable} : \"FK\"");
                }
            }
        }

        return sb.ToString();
    }
}
