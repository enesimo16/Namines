using System.Collections.Generic;
using Namines.Core.Enums;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// Bilinen, motora özgü DEFAULT fonksiyonlarını hedef motorun eşdeğerine çevirir.
///
/// KAPSAM BİLİNÇLİ OLARAK SINIRLI: bu, genel bir SQL ifade çevirici DEĞİLDİR —
/// öyle bir şey ayrı bir ifade ayrıştırıcısı (AST) gerektirir ve NSL'in (Faz 1)
/// işidir. Burada yalnızca küçük, bilinen bir "şimdiki zaman" fonksiyon listesi
/// tanınır. Listede olmayan her şey (sayısal/metin literalleri, '0', 'TR' gibi)
/// OLDUĞU GİBİ geçer — bunlar zaten motor-bağımsızdır.
///
/// Neden gerekli: kullanıcı arayüzünde varsayılan değer olarak GETUTCDATE()
/// yazıp şemayı PostgreSQL'e derlerse, GETUTCDATE() PostgreSQL'de tanımlı
/// değildir ve CREATE TABLE tamamen başarısız olur.
/// </summary>
internal static class DefaultValueSql
{
    private static readonly Dictionary<string, Dictionary<DatabaseType, string>> KnownFunctions = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // MySQL 8.0.13+ ve MariaDB, DEFAULT CURRENT_TIMESTAMP dışındaki her fonksiyon
        // çağrısını DEFAULT (ifade) biçiminde, parantez içinde ister. Çıplak
        // "DEFAULT UTC_TIMESTAMP()" sözdizimi hatasıdır (Hata 1064) — gerçek bir MySQL
        // container'ına karşı çalıştırılan entegrasyon testi bunu kanıtladı.
        ["GETUTCDATE()"] = new()
        {
            [DatabaseType.MSSQL] = "GETUTCDATE()",
            [DatabaseType.PostgreSQL] = "(now() AT TIME ZONE 'utc')",
            [DatabaseType.MySQL] = "(UTC_TIMESTAMP())",
            [DatabaseType.MariaDB] = "(UTC_TIMESTAMP())",
            [DatabaseType.SQLite] = "(datetime('now'))",
            [DatabaseType.Oracle] = "SYS_EXTRACT_UTC(SYSTIMESTAMP)"
        },
        ["GETDATE()"] = new()
        {
            [DatabaseType.MSSQL] = "GETDATE()",
            [DatabaseType.PostgreSQL] = "now()",
            [DatabaseType.MySQL] = "CURRENT_TIMESTAMP",       // özel durum: parantezsiz tek istisna
            [DatabaseType.MariaDB] = "CURRENT_TIMESTAMP",
            [DatabaseType.SQLite] = "(datetime('now', 'localtime'))",
            [DatabaseType.Oracle] = "SYSTIMESTAMP"
        },
        ["NEWID()"] = new()
        {
            [DatabaseType.MSSQL] = "NEWID()",
            [DatabaseType.PostgreSQL] = "gen_random_uuid()",   // pgcrypto veya PG13+ builtin
            [DatabaseType.MySQL] = "(UUID())",
            [DatabaseType.MariaDB] = "(UUID())",
            [DatabaseType.SQLite] = "(lower(hex(randomblob(16))))",
            [DatabaseType.Oracle] = "SYS_GUID()"
        }
    };

    /// <summary>
    /// Değeri çevirir. Bilinen bir fonksiyon değilse (literal, ifade, vb.)
    /// değeri OLDUĞU GİBİ döndürür — sessizce "düzeltmeye" çalışmaz.
    /// </summary>
    public static string Translate(string? defaultValue, DatabaseType engine)
    {
        if (string.IsNullOrWhiteSpace(defaultValue)) return defaultValue ?? string.Empty;

        var trimmed = defaultValue.Trim();
        if (KnownFunctions.TryGetValue(trimmed, out var perEngine) && perEngine.TryGetValue(engine, out var translated))
            return translated;

        return defaultValue;
    }
}
