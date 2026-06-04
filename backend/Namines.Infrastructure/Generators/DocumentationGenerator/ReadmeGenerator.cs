using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DocumentationGenerator;

public class ReadmeGenerator
{
    public string Generate(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        var schemaName = schema.Name ?? "Database Schema";

        sb.AppendLine($"# 🗄️ {schemaName} - Database Schema Documentation");
        sb.AppendLine();
        sb.AppendLine("> This complete documentation was generated automatically by **Namines**.");
        sb.AppendLine("> It details the schema tables, structures, relationships, and deployment procedures.");
        sb.AppendLine();

        sb.AppendLine("## 📊 Database Overview & Statistics");
        sb.AppendLine("A high-level overview of the database assets configured in this schema design:");
        sb.AppendLine();
        sb.AppendLine("| Asset Type | Count | Description |");
        sb.AppendLine("| :--- | :---: | :--- |");
        sb.AppendLine($"| 📋 **Tables** | `{schema.Tables.Count}` | Number of entity tables |");
        sb.AppendLine($"| 🔗 **Relationships** | `{schema.Relations.Count}` | Total foreign key constraints |");
        sb.AppendLine($"| 🔠 **Columns** | `{schema.Tables.Sum(t => t.Columns.Count)}` | Total structural attribute columns |");
        sb.AppendLine();

        sb.AppendLine("## 🧬 Entity-Relationship Diagram (Mermaid)");
        sb.AppendLine("Below is the visual database mapping rendered directly as a Mermaid diagram. Compatible Markdown readers (e.g. GitHub/GitLab) will render the diagram interactively.");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.Append(new MermaidErGenerator().Generate(schema));
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## 📁 Data Dictionary");
        sb.AppendLine("Comprehensive structural layout for each database entity:");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"### 📋 Table: `{table.Name}`");
            sb.AppendLine();
            sb.AppendLine("| Column Name | Type | Nullable | Key | Default Value | Description / Constraint |");
            sb.AppendLine("| :--- | :--- | :---: | :---: | :---: | :--- |");
            foreach (var col in table.Columns)
            {
                var typeStr = $"{col.Type.ToUpper()}{(col.Length.HasValue ? $"({col.Length})" : "")}";
                var nullStr = col.IsNullable ? "✅ YES" : "❌ NO";
                var keyStr = col.IsPK ? "🔑 PK" : (col.IsFK ? "🔗 FK" : "-");
                var defStr = col.DefaultValue ?? "NULL";
                
                string desc = "-";
                if (col.IsPK) desc = "Primary key index identifier";
                else if (col.IsFK) desc = "Foreign key relationship index";

                sb.AppendLine($"| **{col.Name}** | `{typeStr}` | {nullStr} | {keyStr} | `{defStr}` | {desc} |");
            }
            sb.AppendLine();
        }

        if (schema.Relations != null && schema.Relations.Count > 0)
        {
            sb.AppendLine("## 🔗 Relationship Mappings");
            sb.AppendLine("Direct binding rules between foreign key and primary key attributes:");
            sb.AppendLine();
            sb.AppendLine("| Source Entity (FK Table) | Target Entity (PK Table) | Connection Cardinality |");
            sb.AppendLine("| :--- | :--- | :--- |");
            foreach (var rel in schema.Relations)
            {
                var sourceTable = schema.Tables.FirstOrDefault(t => t.Id == rel.SourceTableId);
                var targetTable = schema.Tables.FirstOrDefault(t => t.Id == rel.TargetTableId);
                if (sourceTable != null && targetTable != null)
                {
                    sb.AppendLine($"| `{sourceTable.Name}` | `{targetTable.Name}` | **{rel.Type}** |");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("## 🚀 Quick Start & Integration Guide");
        sb.AppendLine();
        sb.AppendLine("### 1. Database Creation (SQL DDL)");
        sb.AppendLine("1. Extract the raw SQL from the `DDL Script` tab.");
        sb.AppendLine("2. Configure connection parameters inside your target DBMS.");
        sb.AppendLine("3. Run the complete SQL query to initialize the schema structure.");
        sb.AppendLine();
        sb.AppendLine("### 2. Entity Framework Core Setup");
        sb.AppendLine("1. Download the EF Core `.zip` archive containing fully configured entity models.");
        sb.AppendLine("2. Extract the model classes (`.cs` files) into your Application Data Layer Namespace.");
        sb.AppendLine("3. Add `DbContext` dependency injection configurations inside your C# `Program.cs` startup pipeline.");
        sb.AppendLine();
        sb.AppendLine("### 3. Containerized Test Sandboxes");
        sb.AppendLine("- Open the `Docker Sandbox` tab in the Compile workspace to launch a container instance populated with mock seed data.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Documentation generated by [Namines](https://namines.com) - The Smart Database Design Suite.*");

        return sb.ToString();
    }
}
