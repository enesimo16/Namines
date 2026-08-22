using System;
using System.Security.Cryptography;
using System.Text;

namespace Namines.Core.Github;

/// <summary>
/// GitHub webhook imzası (<c>X-Hub-Signature-256</c>).
///
/// <b>Doğrulamasız bir webhook ucu, herkesin çağırabildiği bir uçtur.</b> Bir
/// saldırgan sahte "PR açıldı" olayı göndererek önizleme veritabanı açtırabilir,
/// kaynak tüketebilir ya da sahte bir "onaylandı" yorumu attırabilir. GitHub bunu
/// paylaşılan bir sırla imzalıyor; kontrol etmemek, kapıyı hiç kilitlememek demek.
/// </summary>
public static class GithubWebhook
{
    /// <summary>
    /// İmzayı doğrular.
    ///
    /// Karşılaştırma SABİT ZAMANLI: normal string eşitliği ilk farklı baytta
    /// döner ve saldırganın imzayı bayt bayt tahmin etmesine yetecek zamanlama
    /// bilgisi sızdırır.
    ///
    /// Sır tanımlı değilse <b>false</b> döner — "sır yoksa doğrulamayı atla"
    /// davranışı, yapılandırmayı unutan bir kurulumda ucu tamamen açık bırakırdı.
    /// </summary>
    public static bool IsSignatureValid(string? secret, string payload, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(secret)) return false;
        if (string.IsNullOrWhiteSpace(signatureHeader)) return false;
        if (payload is null) return false;

        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var expected = Compute(secret, payload);
        var provided = signatureHeader[prefix.Length..];

        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var providedBytes = Encoding.ASCII.GetBytes(provided);

        return expectedBytes.Length == providedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    /// <summary>Beklenen imzayı üretir — testler ve giden istekler için.</summary>
    public static string Compute(string secret, string payload)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <param name="Name">Komut adı, küçük harfe indirgenmiş.</param>
/// <param name="Argument">Varsa tek argüman.</param>
public sealed record BotCommand(string Name, string? Argument);

/// <summary>
/// PR yorumlarındaki <c>/namines</c> komutlarını okur
/// (new-phase/11-MIGRATIONS-BRANCHING.md §7).
///
/// <b>Yalnızca bilinen komutlar kabul ediliyor.</b> Tanınmayanı sessizce yok
/// saymak, yazım hatası yapan kullanıcıyı cevap beklerken bırakır; bilinmeyen bir
/// komutu "en yakın" komuta çevirmek ise çok daha kötü — biri <c>/namines aprove</c>
/// yazınca yıkıcı bir değişiklik onaylanmamalı.
/// </summary>
public static class BotCommandParser
{
    public const string Prefix = "/namines";

    private static readonly string[] Known =
    {
        "approve", "plan", "preview", "rollback-plan", "types", "help",
    };

    /// <summary>
    /// Yorum metninden komutu çıkarır; komut yoksa null.
    ///
    /// Komut satırın BAŞINDA olmalı: bir yorumun ortasındaki "…deneyin: /namines
    /// approve" cümlesi bir talimat değil, bir öneri. Ortada geçeni çalıştırmak,
    /// alıntı yapan bir yorumun kazara komut tetiklemesi demek olurdu.
    /// </summary>
    public static BotCommand? Parse(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;

        foreach (var raw in comment.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var rest = line[Prefix.Length..].Trim();
            if (rest.Length == 0) return new BotCommand("help", null);

            var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var name = parts[0].ToLowerInvariant();

            if (!Known.Contains(name)) return null;

            return new BotCommand(name, parts.Length > 1 ? parts[1].Trim() : null);
        }

        return null;
    }

    public static string HelpText() =>
        "**Namines commands**\n\n" +
        "- `/namines plan` — migration plan for this change\n" +
        "- `/namines preview` — provision a throwaway database with this schema\n" +
        "- `/namines rollback-plan` — how to undo it\n" +
        "- `/namines types` — regenerate the TypeScript types\n" +
        "- `/namines approve` — record a human approval for a destructive change\n";
}
