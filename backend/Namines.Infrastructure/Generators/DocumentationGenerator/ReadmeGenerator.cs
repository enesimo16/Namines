using System;
using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DocumentationGenerator;

public class ReadmeGenerator
{
    public string Generate(DatabaseSchema schema, string language = "tr")
    {
        bool isEn = "en".Equals(language, StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        var schemaName = schema.Name ?? (isEn ? "Database Schema" : "Veritabanı Şeması");

        // 1. Title
        sb.AppendLine(isEn 
            ? $"# 🗄️ {schemaName} - Database Schema Documentation" 
            : $"# 🗄️ {schemaName} - Veritabanı Şema Dökümantasyonu");
        sb.AppendLine();
        
        sb.AppendLine(isEn
            ? "> This complete documentation was generated automatically by **Namines**."
            : "> Bu dökümantasyon **Namines** tarafından otomatik olarak oluşturulmuştur.");
        sb.AppendLine(isEn
            ? "> It details the schema tables, structures, relationships, and deployment procedures."
            : "> Şema tablolarını, ilişkilerini, veri tiplerini ve kurulum adımlarını detaylandırır.");
        sb.AppendLine();

        // 2. Statistics
        sb.AppendLine(isEn ? "## 📊 Database Overview & Statistics" : "## 📊 Veritabanı Genel Bakış & İstatistikler");
        sb.AppendLine(isEn 
            ? "A high-level overview of the database assets configured in this schema design:"
            : "Bu şema tasarımında yapılandırılmış veritabanı varlıklarının genel özeti:");
        sb.AppendLine();
        
        sb.AppendLine(isEn 
            ? "| Asset Type | Count | Description |" 
            : "| Varlık Tipi | Sayı | Açıklama |");
        sb.AppendLine("| :--- | :---: | :--- |");
        
        sb.AppendLine(isEn 
            ? $"| 📋 **Tables** | `{schema.Tables.Count}` | Number of entity tables |"
            : $"| 📋 **Tablolar** | `{schema.Tables.Count}` | Veritabanındaki toplam tablo sayısı |");
        sb.AppendLine(isEn 
            ? $"| 🔗 **Relationships** | `{schema.Relations.Count}` | Total foreign key constraints |"
            : $"| 🔗 **İlişkiler** | `{schema.Relations.Count}` | Toplam yabancı anahtar (FK) kısıtlamaları |");
        sb.AppendLine(isEn 
            ? $"| 🔠 **Columns** | `{schema.Tables.Sum(t => t.Columns.Count)}` | Total structural attribute columns |"
            : $"| 🔠 **Kolonlar** | `{schema.Tables.Sum(t => t.Columns.Count)}` | Tüm tablolardaki toplam sütun sayısı |");
        sb.AppendLine();

        // 3. ER Diagram
        sb.AppendLine(isEn ? "## 🧬 Entity-Relationship Diagram (Mermaid)" : "## 🧬 Varlık-İlişki Diyagramı (Mermaid)");
        sb.AppendLine(isEn
            ? "Below is the visual database mapping rendered directly as a Mermaid diagram. Compatible Markdown readers (e.g. GitHub/GitLab) will render the diagram interactively."
            : "Aşağıda, Mermaid formatında görsel veritabanı diyagramı yer almaktadır. Uyumlu Markdown okuyucuları (örneğin GitHub/GitLab) diyagramı etkileşimli olarak çizecektir.");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.Append(new MermaidErGenerator().Generate(schema));
        sb.AppendLine("```");
        sb.AppendLine();

        // 4. Data Dictionary
        sb.AppendLine(isEn ? "## 📁 Data Dictionary" : "## 📁 Veri Sözlüğü");
        sb.AppendLine(isEn 
            ? "Comprehensive structural layout for each database entity:"
            : "Her bir tablo için detaylı yapısal kolon şeması:");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"### 📋 {(isEn ? "Table" : "Tablo")}: `{table.Name}`");
            sb.AppendLine();
            sb.AppendLine(isEn 
                ? "| Column Name | Type | Nullable | Key | Default Value | Description / Constraint |"
                : "| Kolon Adı | Veri Tipi | Boş Olabilir | Kısıt | Varsayılan Değer | Açıklama / Kısıtlama |");
            sb.AppendLine("| :--- | :--- | :---: | :---: | :---: | :--- |");
            
            foreach (var col in table.Columns)
            {
                var typeStr = $"{col.Type.ToUpper()}{(col.Length.HasValue ? $"({col.Length})" : "")}";
                var nullStr = col.IsNullable 
                    ? (isEn ? "✅ YES" : "✅ EVET") 
                    : (isEn ? "❌ NO" : "❌ HAYIR");
                var keyStr = col.IsPK ? "🔑 PK" : (col.IsFK ? "🔗 FK" : "-");
                var defStr = col.DefaultValue ?? "NULL";
                
                string desc = "-";
                if (col.IsPK) desc = isEn ? "Primary key index identifier" : "Birincil anahtar benzersiz tanımlayıcı";
                else if (col.IsFK) desc = isEn ? "Foreign key relationship index" : "Yabancı anahtar ilişki kolonu";

                sb.AppendLine($"| **{col.Name}** | `{typeStr}` | {nullStr} | {keyStr} | `{defStr}` | {desc} |");
            }
            sb.AppendLine();
        }

