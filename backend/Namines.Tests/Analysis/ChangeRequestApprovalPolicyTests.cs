using Namines.Core.Analysis;
using Namines.Core.Enums;

namespace Namines.Tests.Analysis;

/// <summary>
/// G11 — new-phase/29-DATABASE-CHANGE-REVIEW.md §3'teki onay kuralı. Saf fonksiyon
/// olarak tasarlandığı için (bkz. <see cref="ChangeRequestApprovalPolicy"/> XML yorumu)
/// DB/HTTP olmadan doğrudan test edilebiliyor.
/// </summary>
public class ChangeRequestApprovalPolicyTests
{
    [Theory]
    [InlineData(RiskLevel.Safe, 1)]
    [InlineData(RiskLevel.Risky, 1)]
    [InlineData(RiskLevel.Destructive, 2)]
    [InlineData(RiskLevel.Breaking, 2)]
    public void Required_approvals_matches_doc_table(RiskLevel risk, int expected)
    {
        Assert.Equal(expected, ChangeRequestApprovalPolicy.RequiredApprovals(risk));
    }

    [Theory]
    [InlineData(RiskLevel.Safe, false)]
    [InlineData(RiskLevel.Risky, false)]
    [InlineData(RiskLevel.Destructive, true)]
    [InlineData(RiskLevel.Breaking, true)]
    public void Distinct_from_author_required_only_for_high_risk(RiskLevel risk, bool expected)
    {
        Assert.Equal(expected, ChangeRequestApprovalPolicy.RequiresDistinctFromAuthor(risk));
    }

