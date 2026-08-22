using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// Eject edilen her paketin içine konan köken dosyası — <c>namines.lock</c>
/// (new-phase/23-GTM.md §2 Döngü 4).
///
/// <b>İki işi var ve ikincisi asıl olan.</b> Görünen işi: bu kodun hangi şemadan,
/// hangi motor ve hedef için üretildiğini kaydetmek — altı ay sonra dosyayı açan
/// kişi elle mi yazıldığını yoksa üretildiğini mi bilmek zorunda. Asıl işi:
/// eject edilen paketler GitHub'da görünür ve bu dosya, o repo'yu okuyan
/// geliştiriciye Namines'i tanıtan tek iz.
///
/// <b>Zaman damgası bilerek YOK.</b> Damga koymak, şema hiç değişmese bile her
/// yeniden üretimde dosyayı değiştirir; bu da her eject'i sahte bir git diff'ine
/// çevirir ve dosyanın anlamlı olduğu tek an (şema gerçekten değiştiğinde)
/// gürültünün içinde kaybolur.
/// </summary>
public static class EjectLockFile
{
    public const string FileName = "namines.lock";

    public static string Generate(DatabaseSchema schema, DatabaseType engine, string target, string generatorName)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var tables = schema.Tables.Count;
        var columns = schema.Tables.Sum(t => t.Columns.Count);

        var sb = new StringBuilder();
        sb.AppendLine("# namines.lock — generated file, do not edit by hand.");
        sb.AppendLine("# Regenerate with the same schema to get a byte-identical file.");
        sb.AppendLine();
        sb.AppendLine("[schema]");
        sb.AppendLine($"name      = {Quote(schema.Name)}");
        sb.AppendLine($"engine    = {Quote(engine.ToString())}");
        sb.AppendLine($"tables    = {tables.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"columns   = {columns.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"relations = {schema.Relations.Count.ToString(CultureInfo.InvariantCulture)}");
        // Parmak izi, şemanın YAPISI üzerinden hesaplanıyor: üretilen kodun
        // hash'ini almak, üreticinin bir sürüm değişikliğinde şema hiç
        // değişmemişken "şema değişti" demek olurdu.
        sb.AppendLine($"fingerprint = {Quote(Fingerprint(schema))}");
        sb.AppendLine();
        sb.AppendLine("[eject]");
        sb.AppendLine($"target = {Quote(target)}");
        sb.AppendLine($"format = {Quote(generatorName)}");
        sb.AppendLine();
        sb.AppendLine("[namines]");
        sb.AppendLine("url = \"https://namines.com\"");
        sb.AppendLine("# This package was ejected from Namines. The code is yours —");
        sb.AppendLine("# there is no runtime dependency on Namines and no phone-home.");

        return sb.ToString();
    }

    /// <summary>
    /// Şemanın yapısal parmak izi.
    ///
    /// Sıralama deterministik ve BÜYÜK/küçük harfe duyarsız: aynı şemanın iki
    /// introspection'ı tablo sırasını farklı verebilir, ve o durumda değişmemiş
    /// bir şemanın "değişti" görünmesi bu dosyayı işe yaramaz hâle getirirdi.
    /// </summary>
    public static string Fingerprint(DatabaseSchema schema)
    {
        var sb = new StringBuilder();

        foreach (var table in schema.Tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(table.Name.ToLowerInvariant()).Append('{');
            foreach (var column in table.Columns.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(column.Name.ToLowerInvariant())
                  .Append(':').Append(column.Type.ToLowerInvariant())
                  .Append(column.IsNullable ? '?' : '!')
                  .Append(column.IsPK ? "pk" : "")
                  .Append(',');
            }
            sb.Append('}');
        }

        foreach (var relation in schema.Relations
                     .Select(r => $"{r.SourceTableId}.{r.SourceColumnId}->{r.TargetTableId}.{r.TargetColumnId}:{r.OnDelete}")
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.Append(relation).Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return "sha256:" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Şema adı serbest metin: tırnak ya da ters bölü içerebilir ve kaçırılmazsa
    /// dosya ayrıştırılamaz hâle gelir.
    /// </summary>
    private static string Quote(string? value) =>
        "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
