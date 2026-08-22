using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>Gateway üzerinden yapılan yazma işleminin türü.</summary>
public enum GatewayWriteKind
{
    Create,
    Update,
    Delete,
    Import,
    Rpc,
    Sql,
}

/// <summary>
/// Gateway üzerinden yapılan her YAZMA işleminin kaydı (07 §5).
///
/// <b>Kayıt Gateway'de tutuluyor, üretilen panelde değil — ve bu güvenlik
/// kararıdır.</b> Panel müşterinin kendi sunucusunda çalışan, kaynağı ona ait
/// bir uygulama; oradaki bir kaydı silmek ya da hiç yazmamak tamamen mümkün.
/// Denetim kaydının değeri, kaydı tutanın kaydı yapanla aynı taraf OLMAMASINDAN
/// gelir. Panel yazma yolunu Gateway'den geçmek zorunda, dolayısıyla buradaki
/// kayıt atlatılamaz.
///
/// <b>Değerler saklanmıyor, yalnızca hangi satırın hangi kolonlarına
/// dokunulduğu.</b> Yazılan içerik müşterinin verisi — çoğu zaman kişisel veri —
/// ve onu bizim veritabanımıza kopyalamak, tek bir denetim özelliği uğruna yeni
/// bir sızıntı yüzeyi açmak olurdu.
/// </summary>
public class GatewayAuditEntry
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ProjectId { get; set; } = null!;

    /// <summary>
    /// İşlemi yapan anahtar. Anahtar iptal edilse bile kayıt kalmalı, o yüzden
    /// yabancı anahtar değil düz metin: iptal edilen bir anahtarın geçmişini
    /// silmek, denetim kaydının tam olarak lazım olduğu anda kaybolması demek.
    /// </summary>
    public string? ApiKeyId { get; set; }

    /// <summary>Anahtarın gizli olmayan ön eki — log'da anahtarı tanımak için.</summary>
    public string? ApiKeyPrefix { get; set; }

    /// <summary>Oturum yoluyla gelen istekte kullanıcı; anahtar yolunda null.</summary>
    public string? ActorUserId { get; set; }

    public GatewayWriteKind Kind { get; set; }

    /// <summary>Hedef tablo; <see cref="GatewayWriteKind.Sql"/> için null.</summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Etkilenen satırın anahtarı. Değerin kendisi bir kimlik olabilir ama
    /// onsuz kayıt "bir satır değişti" demekten öteye gitmez ve kimse hangi
    /// satırın değiştiğini bulamaz.
    /// </summary>
    public string? RowKey { get; set; }

    /// <summary>Dokunulan kolon adları, virgülle ayrılmış. Değerler YOK.</summary>
    public string? Columns { get; set; }

    public int AffectedRows { get; set; }

    /// <summary>
    /// Başarısız denemeler de kaydediliyor: reddedilen bir yazma girişimi,
    /// başarılı olanı kadar ilgi çekicidir.
    /// </summary>
    public bool Succeeded { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
