using System;

namespace Namines.Core.Models;

/// <summary>
/// Sistemdeki araç ve özelliklerin admin panelinden açılıp kapatılmasını (kill-switch) sağlayan bayrak.
/// </summary>
public sealed class AdminFeatureFlag
{
    /// <summary>
    /// Özelliğin sistemdeki benzersiz kimliği.
    /// Örn: "canvas", "review", "flow", "vault", "ground", "desk", "compile", "schema_generator", "ai_dba", "smart_seed"
    /// </summary>
    public string FeatureKey { get; set; } = string.Empty;

    /// <summary>
    /// Admin ve arayüzde gösterilen ad.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Özelliğin işlevi hakkında kısa açıklama.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Özellik kategorisi: "Studio", "Governance", "AI", "DataPlane"
    /// </summary>
    public string Category { get; set; } = "Studio";

    /// <summary>
    /// Özelliğin açık olup olmadığı.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Kapatıldığında arayüzdeki davranış biçimi:
    /// - "DisabledNotice": Arayüzde görünür kalır, buton pasifleşir ve "Bakımda / Devre dışı" uyarısı verir.
    /// - "Hidden": Arayüzden, menüden ve tuvalden tamamen kaldırılır, hiç görünmez.
    /// </summary>
    public string Mode { get; set; } = "DisabledNotice";

    /// <summary>
    /// "DisabledNotice" modunda kullanıcıya gösterilecek özel bilgilendirme mesajı.
    /// </summary>
    public string? NoticeMessage { get; set; } = "Bu özellik şu anda bakım çalışması nedeniyle geçici olarak devre dışıdır.";

    /// <summary>
    /// Son güncelleme zamanı (UTC).
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
