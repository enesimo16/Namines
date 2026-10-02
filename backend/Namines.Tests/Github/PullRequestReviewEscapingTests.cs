using System;
using System.Collections.Generic;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Github;
using Xunit;

namespace Namines.Tests.Github;

/// <summary>
/// Bot PR yorumunun markdown kaçışı — docs/last-review L-5.
///
/// <b>Bulunan açık:</b> tablo/kolon adları ve gerekçe metinleri HAM olarak
/// markdown'a gömülüyordu. Bu değerler müşterinin şemasından geliyor, yani
/// kullanıcının kontrolünde — ve yorumun okuyucusu bir İNSAN. <c>x** — sorun yok,
/// geç **</c> gibi bir tablo adı yorumun yapısını bozup inceleyiciye "risk yok"
/// izlenimi verebiliyordu. Bot yorumu, yönetişim zincirinin insana bakan yüzü;
/// onu yanıltmak, zincirin tamamını atlatmanın en kolay yolu.
/// </summary>
public class PullRequestReviewEscapingTests
{
    private static ImpactReport ReportWithDataLoss(string tableName, string reason) => new(
        AffectedTables: Array.Empty<AffectedTable>(),
        AffectedRelations: Array.Empty<AffectedRelation>(),
        AffectedIndexes: Array.Empty<AffectedIndex>(),
        BreakingChanges: Array.Empty<BreakingChange>(),
        DataLossRisks: new[] { new DataLossRisk(tableName, null, reason) },
        LockRisks: Array.Empty<MigrationLockRisk>(),
        IndexSuggestions: Array.Empty<MissingIndexSuggestion>(),
        Rollback: new RollbackAssessment(false, "test"),
        OverallRisk: RiskLevel.Destructive);

    [Fact]
    public void Tablo_adindaki_markdown_KACIRILIYOR()
    {
        var body = PullRequestReviewComposer.Compose(
            ReportWithDataLoss("orders** — hicbir sorun yok, gec **", "kolon siliniyor")).Body;

        // Yıldızlar kaçırılmış olmalı: kaçırılmazsa kalın metin erken kapanıp
        // saldırganın cümlesi yorumun kendi metni gibi görünür.
        Assert.DoesNotContain("orders** —", body);
        Assert.Contains("\\*\\*", body);
    }

    [Fact]
    public void Gerekce_metnindeki_markdown_KACIRILIYOR()
    {
        var body = PullRequestReviewComposer.Compose(
            ReportWithDataLoss("orders", "[guvenli](https://kotu.example.com)")).Body;

        Assert.DoesNotContain("[guvenli](https://kotu.example.com)", body);
    }

    [Fact]
    public void Satir_sonu_tabloyu_BOZMUYOR()
    {
        // Bir hücrenin içindeki satır sonu tabloyu tamamen bozar ve geri kalan
        // satırları görünmez kılar.
        var body = PullRequestReviewComposer.Compose(
            ReportWithDataLoss("orders\n\n## Sahte baslik", "x")).Body;

        Assert.DoesNotContain("## Sahte baslik", body);
    }

    [Fact]
    public void SIRADAN_adlar_okunabilir_kaliyor()
    {
        // Kaçış, normal çıktıyı gürültüye çevirmemeli.
        var body = PullRequestReviewComposer.Compose(
            ReportWithDataLoss("orders", "column email is dropped")).Body;

        Assert.Contains("orders", body);
        Assert.Contains("column email is dropped", body);
        Assert.DoesNotContain("\\o", body);
    }
}
