using System;

namespace Namines.Core.Models.Auth;

/// <summary>
/// Bir organizasyonun günlük AI token tüketimi (Team planının ortak havuzu).
///
/// <b>Neden ortak havuz, üyeye bölünmüş kota değil:</b> bölme modelinde
/// (600K / üye sayısı) kullanıcının kotası, birini ekibe davet ettiği anda
/// DÜŞÜYOR — 600K'dan 300K'ya. Bu, Team planının teşvik etmesi gereken tek
/// davranışı (insan davet etmek) cezalandırmak demek. Kullanıcı "davet
/// edersem token'ım yarıya iner" diye öğrenir ve etmez.
///
/// Ortak havuzda ise ekibin toplam hakkı sabit: koltuk başına pay × koltuk
/// sayısı. Kimse davet ettiği için kaybetmiyor, boşta duran bir üyenin payı
/// da çöpe gitmiyor.
///
/// <b>Açlık riski ve çözümü:</b> ortak havuzun bilinen zayıflığı, bir üyenin
/// hepsini tüketip diğerlerine bir şey bırakmamasıdır. Bu yüzden üye başına
/// ayrı bir tavan da uygulanıyor (bkz. AiQuotaService.PerUserCapAsync) —
/// paydan fazlasını kullanabilirsin ama hepsini alamazsın. Aynı desen zaten
/// global havuzda kullanılıyor ve test edilmiş durumda.
/// </summary>
public class OrgAiUsage
{
    public int Id { get; set; }

    public string OrganizationId { get; set; } = null!;

    /// <summary>UTC gün (yyyy-MM-dd, saat 00:00).</summary>
    public DateTime Date { get; set; }

    public long TokensUsed { get; set; }
}
