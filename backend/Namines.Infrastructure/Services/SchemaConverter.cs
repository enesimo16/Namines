using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Infrastructure.Services;

/// <summary>
/// second-phase/07-MOTOR-DONUSUMU.md — <see cref="EngineConversionAnalyzer"/>'ın
/// bulduğu noktaları, kullanıcının seçtiği çözümle şemaya uygular.
///
/// <b>Yalnızca şema.</b> Veri taşımaz, hiçbir dış sisteme bağlanmaz — girdi ve
/// çıktı ikisi de <see cref="DatabaseSchema"/>. Asıl DDL, çağıran tarafından
/// döndürülen şema <see cref="IDdlGeneratorFactory"/>'ye verilerek üretilir.
///
/// <b>Karşılanmayan/"manual" seçilen bulgular şemayı DEĞİŞTİRMEZ.</b> Bu
/// kasıtlı: o kolon için hedef motorun DDL üretici hâlâ hata verecek — bu bir
/// kusur değil, kullanıcı "elle çözeceğim" dediğinde motorun kendisinin
/// kontrol etmesi gerekiyor.
/// </summary>
public static class SchemaConverter
{
    /// <summary><paramref name="resolutions"/>: bulgu id'si → seçilen seçenek key'i.</summary>
    public static DatabaseSchema Apply(
        DatabaseSchema schema,
        DatabaseType target,
        IReadOnlyList<ConversionFinding> findings,
        IReadOnlyDictionary<string, string> resolutions)
    {
        var converted = Clone(schema);

        foreach (var finding in findings)
        {
            if (!resolutions.TryGetValue(finding.Id, out var chosen)) continue;

            var table = converted.Tables.FirstOrDefault(t => t.Id == finding.TableId);
            var column = table?.Columns.FirstOrDefault(c => c.Id == finding.ColumnId);
            if (table is null || column is null) continue;

            switch (finding.Category)
            {
                case ConversionCategory.Array:
                    ApplyArrayResolution(converted, table, column, chosen);
                    break;
                case ConversionCategory.Collation:
                    ApplyCollationResolution(column, chosen, target);
                    break;
                case ConversionCategory.GeneratedPrimaryKey:
                    ApplyGeneratedPkResolution(column, chosen);
                    break;
            }
        }

        return converted;
    }

    private static void ApplyArrayResolution(DatabaseSchema schema, SchemaTable table, SchemaColumn column, string chosen)
    {
        switch (chosen)
        {
            case "json_text":
                column.IsArray = false;
                column.Type = "TEXT";
                column.Length = null;
                break;

            case "child_table":
                // Alt tablo, ebeveyne bir yabancı anahtarla bağlanacak — bunun
                // için ebeveynin bir birincil anahtarı OLMAK ZORUNDA. Eskiden
                // burada doğrudan First(c => c.IsPK) çağrılıyordu ve PK'siz bir
                // tabloda InvalidOperationException fırlatıp uca 500 döndürüyordu.
                // Şimdi çağıranın yakalayıp kullanıcıya anlatabileceği, ne
                // yapması gerektiğini söyleyen bir hata veriliyor.
                var parentKey = table.Columns.FirstOrDefault(c => c.IsPK);
                if (parentKey is null)
                    throw new NotSupportedException(
                        $"'{table.Name}' has no primary key, so '{column.Name}' cannot be moved into a child table " +
                        "(the child row would have nothing to point back to). Give the table a primary key first, " +
                        "or choose the JSON column option instead.");

                var elementType = column.Type;
                table.Columns.Remove(column);

                var childTable = new SchemaTable
                {
                    Id = $"{table.Id}_{column.Id}_items",
                    Name = $"{table.Name}_{column.Name}",
                };
                var pkCol = new SchemaColumn { Id = "id", Name = "id", Type = "INT", IsPK = true, Identity = true };
                var fkCol = new SchemaColumn
                {
                    Id = $"{table.Name.ToLowerInvariant()}_id",
                    Name = $"{table.Name}Id",
                    Type = "INT",
                    IsFK = true,
                    IsNullable = false,
                };
                var valueCol = new SchemaColumn { Id = "value", Name = "value", Type = elementType, IsNullable = false };
                childTable.Columns.Add(pkCol);
                childTable.Columns.Add(fkCol);
                childTable.Columns.Add(valueCol);
                schema.Tables.Add(childTable);

                // Source = FK kolonunu TAŞIYAN taraf (alt tablo), Target = referans
                // verilen taraf (ebeveyn) — bkz. MssqlDdlGenerator FK üretimi:
                // "ALTER TABLE [sourceTable] ... FOREIGN KEY([sourceCol]) REFERENCES [targetTable]".
                schema.Relations.Add(new SchemaRelation
                {
                    Id = $"{childTable.Id}_fk",
                    Type = "OneToMany",
                    SourceTableId = childTable.Id,
                    SourceColumnId = fkCol.Id,
                    TargetTableId = table.Id,
                    TargetColumnId = parentKey.Id,
                    OnDelete = ReferentialAction.Cascade,
                });
                break;

            case "manual":
            default:
                break;
        }
    }

    private static void ApplyCollationResolution(SchemaColumn column, string chosen, DatabaseType target)
    {
        switch (chosen)
        {
            case "drop":
                column.Collation = null;
                break;

            case "map":
                column.Collation = CollationMap.BestEffort(column.Collation, target) ?? column.Collation;
                break;

            case "manual":
            default:
                break;
        }
    }

    private static void ApplyGeneratedPkResolution(SchemaColumn column, string chosen)
    {
        if (chosen == "plain_column")
            column.Generated = null;
    }

    private static DatabaseSchema Clone(DatabaseSchema schema)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(schema);
        return System.Text.Json.JsonSerializer.Deserialize<DatabaseSchema>(json)
               ?? throw new InvalidOperationException("Schema could not be cloned.");
    }
}

/// <summary>
/// Birkaç yaygın yerel adın hedef motordaki en yakın çıplak karşılığı.
/// <b>Kesin değil, yaklaşık</b> — bu yüzden çağıran taraf her zaman
/// <c>DataLossRisk: true</c> işaretliyor; kullanıcı doğrulamalı.
/// </summary>
internal static class CollationMap
{
    public static string? BestEffort(string? source, DatabaseType target)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var normalized = source.ToLowerInvariant();
        var isTurkish = normalized.Contains("tr") || normalized.Contains("turkish");

        return (target, isTurkish) switch
        {
            (DatabaseType.MSSQL, true) => "Turkish_CI_AS",
            (DatabaseType.MSSQL, false) => "SQL_Latin1_General_CP1_CI_AS",
            (DatabaseType.MySQL, true) or (DatabaseType.MariaDB, true) => "utf8mb4_turkish_ci",
            (DatabaseType.MySQL, false) or (DatabaseType.MariaDB, false) => "utf8mb4_general_ci",
            _ => null,
        };
    }
}
