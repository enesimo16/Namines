using System;
using Namines.Core.Enums;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// Kanonik kolon tipini (frontend'in <c>TableEditorDrawer</c> açılır listesinde
/// sunduğu ve <see cref="Namines.Core.Enums.ColumnType"/>'ta tanımlı tipler) motora
/// özgü SQL tipine çevirir.
///
/// BULUNMA YERİ: Testcontainers ile PostgreSQL'e karşı çalıştırılan bir entegrasyon
/// testi "type nvarchar does not exist" hatası verdi. İncelemede şu ortaya çıktı:
/// MSSQL, PostgreSQL, MySQL ve MariaDB üreticilerinin DÖRDÜ DE hiçbir tip eşlemesi
/// yapmıyordu — <c>col.Type.ToUpper()</c> ile ham metni olduğu gibi yazıyorlardı.
///
/// Kullanıcı arayüzü NVARCHAR, NTEXT, UNIQUEIDENTIFIER, UUID, BOOLEAN, IMAGE gibi
/// "kanonik" (çoğunlukla MSSQL kökenli) tipleri sunuyor ve kullanıcı bunları HERHANGİ
/// bir motora derleyebiliyordu. Sonuç: NVARCHAR → PostgreSQL'de yok, UUID → MSSQL'de
/// yok, BLOB → MSSQL'de yok, BOOLEAN → MSSQL'de yok. Yani "6 motora derleme" iddiası
/// SQLite ve Oracle dışında gerçekte çalışmıyordu.
///
/// SQLite ve Oracle üreticilerinin kendi tip eşleme fonksiyonları zaten vardı ve
/// doğruydu — buraya dahil edilmedi.
/// </summary>
internal static class TypeSql
{
    /// <summary>
    /// Kanonik tipi + uzunluğu hedef motorun native SQL tipine çevirir.
    /// Dönen değer, uzunluk/hassasiyet varsa parantezi de içerir — çağıran taraf
    /// ayrıca bir "(length)" eklememelidir.
    /// </summary>
    public static string Map(string canonicalType, int? length, DatabaseType engine)
    {
        var t = (canonicalType ?? string.Empty).Trim().ToUpperInvariant();

        return engine switch
        {
            DatabaseType.MSSQL => Mssql(t, length),
            DatabaseType.PostgreSQL => Postgres(t, length),
            DatabaseType.MySQL or DatabaseType.MariaDB => MySqlFamily(t, length),
            _ => WithLength(t, length) // SQLite/Oracle buraya gelmez, kendi eşlemeleri var
        };
    }

    private static string WithLength(string type, int? length) =>
        length.HasValue ? $"{type}({length})" : type;

    // ── MSSQL ────────────────────────────────────────────────────────────────
    // Çoğu kanonik tip zaten native T-SQL'dir (NVARCHAR, NTEXT, IMAGE, UNIQUEIDENTIFIER
    // hâlâ geçerli T-SQL tipleridir — bilinçli olarak dokunulmadı, mevcut golden
    // dosyalar bozulmasın diye). Yalnızca T-SQL'de HİÇ VAR OLMAYAN 4 tip aliaslanır.
    private static string Mssql(string t, int? length) => t switch
    {
        "BOOLEAN" => "BIT",
        "UUID" => "UNIQUEIDENTIFIER",
        "BLOB" => "VARBINARY(MAX)",
        "JSON" => "NVARCHAR(MAX)",
        _ => WithLength(t, length)
    };

    // ── PostgreSQL ───────────────────────────────────────────────────────────
    private static string Postgres(string t, int? length) => t switch
    {
        "INT" => "integer",
        "BIGINT" => "bigint",
        "SMALLINT" => "smallint",
        "TINYINT" => "smallint",                 // PostgreSQL'de tinyint yok
        "VARCHAR" or "NVARCHAR" => WithLength("varchar", length),
        "CHAR" => WithLength("char", length),
        "TEXT" or "NTEXT" => "text",
        "DATETIME" or "DATETIME2" or "TIMESTAMP" => "timestamp",
        "DATE" => "date",
        "TIME" => "time",
        "DECIMAL" or "NUMERIC" => WithLength("numeric", length),
        "FLOAT" => "double precision",
        "REAL" => "real",
        "BIT" or "BOOLEAN" => "boolean",
        "UNIQUEIDENTIFIER" or "UUID" => "uuid",
        "BLOB" or "BINARY" or "VARBINARY" or "IMAGE" => "bytea",
        "JSON" => "jsonb",                       // indekslenebilir, PostgreSQL'de idiomatik seçim
        _ => WithLength(t.ToLowerInvariant(), length)
    };

    // ── MySQL / MariaDB ──────────────────────────────────────────────────────
    private static string MySqlFamily(string t, int? length) => t switch
    {
        "NVARCHAR" => WithLength("VARCHAR", length),   // MySQL'de ayrı bir NVARCHAR yok, tablo utf8mb4
        "NTEXT" => "TEXT",
        "DATETIME2" => "DATETIME",
        "NUMERIC" => WithLength("DECIMAL", length),
        "REAL" => "DOUBLE",
        "BIT" or "BOOLEAN" => "TINYINT(1)",            // MySQL boolean konvansiyonu
        "UNIQUEIDENTIFIER" or "UUID" => "CHAR(36)",
        "IMAGE" => "BLOB",
        _ => WithLength(t, length)                     // INT/BIGINT/VARCHAR/CHAR/TEXT/DATE/TIME/
                                                        // TIMESTAMP/DECIMAL/FLOAT/BLOB/BINARY/
                                                        // VARBINARY/JSON zaten native
    };
}
