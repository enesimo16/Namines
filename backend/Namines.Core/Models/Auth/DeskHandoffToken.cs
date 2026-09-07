using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>
/// Namines Desk v2 §E1.2 — ana uygulamadan Desk'e tek tıkla, PAROLA SORMADAN
/// geçiş. Aynı tek-kullanımlık desen <see cref="TeamInvite"/> ile — bu dosyanın
/// yorumları o dosyanınkiyle birebir aynı gerekçeyi taşıyor, kopyala-yapıştır
/// değil: iki ayrı kavram (ekibe katılma / oturum devri) aynı güvenlik şeklini
/// paylaşıyor.
///
/// <b>En güvenli seçenek uygulandı (34-SENDEN-BEKLENENLER.md madde 11'in
/// kararı):</b> jeton URL'de/query string'te ASLA taşınmaz — yalnızca bir
/// form POST gövdesinde gider (bkz. `frontend`'in "Namines Desk" düğmesi ve
/// Desk'in `/handoff` Route Handler'ı). Bu, jetonun tarayıcı geçmişine ve
/// `Referer` başlığına düşmesini engeller; URL'ler loglanır/önbelleklenir,
/// POST gövdeleri loglanmaz.
///
/// <b>Ömür 30 saniye</b> (01-KIMLIK-VE-OTURUM.md'nin kendi uyarısı: saniyeler
/// ömürlü olmalı) — bu, ana uygulama sayfasından Desk'in yeni sekmesine
/// geçişin gerçekleşmesi için fazlasıyla yeterli, ama çalınan bir jetonun
/// kullanılabilir kalma penceresini pratikte sıfıra indiriyor.
/// </summary>
public class DeskHandoffToken
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Ham değerin SHA-256 özeti — TeamInvite ile aynı gerekçe: ham saklansaydı
    /// veritabanı yedeğine erişen herkes birinin oturumunu Desk'te açabilirdi.</summary>
    public string TokenHash { get; set; } = null!;

    public string UserId { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    /// <summary>Kullanıldığında dolar; null ise jeton hâlâ geçerli.</summary>
    public DateTime? UsedAt { get; set; }

    public bool IsUsable(DateTime nowUtc) => UsedAt is null && ExpiresAt > nowUtc;
}
