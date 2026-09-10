using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>
/// Desk SQL konsolunda çalıştırılan bir sorgunun kaydı (F-01).
///
/// <b>Neden <see cref="GatewayAuditEntry"/> yetmedi:</b> O tablo bilinçli olarak
/// SQL METNİNİ SAKLAMIYOR — "değerler saklanmıyor" ilkesi gereği. Denetim kaydı
/// için doğru karar, ama "20 dakika önce çalıştırdığım sorguyu bulamıyorum"
/// problemini çözemez. İki tablonun amacı farklı: biri <b>hesap verebilirlik</b>
/// (kim, ne zaman, kaç satır), diğeri <b>kullanıcının kendi belleği</b>.
///
/// <b>Gizlilik — bu tablo hassas:</b> Sorgu metni müşterinin verisini içerebilir
/// (<c>WHERE email = '...'</c>). Bu yüzden:
///
/// 1. <b>Kayıt kullanıcıya özel okunur.</b> Proje Owner'ı bile başkasının
///    geçmişini göremez. Bir meslektaşın hangi müşteriyi aradığı, projeye
///    sahip olmakla kazanılan bir bilgi değil.
/// 2. <b>Kullanıcı kendi geçmişini silebilir.</b> Denetim kaydı silinemez
///    (<see cref="GatewayAuditEntry"/> duruyor), ama bu tablo bir kolaylık —
///    kalıcı olması zorunlu değil, ve zorunlu olmayan hassas veriyi tutmakta
///    ısrar etmek gereksiz risk.
/// 3. <b>Sınırlı sayıda tutulur.</b> Bkz. <see cref="RetentionPerUserPerProject"/>.
/// </summary>
public class SqlQueryHistoryEntry
{
    /// <summary>
    /// Kullanıcı + proje başına saklanan en fazla kayıt sayısı.
    ///
    /// <b>Neden bir sınır var:</b> Sınırsız büyüyen bir tablo, sonsuza kadar
    /// biriken hassas metin demek. Ayrıca kimse 500 sorgu öncesine bakmıyor —
    /// özelliğin amacı "az önce ne çalıştırdım", arşiv değil.
    /// </summary>
    public const int RetentionPerUserPerProject = 100;

    /// <summary>
    /// Saklanan en uzun SQL metni. Daha uzunu kırpılır.
    ///
    /// Kırpma <b>görünür</b> olmalı (<see cref="Truncated"/>): sessizce kırpılmış
    /// bir sorguyu kopyalayıp çalıştıran kullanıcı, farklı bir sorgu çalıştırmış
    /// olduğunu fark etmezdi.
    /// </summary>
    public const int MaxSqlLength = 8000;

    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ProjectId { get; set; } = null!;

    /// <summary>Sorguyu çalıştıran kullanıcı. Okuma yetkisi YALNIZCA buna bağlı.</summary>
    public string UserId { get; set; } = null!;

    public string Sql { get; set; } = null!;

    /// <summary><see cref="MaxSqlLength"/> aşıldığı için kırpıldı mı.</summary>
    public bool Truncated { get; set; }

    /// <summary>Dönen satır sayısı; başarısız çalıştırmada 0.</summary>
    public int RowCount { get; set; }

    /// <summary>
    /// Başarısız sorgular da kaydediliyor — çoğu zaman aranan tam da odur
    /// ("hata veren sorguyu düzeltip tekrar deneyeceğim").
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>Hata mesajı (varsa). Sunucunun kullanıcıya zaten gösterdiği metin.</summary>
    public string? ErrorMessage { get; set; }

    public int DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
