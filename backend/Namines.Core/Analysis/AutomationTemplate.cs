using System;
using System.Text;

namespace Namines.Core.Analysis;

/// <summary>
/// Aksiyon yapılandırmasındaki <c>{{degisken}}</c> yer tutucularını tetiklemenin
/// somut değerleriyle doldurur.
///
/// <b>Neden elle yazılmış küçük bir değiştirici:</b> tam bir şablon motoru
/// (koşul, döngü, ifade) kullanıcıdan gelen metni sunucuda ÇALIŞTIRMAK demek
/// olurdu. Burada değerlendirme yok, yalnızca sabit bir sözlükten düz metin
/// değişimi var — yer tutucu bilinmiyorsa olduğu gibi bırakılıyor.
///
/// Bilinmeyen bir yer tutucunun boşa çevrilmemesi bilinçli: Slack mesajında
/// <c>{{typo}}</c> yazan kullanıcı, mesajın ortasında sessiz bir boşluk yerine
/// yazdığı şeyi görüp hatasını fark ediyor.
/// </summary>
public static class AutomationTemplate
{
    /// <summary>Şablonda kullanılabilecek değişkenler — arayüz bu listeyi kullanıcıya gösteriyor.</summary>
    public static readonly string[] Variables =
    {
        "trigger", "tableName", "columnName", "columnType", "projectName", "timestamp",
    };

    public static string Render(
        string? template,
        string trigger,
        AutomationTriggerContext context,
        string projectName,
        DateTime timestampUtc)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        if (template.IndexOf("{{", StringComparison.Ordinal) < 0) return template;

        // TEK GEÇİŞ, bilerek. Değişkenleri sırayla `Replace` etmek, bir
        // değerin İÇİNDEKİ yer tutucunun sonraki geçişte çözülmesine yol
        // açıyor: "{{projectName}}" adlı bir tablo, {{tableName}} yazan bir
        // şablonda proje adına dönüşürdü. Tablo/kolon adları kullanıcıdan
        // geldiği için bu, webhook gövdesine değer enjekte etmenin yolu olurdu.
        // Yerine konan metin bir daha okunmuyor.
        var result = new StringBuilder(template.Length);
        var i = 0;
        while (i < template.Length)
        {
            var open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0)
            {
                result.Append(template, i, template.Length - i);
                break;
            }

            var close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                result.Append(template, i, template.Length - i);
                break;
            }

            result.Append(template, i, open - i);
            var name = template[(open + 2)..close];

            // Değeri olmayan bir değişken (ör. tablo olayında columnName) BOŞ
            // STRING'e çevriliyor; bilinmeyen bir değişken ise olduğu gibi
            // kalıyor, böylece yazım hatası görünür oluyor.
            var value = name switch
            {
                "trigger" => trigger,
                "tableName" => context.TableName ?? string.Empty,
                "columnName" => context.ColumnName ?? string.Empty,
                "columnType" => context.ColumnType ?? string.Empty,
                "projectName" => projectName,
                "timestamp" => timestampUtc.ToString("O"),
                _ => null,
            };

            result.Append(value ?? template[open..(close + 2)]);
            i = close + 2;
        }

        return result.ToString();
    }
}
