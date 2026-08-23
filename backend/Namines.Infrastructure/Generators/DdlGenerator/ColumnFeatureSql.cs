using System;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// Kolon üzerindeki motora bağlı ek özellikler: dizi, hesaplanan kolon,
/// collation (04 §3).
///
/// <b>Tek karar noktası, altı motor.</b> Aynı kuralı her üreticide ayrı yazmak,
/// biri güncellenip diğerleri unutulduğunda aynı şemanın motorlara göre farklı
/// davranması demek — ve bu farkı ancak biri üretime çıkınca fark edersin.
/// </summary>
internal static class ColumnFeatureSql
{
    /// <summary>
    /// Dizi tipini uygular.
    ///
    /// <b>Desteklemeyen motorda REDDEDİLİYOR.</b> Skalere düşmek kolonun
    /// anlamını değiştirir: uygulama listeye yazmaya çalışır, veritabanı tek
    /// değer bekler. Bu, çalışmayan bir DDL'den çok daha kötü — DDL çalışır,
    /// hata çalışma zamanına ertelenir.
    /// </summary>
    public static string ApplyArray(string sqlType, SchemaColumn column, DatabaseType engine)
    {
        if (!column.IsArray) return sqlType;

        if (engine != DatabaseType.PostgreSQL)
            throw new NotSupportedException(
                $"Column '{column.Name}' is an array, which only PostgreSQL supports. " +
                "Model it as a child table, or a JSON column, for the other engines.");

        return sqlType + "[]";
    }

    /// <summary>
    /// Collation eki; tanımlı değilse boş.
    ///
    /// <b>Tırnaklama motora göre değişiyor ve bu tahmin değil, ölçüm:</b>
    /// PostgreSQL ve SQLite adı tırnak içinde kabul eder — <c>tr-TR-x-icu</c>
    /// gibi TİRE içeren bir ad tırnaksız yazıldığında ikisi de sözdizimi hatası
    /// verir (gerçek motorlarda görüldü). SQL Server ve MySQL ailesi ise
    /// <c>COLLATE</c>'den sonra çıplak bir tanımlayıcı bekler ve o motorların
    /// collation adları zaten tire içermez.
    ///
    /// Çıplak yazılan motorlarda ad DOĞRULANIYOR: beklenmeyen bir karakter
    /// gördüğümüzde bozuk SQL üretip veritabanının anlaşılmaz bir hatayla
    /// düşmesini beklemek yerine, sorunu burada söylüyoruz.
    /// </summary>
    public static string Collate(SchemaColumn column, DatabaseType engine)
    {
        if (string.IsNullOrWhiteSpace(column.Collation)) return string.Empty;

        var name = column.Collation.Trim();

        if (engine == DatabaseType.Oracle)
            // Oracle'da collation yalnızca 12.2+ ve yalnızca belirli tiplerde
            // geçerli; üretip veritabanının reddetmesini beklemek yerine
            // desteklenmediğini söylüyoruz.
            throw new NotSupportedException(
                $"Column '{column.Name}' sets a collation, which this generator does not emit for Oracle.");

        if (engine is DatabaseType.PostgreSQL or DatabaseType.SQLite)
            return $" COLLATE \"{name}\"";

        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_') continue;

            throw new NotSupportedException(
                $"Collation '{name}' cannot be written for {engine}: that engine expects a bare " +
                "identifier after COLLATE, and this name contains characters it would reject.");
        }

        return $" COLLATE {name}";
    }

    /// <summary>
    /// Hesaplanan kolon tanımı; kolon hesaplanan değilse <c>null</c>.
    ///
    /// <b>Dönen metin, kolonun TİPİNDEN SONRA gelir</b> — tek istisna SQL Server.
    ///
    /// BULUNMA YERİ: ilk hâlinde tip hiç yazılmıyordu ("nasılsa ifadeden
    /// çıkarılır" varsayımıyla) ve birim testleri bunu doğruluyordu. <b>Gerçek
    /// PostgreSQL DDL'i reddetti:</b> <c>syntax error at or near "ALWAYS"</c>.
    /// PostgreSQL, MySQL ve MariaDB tipi ZORUNLU kılar; SQL Server ise tip
    /// yazılmasına izin VERMEZ. Yani "tek bir doğru" yok, motora bağlı — ve bu
    /// ancak gerçek motorda çalıştırınca görülüyor.
    ///
    /// <c>DEFAULT</c> hiçbir motorda hesaplanan kolonla birleşmez; çağıran onu
    /// atlamalı.
    /// </summary>
    public static string? Generated(SchemaColumn column, DatabaseType engine)
    {
        if (string.IsNullOrWhiteSpace(column.Generated)) return null;

        var expression = column.Generated.Trim();

        // SQLite hesaplanan bir kolonun birincil anahtar olmasına İZİN VERMİYOR
        // ("generated columns cannot be part of the PRIMARY KEY"). Bu tahmin değil,
        // ölçüm: gerçek SQLite'a karşı çalıştırınca çıktı. Üretip motorun
        // reddetmesini beklemek yerine, sebebini burada söylüyoruz — aynı şema
        // PostgreSQL'de sorunsuz çalıştığı için kullanıcı farkı bilmeli.
        if (engine == DatabaseType.SQLite && column.IsPK)
            throw new NotSupportedException(
                $"Column '{column.Name}' is both generated and part of the primary key, " +
                "which SQLite does not allow. PostgreSQL accepts this; for SQLite, give the " +
                "table a separate key column.");

        return engine switch
        {
            // STORED: sanal kolon her okumada yeniden hesaplanır ve index'lenemez.
            // Şemayı yazan kişi "bu değer hep hazır olsun" diyor; sanal seçmek
            // sessizce daha yavaş bir şey üretmek olurdu.
            DatabaseType.PostgreSQL => $"GENERATED ALWAYS AS ({expression}) STORED",
            DatabaseType.MySQL or DatabaseType.MariaDB => $"GENERATED ALWAYS AS ({expression}) STORED",
            // SQL Server tip yazılmasına İZİN VERMEZ; bkz. TypePrecedesGenerated.
            DatabaseType.MSSQL => $"AS ({expression}) PERSISTED",
            DatabaseType.SQLite => $"GENERATED ALWAYS AS ({expression}) STORED",
            DatabaseType.Oracle => $"GENERATED ALWAYS AS ({expression}) VIRTUAL",
            _ => throw new NotSupportedException(
                $"Generated columns are not supported for {engine}."),
        };
    }

    /// <summary>
    /// Hesaplanan kolonun tanımından ÖNCE tip yazılmalı mı?
    ///
    /// PostgreSQL/MySQL/MariaDB tipi zorunlu kılar, SQL Server yasaklar,
    /// SQLite ve Oracle ikisini de kabul eder. Kabul edenlerde de yazıyoruz:
    /// tipi görünür kılmak, şemayı okuyan kişinin kolonun neye çözüldüğünü
    /// ifadeden tahmin etmesini gerektirmiyor.
    /// </summary>
    public static bool TypePrecedesGenerated(DatabaseType engine) => engine != DatabaseType.MSSQL;
}
