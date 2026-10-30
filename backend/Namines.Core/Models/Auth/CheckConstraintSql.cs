using System;
using System.Linq;

namespace Namines.Core.Models.Auth;

/// <summary>
/// CHECK kısıtı ifadeleri için TEK üretici (docs/last-review/05 DB-21/22).
///
/// <b>Bir enum'a yeni üye eklemek MIGRATION GEREKTİRİR:</b> enum'a bağlı tamsayı kolonların
/// CHECK'i buradaki <see cref="EnumIn{TEnum}"/> ile enum'un o anki üyelerinden türetilir;
/// üye ekleyince model değişir, <c>dotnet ef migrations add</c> eski kısıtı düşürüp yenisini
/// ekleyen bir migration üretir. Migration eklenmezse yeni değeri yazan kod CHECK ihlaliyle
/// (SQLSTATE 23514) patlar — ve MigrationModelSnapshotTests modelin migration'larla senkron olmadığını yakalar.
/// </summary>
public static class CheckConstraintSql
{
    /// <summary>"Kolon" IN (enum değerleri). NULL kolonlarda CHECK NULL'ı geçirir.</summary>
    public static string EnumIn<TEnum>(string column) where TEnum : struct, Enum =>
        $"\"{column}\" IN ({string.Join(", ", Enum.GetValues<TEnum>().Select(v => Convert.ToInt64(v)).Distinct().OrderBy(v => v))})";

    public static string Between(string column, long min, long max) =>
        $"\"{column}\" BETWEEN {min} AND {max}";

    public static string AtLeast(string column, long min) =>
        $"\"{column}\" >= {min}";

    /// <summary>
    /// Uzunluk tavanı. <c>char_length</c> PostgreSQL'e özgüdür (SQLite'ta yoktur), bu yüzden
    /// çağıranlar yalnızca Npgsql modelinde ekler.
    /// </summary>
    public static string MaxLength(string column, int max) =>
        $"char_length(\"{column}\") <= {max}";

    public static string Name(string table, string column, string? suffix = null) =>
        $"CK_{table}_{column}{(suffix is null ? "" : "_" + suffix)}";
}
