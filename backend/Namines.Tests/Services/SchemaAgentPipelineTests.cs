using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// İlk prompt'tan çalışan bir şemaya giden ajan hattı (09-AI-LAYER.md).
///
/// <b>Sahte bir AI kullanılıyor ve bu testin ZAYIFLIĞI değil, konusu.</b> Hattın
/// değeri modelin ne ürettiğinde değil, <b>üretileni kimin denetlediğinde</b>:
/// kapı deterministik (linter + gerçek DDL üreticileri), döngü sınırlı ve sonuç
/// gizlenmiyor. Sahte model, bu üç şeyi gerçek bir LLM'in kaprisleri olmadan
/// kanıtlamayı sağlıyor.
/// </summary>
public class SchemaAgentPipelineTests
{
    // ── Sahte model ──────────────────────────────────────────────────────────

    private sealed class FakeSource : ISchemaDraftSource
    {
        private readonly Queue<DatabaseSchema> _answers;

        public FakeSource(params DatabaseSchema[] answers) => _answers = new Queue<DatabaseSchema>(answers);

        public List<IReadOnlyList<string>> SeenFindings { get; } = new();
        public int DraftCalls { get; private set; }
        public int RepairCalls { get; private set; }

        public Task<DatabaseSchema> DraftAsync(string prompt, DatabaseType engine, CancellationToken ct = default)
        {
            DraftCalls++;
            return Task.FromResult(Next());
        }

        public Task<DatabaseSchema> RepairAsync(
            DatabaseSchema schema, IReadOnlyList<string> findings, DatabaseType engine, CancellationToken ct = default)
        {
            RepairCalls++;
            SeenFindings.Add(findings);
            return Task.FromResult(Next());
        }

        // Cevap kalmadıysa sonuncuyu tekrarlıyor: "model aynı şeyi döndürmeye
        // devam ediyor" senaryosunu üretmenin yolu bu.
        private DatabaseSchema Next() => _answers.Count > 1 ? _answers.Dequeue() : _answers.Peek();
    }

    private static SchemaAgentPipeline Pipeline(FakeSource source) =>
        new(source, new LinterService(), new DdlGeneratorFactory(),
            NullLogger<SchemaAgentPipeline>.Instance);

    // ── Şemalar ──────────────────────────────────────────────────────────────

    private static DatabaseSchema Healthy() => new()
    {
        Name = "shop",
        Tables =
        {
            new SchemaTable
            {
                Id = "t1", Name = "users",
                Columns =
                {
                    new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "c2", Name = "email", Type = "VARCHAR", Length = 255 },
                },
            },
        },
    };

    /// <summary>Hedef motorda DERLENMEYEN şema: tanımsız bir enum'a başvuruyor.</summary>
    private static DatabaseSchema Broken()
    {
        var schema = Healthy();
        schema.Tables[0].Columns[1].EnumRef = "status_that_does_not_exist";
        return schema;
    }

    /// <summary>Yalnızca PostgreSQL'de çalışan şema: dizi kolonu.</summary>
    private static DatabaseSchema PostgresOnly()
    {
        var schema = Healthy();
        schema.Tables[0].Columns.Add(
            new SchemaColumn { Id = "c3", Name = "tags", Type = "TEXT", IsArray = true, IsNullable = true });
        return schema;
    }

    // ── Temel akış ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_clean_draft_needs_no_repair_round()
    {
        // Sorunsuz bir taslakta ikinci bir çağrı yapmak, kullanıcının bütçesini
        // hiçbir şey için harcamak olurdu.
        var source = new FakeSource(Healthy());

        var result = await Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL);

        Assert.True(result.Clean);
        Assert.Equal(1, result.Rounds);
        Assert.Equal(0, source.RepairCalls);
    }

    [Fact]
    public async Task A_broken_draft_is_repaired_and_the_result_is_clean()
    {
        var source = new FakeSource(Broken(), Healthy());

        var result = await Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL);

        Assert.True(result.Clean);
        Assert.Equal(2, result.Rounds);
        Assert.Empty(result.RemainingFindings);
    }

    [Fact]
    public async Task The_model_is_told_exactly_what_is_wrong()
    {
        // "Bir daha bak" demek modelin aynı yanılgıyı tekrarlamasına yol açar;
        // bulgular deterministik motorlardan geliyor ve somut.
        var source = new FakeSource(Broken(), Healthy());

        await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL);

        var findings = Assert.Single(source.SeenFindings);
        Assert.Contains(findings, f => f.Contains("status_that_does_not_exist"));
    }

    // ── Döngü sınırı ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_model_that_never_fixes_it_does_not_loop_forever()
    {
        // Sınırsız bir döngü, modelin çözemediği bir bulguda kullanıcının
        // bütçesini sessizce tüketirdi.
        var source = new FakeSource(Broken());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 5);

        Assert.False(result.Clean);
        // İkinci tur ilkiyle aynı bulguları verdiği anda duruyor: devam etmek
        // yalnızca bütçe harcar.
        Assert.Equal(2, result.Rounds);
    }

    [Fact]
    public async Task A_failed_schema_is_returned_with_its_findings_not_hidden()
    {
        // "Çalışıyor gibi görünen" bir şema vermek, hiç vermemekten kötüdür:
        // kullanıcı onu kullanmaya kalkar ve hata veritabanında patlar.
        var source = new FakeSource(Broken());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL);

        Assert.NotNull(result.Schema);
        Assert.NotEmpty(result.RemainingFindings);
        Assert.False(result.Clean);
    }

    [Fact]
    public async Task The_budget_caps_the_number_of_ai_calls()
    {
        var source = new FakeSource(Broken(), Broken(), Broken(), Healthy());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 2);

        Assert.Equal(2, result.Rounds);
        Assert.Equal(1, source.RepairCalls);
    }

    [Fact]
    public async Task With_no_budget_the_pipeline_refuses_to_start()
    {
        // Yarım harcanmış bir bütçe kullanıcıya hiçbir şey vermez.
        var source = new FakeSource(Healthy());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 0));

        Assert.Equal(0, source.DraftCalls);
    }

    // ── Taşınabilirlik ───────────────────────────────────────────────────────

    [Fact]
    public async Task Another_engines_limitation_does_not_trigger_a_repair_round()
    {
        // Kullanıcı PostgreSQL istediyse Oracle'ın diziyi desteklememesi onun
        // sorunu değil; bunun için tur harcamak, istenmemiş bir uyum uğruna
        // bütçe yakmak olurdu.
        var source = new FakeSource(PostgresOnly());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL);

        Assert.True(result.Clean);
        Assert.Equal(0, source.RepairCalls);
    }

    [Fact]
    public async Task But_that_limitation_is_still_reported()
    {
        // "Bu şemayı yarın MySQL'e taşıyabilir miyim" sorusu cevapsız kalmamalı.
        var source = new FakeSource(PostgresOnly());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL);

        Assert.NotEmpty(result.PortabilityNotes);
        Assert.False(result.PortableEverywhere);
        Assert.Contains(result.PortabilityNotes, n => n.Contains("Oracle") || n.Contains("MySQL"));
    }

    [Fact]
    public async Task A_portable_schema_says_so()
    {
        var source = new FakeSource(Healthy());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL);

        Assert.True(result.PortableEverywhere);
        Assert.Empty(result.PortabilityNotes);
    }
}
