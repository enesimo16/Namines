using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Nsl;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Infrastructure.Services;

/// <param name="Schema">Elde kalan şema — hatalı olsa bile döner.</param>
/// <param name="RemainingFindings">
/// HEDEF motorda düzeltilemeyen bulgular. Boşsa şema o motorda çalışır.
/// </param>
/// <param name="PortabilityNotes">
/// Şemanın DİĞER motorlarda takıldığı yerler.
///
/// <b>Bunlar bulgu değil, bilgi — ve düzeltme turu harcatmıyorlar.</b> Kullanıcı
/// PostgreSQL istediyse Oracle'ın collation'ı desteklememesi onun sorunu değil;
/// modeli bunun için tura sokmak, istenmemiş bir uyum uğruna kullanıcının
/// bütçesini harcamak olurdu. Yine de raporlanıyor ki "bu şemayı yarın MySQL'e
/// taşıyabilir miyim" sorusu cevapsız kalmasın.
/// </param>
/// <param name="Rounds">Kaç AI turu harcandı (taslak dahil).</param>
/// <param name="MergeNotes">
/// Parçalı (chunked) üretim yolunda <see cref="Namines.Core.Models.SchemaChunkMerger"/>'ın
/// ürettiği birleştirme notları artı başarısız parça bildirimleri. Plan
/// bölünmeye ihtiyaç duymadıysa (tek çağrılık yol) her zaman boş liste —
/// mevcut çağıranların pozisyonel oluşturması bu yüzden bozulmuyor.
/// </param>
public sealed record SchemaAgentResult(
    DatabaseSchema Schema,
    IReadOnlyList<string> RemainingFindings,
    IReadOnlyList<string> PortabilityNotes,
    int Rounds,
    IReadOnlyList<string>? MergeNotes = null)
{
    /// <summary>Ham alan her zaman non-null olsun diye normalize edilmiş erişim.</summary>
    public IReadOnlyList<string> MergeNotes { get; init; } = MergeNotes ?? Array.Empty<string>();

    /// <summary>Hedef motorda hiçbir bulgu kalmadıysa true.</summary>
    public bool Clean => RemainingFindings.Count == 0;

    /// <summary>Şema altı motorun hepsinde derleniyor mu?</summary>
    public bool PortableEverywhere => Clean && PortabilityNotes.Count == 0;
}

/// <summary>
/// İlk prompt'tan çalışan bir şemaya giden ajan hattı (09-AI-LAYER.md).
///
/// <b>Çözdüğü sorun:</b> şema üretimi bugüne kadar <b>tek bir LLM çağrısıydı</b> —
/// model ne döndürdüyse kullanıcıya o gidiyordu. Oysa bu kod tabanının her
/// yerinde geçerli olan kural şu: <i>AI bulgu üretmez, kural motoru üretir.</i>
/// Şema üretimi bu kuralın dışında kalmış tek yerdi ve sonucu şuydu: model
/// birincil anahtarı unutabilir, var olmayan bir tabloya yabancı anahtar
/// yazabilir, ya da bir motorun kabul etmeyeceği bir tip seçebilirdi — ve bunu
/// kullanıcı ancak veritabanı reddedince öğrenirdi.
///
/// <b>Hattın şekli:</b>
/// <code>
/// taslak (AI)
///   → DENETİM (linter + GERÇEK DDL üreticileri, altı motor)
///   → bulgu varsa: düzelt (AI) → tekrar denetle
///   → tur sınırına gelince: kalan bulguları AÇIKÇA söyle
/// </code>
///
/// <b>Kapı deterministik; araçlar modelin gözü, hakemi değil.</b> Model artık
/// düzeltme turunda kural motorunu ve gerçek DDL üreticisini ARAÇ olarak
/// çağırabiliyor (bkz. AgentTools) — yani yazmadan önce bakabiliyor. Ama turu
/// bitiren karar hâlâ burada, deterministik tarafta veriliyor: modelin kendi
/// çıktısına "temiz" demesi hiçbir şeyi kapatmaz, çünkü aynı yanılgıyı iki kez
/// üretebilir. Araçlar bulguyu ERKEN göstermeye yarıyor, bulguyu KALDIRMAYA değil.
///
/// <b>Döngü SINIRLI ve sonucu gizlemiyor.</b> Sınırsız bir düzeltme döngüsü,
/// modelin çözemediği bir bulguda kullanıcının bütçesini sessizce tüketirdi.
/// Sınıra gelindiğinde şema yine dönüyor ama <see cref="SchemaAgentResult.Clean"/>
/// false ve kalan bulgular listeleniyor — "çalışıyor gibi görünen" bir şema
/// vermek, hiç vermemekten kötüdür.
/// </summary>
public sealed class SchemaAgentPipeline
{
    /// <summary>
    /// Varsayılan düzeltme turu sayısı.
    ///
    /// İki tur ampirik bir denge: bir tur çoğu unutulmuş anahtarı/ilişkiyi
    /// düzeltiyor, ikinci tur ilkinde ortaya çıkan yan etkiyi topluyor. Üçüncü
    /// turda model genelde aynı yerde dönmeye başlıyor — o noktada tur eklemek
    /// bütçe harcamaktan başka işe yaramıyor.
    /// </summary>
    public const int DefaultRepairRounds = 2;

