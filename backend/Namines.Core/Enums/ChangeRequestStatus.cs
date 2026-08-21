namespace Namines.Core.Enums;

/// <summary>
/// new-phase/29-DATABASE-CHANGE-REVIEW.md §1'deki yaşam döngüsünün Faz 0 alt kümesi.
/// Doc'taki tam döngü draft→analyzing→pending_review→approved→applying→applied/failed'i
/// tanımlıyor; Faz 0'da "applying/applied/failed" için bir worker/apply pipeline'ı henüz
/// yok (CHECKLIST.md G12'ye bırakıldı — DbPushModal zaten manuel bir "apply" yolu sağlıyor,
/// CR'a bağlı otomatik apply ayrı iş). Burada yalnızca REVIEW kısmı — analiz zaten CR
/// oluşturulurken senkron tamamlanıyor, "analyzing" ara durumuna Faz 0'da gerek yok
/// (ImpactAnalyzer milisaniyeler içinde biter, büyük şemada bile arka plan gerekmiyor).
/// </summary>
public enum ChangeRequestStatus
{
    PendingReview,
    Approved,
    Rejected
}
