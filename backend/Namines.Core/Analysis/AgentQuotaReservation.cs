using System;

namespace Namines.Core.Analysis;

/// <summary>
/// Bir şema üretim isteği için PEŞİNEN rezerve edilecek "tur eşdeğeri"
/// sayısını hesaplar.
///
/// <b>Neden gerekli:</b> gelişmiş moddaki bir onarım turu, araç döngüsü
/// (birkaç çağrı) VE okunamayan bir cevapta tetiklenen fallback'in kendi iç
/// yeniden deneme döngüsü yüzünden TEK bir "tur" için birden çok upstream
/// çağrıya açılabiliyor (bkz. GroqSchemaDraftSource.RepairWithToolsAsync).
/// Rezervasyon bunu görmezden gelirse TryReserveAsync isteği izin verir,
/// gerçek harcama SettleAsync'te doğru ölçülür — ama kullanıcı günlük
/// bütçesinin TAMAMINI tek bir istekte, önceden hiç uyarılmadan tüketebilir.
///
/// <b>Neden yalnızca onarım turları çarpılıyor:</b> plan ve taslak turları
/// (GroqSchemaDraftSource.PlanAsync/DraftAsync) TEK bir upstream çağrısı
/// yapıyor; fan-out riski yalnızca onarım turunda, araç döngüsünde.
/// </summary>
public static class AgentQuotaReservation
{
    /// <summary>
    /// Bir onarım turunun "en kötü ihtimal" gerçek çağrı sayısı 7'ye kadar
    /// çıkabiliyor (araç döngüsü: 3 tool-turu + 1 son çağrı; okunamayan
    /// cevapta devreye giren fallback'in kendi 3 denemesi). Rezervasyonu
    /// doğrudan bu sayıyla çarpmak, düşük günlük bütçeli bir planı TEK
    /// gelişmiş istekte tüketirdi. Bunun yerine daha gerçekçi, ORTA bir
    /// çarpan kullanılıyor: fazlası harcanmazsa zaten iade ediliyor (bkz.
    /// AiQuotaService.RefundAsync/ReconcileAsync) — az rezerve edip
    /// sessizce aşmaktansa, biraz fazla rezerve edip gerektiğinde erken
    /// reddetmek (429) tercih ediliyor.
    /// </summary>
    private const int RepairRoundMultiplier = 3;

    /// <param name="budgetRounds">Toplam tur bütçesi (plan + taslak + onarım turları).</param>
    /// <param name="fixedRounds">
    /// Onarım OLMAYAN, tek çağrılık turlar (plan + taslak; plan atlandıysa
    /// yalnızca taslak) — <c>SchemaAgentPipeline</c>'ın gerçekte harcadığı
    /// sabit tur sayısıyla aynı hesapla üretilmeli.
    /// </param>
    public static int RoundEquivalents(int budgetRounds, int fixedRounds)
    {
        var maxRepairRounds = Math.Max(0, budgetRounds - fixedRounds);
        var fixedRoundsCounted = Math.Min(budgetRounds, fixedRounds);

        return fixedRoundsCounted + maxRepairRounds * RepairRoundMultiplier;
    }
}
