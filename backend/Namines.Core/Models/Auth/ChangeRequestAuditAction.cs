namespace Namines.Core.Models.Auth;

/// <summary>
/// G16 — new-phase/29-DATABASE-CHANGE-REVIEW.md §3, new-phase/18-CONTROL-PLANE-DDL.md'nin
/// `audit_log` fikri. Bilinçli kapsam sadeleştirmesi: doc'taki `audit_log` çok-kiracılı
/// (org_id), genel amaçlı bir tablo — bu projede henüz bir Organization kavramı yok
/// (CloudProject doğrudan UserId'ye bağlı). Onun yerine ChangeRequest'e ÖZEL, append-only
/// bir denetim izi: CR'ın yaşam döngüsü boyunca durum geçişlerini kim/ne zaman/neden
/// yaptığını kaydeder. İnsan onayları zaten <c>ChangeRequestApproval</c>'da —
/// bu tablo ONUN yerine değil, sistem-güdümlü olaylar (otomatik onay gibi insan
/// onayı olmayan olaylar) için de aynı zaman çizelgesinde görünür olsun diye var.
/// </summary>
public enum ChangeRequestAuditAction
{
    Created,
    AutoApproved,
    Approved,
    Rejected,

    // Silme olayları: bu tablo CR'ın dışında, kalıcı silmelerin DAYANAĞI olarak da kullanılıyor
    // (yeni tablo açmamak için). Bu satırlarda ChangeRequestId = silinen proje/kullanıcı kimliği,
    // Details = yalnızca sayaçlar içeren JSON (kişisel veri yok).
    /// <summary>Proje silme başladı (dış kaynaklar temizlenmeden ÖNCE yazılır).</summary>
    ProjectDeletionRequested,
    /// <summary>Proje ve bağlı her şey silindi.</summary>
    ProjectDeleted,
    /// <summary>Proje silme dış kaynak adımında durdu; proje yerinde.</summary>
    ProjectDeletionFailed,
    /// <summary>Kullanıcı hesabı silindi (KVKK/GDPR silme hakkı).</summary>
    AccountDeleted,

    // Abonelik iptali sonradan (işçi ya da yeniden deneme ile) tamamlandı. Satırlar değiştirilemediği için
    // "AccountDeleted" satırı güncellenmez; sonuç yeni satır olarak eklenir. SubjectId = outbox kimliği.
    SubscriptionCancellationCompleted
}
