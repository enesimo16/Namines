using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// second-phase/11-KODDAN-SEMA.md ("ham CREATE TABLE dosyaları") ve
/// second-phase/12-ENTEGRASYONLAR.md adım 2 — Supabase migration klasöründeki
/// <c>*.sql</c> dosyaları.
///
/// <b>Migration DOSYALARI OKUNUR, ÇALIŞTIRILMAZ.</b> Doc 11'in açık güvenlik
/// yasağı: rastgele bir depodan gelen migration'ı çalıştırmak kod çalıştırmaktır.
/// Bu sınıf yalnızca metin ayrıştırır; hiçbir veritabanına bağlanmaz.
///
/// <b>Kapsam bilinçli olarak dar:</b> <c>CREATE TABLE</c> ve
/// <c>ALTER TABLE ... ADD CONSTRAINT ... FOREIGN KEY</c>. Bir migration
/// klasörü çok daha fazlasını içerir (fonksiyonlar, tetikleyiciler, RLS
/// politikaları, <c>ALTER COLUMN</c>); anlaşılmayan her ifade
/// <see cref="SkippedItem"/> olarak SAYILIR — sessizce atlamak, kullanıcının
/// eksik bir şemayı tam sanmasına yol açardı.
///
/// <b>Sıra önemlidir</b> — migration dosyaları zaman sırasıyla verilmelidir;
/// sonraki bir dosya öncekinin tablosuna kolon ekleyebilir.
/// </summary>
public static class SqlDdlSchemaParser
{
    /// <summary>
    /// Tırnaklı bir tanımlayıcı (içinde BOŞLUK olabilir: <c>"Order Items"</c>)
    /// ya da tırnaksız bir ad. Şema öneki (<c>public.users</c>) da kabul edilir.
    /// </summary>
    private const string Ident = @"(?:""[^""]+""|`[^`]+`|\[[^\]]+\]|[\w]+)(?:\.(?:""[^""]+""|`[^`]+`|\[[^\]]+\]|[\w]+))?";

