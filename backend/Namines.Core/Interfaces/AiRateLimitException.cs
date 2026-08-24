using System;

namespace Namines.Core.Interfaces;

/// <summary>
/// AI sağlayıcısı geçici olarak sınıra takıldı.
///
/// <b>Ayrı bir tip olmasının sebebi doğru HTTP kodu.</b> Bu bir arıza değil, bir
/// sınır: genel bir <see cref="Exception"/> olarak fırlatıldığında çağıran onu
/// 500'e çeviriyordu ve kullanıcı "ürün bozuldu" sanıyordu. Oysa doğru cevap
/// "birazdan tekrar dene" — ve <see cref="RetryAfterSeconds"/> ne kadar
/// bekleyeceğini de söylüyor.
/// </summary>
public sealed class AiRateLimitException : Exception
{
    public AiRateLimitException(string retryAfterSeconds, string? detail = null)
        : base($"The AI provider is rate limited. Try again in about {retryAfterSeconds} seconds.")
    {
        RetryAfterSeconds = retryAfterSeconds;
        Detail = detail;
    }

    /// <summary>
    /// Sağlayıcının verdiği bekleme süresi, olduğu gibi.
    ///
    /// Sayıya çevrilmiyor: sağlayıcı "32.5" gibi ondalıklı da verebiliyor ve
    /// tam sayıya yuvarlamak, erken tekrar deneyip yeniden reddedilmek demek.
    /// </summary>
    public string RetryAfterSeconds { get; }

    /// <summary>Sağlayıcının ham açıklaması — log'a gider, çağırana değil.</summary>
    public string? Detail { get; }
}
