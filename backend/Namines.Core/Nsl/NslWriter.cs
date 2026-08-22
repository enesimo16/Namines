using System;
using Namines.Core.Analysis;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Nsl;

/// <summary>
/// Şemayı <c>.nsl</c> metin biçimine yazar (new-phase/04-NSL-SCHEMA-IR.md §2).
///
/// <b>Neden metin biçimi:</b> tasarım hedefi #3 — git'te diff'lenebilir olması.
/// JSON şema da diff'lenir ama okunmaz: bir kolonun eklendiğini görmek için 40
/// satırlık bir blok farkını okumak gerekir. NSL'de aynı değişiklik tek satır.
///
/// <b>Kapsam, bilinçli:</b> doküman §2 enum, view, RLS, <c>@ui</c> ve <c>@tag</c>
/// da tanımlıyor; mevcut <see cref="DatabaseSchema"/> bunları TAŞIMIYOR. Yazıcı
/// yalnızca modelde gerçekten var olanı üretiyor — olmayan bir alanı boş da olsa
/// yazmak, biçimi destekleniyormuş gibi gösterip ilk kullanımda hayal kırıklığı
/// yaratırdı. Model büyüdükçe biçim de büyür.
///
/// <b>Deterministik:</b> aynı şema her zaman aynı metni verir (hedef #4). Sıra
/// modelin sırasıdır; alfabetik sıralamak, kullanıcının canvas'ta düzenlediği
/// mantıksal sırayı bozardı ve her açılışta gereksiz diff üretirdi.
/// </summary>
public static class NslWriter
{
    public const string FormatVersion = "1.0";

    public static string Write(DatabaseSchema schema, DatabaseType engine = DatabaseType.PostgreSQL)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var sb = new StringBuilder();
        sb.AppendLine($"nsl {FormatVersion}");
        sb.AppendLine();
        sb.AppendLine($"project {Quote(schema.Name)} {{");
        sb.AppendLine($"  engine {engine.ToString().ToLowerInvariant()}");
        sb.AppendLine("}");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            WriteTable(sb, schema, table);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void WriteTable(StringBuilder sb, DatabaseSchema schema, SchemaTable table)
    {
        sb.AppendLine($"table {Identifier(table.Name)} {{");

        // Kararlı kimlik (hedef #5): yeniden adlandırma diff'i bozmasın diye uuid
        // metne yazılıyor. Yazılmazsa, dosyadan geri okunan şema her seferinde yeni
        // kimlik alır ve "yeniden adlandırma" ile "sil + ekle" ayırt edilemez —
        // bu tam olarak SchemaIdentity'de düzeltilen hatanın metin karşılığı olurdu.
        if (!string.IsNullOrWhiteSpace(table.StableUuid))
            sb.AppendLine($"  @uuid({Quote(table.StableUuid)})");

        var pkCount = table.Columns.Count(c => c.IsPK);

        foreach (var column in table.Columns)
            sb.AppendLine("  " + ColumnLine(column, pkCount));

        var pks = table.Columns.Where(c => c.IsPK).ToList();
        // Bileşik anahtar ayrı satırda: kolon başına "pk" yazmak, hangi kolonların
        // BİRLİKTE anahtar olduğunu belirsiz bırakır.
        if (pks.Count > 1)
            sb.AppendLine($"  primary key ({string.Join(", ", pks.Select(c => Identifier(c.Name)))})");

        foreach (var unique in table.Uniques)
        {
            var columns = ResolveColumns(table, unique.ColumnIds);
            if (columns.Count == 0) continue;
            sb.AppendLine($"  unique ({string.Join(", ", columns)}){NameSuffix(unique.Name)}");
        }

        foreach (var index in table.Indexes)
        {
            var columns = index.Columns
                .Select(ic => table.Columns.FirstOrDefault(c => c.Id == ic.ColumnId) is { } column
                    ? Identifier(column.Name) + (ic.Descending ? " desc" : string.Empty)
                    : null)
                .Where(c => c is not null)
                .ToList();

            if (columns.Count == 0) continue;

            var line = new StringBuilder($"  index ({string.Join(", ", columns)})");
            if (index.IsUnique) line.Append(" unique");
            if (!string.IsNullOrWhiteSpace(index.Where)) line.Append($" where {Quote(index.Where!)}");
            line.Append(NameSuffix(index.Name));
            sb.AppendLine(line.ToString());
        }

        foreach (var check in table.Checks.Where(c => !string.IsNullOrWhiteSpace(c.Expression)))
            sb.AppendLine($"  check {Quote(check.Expression)}{NameSuffix(check.Name)}");

        foreach (var relation in schema.Relations.Where(r => r.SourceTableId == table.Id))
        {
            var source = table.Columns.FirstOrDefault(c => c.Id == relation.SourceColumnId);
            var target = schema.Tables.FirstOrDefault(t => t.Id == relation.TargetTableId);
            var targetColumn = target?.Columns.FirstOrDefault(c => c.Id == relation.TargetColumnId);
            if (source is null || target is null || targetColumn is null) continue;

            sb.AppendLine(
                $"  fk ({Identifier(source.Name)}) -> {Identifier(target.Name)}({Identifier(targetColumn.Name)})" +
                $" on delete {Action(relation.OnDelete)} on update {Action(relation.OnUpdate)}");
        }

        sb.AppendLine("}");
    }