    private static readonly Regex CreateTable = new(
        $@"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?({Ident})\s*\((.*?)\)\s*;",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AlterAddFk = new(
        $@"ALTER\s+TABLE\s+({Ident})\s+ADD\s+CONSTRAINT\s+{Ident}\s+FOREIGN\s+KEY\s*\(\s*({Ident})\s*\)\s*REFERENCES\s+({Ident})\s*\(\s*({Ident})\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex InlineFk = new(
        $@"REFERENCES\s+({Ident})\s*\(\s*({Ident})\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Kolon adı + geri kalan her şey. Tip ayrıştırması <see cref="SplitTypeAndRest"/>'te.</summary>
    private static readonly Regex ColumnDef = new(
        $@"^\s*({Ident})\s+(.+)$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Tipten SONRA gelebilecek anahtar kelimeler. Tip bunlardan birine kadar
    /// olan kısımdır — böylece hem tek kelimelik (<c>timestamptz</c>) hem çok
    /// kelimelik (<c>character varying</c>, <c>double precision</c>) tipler
    /// doğru yakalanıyor.
    /// </summary>
    private static readonly string[] PostTypeKeywords =
    {
        "NOT", "NULL", "DEFAULT", "PRIMARY", "REFERENCES", "CHECK", "UNIQUE",
        "GENERATED", "CONSTRAINT", "COLLATE", "IDENTITY", "AUTO_INCREMENT",
    };

    /// <summary>Kolon satırı değil, tablo seviyesi kısıt olan başlangıçlar.</summary>
    private static readonly string[] ConstraintStarters =
        { "PRIMARY", "FOREIGN", "UNIQUE", "CHECK", "CONSTRAINT", "KEY", "INDEX", "EXCLUDE" };

    public static CodeExtractionResult Parse(string sql)
    {
        var schema = new DatabaseSchema { Name = "from-sql" };
        var parsed = new List<string>();
        var skipped = new List<SkippedItem>();
        var pendingFks = new List<(string SourceTable, string SourceCol, string TargetTable, string TargetCol)>();

        var cleaned = StripComments(sql);

        foreach (Match m in CreateTable.Matches(cleaned))
        {
            var rawName = m.Groups[1].Value;
            var tableName = Unquote(rawName);

            // Supabase/Postgres'te şema öneki olabilir ("public.users"). İç
            // şemalar (auth, storage, realtime) kullanıcının tablosu DEĞİLDİR —
            // onları şemaya koymak, kullanıcıya kendi olmayan tabloları gösterir.
            var (schemaPrefix, bareName) = SplitSchema(tableName);
            if (schemaPrefix is "auth" or "storage" or "realtime" or "vault" or "extensions" or "graphql" or "supabase_functions")
            {
                skipped.Add(new SkippedItem(tableName, $"'{schemaPrefix}' is an internal schema, not one of your tables"));
                continue;
            }

            var table = new SchemaTable { Id = bareName, Name = bareName };
            ParseBody(table, m.Groups[2].Value, skipped, pendingFks);

            if (table.Columns.Count == 0)
            {
                skipped.Add(new SkippedItem(tableName, "no columns could be parsed from the CREATE TABLE body"));
                continue;
            }

            // Aynı tablo iki migration'da geçebilir (nadir ama olur) — sonuncusu kazanır.
            var existing = schema.Tables.FirstOrDefault(t => t.Id == table.Id);
            if (existing is not null) schema.Tables.Remove(existing);

            schema.Tables.Add(table);
            if (!parsed.Contains(bareName)) parsed.Add(bareName);
        }

        foreach (Match m in AlterAddFk.Matches(cleaned))
        {
            pendingFks.Add((
                SplitSchema(Unquote(m.Groups[1].Value)).Bare,
                Unquote(m.Groups[2].Value),
                SplitSchema(Unquote(m.Groups[3].Value)).Bare,
                Unquote(m.Groups[4].Value)));
        }

        ResolveForeignKeys(schema, pendingFks, skipped);

        if (schema.Tables.Count == 0)
            skipped.Add(new SkippedItem("(file)", "no CREATE TABLE statement was found"));

        return new CodeExtractionResult(schema, "sql", parsed, skipped);
    }

    private static void ParseBody(
        SchemaTable table,
        string body,
        List<SkippedItem> skipped,
        List<(string, string, string, string)> pendingFks)
    {
        foreach (var part in SplitTopLevel(body))
        {
            var line = part.Trim().TrimEnd(',').Trim();
            if (line.Length == 0) continue;

            var firstWord = line.Split(' ', '(')[0].TrimStart('"', '`', '[').ToUpperInvariant();

            if (ConstraintStarters.Contains(firstWord))
            {
                // Tablo seviyesi PRIMARY KEY (a, b) — kolonları işaretle.
                if (firstWord == "PRIMARY")
                {
                    var cols = Regex.Match(line, @"\(\s*(.+?)\s*\)");
                    if (cols.Success)
                    {
                        foreach (var name in cols.Groups[1].Value.Split(',').Select(s => Unquote(s.Trim())))
                        {
                            var pkCol = table.Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                            if (pkCol is not null) { pkCol.IsPK = true; pkCol.IsNullable = false; }
                        }
                    }
                }
                // Diğer tablo seviyesi kısıtlar (CHECK/UNIQUE/EXCLUDE) bu
                // kademede modele taşınmıyor ve bu, dürüstçe bildiriliyor.
                else if (firstWord is "CHECK" or "UNIQUE" or "EXCLUDE")
                {
                    skipped.Add(new SkippedItem($"{table.Name}: {Truncate(line)}", $"table-level {firstWord} constraint is not imported"));
                }
                continue;
            }

            var col = ColumnDef.Match(line);
            if (!col.Success)
            {
                skipped.Add(new SkippedItem($"{table.Name}: {Truncate(line)}", "line could not be parsed as a column"));
                continue;
            }

            var colName = Unquote(col.Groups[1].Value);
            var (rawType, length, rest) = SplitTypeAndRest(col.Groups[2].Value);

            var column = new SchemaColumn
            {
                Id = $"{table.Id}.{colName}",
                Name = colName,
                Type = NormaliseType(rawType),
                IsNullable = !Regex.IsMatch(rest, @"\bNOT\s+NULL\b", RegexOptions.IgnoreCase),
                Length = length,
            };

            // SERIAL/BIGSERIAL ve IDENTITY: değeri veritabanı üretir.
            if (rawType.Contains("SERIAL") || Regex.IsMatch(rest, @"\bGENERATED\s+.*AS\s+IDENTITY\b", RegexOptions.IgnoreCase))
                column.Identity = true;

            if (Regex.IsMatch(rest, @"\bPRIMARY\s+KEY\b", RegexOptions.IgnoreCase))
            {
                column.IsPK = true;
                column.IsNullable = false;
            }

            var def = Regex.Match(rest, @"\bDEFAULT\s+(.+?)(?:\s+(?:NOT\s+NULL|NULL|PRIMARY|REFERENCES|CHECK|UNIQUE)\b|$)", RegexOptions.IgnoreCase);
            if (def.Success) column.DefaultValue = def.Groups[1].Value.Trim();

            var fk = InlineFk.Match(rest);
            if (fk.Success)
            {
                pendingFks.Add((
                    table.Id,
                    colName,
                    SplitSchema(Unquote(fk.Groups[1].Value)).Bare,
                    Unquote(fk.Groups[2].Value)));
            }

            table.Columns.Add(column);
        }
    }

    private static void ResolveForeignKeys(
        DatabaseSchema schema,
        List<(string SourceTable, string SourceCol, string TargetTable, string TargetCol)> pending,
        List<SkippedItem> skipped)
    {
        foreach (var (sourceTable, sourceCol, targetTable, targetCol) in pending)
        {
            var src = schema.Tables.FirstOrDefault(t => t.Id.Equals(sourceTable, StringComparison.OrdinalIgnoreCase));
            var tgt = schema.Tables.FirstOrDefault(t => t.Id.Equals(targetTable, StringComparison.OrdinalIgnoreCase));

            if (src is null || tgt is null)
            {
                skipped.Add(new SkippedItem($"{sourceTable}.{sourceCol} → {targetTable}.{targetCol}", "foreign key points at a table that was not parsed"));
                continue;
            }

            var srcCol = src.Columns.FirstOrDefault(c => c.Name.Equals(sourceCol, StringComparison.OrdinalIgnoreCase));
            var tgtCol = tgt.Columns.FirstOrDefault(c => c.Name.Equals(targetCol, StringComparison.OrdinalIgnoreCase));

            if (srcCol is null || tgtCol is null)
            {
                skipped.Add(new SkippedItem($"{sourceTable}.{sourceCol} → {targetTable}.{targetCol}", "foreign key references a column that was not parsed"));
                continue;
            }

            if (schema.Relations.Any(r => r.SourceColumnId == srcCol.Id && r.TargetColumnId == tgtCol.Id)) continue;

            srcCol.IsFK = true;
            schema.Relations.Add(new SchemaRelation
            {
                Id = $"{src.Id}_{srcCol.Name}_fk",
                Type = "ManyToOne",
                SourceTableId = src.Id,
                SourceColumnId = srcCol.Id,
                TargetTableId = tgt.Id,
                TargetColumnId = tgtCol.Id,
            });
        }
    }

    /// <summary>
    /// Kolon tanımının tip kısmını geri kalanından ayırır.
    ///
    /// <b>Neden ayrı bir adım:</b> tek bir regex ile denendi ve tembel niceleyici
    /// (<c>+?</c>) tipin yalnızca İLK HARFİNİ yakaladı ("BIGSERIAL" → "B") —
    /// gerçek bir hata, testlerle yakalandı. Tip, bir sonraki SQL anahtar
    /// kelimesine kadar olan kısımdır; bu, hem <c>timestamptz</c> hem
    /// <c>character varying</c> için doğru çalışır.
    /// </summary>
    private static (string Type, int? Length, string Remainder) SplitTypeAndRest(string afterName)
    {
        var text = afterName.Trim();
        var typeWords = new List<string>();
        int? length = null;
        var i = 0;

        while (i < text.Length)
        {
            // Boşlukları atla
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;

            // Uzunluk/hassasiyet: "(64)" ya da "(10,2)" — tipin parçası, sonrası "rest".
            if (text[i] == '(')
            {
                var close = text.IndexOf(')', i);
                if (close < 0) break;
                var inner = text[(i + 1)..close];
                var first = inner.Split(',')[0].Trim();
                if (int.TryParse(first, out var parsedLen)) length = parsedLen;
                i = close + 1;
                break; // parantezden sonrası her zaman "rest"
            }

            var wordStart = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(') i++;
            var word = text[wordStart..i];

            // Anahtar kelimeye geldiysek tip bitti — bu kelime "rest"e ait.
            if (PostTypeKeywords.Contains(word.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase))
            {
                i = wordStart;
                break;
            }

            typeWords.Add(word);
        }

        return (string.Join(" ", typeWords).ToUpperInvariant(), length, text[Math.Min(i, text.Length)..]);
    }

    /// <summary>Parantez derinliğini sayarak virgülle böler — <c>NUMERIC(10,2)</c> içindeki virgül kolon ayırıcısı değildir.</summary>
    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '(') depth++;
            else if (body[i] == ')') depth--;
            else if (body[i] == ',' && depth == 0)
            {
                yield return body[start..i];
                start = i + 1;
            }
        }
        if (start < body.Length) yield return body[start..];
    }

    private static string StripComments(string sql)
    {
        sql = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        sql = Regex.Replace(sql, @"--[^\n]*", " ");
        return sql;
    }

    private static string Unquote(string identifier) =>
        identifier.Trim().Trim('"', '`', '[', ']');

    private static (string? Prefix, string Bare) SplitSchema(string name)
    {
        var idx = name.IndexOf('.');
        return idx < 0
            ? (null, name)
            : (Unquote(name[..idx]).ToLowerInvariant(), Unquote(name[(idx + 1)..]));
    }

    /// <summary>Motor-özel tipleri kanonik modelin bildiği adlara çevirir.</summary>
    private static string NormaliseType(string rawType)
    {
        var t = Regex.Replace(rawType, @"\s+", " ").Trim();

        return t switch
        {
            "SERIAL" or "SERIAL4" => "INT",
            "BIGSERIAL" or "SERIAL8" => "BIGINT",
            "SMALLSERIAL" or "SERIAL2" => "SMALLINT",
            "INT4" or "INTEGER" => "INT",
            "INT8" => "BIGINT",
            "INT2" => "SMALLINT",
            "BOOL" => "BOOLEAN",
            "CHARACTER VARYING" or "VARCHAR" => "VARCHAR",
            "CHARACTER" => "CHAR",
            "TIMESTAMPTZ" or "TIMESTAMP WITH TIME ZONE" or "TIMESTAMP WITHOUT TIME ZONE" => "TIMESTAMP",
            "FLOAT8" or "DOUBLE PRECISION" => "FLOAT",
            "FLOAT4" or "REAL" => "FLOAT",
            "BYTEA" => "BLOB",
            _ => t,
        };
    }

    private static string Truncate(string s) => s.Length <= 60 ? s : s[..60] + "…";
}
