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
        header.AppendLine("-- Namines — generated for shared hosting (second-phase/13-DAGITIM-HEDEFLERI.md)");
        header.AppendLine("-- This file assumes the database ALREADY EXISTS; it contains no CREATE DATABASE.");
        header.AppendLine("-- Create an empty database in your panel first, then import this file via phpMyAdmin.");

        if (schema.Tables.Any(t => t.Checks.Count > 0))
        {
            header.AppendLine("--");
            header.AppendLine("-- NOTE: CHECK constraints are ENFORCED only on MySQL 8.0.16+ / MariaDB 10.2+.");
            header.AppendLine("-- MySQL 5.7, still common on shared hosting, ACCEPTS this syntax but silently");
            header.AppendLine("-- IGNORES it — check the engine version of the target panel beforehand.");
        }
        header.AppendLine();

        // Satır sonlarını normalize et: hedef panellerin çoğu Linux ve karışık
        // CRLF/LF bir dosya bazı içe aktarma araçlarını şaşırtıyor. Ayrıca
        // bölmenin ölçtüğü boyutla yazılan boyut birebir aynı kalıyor.
        var body = (header + ddl).Replace("\r\n", "\n");

        // Her parçaya AYRI AYRI sarmalanıyor, tek bir kez başa değil:
        // FOREIGN_KEY_CHECKS MySQL'de OTURUM kapsamlıdır ve README kullanıcıya
        // dosyaları tek tek içe aktarmasını söylüyor — her içe aktarma yeni bir
        // oturum. Yalnızca ilk parçaya yazılırsa 2. ve sonraki parçalar
        // kontroller AÇIK yüklenir ve sarmalayıcının var olma sebebi olan
        // döngüsel/ileri referanslı FK durumu yine patlar.
        const string prologue = "SET FOREIGN_KEY_CHECKS=0;\n\n";
        const string epilogue = "\nSET FOREIGN_KEY_CHECKS=1;\n";
        var overhead = Encoding.UTF8.GetByteCount(prologue) + Encoding.UTF8.GetByteCount(epilogue);

        // Bütçeden sarmalayıcı payı düşülüyor; aksi hâlde sarmalanmış parça
        // sınırı aşar ve panel yine reddeder.
        var parts = SqlFileSplitter.Split(body, MaxFileBytes - overhead);
        var wrapped = parts.Select(p => prologue + p + epilogue).ToList();

        var files = wrapped.Count == 1
            ? new List<ExportedFile> { new("schema.sql", Encoding.UTF8.GetBytes(wrapped[0])) }
            : wrapped.Select((p, i) => new ExportedFile($"schema_part{i + 1}_of_{wrapped.Count}.sql", Encoding.UTF8.GetBytes(p))).ToList();

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
        sb.AppendLine($"Namines — {panelName} shared hosting package");
        sb.AppendLine("=========================================");
        sb.AppendLine();
        sb.AppendLine("1. Sign in to your hosting panel (Plesk / cPanel / DirectAdmin).");
        sb.AppendLine("2. In the Databases section, create an EMPTY database (Namines does not");
        sb.AppendLine("   create it for you — shared hosting rarely grants that permission).");
        sb.AppendLine("3. Open the \"Manage Database\" / phpMyAdmin link.");
        sb.AppendLine("4. Go to the \"Import\" tab.");
        if (isSplit)
        {
            sb.AppendLine("5. This package contains MULTIPLE .sql files (split to stay under the file");
            sb.AppendLine("   size limit) — import them ONE BY ONE in numerical order (part1, part2,");
            sb.AppendLine("   ...). Do not change the order.");
        }
        else
        {
            sb.AppendLine("5. Select schema.sql and import it.");
        }
        sb.AppendLine();
        sb.AppendLine("Please note:");
        sb.AppendLine("- Character set: the file uses utf8mb4. If your panel defaults to something");
        sb.AppendLine("  else (latin1/utf8), select utf8mb4 as the character set during import —");
        sb.AppendLine("  otherwise non-ASCII characters may be corrupted.");
        sb.AppendLine("- Namines generated this file but did NOT execute it. Review it for");
        sb.AppendLine("  correctness before importing.");
        return sb.ToString();
    }

    private const string SqliteInstructions =
        "Namines — Mobile (SQLite) package\n" +
        "=================================\n\n" +
        "schema.db  — a ready-built SQLite database (tables included, no data) that you\n" +
        "             can embed in your application's asset/resource folder.\n" +
        "schema.sql — the plain-text DDL of the same schema, for reference when you write\n" +
        "             migrations later.\n\n" +
        "Namines generated this file but never installed or executed it on any device —\n" +
        "wiring it into your application's own packaging step is up to you.\n\n" +
        "Example (iOS/Swift, copying the bundled .db on first launch):\n" +
        "  let bundled = Bundle.main.url(forResource: \"schema\", withExtension: \"db\")!\n" +
        "  try FileManager.default.copyItem(at: bundled, to: destinationURL)\n\n" +
        "Example (Android/Kotlin, copying from assets):\n" +
        "  assets.open(\"schema.db\").use { input -> destFile.outputStream().use { input.copyTo(it) } }\n\n" +
        "Example (Flutter, with sqflite):\n" +
        "  final bytes = await rootBundle.load('assets/schema.db');\n" +
        "  await File(path).writeAsBytes(bytes.buffer.asUint8List());\n\n" +
        "If you change the schema later, regenerate this file and replace the old one IN\n" +
        "YOUR PACKAGE — Namines does not migrate .db files already on devices; that is\n" +
        "your application's own migration logic.\n";
}
