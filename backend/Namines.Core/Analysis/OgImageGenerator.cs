using System;
using System.Linq;
using System.Net;
using System.Text;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// Paylaşılan şema için sosyal önizleme görseli (new-phase/23-GTM.md §2 Döngü 1).
///
/// <b>SVG üretiliyor, PNG değil.</b> PNG bir görüntü kütüphanesi (SkiaSharp,
/// ImageSharp) ve font dosyası gerektirir — Docker imajına onlarca megabayt ve
/// yeni bir güvenlik yüzeyi ekler. SVG deterministik, bağımlılıksız ve metin
/// olduğu için önbelleğe alması ucuz. Twitter/LinkedIn hâlâ raster istiyor;
/// dönüştürme, o platformlar hedeflendiğinde bir kenar servisiyle (CDN) yapılır
/// ve bu üretici değişmez.
///
/// <b>Neden otomatik görsel:</b> paylaşılan bir bağlantı önizlemesiz olduğunda
/// akışta düz bir URL olarak görünür ve tıklanmaz. Döngü 1'in tamamı bu görselin
/// dikkat çekmesine bağlı.
/// </summary>
public static class OgImageGenerator
{
    // 1200×630 sosyal önizlemelerin fiili standardı; başka bir oran platformlarda
    // kırpılır ve metin kesilir.
    private const int Width = 1200;
    private const int Height = 630;

    public static string Generate(DatabaseSchema schema, string engine)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var title = string.IsNullOrWhiteSpace(schema.Name) ? "Untitled schema" : schema.Name;
        var tables = schema.Tables.Count;
        var relations = schema.Relations.Count;
        var columns = schema.Tables.Sum(t => t.Columns.Count);

        // En büyük tabloların adları: görsele "gerçek bir şema" hissi veren şey
        // sayılar değil, tanıdık tablo adları.
        var preview = schema.Tables
            .OrderByDescending(t => t.Columns.Count)
            .Take(5)
            .Select(t => t.Name)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{Width}" height="{Height}" viewBox="0 0 {Width} {Height}">""");

        // Tema sabit koyu: sosyal önizleme, izleyicinin tema tercihini bilmez ve
        // şeffaf zemin bazı istemcilerde siyah metni siyah üstüne koyar.
        sb.AppendLine("""  <rect width="1200" height="630" fill="#0b0c10"/>""");
        sb.AppendLine("""  <rect x="0" y="0" width="1200" height="6" fill="#4b8a6f"/>""");

        sb.AppendLine("""  <text x="72" y="132" font-family="system-ui,-apple-system,Segoe UI,sans-serif" font-size="26" fill="#7a8194" letter-spacing="3">NAMINES</text>""");

        sb.AppendLine($"""  <text x="72" y="232" font-family="system-ui,-apple-system,Segoe UI,sans-serif" font-size="72" font-weight="600" fill="#f5f6f8">{Escape(Truncate(title, 24))}</text>""");

        sb.AppendLine($"""  <text x="72" y="292" font-family="system-ui,-apple-system,Segoe UI,sans-serif" font-size="30" fill="#c2c7d1">{Escape(engine)} schema</text>""");

        var stats = new[]
        {
            (Label: "TABLES", Value: tables.ToString()),
            (Label: "COLUMNS", Value: columns.ToString()),
            (Label: "RELATIONS", Value: relations.ToString()),
        };

        var x = 72;
        foreach (var (label, value) in stats)
        {
            sb.AppendLine($"""  <text x="{x}" y="412" font-family="system-ui,-apple-system,Segoe UI,sans-serif" font-size="64" font-weight="600" fill="#f5f6f8">{value}</text>""");
            sb.AppendLine($"""  <text x="{x}" y="446" font-family="system-ui,-apple-system,Segoe UI,sans-serif" font-size="20" fill="#7a8194" letter-spacing="2">{label}</text>""");
            x += 260;
        }

        if (preview.Count > 0)
        {
            var chipX = 72;
            foreach (var name in preview)
            {
                var text = Truncate(name, 16);
                // Genişlik karakter sayısından tahmin ediliyor: SVG'de metin
                // ölçmenin yolu yok ve sabit genişlik uzun adları taşırırdı.
                var chipWidth = 24 + text.Length * 15;
                if (chipX + chipWidth > Width - 72) break;

                sb.AppendLine($"""  <rect x="{chipX}" y="510" width="{chipWidth}" height="52" rx="10" fill="#1a1f2e"/>""");
                sb.AppendLine($"""  <text x="{chipX + 14}" y="544" font-family="ui-monospace,SFMono-Regular,Menlo,monospace" font-size="22" fill="#c2c7d1">{Escape(text)}</text>""");
                chipX += chipWidth + 14;
            }
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    /// <summary>
    /// XML kaçışı ŞART: tablo adı <c>&amp;</c> ya da <c>&lt;</c> içeriyorsa
    /// kaçırılmadığında SVG ayrıştırılamaz hâle gelir ve önizleme bozuk bir
    /// görsel olarak görünür — hiç görsel olmamasından kötü.
    /// </summary>
    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
