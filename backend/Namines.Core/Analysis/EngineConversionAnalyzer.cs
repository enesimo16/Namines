using System.Collections.Generic;
using System.Linq;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// Bir kaydı bir seçenek olarak taşıyan, dönüşüm kararı.
/// <see cref="DataLossRisk"/> true ise seçim bir davranış/anlam kaybına yol açar —
/// arayüz bunu gizlemeden göstermeli.
/// </summary>
public sealed record ConversionOption(string Key, string Label, bool DataLossRisk);

public enum ConversionCategory { Array, Collation, GeneratedPrimaryKey }

/// <summary>
/// Şemadaki tek bir motor-uyumsuzluğu noktası ve onu çözmek için kullanıcıya
/// sunulan seçenekler. <see cref="Id"/> stabildir (tablo+kolon+kategori) —
/// istemci kullanıcının seçimini bu id'ye karşı saklar, <c>SchemaConverter</c>
/// buradan okur.
/// </summary>
public sealed record ConversionFinding(
    string Id,
    ConversionCategory Category,
    string TableId,
    string TableName,
    string ColumnId,
    string ColumnName,
    string Description,
    IReadOnlyList<ConversionOption> Options);

public sealed record EngineConversionReport(
    DatabaseType Source,
    DatabaseType Target,
    IReadOnlyList<ConversionFinding> Findings)
{
    public bool HasFindings => Findings.Count > 0;
}

/// <summary>
/// second-phase/07-MOTOR-DONUSUMU.md — iki motor arasında kayıp noktalarını
/// belirler.
///
/// <b>Tahmin değil, ölçüm.</b> Buradaki her koşul, DDL üreticilerinin (bkz.
/// <c>ColumnFeatureSql</c>, <c>EnumSql</c>) gerçek motora karşı test edilirken
/// <c>NotSupportedException</c> fırlattığı yerlerle bire bir eşleşiyor — ayrı
/// bir "yetenek matrisi" icat edip zamanla üreticiden sapmasına izin vermek
/// yerine, aynı koşulu burada tekrarlıyoruz. Enum kasıtlı olarak burada YOK:
/// <see cref="Namines.Infrastructure"/> tarafındaki üretici onu zaten motor
/// desteklemese bile CHECK kısıtına çevirip kayıpsız çözüyor (bkz. EnumSql),
/// yani kullanıcıya sorulacak bir karar değil.
/// </summary>
public static class EngineConversionAnalyzer
{
    public static EngineConversionReport Analyze(DatabaseSchema schema, DatabaseType source, DatabaseType target)
    {
        var findings = new List<ConversionFinding>();

        foreach (var table in schema.Tables)
        {
            foreach (var column in table.Columns)
            {
                if (column.IsArray && target != DatabaseType.PostgreSQL)
                    findings.Add(ArrayFinding(table, column));

                if (!string.IsNullOrWhiteSpace(column.Collation))
                {
                    var finding = CollationFinding(table, column, target);
                    if (finding is not null) findings.Add(finding);
                }

                if (!string.IsNullOrWhiteSpace(column.Generated) && column.IsPK && target == DatabaseType.SQLite)
                    findings.Add(GeneratedPrimaryKeyFinding(table, column));
            }
        }

        return new EngineConversionReport(source, target, findings);
    }

    private static ConversionFinding ArrayFinding(SchemaTable table, SchemaColumn column) => new(
        Id: $"{table.Id}.{column.Id}.array",
        Category: ConversionCategory.Array,
        TableId: table.Id,
        TableName: table.Name,
        ColumnId: column.Id,
        ColumnName: column.Name,
        Description: $"'{table.Name}.{column.Name}' is an array (PostgreSQL '{column.Type}[]'). " +
                     "The target engine has no array type.",
        Options: new[]
        {
            new ConversionOption("child_table", "Move it into a child table (relational, and still queryable)", DataLossRisk: false),
            new ConversionOption("json_text", "Store it as JSON in a text column (you lose the ability to query it)", DataLossRisk: true),
            new ConversionOption("manual", "I will handle it myself (the schema stays as it is)", DataLossRisk: false),
        });

    /// <summary>
    /// <c>ColumnFeatureSql.Collate</c> ile aynı iki koşul: Oracle'da collation
    /// hiç yazılmıyor; MSSQL/MySQL/MariaDB çıplak tanımlayıcı bekliyor ve
    /// harf/rakam/alt çizgi dışı bir karakter (ör. PostgreSQL'in
    /// <c>tr-TR-x-icu</c>'sundaki tireler) orada sözdizimi hatası verir.
    /// </summary>
    private static ConversionFinding? CollationFinding(SchemaTable table, SchemaColumn column, DatabaseType target)
    {
        var name = column.Collation!.Trim();

        if (target == DatabaseType.Oracle)
            return CollationFindingFor(table, column,
                $"'{table.Name}.{column.Name}' specifies a collation ('{name}'). The Oracle generator never emits collations.",
                allowMap: false);

        if (target is DatabaseType.PostgreSQL or DatabaseType.SQLite)
            return null; // İkisi de tırnaklı ad kabul eder, adı olduğu gibi taşır.

        var hasIllegalChar = name.Any(c => !char.IsLetterOrDigit(c) && c != '_');
        if (!hasIllegalChar) return null; // Zaten çıplak bir tanımlayıcı, sorun yok.

        return CollationFindingFor(table, column,
            $"'{table.Name}.{column.Name}' has a collation ('{name}') that is not valid for {target} " +
            "— that engine expects a bare identifier after COLLATE.",
            allowMap: true);
    }

    private static ConversionFinding CollationFindingFor(SchemaTable table, SchemaColumn column, string description, bool allowMap)
    {
        var options = new List<ConversionOption>();
        if (allowMap)
            options.Add(new ConversionOption("map", "Map it to the closest known equivalent (approximate — review the result)", DataLossRisk: true));
        options.Add(new ConversionOption("drop", "Drop the collation and use the engine default (sorting and comparison may change)", DataLossRisk: true));
        options.Add(new ConversionOption("manual", "I will handle it myself (the schema stays as it is)", DataLossRisk: false));

        return new ConversionFinding(
            Id: $"{table.Id}.{column.Id}.collation",
            Category: ConversionCategory.Collation,
            TableId: table.Id,
            TableName: table.Name,
            ColumnId: column.Id,
            ColumnName: column.Name,
            Description: description,
            Options: options);
    }

    private static ConversionFinding GeneratedPrimaryKeyFinding(SchemaTable table, SchemaColumn column) => new(
        Id: $"{table.Id}.{column.Id}.generated-pk",
        Category: ConversionCategory.GeneratedPrimaryKey,
        TableId: table.Id,
        TableName: table.Name,
        ColumnId: column.Id,
        ColumnName: column.Name,
        Description: $"'{table.Name}.{column.Name}' is both computed and a primary key. SQLite will not accept both.",
        Options: new[]
        {
            new ConversionOption("plain_column", "Drop the computed expression and make it an ordinary column (your app writes the value)", DataLossRisk: true),
            new ConversionOption("manual", "I will handle it myself (I'll add a separate key column)", DataLossRisk: false),
        });
}
