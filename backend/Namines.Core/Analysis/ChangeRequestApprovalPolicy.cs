using Namines.Core.Enums;

namespace Namines.Core.Analysis;

/// <summary>
/// new-phase/29-DATABASE-CHANGE-REVIEW.md §3'teki onay kuralının deterministik,
/// bağımsız test edilebilir hâli. Bilinçli olarak <c>ChangeRequestController</c>'ın
/// dışına çıkarıldı — G8'in <see cref="SchemaImpactAnalyzer"/>'ında işe yarayan desen
/// burada da geçerli: iş kuralı bir HTTP/DB pipeline'ına gömülürse ucuz, hızlı birim
/// testlerle kanıtlanamaz.
///
/// | Risk | Onay gereksinimi |
/// |---|---|
/// | Safe | Otomatik onaylanabilir (opt-in proje ayarı — bkz. CloudProject.AutoApproveSafeChanges,
///         G16); ayar kapalıysa 1 kişi |
/// | Risky | 1 kişi |
/// | Destructive / Breaking | 2 kişi, oluşturanla aynı olamaz |
///
/// Otomatik onay kararı burada DEĞİL — <c>ChangeRequestController.CreateQuick</c>'te
/// proje ayarına bakılarak veriliyor (bu sınıf saf risk→onay-sayısı eşlemesidir, proje
/// ayarına erişimi yok, bilerek).
/// </summary>
public static class ChangeRequestApprovalPolicy
{
    /// <summary>Risk seviyesinin İDEAL onay sayısı — ekip büyüklüğü hesaba katılmaz.</summary>
    public static int RequiredApprovals(RiskLevel risk) => risk switch
    {
        RiskLevel.Destructive or RiskLevel.Breaking => 2,
        _ => 1
    };

    /// <summary>
    /// Ekip büyüklüğüne göre GERÇEKTEN uygulanabilir onay sayısı.
    ///
    /// Neden gerekli: ideal kural (Breaking → yazar hariç 2 onay) 3 kişiden küçük
    /// ekiplerde matematiksel olarak sağlanamaz ve change request kalıcı olarak
    /// kilitlenir. Kuralı ekibin gerçeğine uyarlıyoruz:
    ///
    ///   ekip 3+  → 2 onay (ideal korunur)
    ///   ekip 2   → 1 onay (yazar dışındaki tek kişi yeterli)
    ///   ekip 1   → 1 onay, ama onaylayacak başka kimse yok → yazar kendi onaylar
    ///
    /// ASLA 0 döndürmez: onay sayısını sıfıra düşürmek, yüksek riskli bir değişikliği
    /// sessizce otomatik onaylamak olurdu — <c>AutoApproveSafeChanges</c> yalnızca
    /// Safe risk için ve açık opt-in ile vardır, buradan arka kapı açılmaz.
    /// </summary>
    public static int EffectiveRequiredApprovals(RiskLevel risk, int teamSize)
    {
        var ideal = RequiredApprovals(risk);
        var eligible = Math.Max(0, teamSize - 1); // yazar kendi oyunu veremez (çok kişilik ekipte)
        return Math.Max(1, Math.Min(ideal, eligible));
    }

    /// <summary>
    /// Yazarın kendi değişikliğini onaylaması yasak mı?
    ///
    /// Yüksek riskte kural evet der — AMA tek kişilik ekipte uygulanamaz: onaylayacak
    /// başka kimse yoktur ve kuralı korumak CR'ı sonsuza kadar kilitler. O durumda
    /// yazarın kendi onayına izin verilir; denetim izi (ChangeRequestAuditLog) bunu
    /// zaten kimin yaptığıyla birlikte kaydeder — yönetişimin değeri kaydın kendisidir.
    /// </summary>
    public static bool RequiresDistinctFromAuthor(RiskLevel risk, int teamSize) =>
        risk is RiskLevel.Destructive or RiskLevel.Breaking && teamSize > 1;

    /// <summary>Ekip büyüklüğü bilinmeyen çağrılar için — ideal kuralı uygular.</summary>
    public static bool RequiresDistinctFromAuthor(RiskLevel risk) =>
        RequiresDistinctFromAuthor(risk, teamSize: int.MaxValue);

    public enum VoteOutcome
    {
        Recorded,
        RejectedAlreadyResolved,
        RejectedAlreadyVoted,
        RejectedSelfApprovalNotAllowed
    }

    public sealed record VoteEvaluation(VoteOutcome Outcome, ChangeRequestStatus? NewStatus);

    /// <summary>
    /// Bir oyun kabul edilip edilmeyeceğine ve kabul edilirse CR'ın yeni durumuna karar verir.
    /// Saf fonksiyon — DB'ye yazmaz, sadece karar üretir; çağıran taraf (controller) uygular.
    /// </summary>
    /// <param name="teamSize">Projenin organizasyonundaki oy verebilecek üye sayısı
    /// (Editor+). Kuralın ekip gerçeğine uyarlanması için gerekli — bkz.
    /// <see cref="EffectiveRequiredApprovals"/>.</param>
    public static VoteEvaluation EvaluateVote(
        RiskLevel risk,
        ChangeRequestStatus currentStatus,
        string createdByUserId,
        string voterUserId,
        ApprovalDecision decision,
        int approvedCountBeforeThisVote,
        bool voterAlreadyVoted,
        int teamSize)
    {
        if (currentStatus != ChangeRequestStatus.PendingReview)
            return new VoteEvaluation(VoteOutcome.RejectedAlreadyResolved, null);

        if (voterAlreadyVoted)
            return new VoteEvaluation(VoteOutcome.RejectedAlreadyVoted, null);

        if (decision == ApprovalDecision.Rejected)
            return new VoteEvaluation(VoteOutcome.Recorded, ChangeRequestStatus.Rejected);

        // Approved dalı
        if (RequiresDistinctFromAuthor(risk, teamSize) && voterUserId == createdByUserId)
            return new VoteEvaluation(VoteOutcome.RejectedSelfApprovalNotAllowed, null);

        var approvedCountAfter = approvedCountBeforeThisVote + 1;
        var newStatus = approvedCountAfter >= EffectiveRequiredApprovals(risk, teamSize)
            ? ChangeRequestStatus.Approved
            : (ChangeRequestStatus?)null; // hâlâ pending_review — daha fazla onay gerekiyor

        return new VoteEvaluation(VoteOutcome.Recorded, newStatus);
    }
}
