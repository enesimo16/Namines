using System;
using System.Linq;
using System.Text;

namespace Namines.Infrastructure.Generators.PrismaGenerator;

/// <summary>
/// Veritabanı adlarını Prisma tanımlayıcılarına çevirir.
///
/// Prisma model adları PascalCase, alan adları camelCase olmalıdır (üretilen istemci
/// bu adlarla çıkar). Veritabanı adı farklıysa <c>@@map</c>/<c>@map</c> ile eşlenir —
/// yani <b>veritabanına dokunulmaz</b>, yalnızca istemci tarafındaki ad değişir.
/// Eşlemeyi atlayıp adı doğrudan kullanmak, <c>prisma db push</c> çalıştıran birinin
/// tablolarını yeniden adlandırmasına yol açardı.
/// </summary>
internal static class PrismaNaming
{
    public static string ToModelName(string dbName)
    {
        var parts = Split(dbName);
        if (parts.Length == 0) return "Model";

        var sb = new StringBuilder();
        foreach (var part in parts) sb.Append(Capitalize(part));

        return EnsureValid(sb.ToString(), "Model");
    }

    public static string ToFieldName(string dbName)
    {
        var parts = Split(dbName);
        if (parts.Length == 0) return "field";

        var sb = new StringBuilder(parts[0].ToLowerInvariant());
        for (var i = 1; i < parts.Length; i++) sb.Append(Capitalize(parts[i]));

        return EnsureValid(sb.ToString(), "field");
    }

    /// <summary>
    /// Bir tablonun ilişki listesindeki alan adı: çoğul, camelCase.
    ///
    /// Tablo adı ZATEN çoğulsa olduğu gibi bırakılır. Körlemesine ek yapmak
    /// <c>posts</c> → <c>postses</c> gibi adlar üretiyordu. Ters yön (tekilleştirme)
    /// denenmedi: "address" → "addres", "status" → "statu" gibi düzensiz adlarda
    /// sessizce yanlış sonuç verir. Tablo adına sadık kalmak tahmin etmekten iyidir;
    /// gerçek tablo adı zaten <c>@@map</c> ile korunuyor.
    /// </summary>
    public static string ToPluralFieldName(string dbName)
    {
        var single = ToFieldName(dbName);

        if (single.EndsWith("s", StringComparison.Ordinal)) return single;

        if (single.EndsWith("x", StringComparison.Ordinal) ||
            single.EndsWith("z", StringComparison.Ordinal) ||
            single.EndsWith("ch", StringComparison.Ordinal) ||
            single.EndsWith("sh", StringComparison.Ordinal))
            return single + "es";

        if (single.EndsWith("y", StringComparison.Ordinal) && single.Length > 1 &&
            !"aeiou".Contains(single[^2]))
            return single[..^1] + "ies";

        return single + "s";
    }

    /// <summary>
    /// Prisma dizesi içine gömülecek metin. Ad tırnak içinde geçtiği için kaçış şart:
    /// tırnak taşıyan bir tablo adı üretilen şemayı ayrıştırılamaz hâle getirirdi.
    /// </summary>
    public static string Escape(string value) =>
        (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string[] Split(string? dbName) =>
        (dbName ?? string.Empty)
            .Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);

    private static string Capitalize(string word) =>
        word.Length switch
        {
            0 => word,
            1 => word.ToUpperInvariant(),
            // Zaten karışık büyük/küçük yazılmış adlar (ör. "userID") bozulmasın diye
            // yalnızca ilk harf büyütülür, gerisi olduğu gibi bırakılır.
            _ => char.ToUpperInvariant(word[0]) + word[1..]
        };

    // Prisma tanımlayıcıları harf veya alt çizgiyle başlar, [A-Za-z0-9_] içerir.
    private static string EnsureValid(string candidate, string fallbackPrefix)
    {
        var cleaned = new string(candidate.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (cleaned.Length == 0) return fallbackPrefix;
        if (char.IsDigit(cleaned[0])) return fallbackPrefix + cleaned;
        return cleaned;
    }
}
