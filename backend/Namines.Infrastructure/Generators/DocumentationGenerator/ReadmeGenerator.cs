using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DocumentationGenerator;

public class ReadmeGenerator
{
    /// <param name="language">
    /// Çağıran taraflarla uyum için korunuyor. Üretilen döküman artık tek dilde
    /// (İngilizce) — ürünün geri kalanıyla aynı dil.
    /// </param>
    public string Generate(DatabaseSchema schema, string language = "en")
    {
        var sb = new StringBuilder();
        var schemaName = schema.Name ?? "Database Schema";

        // 1. Title
        sb.AppendLine($"# 🗄️ {schemaName} — Database Schema Documentation");
        sb.AppendLine();

        sb.AppendLine("> This documentation was generated automatically by **Namines**.");
        sb.AppendLine("> It details the schema tables, structures, relationships, and deployment procedures.");
        sb.AppendLine();

        // 2. Statistics
        sb.AppendLine("## 📊 Database Overview & Statistics");
        sb.AppendLine("A high-level overview of the database assets configured in this schema design:");
        sb.AppendLine();

        sb.AppendLine("| Asset Type | Count | Description |");
        sb.AppendLine("| :--- | :---: | :--- |");

        sb.AppendLine($"| 📋 **Tables** | `{schema.Tables.Count}` | Number of entity tables |");
        sb.AppendLine($"| 🔗 **Relationships** | `{schema.Relations.Count}` | Total foreign key constraints |");
        sb.AppendLine($"| 🔠 **Columns** | `{schema.Tables.Sum(t => t.Columns.Count)}` | Total structural attribute columns |");
        sb.AppendLine();

        // 3. ER Diagram
        sb.AppendLine("## 🧬 Entity-Relationship Diagram (Mermaid)");
        sb.AppendLine("Below is the visual database mapping rendered as a Mermaid diagram. Compatible Markdown readers (e.g. GitHub/GitLab) will render the diagram interactively.");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.Append(new MermaidErGenerator().Generate(schema));
        sb.AppendLine("```");
        sb.AppendLine();

        // 4. Data Dictionary
        sb.AppendLine("## 📁 Data Dictionary");
        sb.AppendLine("Comprehensive structural layout for each database entity:");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"### 📋 Table: `{table.Name}`");
            sb.AppendLine();
            sb.AppendLine("| Column | Data Type | Nullable | Key | Default Value | Description / Constraint |");
            sb.AppendLine("| :--- | :--- | :---: | :---: | :---: | :--- |");

            foreach (var col in table.Columns)
            {
                var typeStr = $"{col.Type.ToUpperInvariant()}{(col.Length.HasValue ? $"({col.Length})" : "")}";
                var nullStr = col.IsNullable ? "✅ YES" : "❌ NO";
                var keyStr = col.IsPK ? "🔑 PK" : (col.IsFK ? "🔗 FK" : "-");
                var defStr = col.DefaultValue ?? "NULL";

                string desc = "-";
                if (col.IsPK) desc = "Primary key unique identifier";
                else if (col.IsFK) desc = "Foreign key relationship column";

                sb.AppendLine($"| **{col.Name}** | `{typeStr}` | {nullStr} | {keyStr} | `{defStr}` | {desc} |");
            }
            sb.AppendLine();
        }

        // 5. Relations
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

        // 6. Quick Start
        sb.AppendLine("## 🚀 Quick Start & Integration Guide");
        sb.AppendLine();

        sb.AppendLine("### 1. Database Creation (SQL DDL)");
        sb.AppendLine("1. Copy the raw SQL from the `DDL Script` tab.\n2. Run the query against your target database to create the tables.");
        sb.AppendLine();

        sb.AppendLine("### 2. Entity Framework Core Setup");
        sb.AppendLine("1. Download the C# model files from the `EF Core` tab.\n2. Place the model classes (`.cs` files) into your application's data layer.");
        sb.AppendLine();

        sb.AppendLine("### 3. Containerized Test Sandboxes");
        sb.AppendLine("- Open the `Docker Sandbox` tab to spin up your schema, together with mock seed data, inside an isolated Docker container.");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine("*Documentation generated by [Namines](https://namines.com) — the AI-powered database design suite.*");

        return sb.ToString();
    }
}
