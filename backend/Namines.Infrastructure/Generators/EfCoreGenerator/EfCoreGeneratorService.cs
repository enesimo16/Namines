using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.EfCoreGenerator;

public class EfCoreGeneratorService : IEfCoreGenerator
{
    public Dictionary<string, string> Generate(DatabaseSchema schema)
    {
        var files = new Dictionary<string, string>();
        
        // 1. Generate Models
        foreach (var table in schema.Tables)
        {
            files.Add($"{table.Name}.cs", GenerateModelClass(table, schema));
        }

        // 2. Generate DbContext
        files.Add("AppDbContext.cs", GenerateDbContext(schema));

        return files;
    }

    private static string Pluralize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        if (word.EndsWith("y", StringComparison.OrdinalIgnoreCase))
        {
            bool isUpper = char.IsUpper(word[word.Length - 1]);
            string suffix = isUpper ? "IES" : "ies";
            return word.Substring(0, word.Length - 1) + suffix;
        }
        
        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("z", StringComparison.OrdinalIgnoreCase))
        {
            bool isUpper = char.IsUpper(word[word.Length - 1]);
            string suffix = isUpper ? "ES" : "es";
            return word + suffix;
        }

        bool isLastUpper = char.IsUpper(word[word.Length - 1]);
        string standardSuffix = isLastUpper ? "S" : "s";
        return word + standardSuffix;
    }

    private string GenerateModelClass(SchemaTable table, DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.ComponentModel.DataAnnotations;");
        sb.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
        sb.AppendLine();
        sb.AppendLine("namespace Namines.Generated;");
        sb.AppendLine();
        sb.AppendLine($"public class {table.Name}");
        sb.AppendLine("{");

        foreach (var col in table.Columns)
        {
            if (col.IsPK) sb.AppendLine("    [Key]");
            if (!col.IsNullable && !col.IsPK) sb.AppendLine("    [Required]");
            if (col.Length.HasValue && col.Type.ToUpperInvariant().Contains("CHAR")) 
                sb.AppendLine($"    [MaxLength({col.Length})]");
            
            var csharpType = GetCSharpType(col.Type, col.IsNullable);
            sb.AppendLine($"    public {csharpType} {col.Name} {{ get; set; }}");
        }

        // Navigation properties
        if (schema.Relations != null)
        {
            var incomingRels = schema.Relations.Where(r => r.TargetTableId == table.Id).ToList();
            var outgoingRels = schema.Relations.Where(r => r.SourceTableId == table.Id).ToList();

            foreach (var rel in incomingRels)
            {
                var sourceTable = schema.Tables.FirstOrDefault(t => t.Id == rel.SourceTableId);
                if (sourceTable != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"    public virtual ICollection<{sourceTable.Name}> {Pluralize(sourceTable.Name)} {{ get; set; }} = new List<{sourceTable.Name}>();");
                }
            }

            foreach (var rel in outgoingRels)
            {
                var targetTable = schema.Tables.FirstOrDefault(t => t.Id == rel.TargetTableId);
                var sourceCol = table.Columns.FirstOrDefault(c => c.Id == rel.SourceColumnId);
                if (targetTable != null && sourceCol != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"    [ForeignKey(\"{sourceCol.Name}\")]");
                    sb.AppendLine($"    public virtual {targetTable.Name} {targetTable.Name} {{ get; set; }}");
                }
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private string GenerateDbContext(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine();
        sb.AppendLine("namespace Namines.Generated;");
        sb.AppendLine();
        sb.AppendLine("public class AppDbContext : DbContext");
        sb.AppendLine("{");
        sb.AppendLine("    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"    public DbSet<{table.Name}> {Pluralize(table.Name)} {{ get; set; }}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private string GetCSharpType(string dbType, bool isNullable)
    {
        var type = dbType.ToUpperInvariant();
        string csharpType = "string";

        if (type.Contains("INT")) csharpType = "int";
        else if (type.Contains("BIGINT")) csharpType = "long";
        else if (type.Contains("SMALLINT") || type.Contains("TINYINT")) csharpType = "short";
        else if (type.Contains("BIT") || type.Contains("BOOL")) csharpType = "bool";
        else if (type.Contains("DECIMAL") || type.Contains("NUMERIC")) csharpType = "decimal";
        else if (type.Contains("FLOAT") || type.Contains("REAL")) csharpType = "double";
        else if (type.Contains("DATE") || type.Contains("TIME")) csharpType = "DateTime";
        else if (type.Contains("UNIQUEIDENTIFIER") || type.Contains("UUID")) csharpType = "Guid";

        if (isNullable && csharpType != "string") csharpType += "?";
        return csharpType;
    }
}
