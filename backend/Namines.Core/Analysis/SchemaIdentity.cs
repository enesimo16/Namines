namespace Namines.Core.Analysis;

/// <summary>
/// Adından türetilen, çağrılar arasında DEĞİŞMEYEN <c>StableUuid</c> değerleri.
///
/// <b>Çözdüğü hata:</b> <c>DbIntrospectionService</c> her introspection'da
/// <c>Guid.NewGuid()</c> atıyordu. <see cref="SchemaImpactAnalyzer"/> ise tabloları
/// StableUuid ile eşleştirir ve eşleşme bulamayınca güvenli tarafta kalıp bunu
/// "kaldırıldı + eklendi" sayar. Sonuç: AYNI veritabanını iki kez çekip
/// karşılaştırmak — MCP/CLI'ın birincil akışı tam olarak budur — hiçbir değişiklik
/// yokken "tüm tablolar silinecek, veri kaybı, Breaking" diyordu. Adı "stable" olan
/// bir alanın her çağrıda değişmesi zaten çelişkiydi.
///
/// Web canvas'ında sorun görünmüyordu, çünkü orada uuid'ler düzenlemeler boyunca
/// yaşıyor; introspection'ın hafızası yok, dolayısıyla rastgele bir kimlik hiçbir
/// zaman kararlı olamaz. İsim, canlı bir veritabanında zaten kimliğin kendisidir.
///
/// Rename tespiti korunur: uuid'i AÇIKÇA veren (canvas gibi) kaynaklar kendi
/// değerlerini taşımaya devam eder, buradaki türetme yalnızca kimlik yoksa devreye
/// girer. Bkz. <c>SchemaIdentityTests</c>.
/// </summary>
public static class SchemaIdentity
{
    /// <summary>Türetilmiş olduğu ayıklama sırasında görünsün diye önek taşır.</summary>
    private const string Prefix = "name:";

    public static string ForTable(string? tableName) =>
        Prefix + Normalize(tableName);

    public static string ForColumn(string? tableName, string? columnName) =>
        Prefix + Normalize(tableName) + "." + Normalize(columnName);

    public static string ForIndex(string? tableName, string? indexName) =>
        Prefix + Normalize(tableName) + "#" + Normalize(indexName);

    /// <summary>Bu kimlik türetilmiş mi (yoksa gerçek bir uuid mi)?</summary>
    public static bool IsDerived(string? stableUuid) =>
        stableUuid is not null && stableUuid.StartsWith(Prefix, StringComparison.Ordinal);

    // Çoğu motor tanımlayıcıları büyük/küçük harf duyarsız ele alır; "Users" ile
    // "users"ı ayrı tablo saymak yeniden aynı sahte "silindi+eklendi" sonucunu verirdi.
    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
