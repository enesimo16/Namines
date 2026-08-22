using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Core.Nsl;

/// <summary>Ayrıştırma hatası — satır numarasıyla, çünkü "geçersiz NSL" tek başına işe yaramaz.</summary>
public sealed class NslParseException : Exception
{
    public NslParseException(int line, string message)
        : base($"Line {line}: {message}") => Line = line;

    public int Line { get; }
}

/// <summary>
/// <c>.nsl</c> metnini şemaya çevirir (new-phase/04-NSL-SCHEMA-IR.md §2).
///
/// <b>Satır tabanlı, token tabanlı değil.</b> Tam bir sözcüksel çözümleyici +
/// özyinelemeli iniş ayrıştırıcısı yazmak, biçim bugünkü hâlinde satır başına bir
/// bildirim taşıdığı için karşılığını vermezdi. Biçim iç içe ifadeler kazandığında
/// (view SQL'i, hesaplanmış kolonlar) gerçek bir ayrıştırıcıya geçilmeli — o zaman
/// bu sınıf değişir, çağıranlar değişmez.
///
/// <b>Tanımadığı satırda PATLAR, atlamaz.</b> Sessizce atlamak, yazım hatası
/// içeren bir kısıtın kaybolması ve kullanıcının şemasını eksik geri alması
/// demektir — ve bunu ancak veritabanı reddettiğinde fark eder.
/// </summary>
public static class NslParser
{
    public static DatabaseSchema Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var schema = new DatabaseSchema { Name = string.Empty };
        var pendingRelations = new List<(SchemaTable From, string FromColumn, string ToTable, string ToColumn, string OnDelete, string OnUpdate)>();

