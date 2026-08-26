using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Infrastructure.Services;

/// <summary>
/// second-phase/13-DAGITIM-HEDEFLERI.md — üretilen şemayı komut satırı/Docker
/// OLMAYAN ortamlara (Plesk/cPanel/DirectAdmin, paylaşımlı barındırma, mobil)
/// sokulabilir hâle getirir.
///
/// <b>Yeni bir motor DEĞİL — çıktı biçimlendirme.</b> Var olan DDL üreticileri
/// aynen kullanılıyor; bu sınıf onların çıktısını hedef ortamın gerçek
/// kısıtlarına göre SARIYOR. Temel DDL üretimini (ve onun golden-file
/// testlerini) değiştirmiyor — o üreticiler Docker/CLI akışında da kullanılıyor
/// ve oradaki davranış bu iş yüzünden değişmemeli.
/// </summary>
public static class SharedHostingExporter
{
    /// <summary>Tek bir SQL dosyasının aşılmaması istenen üst sınırı (bayt). Çoğu paylaşımlı barındırma panelinde yükleme boyutu sınırlı.</summary>
    private const int MaxFileBytes = 1_000_000;

    public sealed record ExportedFile(string Name, byte[] Content);

    public static async Task<IReadOnlyList<ExportedFile>> ExportAsync(
        DatabaseSchema schema, DatabaseType target, IDdlGeneratorFactory ddlFactory, CancellationToken ct = default)
    {
        if (target == DatabaseType.SQLite)
            return await ExportSqliteAsync(schema, ddlFactory, ct);

        if (target is DatabaseType.MySQL or DatabaseType.MariaDB)
            return ExportMySqlFamily(schema, target, ddlFactory);

        throw new NotSupportedException(
            $"Shared hosting export only supports MySQL, MariaDB, and SQLite (got {target}).");
    }

