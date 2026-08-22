using System.Text;
using Namines.Core.Analysis;
using Namines.Core.Enums;
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
                // Enum'a bağlı kolon kendi tipini enum'dan alır; motorun karşılığı
                // yoksa metin tipine + CHECK'e düşer (bkz. EnumSql).
                var sqlType = EnumSql.ColumnType(col, schema, DatabaseType.MSSQL)
                              ?? TypeSql.Map(col.Type, col.Length, DatabaseType.MSSQL);
                var nullStr = col.IsNullable ? "NULL" : "NOT NULL";
                var defaultValue = DefaultValueSql.Translate(col.DefaultValue, DatabaseType.MSSQL);
                var defaultStr = !string.IsNullOrWhiteSpace(defaultValue) ? $" DEFAULT {defaultValue}" : "";

                // IDENTITY yalnızca TEK KOLONLU PK'da uygulanır. Bileşik PK'nın her iki
                // kolonu da INT ise, ikisine birden IDENTITY vermek SQL Server'ın
                // "Multiple identity columns specified" (Msg 2744) hatasına yol açar —
                // gerçek SQL Server'a karşı çalıştırılan bir entegrasyon testi bunu kanıtladı.
                // Karar IdentityPolicy'de: aynı kural altı motorda ayrı ayrı
                // yazılıydı ve kullanıcının "bu anahtarı ben atıyorum" deme yolu yoktu.
                var identityStr = IdentityPolicy.IsGenerated(col, pkColumns.Count)
                    ? " IDENTITY(1,1)" : "";

                // Hesaplanan kolon tipini ifadeden alır ve NOT NULL/DEFAULT ile
                // birleşmez; o yüzden satırın tamamı ayrı kuruluyor.
                var generatedDef = ColumnFeatureSql.Generated(col, DatabaseType.MSSQL);
                var collate = ColumnFeatureSql.Collate(col, DatabaseType.MSSQL);
                sqlType = ColumnFeatureSql.ApplyArray(sqlType, col, DatabaseType.MSSQL);

                sb.Append(generatedDef is not null
                    ? $"    [{col.Name}] {(ColumnFeatureSql.TypePrecedesGenerated(DatabaseType.MSSQL) ? sqlType + " " : string.Empty)}{generatedDef}"
                    : $"    [{col.Name}] {sqlType}{identityStr}{collate} {nullStr}{defaultStr}");
                
                if (i < table.Columns.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            if (pkColumns.Any())
            {
                sb.AppendLine($"    , CONSTRAINT [PK_{table.Name}] PRIMARY KEY CLUSTERED ({string.Join(", ", pkColumns.Select(c => $"[{c.Name}]"))})");
            }

            foreach (var constraint in ConstraintSql.InlineConstraints(table, DatabaseType.MSSQL, Quote, schema))
                sb.AppendLine($"    , {constraint.TrimStart()}");

            sb.AppendLine(");");
            sb.AppendLine();

            var indexes = ConstraintSql.CreateIndexes(table, DatabaseType.MSSQL, Quote);
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

                var actions = ReferentialActionSql.Clauses(relation.OnDelete, relation.OnUpdate, DatabaseType.MSSQL);

                sb.AppendLine($"ALTER TABLE [{sourceTable.Name}] WITH CHECK ADD CONSTRAINT [FK_{sourceTable.Name}_{targetTable.Name}_{sourceCol.Name}] FOREIGN KEY([{sourceCol.Name}])");
                sb.AppendLine($"REFERENCES [{targetTable.Name}] ([{targetCol.Name}]){actions};");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string Quote(string identifier) => $"[{identifier}]";
}
