using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Namines.Core.Enums;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// Şemadaki HAM SQL parçalarının (hesaplanan kolon ifadesi, CHECK, index
/// <c>WHERE</c>, RLS ifadeleri, tanınmayan DEFAULT, tip adı, index yöntemi)
/// üretilen betiğe girmeden önceki TEK doğrulama noktası.
///
/// <b>Neden ortak:</b> C-1 yalnızca tanımlayıcıları (<see cref="SqlIdentifier"/>)
/// kapattı; ifade alanları ise her yerde ayrı ayrı, kimi yerde hiç
/// doğrulanmadan satıra yapıştırılıyordu. Üretilen betik sandbox'ta
/// <c>psql</c> / <c>mysql</c> istemcisine verildiği için bir satır sonundan sonra
/// gelen <c>\! komut</c> (istemci meta-komutu) ya da <c>--</c> ile kapatılan
/// kuyruk, container içinde komut çalıştırmaya kadar gidiyordu.
///
/// <b>Normalize edilen:</b> satır sonu ve sekme tek boşluğa indirilir. Ters eğik çizgi,
/// <c>--</c>, blok yorum ve noktalı virgül yasak olduğundan tek başına bir satır sonu
/// meta-komut ya da yorum başlatamaz; yapıştırılan/AI üretimi çok satırlı ifadeler geçerli.
///
/// <b>Reddedilen:</b> diğer her kontrol karakteri, noktalı virgül, ters eğik çizgi,
/// <c>--</c>, blok yorum açılış/kapanışı, MySQL ailesinde <c>#</c> ve dengesiz tek tırnak.
/// Bunlar temizlenmez REDDEDİLİR: temizlemek, kullanıcının yazdığından farklı bir ifadeyi
/// sessizce çalıştırırdı (bkz. <see cref="SqlIdentifier"/> ile aynı gerekçe).
///
/// <b>Gövdeler kapsam dışı:</b> trigger/prosedür gövdeleri tasarım gereği ham
/// SQL'dir (bkz. <see cref="TriggerProcedureSql"/>); burada yalnızca onların
/// yorum satırına giren adları temizlenir.
///
/// <see cref="ArgumentException"/> atılır — üreticilerin geçersiz girdi için
/// zaten kullandığı tür, <c>CompileController</c> bunu 400'e çeviriyor.
/// </summary>
internal static class SqlFragmentGuard
{
    private static readonly string[] ForbiddenTokens = [";", "\\", "--", "/*", "*/"];