    [Fact]
    public void Safe_change_is_approved_after_a_single_vote()
    {
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Safe, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "author", // Safe'de kendi kendine onay serbest
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 0, voterAlreadyVoted: false, teamSize: 3);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.Recorded, eval.Outcome);
        Assert.Equal(ChangeRequestStatus.Approved, eval.NewStatus);
    }

    [Fact]
    public void Breaking_change_stays_pending_after_first_approval()
    {
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "reviewer1",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 0, voterAlreadyVoted: false, teamSize: 3);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.Recorded, eval.Outcome);
        Assert.Null(eval.NewStatus); // hâlâ 1 onay daha lazım — durum pending_review'de kalır
    }

    [Fact]
    public void Breaking_change_is_approved_after_second_distinct_vote()
    {
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "reviewer2",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 1, voterAlreadyVoted: false, teamSize: 3);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.Recorded, eval.Outcome);
        Assert.Equal(ChangeRequestStatus.Approved, eval.NewStatus);
    }

    [Fact]
    public void Author_cannot_self_approve_a_breaking_change()
    {
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "author",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 0, voterAlreadyVoted: false, teamSize: 3);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.RejectedSelfApprovalNotAllowed, eval.Outcome);
        Assert.Null(eval.NewStatus);
    }

    [Fact]
    public void Author_cannot_self_approve_a_destructive_change()
    {
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Destructive, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "author",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 0, voterAlreadyVoted: false, teamSize: 3);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.RejectedSelfApprovalNotAllowed, eval.Outcome);
    }

    [Fact]
    public void A_single_rejection_closes_the_change_request_immediately_even_with_prior_approvals()
    {
        // Breaking bir değişiklikte 1 onay olsa bile (henüz eşik dolmamış), bir reddetme
        // hemen kapatmalı — "reddet" iki-buton UI'ında kararlı bir aksiyon (bkz. doc §2).
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "reviewer2",
            decision: ApprovalDecision.Rejected, approvedCountBeforeThisVote: 1, voterAlreadyVoted: false, teamSize: 3);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.Recorded, eval.Outcome);
        Assert.Equal(ChangeRequestStatus.Rejected, eval.NewStatus);
    }

    [Fact]
    public void Rejection_does_not_require_distinct_author_check()
    {
        // Reddetme için "yazarla aynı olamaz" kısıtı yok — sadece onay için var (doc §3).
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "author",
            decision: ApprovalDecision.Rejected, approvedCountBeforeThisVote: 0, voterAlreadyVoted: false, teamSize: 3);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.Recorded, eval.Outcome);
        Assert.Equal(ChangeRequestStatus.Rejected, eval.NewStatus);
    }

    [Fact]
    public void Already_resolved_change_request_rejects_further_votes()
    {
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Safe, ChangeRequestStatus.Approved,
            createdByUserId: "author", voterUserId: "reviewer1",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 1, voterAlreadyVoted: false, teamSize: 3);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.RejectedAlreadyResolved, eval.Outcome);
    }

    [Fact]
    public void Same_user_cannot_vote_twice()
    {
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "reviewer1",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 1, voterAlreadyVoted: true, teamSize: 3);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.RejectedAlreadyVoted, eval.Outcome);
    }

    // ── Ekip büyüklüğüne uyarlanan onay kuralı ────────────────────────────
    // İdeal kural (Breaking → yazar hariç 2 onay) küçük ekiplerde sağlanamaz ve
    // change request'i kalıcı kilitler. Kural ekibin gerçeğine uyarlanır.

    [Theory]
    [InlineData(RiskLevel.Breaking, 5, 2)]      // büyük ekip → ideal korunur
    [InlineData(RiskLevel.Breaking, 3, 2)]
    [InlineData(RiskLevel.Breaking, 2, 1)]      // 2 kişi → tek onay yeterli
    [InlineData(RiskLevel.Breaking, 1, 1)]      // tek kişi → asla 0'a düşmez
    [InlineData(RiskLevel.Destructive, 2, 1)]
    [InlineData(RiskLevel.Risky, 1, 1)]
    [InlineData(RiskLevel.Safe, 1, 1)]
    public void Effective_required_approvals_adapts_to_team_size(RiskLevel risk, int teamSize, int expected)
    {
        Assert.Equal(expected, ChangeRequestApprovalPolicy.EffectiveRequiredApprovals(risk, teamSize));
    }

    [Fact]
    public void Effective_required_never_drops_to_zero_even_for_empty_team()
    {
        // 0'a düşmesi, yüksek riskli bir değişikliği sessizce otomatik onaylamak olurdu.
        Assert.Equal(1, ChangeRequestApprovalPolicy.EffectiveRequiredApprovals(RiskLevel.Breaking, teamSize: 0));
    }

    [Fact]
    public void Two_person_team_approves_a_breaking_change_with_one_vote()
    {
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "reviewer",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 0,
            voterAlreadyVoted: false, teamSize: 2);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.Recorded, eval.Outcome);
        Assert.Equal(ChangeRequestStatus.Approved, eval.NewStatus);
    }

    [Fact]
    public void Two_person_team_still_forbids_the_author_approving_their_own_breaking_change()
    {
        // Ekip küçüldü diye "yazar kendi onaylasın" DEMİYORUZ — başka biri var.
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "author",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 0,
            voterAlreadyVoted: false, teamSize: 2);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.RejectedSelfApprovalNotAllowed, eval.Outcome);
    }

    [Fact]
    public void Solo_team_author_may_approve_their_own_breaking_change()
    {
        // Tek kişilik ekipte onaylayacak başka kimse yok; kuralı korumak CR'ı
        // sonsuza kadar kilitlerdi. Denetim izi kimin onayladığını zaten kaydediyor.
        var eval = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "solo", voterUserId: "solo",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 0,
            voterAlreadyVoted: false, teamSize: 1);

        Assert.Equal(ChangeRequestApprovalPolicy.VoteOutcome.Recorded, eval.Outcome);
        Assert.Equal(ChangeRequestStatus.Approved, eval.NewStatus);
    }

    [Fact]
    public void Three_person_team_needs_two_distinct_approvers_for_breaking()
    {
        var first = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "rev1",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 0,
            voterAlreadyVoted: false, teamSize: 3);
        Assert.Null(first.NewStatus);   // hâlâ bekliyor

        var second = ChangeRequestApprovalPolicy.EvaluateVote(
            RiskLevel.Breaking, ChangeRequestStatus.PendingReview,
            createdByUserId: "author", voterUserId: "rev2",
            decision: ApprovalDecision.Approved, approvedCountBeforeThisVote: 1,
            voterAlreadyVoted: false, teamSize: 3);
        Assert.Equal(ChangeRequestStatus.Approved, second.NewStatus);
    }
}