    /// <summary>
    /// Düzeltme turlarının DIŞINDA kalan sabit turlar: plan + taslak.
    ///
    /// <b>Neden adı var:</b> tavanı hesaplayan çağıran (bkz.
    /// <c>SchemaController.AffordableRoundsAsync</c>) aritmetiği kendi başına
    /// yaparsa, tur yapısı değiştiğinde sessizce yanlış hesaplar — plan turu
    /// eklendiğinde tam olarak bu olmuş ve bir düzeltme hakkı yenmişti.
    /// </summary>
    public const int FixedRounds = 2;

    /// <summary>
    /// Tam tur bütçesi: plan (1) + taslak (1) + düzeltme turları.
    ///
    /// <b>Neden ayrı bir sabit:</b> plan turu eklendiğinde bütçe tavanı
    /// değişmeseydi, plan turu düzeltme turlarından birini yiyecekti — yani
    /// kullanıcı fark etmeden bir düzeltme hakkı kaybedecekti. Çağıranların
    /// <c>+1</c>/<c>+2</c> aritmetiğini kendi başlarına yapması da aynı hatayı
    /// bir sonraki değişiklikte tekrar üretirdi.
    /// </summary>
    public const int DefaultTotalRounds = FixedRounds + DefaultRepairRounds;

    private readonly ISchemaDraftSource _source;
    private readonly IDdlGeneratorFactory _ddlFactory;
    private readonly ILogger<SchemaAgentPipeline> _logger;

    public SchemaAgentPipeline(
        ISchemaDraftSource source,
        IDdlGeneratorFactory ddlFactory,
        ILogger<SchemaAgentPipeline> logger)
    {
        _source = source;
        _ddlFactory = ddlFactory;
        _logger = logger;
    }

