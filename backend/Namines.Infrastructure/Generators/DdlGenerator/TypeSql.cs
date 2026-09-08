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
    public static string Map(string canonicalType, int? length, int? scale, DatabaseType engine)
    {
        var t = (canonicalType ?? string.Empty).Trim().ToUpperInvariant();

        return engine switch
        {
            DatabaseType.MSSQL => Mssql(t, length, scale),
            DatabaseType.PostgreSQL => Postgres(t, length, scale),
            DatabaseType.MySQL or DatabaseType.MariaDB => MySqlFamily(t, length, scale),
            _ => WithLength(t, length) // SQLite/Oracle buraya gelmez, kendi eşlemeleri var
        };
    }

    private static string WithLength(string type, int? length) =>
        length.HasValue ? $"{type}({length})" : type;

    /// <summary>
    /// DECIMAL/NUMERIC için hassasiyet + ölçek. Ölçek verilmemişse çıktı ESKİSİYLE
    /// aynı kalır — mevcut golden dosyalar bozulmasın diye.
    /// </summary>
    private static string WithPrecision(string type, int? precision, int? scale) =>
        precision.HasValue && scale.HasValue ? $"{type}({precision},{scale})"
                                             : WithLength(type, precision);

    // ── MSSQL ────────────────────────────────────────────────────────────────
    // Çoğu kanonik tip zaten native T-SQL'dir (NVARCHAR, NTEXT, IMAGE, UNIQUEIDENTIFIER
    // hâlâ geçerli T-SQL tipleridir — bilinçli olarak dokunulmadı, mevcut golden
    // dosyalar bozulmasın diye).
    private static string Mssql(string t, int? length, int? scale) => t switch
    {
        "DECIMAL" or "NUMERIC" => WithPrecision(t, length, scale),
        "BOOLEAN" => "BIT",
        "UUID" => "UNIQUEIDENTIFIER",
        "BLOB" => "VARBINARY(MAX)",
        "JSON" => "NVARCHAR(MAX)",

        // T-SQL'de "timestamp" bir tarih tipi DEĞİL — rowversion'ın eski adı, yani
        // veritabanının ürettiği 8 baytlık satır sürümü. DEFAULT da alamaz, bu yüzden
        // olduğu gibi yazmak yalnızca yanlış tip değil, çalıştırılamayan DDL üretiyordu.
        // DATETIME de deprecated; ikisi de DATETIME2'ye çekiliyor.
        // Bkz. TypeMappingTests.Mssql_maps_date_time_types_to_datetime2.
        "TIMESTAMP" or "DATETIME" => "DATETIME2",

        _ => WithLength(t, length)
    };

    // ── PostgreSQL ───────────────────────────────────────────────────────────
    private static string Postgres(string t, int? length, int? scale) => t switch
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
        "DECIMAL" or "NUMERIC" => WithPrecision("numeric", length, scale),
        "FLOAT" => "double precision",
        "REAL" => "real",
        "BIT" or "BOOLEAN" => "boolean",
        "UNIQUEIDENTIFIER" or "UUID" => "uuid",
        "BLOB" or "BINARY" or "VARBINARY" or "IMAGE" => "bytea",
        "JSON" => "jsonb",                       // indekslenebilir, PostgreSQL'de idiomatik seçim
        _ => WithLength(t.ToLowerInvariant(), length)
    };

    // ── MySQL / MariaDB ──────────────────────────────────────────────────────
    private static string MySqlFamily(string t, int? length, int? scale) => t switch
    {
        "NVARCHAR" => WithLength("VARCHAR", length),   // MySQL'de ayrı bir NVARCHAR yok, tablo utf8mb4
        "NTEXT" => "TEXT",
        "DATETIME2" => "DATETIME",
        "NUMERIC" or "DECIMAL" => WithPrecision("DECIMAL", length, scale),
        "REAL" => "DOUBLE",
        "BIT" or "BOOLEAN" => "TINYINT(1)",            // MySQL boolean konvansiyonu
        "UNIQUEIDENTIFIER" or "UUID" => "CHAR(36)",
        "IMAGE" => "BLOB",
        _ => WithLength(t, length)                     // INT/BIGINT/VARCHAR/CHAR/TEXT/DATE/TIME/
                                                        // TIMESTAMP/DECIMAL/FLOAT/BLOB/BINARY/
                                                        // VARBINARY/JSON zaten native
    };
}
