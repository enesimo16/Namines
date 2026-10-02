using System;
using System.Linq;
using Namines.Core.Enums;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// DDL tanımlayıcılarının TEK alıntılama noktası.
///
/// <b>Neden ortak:</b> altı motor üreticisi ve <see cref="EnumSql"/> kendi
/// <c>Quote</c>'unu taşıyordu ve <b>hiçbiri ayırıcıyı ikilemiyordu</b> —
/// <c>$"\"{identifier}\""</c>. Şema tamamen istemciden geldiği için (bkz.
/// <c>CompileController.CompileSql</c>: gövdeden alınıp yalnızca null kontrolüyle
/// üreticiye veriliyor), tanımlayıcı içindeki bir ayırıcı karakteri alıntılanmış
/// tanımlayıcıyı kapatıp ifadeye devam edebiliyordu. Üretilen betik
/// <c>/api/docker/run</c> ile sandbox container'ında <c>psql -U postgres</c> /
/// <c>mysql -u root</c> olarak çalıştığı için bu, sıradan bir hesaptan container
/// içinde kod çalıştırmaya kadar gidiyordu (bkz. docs/last-review C-1).
///
/// Çalışma zamanındaki <c>GatewayService.Quote</c> ikilemeyi baştan beri doğru
/// yapıyordu; iki katman ayrışmıştı. Yeni bir motor eklenirken aynı hatanın
/// tekrarlanmaması için alıntılama artık yalnızca burada.
/// </summary>
internal static class SqlIdentifier
{
    /// <summary>MSSQL: <c>[ad]</c>, kapanış köşeli ayracı ikilenir.</summary>
    public static string Bracketed(string? identifier) =>
        $"[{Guard(identifier).Replace("]", "]]")}]";

    /// <summary>MySQL/MariaDB: <c>`ad`</c>, ters tırnak ikilenir.</summary>
    public static string Backticked(string? identifier) =>
        $"`{Guard(identifier).Replace("`", "``")}`";

    /// <summary>PostgreSQL/Oracle/SQLite: <c>"ad"</c>, çift tırnak ikilenir.</summary>
    public static string DoubleQuoted(string? identifier) =>
        $"\"{Guard(identifier).Replace("\"", "\"\"")}\"";

    /// <summary>Motora göre doğru ayırıcıyı seçer.</summary>
    public static string For(DatabaseType engine, string? identifier) => engine switch
    {
        DatabaseType.MSSQL => Bracketed(identifier),
        DatabaseType.MySQL or DatabaseType.MariaDB => Backticked(identifier),
        _ => DoubleQuoted(identifier),
    };

    /// <summary>
    /// Ayırıcı ikileme TEK BAŞINA yetmiyor: kontrol karakterleri ikilenerek
    /// etkisizleştirilemez. Bir satır sonu, üretilen betiğin <c>--</c> yorumunu
    /// erken kapatabilir ya da çok-ifadeli bir betikte ifade sınırını kaydırabilir;
    /// NUL bazı sürücülerde dizeyi erken sonlandırır. Hiçbir gerçek şemada bunlara
    /// ihtiyaç yok, o yüzden sessizce temizlemek yerine REDDEDİLİYORLAR —
    /// temizlemek, kullanıcının yazdığından farklı bir ada sessizce tablo açardı.
    /// </summary>
    private static string Guard(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            throw new ArgumentException("A table or column name cannot be empty.", nameof(identifier));

        if (identifier.Any(char.IsControl))
            throw new ArgumentException(
                $"The name '{Describe(identifier)}' contains a control character (line break, tab or NUL), " +
                "which is not allowed in a table, column or constraint name.",
                nameof(identifier));

        return identifier;
    }

    /// <summary>Hata mesajı için okunabilir hâl — kontrol karakterleri mesajı da bozardı.</summary>
    private static string Describe(string identifier) =>
        new(identifier.Select(c => char.IsControl(c) ? '?' : c).Take(64).ToArray());
}