    private static List<ExportedFile> ExportMySqlFamily(DatabaseSchema schema, DatabaseType target, IDdlGeneratorFactory ddlFactory)
    {
        var ddl = ddlFactory.GetGenerator(target).Generate(schema);

        // MariaDB üreticisi zaten utf8mb4'ü açıkça yazıyor; MySQL üreticisi
        // yazmıyor (sunucu varsayılanına güveniyor) — paylaşımlı barındırmada
        // o varsayılan çoğu zaman latin1/utf8(3 bayt) olur ve Türkçe karakterler
        // sessizce bozulur. Yalnızca BU ihracat yolunda düzeltiliyor; temel
        // üreticiyi değiştirmek 100'ün üzerinde golden-file testini kırardı.
        if (target == DatabaseType.MySQL)
            ddl = ddl.Replace(") ENGINE=InnoDB;", ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");

        var header = new StringBuilder();
        header.AppendLine("-- Namines — paylaşımlı barındırma için üretildi (second-phase/13-DAGITIM-HEDEFLERI.md)");
        header.AppendLine("-- Bu dosya bir veritabanının VAR OLDUĞUNU varsayar; CREATE DATABASE içermez.");
        header.AppendLine("-- Panelinizde önce boş bir veritabanı oluşturup phpMyAdmin ile bu dosyayı içe aktarın.");

        if (schema.Tables.Any(t => t.Checks.Count > 0))
        {
            header.AppendLine("--");
            header.AppendLine("-- NOT: CHECK kısıtları yalnızca MySQL 8.0.16+ / MariaDB 10.2+ üzerinde");
            header.AppendLine("-- UYGULANIR. Paylaşımlı barındırmada hâlâ yaygın olan MySQL 5.7 bu");
            header.AppendLine("-- sözdizimini KABUL EDER ama sessizce YOK SAYAR — hedef panelin motor");
            header.AppendLine("-- sürümünü önceden kontrol edin.");
        }
        header.AppendLine();

        // FOREIGN_KEY_CHECKS: normal Docker/CLI akışında gerekmiyor çünkü tablolar
        // zaten bağımlılık sırasına göre üretiliyor. phpMyAdmin gibi araçlar
        // dosyayı tek seferde çalıştırır ve döngüsel/ileri referanslı FK'lerde
        // "tablo henüz yok" hatası verebilir — kapatıp en sonda açmak bunu önler.
        var wrapped = "SET FOREIGN_KEY_CHECKS=0;\n\n" + header + ddl + "\nSET FOREIGN_KEY_CHECKS=1;\n";

        var parts = SqlFileSplitter.Split(wrapped, MaxFileBytes);
        var files = parts.Count == 1
            ? new List<ExportedFile> { new("schema.sql", Encoding.UTF8.GetBytes(parts[0])) }
            : parts.Select((p, i) => new ExportedFile($"schema_part{i + 1}_of_{parts.Count}.sql", Encoding.UTF8.GetBytes(p))).ToList();

        files.Add(new ExportedFile("README.txt", Encoding.UTF8.GetBytes(MySqlInstructions(target, files.Count > 1))));
        return files;
    }

    private static async Task<List<ExportedFile>> ExportSqliteAsync(DatabaseSchema schema, IDdlGeneratorFactory ddlFactory, CancellationToken ct)
    {
        var ddl = ddlFactory.GetGenerator(DatabaseType.SQLite).Generate(schema);
        var dbBytes = await SqliteFileBuilder.BuildAsync(ddl, ct);

        return new List<ExportedFile>
        {
            new("schema.db", dbBytes),
            new("schema.sql", Encoding.UTF8.GetBytes(ddl)),
            new("README.txt", Encoding.UTF8.GetBytes(SqliteInstructions)),
        };
    }

    private static string MySqlInstructions(DatabaseType target, bool isSplit)
    {
        var panelName = target == DatabaseType.MariaDB ? "MariaDB" : "MySQL";
        var sb = new StringBuilder();
        sb.AppendLine($"Namines — {panelName} paylaşımlı barındırma paketi");
        sb.AppendLine("=========================================");
        sb.AppendLine();
        sb.AppendLine("1. Panelinize girin (Plesk / cPanel / DirectAdmin).");
        sb.AppendLine("2. Veritabanları bölümünden BOŞ bir veritabanı oluşturun (Namines bunu");
        sb.AppendLine("   sizin için oluşturmaz — paylaşımlı barındırmada bu yetki genelde yok).");
        sb.AppendLine("3. \"Veritabanını Yönet\" / phpMyAdmin bağlantısını açın.");
        sb.AppendLine("4. \"İçe Aktar\" (Import) sekmesine gidin.");
        if (isSplit)
        {
            sb.AppendLine("5. Bu pakette BİRDEN FAZLA .sql dosyası var (dosya boyutu sınırını aşmamak");
            sb.AppendLine("   için bölündü) — dosyaları numara sırasına göre (part1, part2, ...) TEK TEK");
            sb.AppendLine("   içe aktarın. Sırayı değiştirmeyin.");
        }
        else
        {
            sb.AppendLine("5. schema.sql dosyasını seçip içe aktarın.");
        }
        sb.AppendLine();
        sb.AppendLine("Dikkat:");
        sb.AppendLine("- Karakter kümesi: dosya utf8mb4 kullanır. Panel varsayılanı farklıysa");
        sb.AppendLine("  (latin1/utf8) içe aktarma sırasında karakter kümesini utf8mb4 olarak");
        sb.AppendLine("  seçin — aksi hâlde Türkçe karakterler bozulabilir.");
        sb.AppendLine("- Bu dosyayı Namines üretti ama ÇALIŞTIRMADI. Doğruluğunu içe aktarmadan");
        sb.AppendLine("  önce gözden geçirin.");
        return sb.ToString();
    }

    private const string SqliteInstructions =
        "Namines — Mobil (SQLite) paketi\n" +
        "=================================\n\n" +
        "schema.db  — uygulamanızın asset/kaynak klasörüne gömebileceğiniz, hazır\n" +
        "             oluşturulmuş bir SQLite veritabanı (tablolar dahil, veri yok).\n" +
        "schema.sql — aynı şemanın düz metin DDL'i, ileride migration yazarken referans.\n\n" +
        "Bu dosyayı Namines üretti ama hiçbir cihaza yüklemedi/çalıştırmadı — bunu\n" +
        "uygulamanızın kendi paketleme adımına siz eklersiniz.\n\n" +
        "Örnek (iOS/Swift, Bundle içine gömülü .db'yi ilk açılışta kopyalama):\n" +
        "  let bundled = Bundle.main.url(forResource: \"schema\", withExtension: \"db\")!\n" +
        "  try FileManager.default.copyItem(at: bundled, to: destinationURL)\n\n" +
        "Örnek (Android/Kotlin, assets içinden kopyalama):\n" +
        "  assets.open(\"schema.db\").use { input -> destFile.outputStream().use { input.copyTo(it) } }\n\n" +
        "Örnek (Flutter, sqflite ile):\n" +
        "  final bytes = await rootBundle.load('assets/schema.db');\n" +
        "  await File(path).writeAsBytes(bytes.buffer.asUint8List());\n\n" +
        "Şemayı sonradan değiştirirseniz bu dosyayı yeniden üretip PAKETİNİZDEKİ\n" +
        "eskisinin yerine koyun — Namines cihazlardaki var olan .db'leri migrate etmez,\n" +
        "bu uygulamanızın kendi migration mantığının işi.\n";
}