        SchemaTable? current = null;
        var lineNumber = 0;

        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("nsl ", StringComparison.Ordinal)) continue;

            if (line.StartsWith("project ", StringComparison.Ordinal))
            {
                schema.Name = ReadQuoted(line, lineNumber);
                continue;
            }

            // project/table bloklarının içindeki basit ayarlar (engine, naming).
            // Modelde karşılıkları yok; ATLANMASI güvenli çünkü şema anlamını
            // değiştirmiyorlar, yalnızca derleme hedefini bildiriyorlar.
            if (current is null && (line.StartsWith("engine ", StringComparison.Ordinal) ||
                                    line.StartsWith("default_schema ", StringComparison.Ordinal) ||
                                    line.StartsWith("naming ", StringComparison.Ordinal)))
                continue;

            if (line == "}")
            {
                current = null;
                continue;
            }

            if (line.StartsWith("table ", StringComparison.Ordinal))
            {
                var name = line[6..].Replace("{", string.Empty).Trim();
                current = new SchemaTable
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = Unquote(name),
                };
                schema.Tables.Add(current);
                continue;
            }

            if (current is null)
                throw new NslParseException(lineNumber, $"Unexpected statement outside a table: '{line}'.");

            ParseTableMember(schema, current, line, lineNumber, pendingRelations);
        }

        ResolveRelations(schema, pendingRelations);
        return schema;
    }

    private static void ParseTableMember(
        DatabaseSchema schema, SchemaTable table, string line, int lineNumber,
        List<(SchemaTable, string, string, string, string, string)> pendingRelations)
    {
        if (line.StartsWith("@uuid(", StringComparison.Ordinal))
        {
            table.StableUuid = ReadQuoted(line, lineNumber);
            return;
        }

        if (line.StartsWith("primary key ", StringComparison.Ordinal))
        {
            foreach (var name in ReadList(line, lineNumber))
            {
                var column = FindColumn(table, name, lineNumber);
                column.IsPK = true;
                // Bileşik anahtarın parçası nullable olamaz; NSL016.
                column.IsNullable = false;
            }
            return;
        }

        if (line.StartsWith("unique (", StringComparison.Ordinal))
        {
            table.Uniques.Add(new SchemaUnique
            {
                Id = Guid.NewGuid().ToString(),
                Name = ReadName(line),
                ColumnIds = ReadList(line, lineNumber).Select(n => FindColumn(table, n, lineNumber).Id).ToList(),
            });
            return;
        }

        if (line.StartsWith("index (", StringComparison.Ordinal))
        {
            var index = new SchemaIndex
            {
                Id = Guid.NewGuid().ToString(),
                Name = ReadName(line),
                IsUnique = ContainsKeyword(line, "unique"),
                Where = ReadWhere(line),
            };

            foreach (var entry in ReadList(line, lineNumber))
            {
                var descending = entry.EndsWith(" desc", StringComparison.OrdinalIgnoreCase);
                var name = descending ? entry[..^5].Trim() : entry;
                index.Columns.Add(new SchemaIndexColumn
                {
                    ColumnId = FindColumn(table, name, lineNumber).Id,
                    Descending = descending,
                });
            }

            table.Indexes.Add(index);
            return;
        }

        if (line.StartsWith("check ", StringComparison.Ordinal))
        {
            table.Checks.Add(new SchemaCheck
            {
                Id = Guid.NewGuid().ToString(),
                Name = ReadName(line),
                Expression = ReadQuoted(line, lineNumber),
            });
            return;
        }

        if (line.StartsWith("fk (", StringComparison.Ordinal))
        {
            pendingRelations.Add(ParseForeignKey(table, line, lineNumber));
            return;
        }

        // Buraya düşen her satır bir kolon bildirimi olmalı.
        table.Columns.Add(ParseColumn(line, lineNumber));
    }

    private static SchemaColumn ParseColumn(string line, int lineNumber)
    {
        var uuid = line.Contains("@uuid(", StringComparison.Ordinal) ? ReadQuoted(line, lineNumber, "@uuid(") : null;
        var defaultValue = ReadDefault(line);

        // Nitelikler çıkarıldıktan sonra geriye "ad tip" kalmalı.
        var head = line;
        foreach (var marker in new[] { "@uuid(", "default(" })
        {
            var index = head.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0) head = head[..index];
        }

        var parts = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new NslParseException(lineNumber, $"Could not read a column declaration from '{line}'.");

        // Tip belirteci HARFLE başlamalı. Bu kontrol olmadan yazım hatası içeren
        // bir kısıt ("uniqe (id)") sessizce "uniqe" adlı, tipi "(id)" olan bir
        // HAYALET KOLONA dönüşüyordu — kısıt kayboluyor, şema eksik geri geliyor
        // ve kullanıcı bunu ancak veritabanı reddedince fark ediyordu.
        if (!char.IsLetter(parts[1][0]))
            throw new NslParseException(lineNumber,
                $"'{parts[0]}' is not a known statement, and '{line}' is not a valid column declaration.");

        var name = Unquote(parts[0]);
        var (type, length) = ParseType(parts[1]);

        var isPk = ContainsKeyword(head, "pk");

        // "no identity" ÖNCE aranıyor: "identity" araması ona da eşleşir ve sıra
        // ters olsaydı "no identity" sessizce "identity" olarak okunurdu — yani
        // kullanıcının "bu anahtarı ben atıyorum" demesi tam tersine çevrilirdi.
        bool? identity = ContainsKeyword(head, "no identity") ? false
            : ContainsKeyword(head, "identity") ? true
            : null;

        return new SchemaColumn
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Type = type,
            Length = length,
            IsPK = isPk,
            // Yazıcı PK için "not null" yazmıyor (gereksiz gürültü); okuyucu da
            // PK'yı her zaman NOT NULL saymalı, yoksa round-trip bozulur.
            IsNullable = !isPk && !ContainsKeyword(head, "not null"),
            DefaultValue = defaultValue,
            Identity = identity,
            StableUuid = uuid ?? Guid.NewGuid().ToString(),
        };
    }

    private static (string Type, int? Length) ParseType(string token)
    {
        var open = token.IndexOf('(');
        if (open < 0) return (token.ToUpperInvariant(), null);

        var close = token.IndexOf(')', open);
        if (close < 0) return (token[..open].ToUpperInvariant(), null);

        var inner = token[(open + 1)..close];
        return int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length)
            ? (token[..open].ToUpperInvariant(), length)
            // decimal(19,4) gibi bileşik hassasiyet tek bir int'e sığmıyor; uzunluk
            // olarak saklamak 19'u uzunluk sanmak olurdu, o yüzden null bırakılıyor.
            : (token[..open].ToUpperInvariant(), null);
    }

    private static (SchemaTable, string, string, string, string, string) ParseForeignKey(
        SchemaTable table, string line, int lineNumber)
    {
        var arrow = line.IndexOf("->", StringComparison.Ordinal);
        if (arrow < 0) throw new NslParseException(lineNumber, "A foreign key needs '->'.");

        var sourceColumn = ReadList(line[..arrow], lineNumber).FirstOrDefault()
            ?? throw new NslParseException(lineNumber, "A foreign key needs a source column.");

        var target = line[(arrow + 2)..].Trim();
        var open = target.IndexOf('(');
        var close = target.IndexOf(')');
        if (open < 0 || close < open)
            throw new NslParseException(lineNumber, "A foreign key target must look like table(column).");

        var targetTable = Unquote(target[..open].Trim());
        var targetColumn = Unquote(target[(open + 1)..close].Trim());

        return (table, sourceColumn, targetTable, targetColumn,
            ReadAction(line, "on delete"), ReadAction(line, "on update"));
    }

    private static void ResolveRelations(
        DatabaseSchema schema,
        List<(SchemaTable From, string FromColumn, string ToTable, string ToColumn, string OnDelete, string OnUpdate)> pending)
    {
        foreach (var (from, fromColumn, toTable, toColumn, onDelete, onUpdate) in pending)
        {
            var target = schema.Tables.FirstOrDefault(t =>
                string.Equals(t.Name, toTable, StringComparison.OrdinalIgnoreCase));

            // İleriye referans mümkün olsun diye ilişkiler SONDA çözülüyor: bir tablo
            // kendisinden sonra tanımlanan bir tabloya referans verebilmeli.
            if (target is null)
                throw new NslParseException(0, $"Foreign key targets unknown table '{toTable}'.");

            var source = from.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, fromColumn, StringComparison.OrdinalIgnoreCase))
                ?? throw new NslParseException(0, $"Foreign key source column '{fromColumn}' does not exist.");

            var targetCol = target.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, toColumn, StringComparison.OrdinalIgnoreCase))
                ?? throw new NslParseException(0, $"Foreign key target column '{toColumn}' does not exist.");

            source.IsFK = true;

            schema.Relations.Add(new SchemaRelation
            {
                Id = Guid.NewGuid().ToString(),
                Type = "many-to-one",
                SourceTableId = from.Id,
                SourceColumnId = source.Id,
                TargetTableId = target.Id,
                TargetColumnId = targetCol.Id,
                OnDelete = ParseAction(onDelete),
                OnUpdate = ParseAction(onUpdate),
            });
        }
    }

    // ── Küçük yardımcılar ────────────────────────────────────────────────────

    /// <summary>
    /// Yorumu atar ama TIRNAK İÇİNDEKİ '//' dizisini korur — bir CHECK ifadesi ya
    /// da URL varsayılanı '//' içerebilir ve onu yorum sanmak ifadeyi keser.
    /// </summary>
    internal static string StripComment(string line)
    {
        var inQuotes = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '"') inQuotes = !inQuotes;
            else if (!inQuotes && line[i] == '/' && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    private static SchemaColumn FindColumn(SchemaTable table, string name, int lineNumber) =>
        table.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new NslParseException(lineNumber, $"Column '{name}' is not declared in table '{table.Name}'.");

    private static List<string> ReadList(string line, int lineNumber)
    {
        var open = line.IndexOf('(');
        var close = line.IndexOf(')', open + 1);
        if (open < 0 || close < 0)
            throw new NslParseException(lineNumber, $"Expected a parenthesised column list in '{line}'.");

        return line[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Unquote(part.Trim()))
            .ToList();
    }

    private static string? ReadName(string line)
    {
        var marker = line.IndexOf("name:", StringComparison.Ordinal);
        if (marker < 0) return null;

        var value = line[(marker + 5)..].Trim();
        var end = value.IndexOf(' ');
        return Unquote(end < 0 ? value : value[..end]);
    }

    private static string? ReadWhere(string line)
    {
        var marker = line.IndexOf("where ", StringComparison.Ordinal);
        if (marker < 0) return null;

        var rest = line[(marker + 6)..].Trim();
        return rest.StartsWith('"') ? ReadQuotedFrom(rest) : null;
    }

    private static string? ReadDefault(string line)
    {
        var marker = line.IndexOf("default(", StringComparison.Ordinal);
        if (marker < 0) return null;

        var rest = line[(marker + 8)..];
        // İç içe parantezleri say: default(now()) 'now(' değil 'now()' olmalı.
        var depth = 1;
        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i] == '(') depth++;
            else if (rest[i] == ')' && --depth == 0) return rest[..i];
        }
        return null;
    }

    private static string ReadAction(string line, string keyword)
    {
        var marker = line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return "no action";

        var rest = line[(marker + keyword.Length)..].Trim();
        foreach (var candidate in new[] { "no action", "set null", "set default", "cascade", "restrict" })
            if (rest.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) return candidate;

        return "no action";
    }

    private static Enums.ReferentialAction ParseAction(string value) => value.ToLowerInvariant() switch
    {
        "cascade" => Enums.ReferentialAction.Cascade,
        "restrict" => Enums.ReferentialAction.Restrict,
        "set null" => Enums.ReferentialAction.SetNull,
        "set default" => Enums.ReferentialAction.SetDefault,
        _ => Enums.ReferentialAction.NoAction,
    };

    /// <summary>Anahtar kelimeyi tırnak dışında ve tam sözcük olarak arar.</summary>
    private static bool ContainsKeyword(string line, string keyword)
    {
        var stripped = RemoveQuoted(line);
        var index = stripped.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(stripped[index - 1]);
            var afterIndex = index + keyword.Length;
            var after = afterIndex >= stripped.Length || !char.IsLetterOrDigit(stripped[afterIndex]);
            if (before && after) return true;
            index = stripped.IndexOf(keyword, index + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>Tırnaklı bölümleri boşlukla değiştirir — anahtar kelime araması
    /// bir CHECK ifadesinin içindeki "not null" metnine takılmasın.</summary>
    private static string RemoveQuoted(string line)
    {
        var sb = new StringBuilder(line.Length);
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && inQuotes) { i++; continue; }
            if (line[i] == '"') { inQuotes = !inQuotes; sb.Append(' '); continue; }
            sb.Append(inQuotes ? ' ' : line[i]);
        }
        return sb.ToString();
    }

    private static string ReadQuoted(string line, int lineNumber, string? after = null)
    {
        var start = after is null ? 0 : line.IndexOf(after, StringComparison.Ordinal);
        if (start < 0) throw new NslParseException(lineNumber, $"Expected '{after}' in '{line}'.");

        var quote = line.IndexOf('"', start);
        if (quote < 0) throw new NslParseException(lineNumber, $"Expected a quoted value in '{line}'.");

        return ReadQuotedFrom(line[quote..])
               ?? throw new NslParseException(lineNumber, $"Unterminated quoted value in '{line}'.");
    }

    private static string? ReadQuotedFrom(string text)
    {
        if (text.Length == 0 || text[0] != '"') return null;

        var sb = new StringBuilder();
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length) { sb.Append(text[++i]); continue; }
            if (text[i] == '"') return sb.ToString();
            sb.Append(text[i]);
        }
        return null;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\")
            : value;
}
