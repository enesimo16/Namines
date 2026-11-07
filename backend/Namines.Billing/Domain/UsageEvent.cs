using System;
using System.ComponentModel.DataAnnotations;

using Namines.Core.Models.Auth;

namespace Namines.Billing.Domain;

/// <summary>
/// Tek bir ölçüm kaydı.
///
/// <b>Olay olarak saklanıyor, sayaç olarak değil.</b> Tek bir "bu ay 412 çağrı"
/// alanı tutmak daha ucuz olurdu ama fatura itirazında ("bu 412 nereden geldi?")
/// cevap verecek hiçbir şey kalmazdı. Doküman bu hacim için ClickHouse öngörüyor;
/// başlangıçta kontrol veritabanı yeterli ve taşındığında bu tablo kaynak olur.
/// </summary>
public class UsageEvent
{
    [Key]
    public long Id { get; set; }

    public string? UserId { get; set; }

    public UsageResource Resource { get; set; }

    /// <summary>
    /// Miktar. Çoğu kaynak için 1, ama depolama/transfer kesirli olabilir —
    /// tamsayı tutmak 0.4 GB'lık bir transferi ya 0 ya 1 yapardı.
    /// </summary>
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// Fatura dönemi, ayın ilk günü (UTC). Sorgularken tarih aralığı yerine bu
    /// alanı kullanmak, ay sınırındaki saat dilimi kaymalarını ortadan kaldırır.
    /// </summary>
    public DateTime BillingPeriod { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Hangi proje/branch/tablo — itiraz hâlinde iz sürmek için.</summary>
    public string? Context { get; set; }
}

/// <summary>
/// Kullanıcının aşırı kullanım tercihi (22 §5).
///
/// <b>Varsayılan KAPALI.</b> Doküman açık: "varsayılan: aşırı kullanım kapalı".
/// Açık olsaydı, limitini bilmeyen bir kullanıcı beklemediği bir fatura alırdı —
/// ve bunu ancak fatura gelince öğrenirdi. Kapalıyken hizmet limitte durur;
/// bu, sürpriz faturadan iyidir ve kullanıcı isterse tek tıkla açar.
/// </summary>
public class UserBillingSettings
{
    [Key]
    public string UserId { get; set; } = null!;

    public bool OverageEnabled { get; set; }

    /// <summary>
    /// Aylık harcama tavanı (USD). Aşırı kullanım açık olsa bile bu tavan dolunca
    /// hizmet durur — "sınırsız fatura" hiçbir kullanıcının istediği şey değil.
    /// Null ise tavan yok.
    /// </summary>
    public decimal? MonthlyCapUsd { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
