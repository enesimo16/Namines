using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Namines.Infrastructure.Observability;

/// <summary>
/// Log'lara sızan gizli bilgiyi maskeler (new-phase/21-OBSERVABILITY.md §2).
///
/// <b>Neden zorunlu bir enricher:</b> "connection string'i loglamayın" bir kural
/// olarak yazılabilir ama uygulanamaz — bir istisna mesajı, bir hata ayıklama
/// satırı ya da üçüncü taraf bir kütüphane onu her an log'a düşürebilir. Log'lar
/// da genellikle uygulamadan daha uzun yaşar ve daha çok kişi tarafından görülür.
/// Tek güvenilir yer, log'un yazıldığı andaki son kapı.
///
/// <b>Yanlış pozitif kabul edilir.</b> Desenler geniş tutuldu: gereğinden fazla
/// maskelemek bir hata ayıklama satırını okunmaz yapar, gereğinden az maskelemek
/// bir kimlik bilgisini kalıcı olarak sızdırır. İkisi arasında seçim net.
/// </summary>
public sealed class PiiRedactionEnricher : ILogEventEnricher
{
    public const string Placeholder = "[REDACTED]";

    private static readonly RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // Süre sınırı: log'a düşen kullanıcı girdisi üzerinde çalışıyoruz ve
    // yakalanmamış bir geri izleme (backtracking) patlaması, log yazan her isteği
    // kilitler. Maskeleme uğruna uygulamayı durdurmak kabul edilemez.
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex[] Patterns =
    {
        // anahtar=değer biçimindeki sırlar
        new(@"(?<key>password|pwd|secret|token|api[_-]?key)\s*[=:]\s*[^\s;,""']+", Options, Timeout),

        // Namines API anahtarı (nmn_...) ve doküman §2'deki eski nam_live_ biçimi
        new(@"\b(?:nmn|nam)_(?:live_|test_)?[A-Za-z0-9]{8,}", Options, Timeout),

        // JWT
        new(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", Options, Timeout),

        // ADO.NET bağlantı dizesi: tamamı maskelenir, yalnızca parola değil.
        // Host/kullanıcı da müşteri altyapısını ele verir.
        new(@"\b(?:Host|Server|Data\s*Source)\s*=\s*[^;]+;(?:[^;]*;)*?\s*(?:Password|Pwd)\s*=\s*[^;\s]*;?", Options, Timeout),

        // E-posta
        new(@"\b[\w.+-]+@[\w-]+\.[\w.-]+\b", Options, Timeout),
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Şablonun kendisi (MessageTemplate) sabit metindir ve geliştirici yazar;
        // sır ÖZELLİKLERDE taşınır. Bu yüzden yalnızca özellikler taranıyor.
        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            var redacted = Redact(value);
            if (!ReferenceEquals(redacted, value))
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
        }
    }

    private static LogEventPropertyValue Redact(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string text }:
            {
                var cleaned = Scrub(text);
                return ReferenceEquals(cleaned, text) ? value : new ScalarValue(cleaned);
            }

            // Yapılandırılmış nesneler ve diziler de gizli bilgi taşıyabilir;
            // yalnızca üst seviyeyi taramak, iç içe bir nesnedeki token'ı kaçırırdı.
            case SequenceValue sequence:
            {
                var items = sequence.Elements.Select(Redact).ToArray();
                return items.Zip(sequence.Elements).Any(p => !ReferenceEquals(p.First, p.Second))
                    ? new SequenceValue(items)
                    : value;
            }

            case StructureValue structure:
            {
                var properties = structure.Properties
                    .Select(p => new LogEventProperty(p.Name, Redact(p.Value)))
                    .ToArray();
                return new StructureValue(properties, structure.TypeTag);
            }

            case DictionaryValue dictionary:
            {
                var pairs = dictionary.Elements
                    .Select(e => new KeyValuePair<ScalarValue, LogEventPropertyValue>(e.Key, Redact(e.Value)))
                    .ToArray();
                return new DictionaryValue(pairs);
            }

            default:
                return value;
        }
    }

    /// <summary>Metni maskeler; değişiklik yoksa AYNI referansı döndürür.</summary>
    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = text;
        foreach (var pattern in Patterns)
        {
            try
            {
                result = pattern.Replace(result, match =>
                {
                    // anahtar=değer deseninde ANAHTARI koru: "password=[REDACTED]"
                    // satırı, neyin gizlendiğini söylediği için ayıklamada işe yarar.
                    var key = match.Groups["key"];
                    return key.Success ? $"{key.Value}={Placeholder}" : Placeholder;
                });
            }
            catch (RegexMatchTimeoutException)
            {
                // Desen zaman aşımına uğradıysa metnin güvenli olduğunu VARSAYAMAYIZ.
                // Tamamını maskelemek, kısmen taranmış bir metni yayımlamaktan iyidir.
                return Placeholder;
            }
        }

        return result;
    }
}