        // 5. Relations
        if (schema.Relations != null && schema.Relations.Count > 0)
        {
            sb.AppendLine(isEn ? "## 🔗 Relationship Mappings" : "## 🔗 İlişki Haritası");
            sb.AppendLine(isEn 
                ? "Direct binding rules between foreign key and primary key attributes:"
                : "Yabancı anahtar ve birincil anahtar arasındaki doğrudan ilişki kuralları:");
            sb.AppendLine();
            sb.AppendLine(isEn 
                ? "| Source Entity (FK Table) | Target Entity (PK Table) | Connection Cardinality |"
                : "| Kaynak Tablo (FK) | Hedef Tablo (PK) | İlişki Tipi |");
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
        sb.AppendLine(isEn ? "## 🚀 Quick Start & Integration Guide" : "## 🚀 Hızlı Başlangıç & Entegrasyon Kılavuzu");
        sb.AppendLine();
        
        sb.AppendLine(isEn ? "### 1. Database Creation (SQL DDL)" : "### 1. Veritabanı Kurulumu (SQL DDL)");
        sb.AppendLine(isEn
            ? "1. Extract the raw SQL from the `DDL Script` tab.\n2. Run the complete SQL query to initialize the schema structure."
            : "1. `DDL Script` sekmesindeki ham SQL kodlarını kopyalayın.\n2. Hedef veritabanınızda bu sorguyu çalıştırarak tabloları oluşturun.");
        sb.AppendLine();
        
        sb.AppendLine(isEn ? "### 2. Entity Framework Core Setup" : "### 2. Entity Framework Core Entegrasyonu");
        sb.AppendLine(isEn
            ? "1. Download the EF Core `.zip` archive containing fully configured entity models.\n2. Extract the model classes (`.cs` files) into your Application Data Layer Namespace."
            : "1. `EF Core` sekmesindeki C# model dosyalarını indirin.\n2. Sınıfları (.cs dosyaları) projenizin veri katmanına yerleştirin.");
        sb.AppendLine();
        
        sb.AppendLine(isEn ? "### 3. Containerized Test Sandboxes" : "### 3. Docker Test Ortamı");
        sb.AppendLine(isEn
            ? "- Open the `Docker Sandbox` tab in the Compile workspace to launch a container instance populated with mock seed data."
            : "- `Docker Sandbox` sekmesini açarak, oluşturduğunuz şemayı test verileriyle birlikte izole bir Docker konteynerinde hızlıca ayağa kaldırabilirsiniz.");
        sb.AppendLine();
        
        sb.AppendLine("---");
        sb.AppendLine(isEn
            ? "*Documentation generated by [Namines](https://namines.com) - The Smart Database Design Suite.*"
            : "*Bu döküman [Namines](https://namines.com) - Yapay Zeka Destekli Veritabanı Tasarım Aracı tarafından oluşturulmuştur.*");

        return sb.ToString();
    }
}
