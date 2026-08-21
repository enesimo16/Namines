using Namines.Core.Enums;

namespace Namines.Infrastructure.Generators.PrismaGenerator;

/// <summary>
/// Kanonik kolon tipini Prisma skaler tipine + isteğe bağlı native tip niteleyicisine
/// çevirir.
///
/// Native niteleyici (<c>@db.VarChar(255)</c>) neden önemli: yalnızca <c>String</c>
/// yazılırsa Prisma o motorun varsayılan metin tipini seçer — PostgreSQL'de
/// <c>text</c>, MySQL'de <c>varchar(191)</c>. Kullanıcının <c>VARCHAR(255)</c> dediği
/// yerde bu, <c>prisma db push</c> ile SESSİZCE tip değiştirmek demektir; MySQL'de
/// ayrıca uzunluğu 255'ten 191'e düşürüp veriyi kırpma riski taşır.
/// </summary>
internal static class PrismaTypeMap
{
    /// <param name="scalar">Prisma skaler tipi (String, Int, DateTime, ...).</param>
    /// <param name="nativeAttribute">
    /// <c>@db.X</c> niteleyicisi ya da null. Null olması "motorun varsayılanı kabul"
    /// anlamına gelir ve yalnızca sadakat kaybı olmadığında kullanılır.
    /// </param>
    public static (string scalar, string? nativeAttribute) Map(
        string? canonicalType, int? length, DatabaseType engine)
    {
        var t = (canonicalType ?? string.Empty).Trim().ToUpperInvariant();

        return t switch
        {
            "INT" or "INTEGER" => ("Int", null),
            "SMALLINT" => ("Int", SmallInt(engine)),
            "TINYINT" => ("Int", TinyInt(engine)),
            "BIGINT" => ("BigInt", null),

            "BIT" or "BOOLEAN" or "BOOL" => ("Boolean", null),

            "DECIMAL" or "NUMERIC" or "MONEY" => ("Decimal", null),
            "FLOAT" or "DOUBLE" => ("Float", null),
            "REAL" => ("Float", Real(engine)),

            "DATE" => ("DateTime", "@db.Date"),
            "TIME" => ("DateTime", Time(engine)),
            "DATETIME" or "DATETIME2" or "TIMESTAMP" => ("DateTime", null),

            "CHAR" => ("String", Sized("Char", length ?? 1, engine)),
            "VARCHAR" => ("String", Sized("VarChar", length ?? 255, engine)),
            "NVARCHAR" => ("String", NVarChar(length, engine)),
            "TEXT" or "NTEXT" => ("String", Text(engine)),

            "UUID" or "UNIQUEIDENTIFIER" => ("String", Uuid(engine)),
            "JSON" or "JSONB" => Json(engine),

            "BINARY" or "VARBINARY" or "BLOB" or "IMAGE" => ("Bytes", null),

            // Tanınmayan tip: Prisma'da uydurmak yerine String'e düşülür ve ham tip
            // native niteleyici olarak korunur; kullanıcı ne olduğunu görebilsin.
            _ => ("String", null)
        };
    }

    // SQLite tek bir depolama sınıfı ailesi kullanır ve Prisma orada native
    // niteleyici KABUL ETMEZ — yazmak şemayı geçersiz kılar.
    private static bool SupportsNative(DatabaseType engine) => engine != DatabaseType.SQLite;

    private static string? Sized(string name, int length, DatabaseType engine) =>
        SupportsNative(engine) ? $"@db.{name}({length})" : null;

    private static string? SmallInt(DatabaseType engine) => engine switch
    {
        DatabaseType.PostgreSQL => "@db.SmallInt",
        DatabaseType.MSSQL => "@db.SmallInt",
        DatabaseType.MySQL or DatabaseType.MariaDB => "@db.SmallInt",
        _ => null
    };

    private static string? TinyInt(DatabaseType engine) => engine switch
    {
        // PostgreSQL'in TINYINT'i yoktur; en yakını SmallInt.
        DatabaseType.PostgreSQL => "@db.SmallInt",
        DatabaseType.MSSQL => "@db.TinyInt",
        DatabaseType.MySQL or DatabaseType.MariaDB => "@db.TinyInt",
        _ => null
    };

    private static string? Real(DatabaseType engine) => engine switch
    {
        DatabaseType.PostgreSQL => "@db.Real",
        DatabaseType.MSSQL => "@db.Real",
        DatabaseType.MySQL or DatabaseType.MariaDB => "@db.Float",
        _ => null
    };

    private static string? Time(DatabaseType engine) => engine switch
    {
        DatabaseType.PostgreSQL => "@db.Time",
        DatabaseType.MSSQL => "@db.Time",
        DatabaseType.MySQL or DatabaseType.MariaDB => "@db.Time",
        _ => null
    };

    private static string? NVarChar(int? length, DatabaseType engine) => engine switch
    {
        // NVarChar yalnızca SQL Server'da vardır; diğerlerinde metin zaten Unicode'dur.
        DatabaseType.MSSQL => $"@db.NVarChar({length ?? 255})",
        DatabaseType.PostgreSQL => $"@db.VarChar({length ?? 255})",
        DatabaseType.MySQL or DatabaseType.MariaDB => $"@db.VarChar({length ?? 255})",
        _ => null
    };

    private static string? Text(DatabaseType engine) => engine switch
    {
        DatabaseType.PostgreSQL => "@db.Text",
        DatabaseType.MSSQL => "@db.NVarChar(Max)",
        DatabaseType.MySQL or DatabaseType.MariaDB => "@db.Text",
        _ => null
    };

    private static string? Uuid(DatabaseType engine) => engine switch
    {
        DatabaseType.PostgreSQL => "@db.Uuid",
        DatabaseType.MSSQL => "@db.UniqueIdentifier",
        DatabaseType.MySQL or DatabaseType.MariaDB => "@db.Char(36)",
        _ => null
    };

    private static (string, string?) Json(DatabaseType engine) => engine switch
    {
        // SQL Server'ın JSON tipi yoktur, Prisma orada Json skalerini desteklemez.
        DatabaseType.MSSQL => ("String", "@db.NVarChar(Max)"),
        DatabaseType.SQLite => ("String", null),
        _ => ("Json", null)
    };
}