    /// <summary>
    /// Index yöntemi allow-list'i. Yöntem PostgreSQL'de <c>USING x</c> olarak
    /// çıplak yazıldığı için serbest metin olamaz.
    /// </summary>
    private static readonly HashSet<string> AllowedIndexMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "btree", "hash", "gin", "gist", "spgist", "brin", "rtree",
        "fulltext", "spatial", "clustered", "nonclustered", "columnstore",
    };

    private static readonly Regex TypeNamePattern = new(
        @"^[A-Za-z_][A-Za-z0-9_ ]*(\.[A-Za-z_][A-Za-z0-9_ ]*)*(\(\s*(\d+(\s*,\s*\d+)?|max)\s*\))?(\[\])?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceRun = new(
        @"[ ]*[\r\n\t\u0085  ][\s]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumericLiteral = new(
        @"^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// SQL ifadesi alanını doğrular ve kırpılmış hâlini döndürür.
    /// </summary>
    /// <param name="what">Hata mesajında görünen alan adı ("CHECK", "WHERE"...).</param>
    public static string EnsureSafeExpression(string expression, string what, DatabaseType engine)
    {
        // Satır sonu/sekme tek boşluğa indirilir, REDDEDİLMEZ: ``, `--`, blok yorum ve `;`
        // aşağıda zaten yasak, yani yalnızca satır sonu bir meta-komut ya da yorum başlatamaz;
        // yapıştırılan/AI üretimi çok satırlı ifadeler geçerli. Diğer kontrol karakterleri reddedilir.
        var trimmed = WhitespaceRun.Replace(expression, " ").Trim();

        if (trimmed.Any(IsLineOrControl))
            throw Reject(what, "a control character or line break", trimmed);

        foreach (var token in ForbiddenTokens)
            if (trimmed.Contains(token, StringComparison.Ordinal))
                throw Reject(what, $"'{token}'", trimmed);

        // MySQL/MariaDB'de '#' satır sonuna kadar yorumdur; ifadenin kapanış
        // parantezini yutar ve sonraki satırları ifadenin içine katardı.
        if (engine is DatabaseType.MySQL or DatabaseType.MariaDB && trimmed.Contains('#'))
            throw Reject(what, "'#' (a comment in " + engine + ")", trimmed);

        // Dengesiz tek tırnak, ifadenin geri kalanını (ve sonraki alanları)
        // dize sabitinin içine çeker; geçerli hiçbir ifade böyle değil.
        if (trimmed.Count(c => c == '\'') % 2 != 0)
            throw Reject(what, "an unbalanced single quote", trimmed);

        return trimmed;
    }

    /// <summary>
    /// Index yöntemini allow-list'e karşı doğrular; küçük harfli hâli döner.
    /// </summary>
    public static string EnsureIndexMethod(string method)
    {
        var trimmed = method.Trim();
        if (!AllowedIndexMethods.Contains(trimmed))
            throw new ArgumentException(
                $"The index method '{Describe(trimmed)}' is not supported. Allowed: " +
                string.Join(", ", AllowedIndexMethods.OrderBy(m => m, StringComparer.Ordinal)) + ".");

        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Eşlemesi olmayan (varsayılan kola düşen) kolon tipi adını doğrular.
    /// Bilinen tipler zaten sabit karşılıklara çevriliyor; ham metin yalnızca
    /// <c>ad</c>, <c>sema.ad</c>, <c>ad(10)</c>, <c>ad(10,2)</c>, <c>ad(max)</c>, <c>ad[]</c> biçiminde geçer.
    /// </summary>
    public static string EnsureTypeName(string typeName)
    {
        if (typeName.Length == 0) return typeName;

        if (!TypeNamePattern.IsMatch(typeName))
            throw new ArgumentException(
                $"The column type '{Describe(typeName)}' is not a valid type name. " +
                "Use a plain type name with an optional length, such as VARCHAR(50) or NUMERIC(10,2).");

        return typeName;
    }

    /// <summary>
    /// DEFAULT değeri: tanınan sabit (sayı, iyi biçimli dize, NULL/TRUE/FALSE)
    /// olduğu gibi geçer; başka her şey ifade sayılır ve doğrulanır.
    /// </summary>
    public static string EnsureSafeDefault(string value, DatabaseType engine)
    {
        var trimmed = value.Trim();

        if (NumericLiteral.IsMatch(trimmed) ||
            trimmed.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
            IsWellFormedStringLiteral(trimmed, engine))
            return value;

        return EnsureSafeExpression(value, "DEFAULT", engine);
    }

    /// <summary>
    /// Enum değeri için SQL dize sabiti. Tek tırnak ikilenir; MySQL/MariaDB'de
    /// ters eğik çizgi de kaçış karakteridir, ikilenmezse <c>'x\'</c> sabiti
    /// kapanışını yutar ve sonraki metin SQL olarak okunur.
    /// </summary>
    public static string StringLiteral(string? value, DatabaseType engine)
    {
        var text = value ?? string.Empty;

        if (text.Any(IsLineOrControl))
            throw new ArgumentException(
                $"The enum value '{Describe(text)}' contains a control character or line break, " +
                "which is not allowed in a SQL string literal.");

        if (engine is DatabaseType.MySQL or DatabaseType.MariaDB)
            text = text.Replace("\\", "\\\\");

        return "'" + text.Replace("'", "''") + "'";
    }

    /// <summary>
    /// Yorum satırına (<c>--</c>) ya da blok yoruma girecek metni tek satıra indirir.
    /// Yorum metni doğrulanmaz, temizlenir: yorum zaten çalıştırılmaz, ama satır
    /// sonu yorumu erken kapatıp kalanını SQL yapardı.
    /// </summary>
    public static string CommentText(string? text) =>
        string.Concat((text ?? string.Empty).Select(c => IsLineOrControl(c) ? ' ' : c))
            .Replace("*/", "* /", StringComparison.Ordinal);

    private static bool IsLineOrControl(char c) =>
        char.IsControl(c) ||
        CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    private static bool IsWellFormedStringLiteral(string value, DatabaseType engine)
    {
        if (value.Length < 2 || value[0] != '\'' || value[^1] != '\'') return false;

        var interior = value[1..^1];
        if (interior.Any(IsLineOrControl)) return false;

        // MySQL ailesinde ters eğik çizgi kapanış tırnağını kaçırabilir.
        if (engine is DatabaseType.MySQL or DatabaseType.MariaDB && interior.Contains('\\')) return false;

        // İç tırnaklar yalnızca çiftler hâlinde ('') bulunabilir.
        return interior.Replace("''", string.Empty, StringComparison.Ordinal).IndexOf('\'') < 0;
    }

    private static ArgumentException Reject(string what, string offender, string value) =>
        new($"The {what} expression contains {offender}, which is not allowed in a generated SQL fragment: " +
            $"'{Describe(value)}'.");

    private static string Describe(string value) =>
        new(value.Select(c => IsLineOrControl(c) ? '?' : c).Take(64).ToArray());
}