    /// <param name="budgetRounds">
    /// Kullanıcının bütçesinin izin verdiği AI turu sayısı. Çağıran bunu kotadan
    /// hesaplar; hat kendi başına bütçe harcamaya karar veremez.
    /// </param>
    /// <param name="progress">
    /// Adım bildirimi — üretim ekranına akış hâlinde gönderilir
    /// (bkz. second-phase/04-LOADING-EKRANI.md). <c>null</c> olabilir: akış
    /// istemeyen çağıranlar (ör. RegionalPromptPanel'in kullandığı revizyon
    /// yolu) hiçbir şey vermez, hat sessizce çalışır.
    /// </param>
    /// <param name="onScopePlanned">
    /// Plan ayrıştırılır ayrıştırılmaz — taslak/parça çağrılarından ÖNCE —
    /// çağrılan kapsam bildirimi: kaç tablo üretileceği. Yalnızca plan
    /// başarıyla ayrıştıysa çağrılır; plan yoksa/bozuksa hiç çağrılmaz.
    ///
    /// <b>Hat bütçeye kendi başına karar VERMEZ, yalnızca BİLDİRİR.</b> Karar
    /// çağıranın: geri çağrı fırlatırsa hat onu yakalamaz, üretim hiç
    /// başlamadan durur. <c>null</c> ise davranış bugünküyle birebir aynı.
    /// </param>
    public async Task<SchemaAgentResult> RunAsync(
        string prompt,
        DatabaseType engine,
        int budgetRounds = DefaultTotalRounds,
        CancellationToken cancellationToken = default,
        IProgress<AgentStep>? progress = null,
        Func<int, CancellationToken, Task>? onScopePlanned = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        // Taslağın kendisi de bir tur. Bütçe bir tura bile yetmiyorsa hattı hiç
        // başlatmıyoruz: yarım harcanmış bir bütçe kullanıcıya hiçbir şey vermez.
        if (budgetRounds < 1)
            throw new InvalidOperationException("There is not enough AI budget left to generate a schema.");

        // Plan turu bütçe yetiyorsa yapılır. Dar bütçede ATLANIYOR: taslak + en
        // az bir onarım, plandan daha değerli — plan tek başına kullanıcıya
        // çalışan bir şema vermez, yalnızca bir tur harcar.
        string? plan = null;
        var rounds = 0;

        if (budgetRounds >= 3)
        {
            progress?.Report(AgentStep.Plan("Planning…"));
            try
            {
                plan = await _source.PlanAsync(prompt, engine, cancellationToken);
                rounds++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Plan bir İYİLEŞTİRME, zorunluluk değil: onsuz da şema
                // üretilebiliyor. Üretimi burada düşürmek, opsiyonel bir adımı
                // zorunlu kılmak olurdu.
                _logger.LogWarning(ex, "Schema agent plan turn failed; continuing without a plan.");
                plan = null;
            }
        }

        var parsedPlan = Namines.Core.Analysis.SchemaScopePlan.TryParse(plan);

        if (parsedPlan is not null && onScopePlanned is not null)
            await onScopePlanned(parsedPlan.TableCount, cancellationToken);

        DatabaseSchema schema;
        var mergeNotes = new List<string>();

        if (parsedPlan is not null && Namines.Core.Analysis.SchemaScopePartitioner.ShouldPartition(parsedPlan))
        {
            // Büyük plan: birkaç paralel parça çağrısına bölüp sonra
            // birleştiriyoruz — tek çağrı 12'den fazla tabloya güvenle sığmaz.
            var chunks = Namines.Core.Analysis.SchemaScopePartitioner.Partition(parsedPlan);
            progress?.Report(AgentStep.Draft(
                $"Generating {parsedPlan.TableCount} tables across {chunks.Count} domains…"));

            var tasks = chunks.Select(async chunk =>
            {
                try
                {
                    var part = await _source.DraftChunkAsync(
                        prompt, engine, chunk, parsedPlan.AllTableNames, cancellationToken);
                    progress?.Report(AgentStep.Draft($"{chunk.Label} — {part.Tables.Count} tables"));
                    return (Part: part, Error: (string?)null);
                }
                catch (OperationCanceledException)
                {
                    // Kullanıcı iptali hata DEĞİL — diğer parçaların
                    // "başarısızlığı yutup devam et" mantığına girmemeli;
                    // Task.WhenAll bu istisnayı olduğu gibi yukarı taşır.
                    throw;
                }
                catch (Exception ex)
                {
                    // Bir alanın patlaması diğerlerini iptal ETMEZ: 60 tablonun
                    // 50'sini vermek, hiçbir şey vermemekten iyidir.
                    return (Part: (DatabaseSchema?)null,
                        Error: $"[merge] Domain '{chunk.Label}' failed: {ex.Message}");
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            mergeNotes.AddRange(results.Where(r => r.Error is not null).Select(r => r.Error!));

            var parts = results.Where(r => r.Part is not null).Select(r => r.Part!).ToList();
            if (parts.Count == 0)
                throw new InvalidOperationException("Every domain of the schema failed to generate.");

            var merged = Namines.Core.Models.SchemaChunkMerger.Merge(parsedPlan.SchemaName, parts);
            schema = merged.Schema;
            mergeNotes.AddRange(merged.Notes);
        }
        else
        {
            progress?.Report(AgentStep.Draft("Generating draft…"));
            schema = await _source.DraftAsync(prompt, engine, parsedPlan?.RenderAsText() ?? plan, cancellationToken);
        }

        // Parçalı yol birden çok upstream çağrısı yapsa da, araç döngüsünün
        // sayılmaması ile aynı ilkeyle, hat için TEK BİR taslak turu sayılır —
        // gerçek maliyet SettleAsync'in ölçümünden geliyor.
        rounds++;
        progress?.Report(AgentStep.Draft(
            $"Draft generated — {schema.Tables.Count} tables, {schema.Relations.Count} relations"));

        // Buraya kadar harcanan SABİT tur sayısı — plan çalıştıysa 2 (plan+taslak),
        // atlandıysa 1 (yalnız taslak). İlerleme mesajının "kaçıncı ONARIM
        // denemesi" demesi için bu ayrım şart: plan turu eklendiğinde `rounds`
        // taslaktan sonra 2'den başlıyor, ama bu hâlâ 1. onarım denemesi.
        // Sabit bir FixedRounds SAYMAK yanlış olurdu — plan turu hata alıp
        // atlanmışsa (yukarıdaki catch) rounds hâlâ 1'de kalıyor.
        var fixedRoundsUsed = rounds;

        progress?.Report(AgentStep.Inspect($"Compiling on {engine}…"));
        var findings = Inspect(schema, engine);

        while (findings.Count > 0 && rounds < budgetRounds)
        {
            _logger.LogInformation(
                "Schema agent round {Round}: {Count} finding(s) to repair.", rounds, findings.Count);

            foreach (var finding in findings)
                progress?.Report(AgentStep.Finding(finding));

            // Numaratör "kaçıncı onarım denemesi", paydası "toplam kaç onarım
            // hakkı var" — ikisi de fixedRoundsUsed'e göre, budgetRounds'a
            // göre DEĞİL. rounds - fixedRoundsUsed + 1: rounds henüz bu
            // denemeyi saymıyor (artış RepairAsync başarılı dönünce oluyor),
            // yani bu her zaman 1'den başlayan bir deneme numarası verir.
            var repairAttempt = rounds - fixedRoundsUsed + 1;
            var maxRepairAttempts = budgetRounds - fixedRoundsUsed;
            progress?.Report(AgentStep.Repair($"Repairing (round {repairAttempt}/{maxRepairAttempts})…"));

            DatabaseSchema repaired;
            try
            {
                repaired = await _source.RepairAsync(schema, findings, engine, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Kullanıcı iptali hata DEĞİL; bulguya çevrilirse çağıran onu
                // normal bir sonuç sanar ve iptal edilmiş işi tamamlanmış gösterir.
                throw;
            }
            catch (Exception ex)
            {
                // Elde GEÇERLİ bir şema var ve kullanıcı onun bedelini zaten
                // ödedi. Turun patlaması yüzünden onu da çöpe atmak, hattın
                // kendi sözünü ("elde kalan şema — hatalı olsa bile döner")
                // bozmak olurdu. Hata gizlenmiyor: bulgu olarak raporlanıyor.
                _logger.LogWarning(
                    ex, "Schema agent repair round {Round} failed; returning the best schema so far.", rounds);

                findings = findings
                    .Append($"[agent] Repair round {rounds} could not run: {ex.Message}")
                    .ToList();
                break;
            }

            rounds++;

            progress?.Report(AgentStep.Inspect($"Recompiling on {engine}…"));
            var afterRepair = Inspect(repaired, engine);

            // İyileşme YOKSA dur. Model aynı bulgularla dönüyorsa bir tur daha
            // aynı sonucu verir; devam etmek yalnızca bütçe harcar.
            if (afterRepair.Count >= findings.Count && SameFindings(findings, afterRepair))
            {
                _logger.LogInformation("Schema agent stopped early: the repair round changed nothing.");
                // Düzeltilmiş hâli yine de alıyoruz — daha kötü değil, sadece
                // daha iyi de değil; kullanıcının elinde en son hâl olsun.
                schema = repaired;
                findings = afterRepair;
                break;
            }

            schema = repaired;
            findings = afterRepair;
        }

        if (findings.Count == 0)
            progress?.Report(AgentStep.Clean($"Clean on {engine} — no findings left"));

        return new SchemaAgentResult(schema, findings, Portability(schema, engine), rounds, mergeNotes);
    }

    /// <summary>
    /// Şemanın diğer motorlarda takıldığı yerler — döngüyü ETKİLEMEZ.
    ///
    /// Denetimden ayrı bir metot olması bilinçli: aynı listeye koymak, bir
    /// taşınabilirlik notunu düzeltilmesi gereken bir hataya çevirirdi ve model
    /// kullanıcının istemediği bir uyum için tur harcardı.
    /// </summary>
    private List<string> Portability(DatabaseSchema schema, DatabaseType target)
    {
        var notes = new List<string>();

        foreach (var other in AllEngines.Where(e => e != target))
            notes.AddRange(CompileFindings(schema, other));

        return notes;
    }

    private const string CompileFindingPrefix = "[compile]";

    /// <summary>
    /// Deterministik denetim: kural motoru + gerçek DDL üretimi.
    ///
    /// <b>DDL gerçekten üretiliyor, "üretilebilir mi" diye tahmin edilmiyor.</b>
    /// Bu kod tabanında birden çok kez görüldü: metin testleri geçen bir şema
    /// gerçek motorda reddedilebiliyor. Üretici bir istisna fırlatıyorsa
    /// (desteklenmeyen dizi, tanımsız enum, geçersiz birleşim) o bulgudur.
    /// </summary>
    private List<string> Inspect(DatabaseSchema schema, DatabaseType engine)
    {
        var findings = new List<string>();

        // 1) Kural motoru — yalnızca HATALAR. Uyarıları düzeltme döngüsüne
        //    sokmak, modeli stil tercihleri için tur harcamaya iter.
        //
        //    LinterService DEĞİL, NslValidator: eskisi üç kurallıydı ve bileşik
        //    birincil anahtarı hata sayıyordu — oysa bu kod tabanının kendi golden
        //    fixture'ı (03-composite-key) onu meşru sayıyor ve üreticiler tek bir
        //    bileşik PK kısıtı yazıyor. Yani bileşik anahtarlı her şema, hiçbir
        //    zaman kapanmayacak bir bulguyla tüm bütçeyi yakıyordu.
        //
        //    Kural KODU da bulguya yazılıyor (NSL004 gibi): model hangi kuralı
        //    ihlal ettiğini bilirse düzeltmesi isabetli oluyor, metni yeniden
        //    yorumlamak zorunda kalmıyor.
        foreach (var finding in NslValidator.Validate(schema, engine).Where(f => f.Severity == "error"))
            findings.Add($"[rule] {finding.Code}: {finding.Message}");

        // 2) HEDEF motorda gerçekten derleniyor mu? Yalnızca bu, düzeltme turunu
        //    hak ediyor — kullanıcı bu motoru seçti.
        findings.AddRange(CompileFindings(schema, engine));

        return findings;
    }

    private static readonly DatabaseType[] AllEngines =
    {
        DatabaseType.PostgreSQL, DatabaseType.MSSQL, DatabaseType.MySQL,
        DatabaseType.MariaDB, DatabaseType.Oracle, DatabaseType.SQLite,
    };

    private IEnumerable<string> CompileFindings(DatabaseSchema schema, DatabaseType engine)
    {
        try
        {
            var ddl = _ddlFactory.GetGenerator(engine).Generate(schema);

            // Boş DDL, "hata yok" demek DEĞİL: tablosu olmayan bir şema
            // üretilmiş demektir ve kullanıcı bunu istememişti.
            if (string.IsNullOrWhiteSpace(ddl))
                return new[] { $"{CompileFindingPrefix} {engine}: the schema produced no DDL at all." };

            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            return new[] { $"{CompileFindingPrefix} {engine}: {ex.Message}" };
        }
    }

    /// <summary>
    /// İki bulgu listesi aynı mı? Sıra önemsiz.
    ///
    /// Yalnızca SAYIYA bakmak yetmez: model bir bulguyu düzeltip yerine yenisini
    /// üretmiş olabilir ve o durumda döngü ilerliyor demektir.
    /// </summary>
    private static bool SameFindings(IReadOnlyList<string> before, IReadOnlyList<string> after) =>
        before.Count == after.Count &&
        before.OrderBy(x => x, StringComparer.Ordinal)
              .SequenceEqual(after.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);
}
