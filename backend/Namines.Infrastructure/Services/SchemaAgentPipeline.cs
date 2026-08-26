using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
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
public sealed record SchemaAgentResult(
    DatabaseSchema Schema,
    IReadOnlyList<string> RemainingFindings,
    IReadOnlyList<string> PortabilityNotes,
    int Rounds)
{
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
/// <b>Kapı deterministik, ikinci bir model değil.</b> "Modele kendi çıktısını
/// kontrol ettirmek" aynı yanılgıyı iki kez üretir; linter ve DDL üreticisi ise
/// aynı girdiye her zaman aynı cevabı verir.
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

    private readonly ISchemaDraftSource _source;
    private readonly ILinterService _linter;
    private readonly IDdlGeneratorFactory _ddlFactory;
    private readonly ILogger<SchemaAgentPipeline> _logger;

    public SchemaAgentPipeline(
        ISchemaDraftSource source,
        ILinterService linter,
        IDdlGeneratorFactory ddlFactory,
        ILogger<SchemaAgentPipeline> logger)
    {
        _source = source;
        _linter = linter;
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
    public async Task<SchemaAgentResult> RunAsync(
        string prompt,
        DatabaseType engine,
        int budgetRounds = DefaultRepairRounds + 1,
        CancellationToken cancellationToken = default,
        IProgress<AgentStep>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        // Taslağın kendisi de bir tur. Bütçe bir tura bile yetmiyorsa hattı hiç
        // başlatmıyoruz: yarım harcanmış bir bütçe kullanıcıya hiçbir şey vermez.
        if (budgetRounds < 1)
            throw new InvalidOperationException("There is not enough AI budget left to generate a schema.");

        progress?.Report(AgentStep.Draft("Taslak üretiliyor..."));
        var schema = await _source.DraftAsync(prompt, engine, cancellationToken);
        var rounds = 1;
        progress?.Report(AgentStep.Draft(
            $"Taslak üretildi — {schema.Tables.Count} tablo, {schema.Relations.Count} ilişki"));

        progress?.Report(AgentStep.Inspect($"{engine} üzerinde derleniyor..."));
        var findings = Inspect(schema, engine);

        while (findings.Count > 0 && rounds < budgetRounds)
        {
            _logger.LogInformation(
                "Schema agent round {Round}: {Count} finding(s) to repair.", rounds, findings.Count);

            foreach (var finding in findings)
                progress?.Report(AgentStep.Finding(finding));

            progress?.Report(AgentStep.Repair($"Düzeltiliyor (tur {rounds}/{budgetRounds - 1})..."));
            var repaired = await _source.RepairAsync(schema, findings, engine, cancellationToken);
            rounds++;

            progress?.Report(AgentStep.Inspect($"{engine} üzerinde yeniden derleniyor..."));
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
            progress?.Report(AgentStep.Clean($"{engine} üzerinde temiz — bulgu kalmadı"));

        return new SchemaAgentResult(schema, findings, Portability(schema, engine), rounds);
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
        foreach (var message in _linter.Lint(schema).Messages.Where(m => m.Severity == LintSeverity.Error))
            findings.Add($"[rule] {message.Message}");

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