    private static string ColumnLine(SchemaColumn column, int primaryKeyCount)
    {
        var sb = new StringBuilder($"{Identifier(column.Name)} {TypeText(column)}");

        if (column.IsPK) sb.Append(" pk");

        // Yalnızca ÇIKARIMDAN FARKLI olduğunda yazılıyor. Her tamsayı anahtara
        // "identity" eklemek dosyayı, hiçbir şey söylemeyen bir kelimeyle
        // doldururdu; asıl bilgi, kullanıcının çıkarımı BOZDUĞU yerdedir.
        var inferred = IdentityPolicy.IsGenerated(
            new SchemaColumn { Name = column.Name, Type = column.Type, IsPK = column.IsPK },
            primaryKeyCount);

        if (column.Identity == true && !inferred) sb.Append(" identity");
        else if (column.Identity == false && inferred) sb.Append(" no identity");
        // PK zaten NOT NULL'dır; ayrıca yazmak gürültü ve okuyanı "acaba
        // nullable bir PK mı var" diye düşündürür.
        if (!column.IsNullable && !column.IsPK) sb.Append(" not null");
        if (!string.IsNullOrWhiteSpace(column.DefaultValue)) sb.Append($" default({column.DefaultValue})");
        if (!string.IsNullOrWhiteSpace(column.StableUuid)) sb.Append($" @uuid({Quote(column.StableUuid)})");

        return sb.ToString();
    }

    private static string TypeText(SchemaColumn column)
    {
        var type = (column.Type ?? "text").Trim().ToLowerInvariant();
        return column.Length is > 0 ? $"{type}({column.Length})" : type;
    }

    private static List<string> ResolveColumns(SchemaTable table, IEnumerable<string> columnIds) =>
        columnIds
            .Select(id => table.Columns.FirstOrDefault(c => c.Id == id))
            .Where(c => c is not null)
            .Select(c => Identifier(c!.Name))
            .ToList();

    private static string NameSuffix(string? name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : $" name: {Identifier(name)}";

    private static string Action(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => "cascade",
        ReferentialAction.Restrict => "restrict",
        ReferentialAction.SetNull => "set null",
        ReferentialAction.SetDefault => "set default",
        _ => "no action",
    };

    /// <summary>
    /// Tanımlayıcı. Boşluk ya da özel karakter taşıyorsa tırnaklanır — aksi hâlde
    /// üretilen dosya kendi ayrıştırıcımız tarafından okunamaz hâle gelir.
    /// </summary>
    internal static string Identifier(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";

        var safe = value.All(c => char.IsLetterOrDigit(c) || c == '_') && !char.IsDigit(value[0]);
        return safe ? value : Quote(value);
    }

    internal static string Quote(string? value) =>
        "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
