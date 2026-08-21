using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.Eject;

/// <summary>
/// Bir tablonun panelde nasıl görüneceği (new-phase/07-CONSOLE-ADMIN-UI.md §3.2).
/// </summary>
internal enum PagePattern
{
    /// <summary>Varsayılan liste + form.</summary>
    Crud,

    /// <summary>Yalnızca iki yabancı anahtardan oluşan bileşik anahtar — ayrı sayfa değil, ilişki editörü.</summary>
    Junction,

    /// <summary>Kendine referans veren FK — ağaç görünümü.</summary>
    Tree,

    /// <summary>Birincil anahtarı olmayan tablo; güvenle düzenlenemez.</summary>
    ReadOnly,
}

/// <param name="LabelColumn">
/// Satırı insan diline çeviren kolon. Yabancı anahtar gösterirken ham id yerine
/// bu gösterilir — "42" yerine "ali@example.com".
/// </param>
internal sealed record TableMetadata(
    SchemaTable Table,
    PagePattern Pattern,
    SchemaColumn? PrimaryKey,
    SchemaColumn? LabelColumn);

/// <summary>
/// Şemadan panel meta verisi çıkarır (07 §3.1, §3.2).
///
/// Desen seçimi OTOMATİK — "sıfır konfigürasyonda anlamlı bir panel" vaadi buradan
/// geliyor. Kullanıcıya "bu tablo nasıl görünsün?" diye sormak, panelin değerini
/// yok eder: zaten elle yapacaksa panele ihtiyacı yok.
/// </summary>
internal static class ConsoleMetadata
{
    public static IReadOnlyList<TableMetadata> Describe(DatabaseSchema schema)
    {
        var relations = EjectNaming.Relations(schema).ToList();

        return schema.Tables.Select(table =>
        {
            var pks = EjectNaming.PrimaryKeys(table);
            var foreignKeys = relations.Where(r => r.From.Id == table.Id).ToList();

            var pattern = ChoosePattern(table, pks, foreignKeys);

            return new TableMetadata(
                table,
                pattern,
                pks.Count == 1 ? pks[0] : null,
                ChooseLabelColumn(table, pks));
        }).ToList();
    }

    private static PagePattern ChoosePattern(
        SchemaTable table,
        IReadOnlyList<SchemaColumn> primaryKeys,
        IReadOnlyList<(SchemaTable From, SchemaColumn FromColumn, SchemaTable To, SchemaColumn ToColumn, SchemaRelation Relation)> foreignKeys)
    {
        // Birincil anahtarsız tablo düzenlenemez: hangi satırın güncelleneceğini
        // güvenle söyleyemeyiz ve Gateway zaten anahtarsız yazmayı reddediyor.
        if (primaryKeys.Count == 0) return PagePattern.ReadOnly;

        // Ara tablo: bileşik anahtarın tamamı yabancı anahtar. Kendi sayfası olmaz,
        // iki tarafın ilişki editöründe görünür.
        if (primaryKeys.Count == 2 &&
            primaryKeys.All(pk => foreignKeys.Any(fk => fk.FromColumn.Id == pk.Id)))
            return PagePattern.Junction;

        // Kendine referans veren FK → hiyerarşi.
        if (foreignKeys.Any(fk => fk.To.Id == table.Id)) return PagePattern.Tree;

        return PagePattern.Crud;
    }

    /// <summary>
    /// Satırı temsil edecek kolonu seçer.
    ///
    /// Sıra bilinçli: "name"/"title" gibi bilinen adlar önce, sonra ilk kısa metin
    /// kolonu. Uzun metinler (açıklama, not) etiket olarak kullanılmaz — listede
    /// satırı okunamaz hâle getirirler.
    /// </summary>
    private static SchemaColumn? ChooseLabelColumn(SchemaTable table, IReadOnlyList<SchemaColumn> primaryKeys)
    {
        string[] preferred = { "name", "title", "label", "email", "username", "code", "slug" };

        foreach (var candidate in preferred)
        {
            var match = table.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return table.Columns.FirstOrDefault(c =>
            !c.IsPK &&
            CanonicalType.Classify(c.Type) == TypeKind.Text &&
            c.Length is > 0 and <= 80)
            ?? primaryKeys.FirstOrDefault();
    }

    /// <summary>
    /// Kolon → form widget'ı (07 §3.1'in uygulanabilir alt kümesi).
    ///
    /// Dokümandaki tablo <c>@ui(money)</c>, <c>@ui(markdown)</c>, <c>@tag(pii)</c>
    /// gibi NSL etiketlerine dayanıyor; NSL henüz yok, dolayısıyla yalnızca
    /// TİPTEN çıkarılabilen eşlemeler üretiliyor. Etiket temelli olanları uydurmak,
    /// kullanıcının hiç yazmadığı bir niyeti varsaymak olurdu.
    /// </summary>
    public static string Widget(SchemaColumn column) => CanonicalType.Classify(column.Type) switch
    {
        TypeKind.Boolean => "checkbox",
        TypeKind.Integer or TypeKind.Long or TypeKind.Decimal or TypeKind.Double => "number",
        TypeKind.Date => "date",
        TypeKind.Time => "time",
        TypeKind.DateTime => "datetime-local",
        TypeKind.Json => "textarea",
        // 80 karakterin üstü tek satıra sığmaz; textarea okunabilirliği korur.
        TypeKind.Text when column.Length is null or > 80 => "textarea",
        _ => "text",
    };
}
