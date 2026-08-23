using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// Enum tiplerinin motora çevrilmesi (04 §3 <c>enums</c>).
///
/// <b>Yalnızca iki motorun gerçek bir enum tipi var</b> (PostgreSQL'de
/// <c>CREATE TYPE ... AS ENUM</c>, MySQL/MariaDB'de kolon üstünde <c>ENUM(...)</c>).
/// Diğerlerinde karşılığı yok.
///
/// <b>Karşılığı olmayan motorda kısıt DÜŞÜRÜLMÜYOR, CHECK'e çevriliyor.</b>
/// Sessizce <c>varchar</c>'a düşmek, kullanıcının koruma sandığı şeyi yok
/// etmek olurdu: kolon her değeri kabul eder ve yanlış veri bir kez yazıldıktan
/// sonra temizlenmesi gereken bir borç hâline gelir.
/// <see cref="ReferentialActionSql"/>'deki ilkeyle aynı — bir motor isteneni
/// desteklemiyorsa <b>en kısıtlayıcı</b> karşılığa düşülür, en gevşeğine değil.
/// </summary>
internal static class EnumSql
{
    /// <summary>Motorun adlandırılmış, ayrı bir enum TİPİ var mı?</summary>
    public static bool HasNamedType(DatabaseType engine) => engine == DatabaseType.PostgreSQL;

    /// <summary>Motor kolon üstünde satır içi enum yazımını destekliyor mu?</summary>
    public static bool HasInlineEnum(DatabaseType engine) =>
        engine is DatabaseType.MySQL or DatabaseType.MariaDB;

    /// <summary>
    /// Tablolardan ÖNCE çalışması gereken tip tanımları.
    ///
    /// Sıra zorunlu: PostgreSQL'de bir tablo, henüz var olmayan bir tipe
    /// başvuramaz. Bu yüzden üreticiler bunu çıktının başına koyar.
    /// </summary>
    public static string TypeDefinitions(DatabaseSchema schema, DatabaseType engine)
    {
        if (!HasNamedType(engine) || schema.Enums.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var e in schema.Enums)
        {
            var values = string.Join(", ", e.Values.Select(Literal));
            sb.AppendLine($"CREATE TYPE \"{e.Name}\" AS ENUM ({values});");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Kolonun enum'a bağlı SQL tipi; kolon enum'a bağlı değilse <c>null</c>.
    /// </summary>
    public static string? ColumnType(SchemaColumn column, DatabaseSchema schema, DatabaseType engine)
    {
        var definition = Resolve(column, schema);
        if (definition is null) return null;

        if (HasNamedType(engine)) return Quote(engine, definition.Name);

        if (HasInlineEnum(engine))
            return $"ENUM({string.Join(", ", definition.Values.Select(Literal))})";

        // Karşılığı yok: değerleri tutabilecek bir metin tipi + ayrıca CHECK.
        // Uzunluk en uzun değere göre; sabit bir 255 seçmek, kısa bir enum için
        // gereksiz yer ayırmak ve uzun bir değeri sessizce kesmek olurdu.
        var length = Math.Max(1, definition.Values.Max(v => v.Length));

        return engine switch
        {
            DatabaseType.MSSQL => $"NVARCHAR({length})",
            DatabaseType.Oracle => $"VARCHAR2({length})",
            _ => "TEXT",
        };
    }

    /// <summary>
    /// Enum'ın CHECK karşılığı; motorun kendi enum'ı varsa <c>null</c>
    /// (tip zaten kısıtlıyor, ikinci bir kontrol gürültü olurdu).
    /// </summary>
    public static string? CheckConstraint(SchemaTable table, SchemaColumn column, DatabaseSchema schema, DatabaseType engine)
    {
        if (HasNamedType(engine) || HasInlineEnum(engine)) return null;

        var definition = Resolve(column, schema);
        if (definition is null) return null;

        var values = string.Join(", ", definition.Values.Select(Literal));

        // Nullable kolonda ayrıca "OR col IS NULL" YAZILMIYOR ve buna gerek yok:
        // SQL'de NULL bir CHECK'i ihlal etmez, çünkü karşılaştırma true değil
        // UNKNOWN döner. Fazladan yazmak, okuyanı "demek ki gerekiyormuş" diye
        // düşündürüp aynı kalıbı gereksiz yere çoğaltırdı.
        var quoted = Quote(engine, column.Name);
        return $"CONSTRAINT {Quote(engine, "CK_" + table.Name + "_" + column.Name)} CHECK ({quoted} IN ({values}))";
    }

    /// <summary>
    /// Kolonun başvurduğu enum; başvuru yoksa <c>null</c>.
    ///
    /// <b>Bulunamayan ad HATA veriyor.</b> Sessizce metne düşmek, kullanıcının
    /// yazdığı kısıtın hiç uygulanmaması ve bunu ancak yanlış veri girildiğinde
    /// fark etmesi demek olurdu.
    /// </summary>
    public static SchemaEnum? Resolve(SchemaColumn column, DatabaseSchema schema)
    {
        if (string.IsNullOrWhiteSpace(column.EnumRef)) return null;

        var match = schema.Enums.FirstOrDefault(
            e => string.Equals(e.Name, column.EnumRef, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            throw new NotSupportedException(
                $"Column '{column.Name}' refers to enum '{column.EnumRef}', which is not defined in this schema.");

        if (match.Values.Count == 0)
            throw new NotSupportedException(
                $"Enum '{match.Name}' has no values, so there is nothing a column could hold.");

        return match;
    }

    /// <summary>
    /// SQL dize sabiti. Tek tırnak ikilenir — bir enum değeri kesme işareti
    /// içeriyorsa (<c>can't_ship</c>) kaçırılmadığında DDL ayrıştırılamaz olur.
    /// </summary>
    private static string Literal(string value) => "'" + (value ?? string.Empty).Replace("'", "''") + "'";

    private static string Quote(DatabaseType engine, string identifier) => engine switch
    {
        DatabaseType.MSSQL => $"[{identifier}]",
        DatabaseType.MySQL or DatabaseType.MariaDB => $"`{identifier}`",
        _ => $"\"{identifier}\"",
    };
}
