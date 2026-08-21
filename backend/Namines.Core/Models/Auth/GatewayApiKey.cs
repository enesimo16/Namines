using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth;

/// <summary>
/// Gateway'e programatik erişim anahtarı (new-phase/08-GATEWAY-API.md §4.3).
///
/// <b>Ham anahtar SAKLANMAZ.</b> Yalnızca SHA-256 özeti tutulur; anahtar
/// oluşturulduğu anda BİR KEZ gösterilir ve bir daha görülemez. Kontrol
/// veritabanının bir yedeği sızsa bile müşterinin veritabanına erişim vermez —
/// ham anahtarı saklamak tam olarak bunu verirdi.
///
/// <see cref="Prefix"/> arayüzde "hangi anahtar bu?" sorusunu cevaplamak için
/// tutulur ve aramayı ucuzlatır; tek başına kimlik doğrulamaz.
/// </summary>
public class GatewayApiKey
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ProjectId { get; set; } = null!;
    public CloudProject Project { get; set; } = null!;

    /// <summary>İnsan tarafından verilen etiket ("production backend", "CI").</summary>
    public string Name { get; set; } = null!;

    /// <summary>Anahtarın ilk parçası — gösterim ve arama için. Gizli değildir.</summary>
    public string Prefix { get; set; } = null!;

    public string KeyHash { get; set; } = null!;

    /// <summary>
    /// Yazma izni. Varsayılan false: 08 §1'in "güvenli varsayılan" ilkesi, bir
    /// anahtarın kaza ile yazma yetkisiyle doğmamasını gerektirir.
    /// </summary>
    public bool CanWrite { get; set; }

    /// <summary>
    /// İzin verilen kaynaklar, virgülle ayrılmış (<c>https://app.musteri.com</c>).
    /// Boşsa kısıt yok. Doluysa <c>Origin</c> başlığı taşımayan istek de reddedilir —
    /// "origin kısıtla" diyen biri, başlığı hiç göndermeyen istemciye kapıyı açık
    /// bırakmayı kastetmez.
    /// </summary>
    public string? AllowedOrigins { get; set; }

    /// <summary>
    /// İzin verilen kaynak adresler, virgülle ayrılmış düz IP ya da CIDR
    /// (<c>1.2.3.4</c>, <c>10.0.0.0/8</c>). Boşsa kısıt yok.
    ///
    /// Uygulama güvenilen proxy ağları tanımlanmadan bir proxy arkasındaysa istemci
    /// adresi taklit edilebilir; o durumda bu liste doluysa istek REDDEDİLİR
    /// (bkz. <c>GatewayKeyRestrictions.IsIpAllowed</c>).
    /// </summary>
    public string? AllowedIps { get; set; }

    /// <summary>
    /// Dakikadaki istek sınırı. Null ise sunucunun genel politikası geçerli.
    /// Anahtar başına sınır, tek bir istemcinin tüm kotayı tüketmesini engeller.
    /// </summary>
    public int? RateLimitPerMinute { get; set; }

    public string CreatedByUserId { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null ise süresiz.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// İptal anahtarı SİLMEZ, işaretler. Silinseydi "bu anahtar ne zaman ve kim
    /// tarafından iptal edildi" sorusu cevapsız kalırdı.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Son kullanım — kullanılmayan anahtarları fark edip kapatabilmek için.</summary>
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>
/// Bir tablonun API anahtarlarına açık olup olmadığı (08 §1: "hiçbir tablo
/// varsayılan olarak public değil").
///
/// Kayıt YOKLUĞU erişim yok demektir. Varsayılanı "her tablo okunabilir" yapmak,
/// projeye sonradan eklenen bir tabloyu — ör. <c>password_resets</c> — kimse
/// istemeden internete açardı.
/// </summary>
public class GatewayTablePermission
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ProjectId { get; set; } = null!;
    public CloudProject Project { get; set; } = null!;

    public string TableName { get; set; } = null!;

    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
