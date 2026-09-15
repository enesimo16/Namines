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

    /// <summary>
    /// Taslak token başına yaklaşık maliyet (bkz. GroqAIService.CalculateMaxTokens'in
    /// aynı 600/tablo oranı) — parçalı (chunked) üretimde her tablo, ayrı bir
    /// domain parçasında kendi tur payını harcıyor.
    /// </summary>
    private const int TokensPerTable = 600;

    /// <summary>
    /// Bir onarım turunun sabit token maliyeti. <see cref="RoundEquivalents"/>'in
    /// kullandığı <see cref="RepairRoundMultiplier"/> ile ÇARPILIYOR — burada
    /// yeni bir sayı icat etmek, iki hesaplamanın aynı "onarım turu ne kadar
    /// pahalı" varsayımından sessizce ayrışmasına yol açardı.
    /// </summary>
    private const int TokensPerRepairRound = 2_500;

    /// <summary>Plan (ön prompt) turunun sabit maliyeti.</summary>
    private const int PlanRoundTokens = 1_500;

    /// <summary>
    /// Bir şema üretim/onarım isteği için PEŞİNEN rezerve edilecek TOKEN
    /// sayısını, şemanın gerçek boyutuna göre hesaplar.
    ///
    /// <b>Neden gerekli:</b> <see cref="RoundEquivalents"/> "tur eşdeğeri"
    /// sayıyor ve çağıran tarafta sabit bir tur-başı token varsayımıyla
    /// (~2500) çarpılıyor. Bu varsayım küçük şemalar için doğruydu ama 50-60
    /// tablolu, parçalı (chunked) üretilen bir şema tek bir turda bunun
    /// KATLARINI harcıyor — <c>RoundEquivalents</c> bunu hiç görmüyor çünkü
    /// tablo sayısından habersiz. Sonuç: 60 tablolu bir istek, kullanıcının
    /// günlük bütçesinin tamamını tek seferde, önceden hiçbir uyarı olmadan
    /// tüketebiliyordu.
    ///
    /// <b>Neden <see cref="RoundEquivalents"/> silinmiyor:</b> eşik altındaki
    /// (küçük/orta) şemalar için hattın hâlâ kullandığı yol bu — burada yeni
    /// bir kapsam-farkında tahmin devreye girmiyor demek, mevcut davranışın
    /// bozulmaması demek.
    /// </summary>
    /// <param name="tableCount">Şemadaki tablo sayısı (taslak maliyeti).</param>
    /// <param name="repairRounds">Bütçelenen onarım turu sayısı.</param>
    public static int TokensForScope(int tableCount, int repairRounds)
    {
        return tableCount * TokensPerTable
            + repairRounds * TokensPerRepairRound * RepairRoundMultiplier
            + PlanRoundTokens;
    }
}
