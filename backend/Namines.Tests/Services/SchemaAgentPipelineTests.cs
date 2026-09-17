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
        public int PlanCalls { get; private set; }

        /// <summary>Plan turunun döndüreceği metin.</summary>
        public string? PlanText { get; set; }

        /// <summary>Taslak turuna gerçekten ULAŞAN plan — bağlanmadıysa null kalır.</summary>
        public string? SeenPlan { get; private set; }

        public Task<string?> PlanAsync(string prompt, DatabaseType engine, CancellationToken ct = default)
        {
            PlanCalls++;
            return Task.FromResult(PlanText);
        }

        public Task<DatabaseSchema> DraftAsync(
            string prompt, DatabaseType engine, string? plan, CancellationToken ct = default)
        {
            DraftCalls++;
            SeenPlan = plan;
            return Task.FromResult(Next());
        }

        public Task<DatabaseSchema> RepairAsync(
            DatabaseSchema schema, IReadOnlyList<string> findings, DatabaseType engine, CancellationToken ct = default)
        {
            RepairCalls++;
            SeenFindings.Add(findings);
            return Task.FromResult(Next());
        }

        public Task<DatabaseSchema> DraftChunkAsync(
            string prompt, DatabaseType engine, Namines.Core.Analysis.SchemaChunk chunk,
            IReadOnlyList<string> allTableNames, CancellationToken ct = default) =>
            throw new System.NotImplementedException("bu test parçalı üretimi kullanmıyor");

        public Task<int> EffectiveMaxOutputTokensAsync(CancellationToken ct = default) => Task.FromResult(0);

        // Cevap kalmadıysa sonuncuyu tekrarlıyor: "model aynı şeyi döndürmeye
        // devam ediyor" senaryosunu üretmenin yolu bu.
        private DatabaseSchema Next() => _answers.Count > 1 ? _answers.Dequeue() : _answers.Peek();
    }

    private static SchemaAgentPipeline Pipeline(FakeSource source) =>
        new(source, new DdlGeneratorFactory(), NullLogger<SchemaAgentPipeline>.Instance);

    /// <summary>
    /// Adımları senkron olarak toplar. <see cref="Progress{T}"/> KULLANILMIYOR:
    /// o sınıf çağrıyı yakaladığı SynchronizationContext üzerinden ASENKRON
    /// gönderir (xUnit'te context yoksa thread pool'a kuyruklar) — yani
    /// RunAsync bittiğinde mesajların hepsi henüz teslim edilmemiş olabilir ve
    /// test kararsız (flaky) olurdu.
    /// </summary>
    private sealed class CapturingProgress : IProgress<AgentStep>
    {
        public List<AgentStep> Steps { get; } = new();
        public void Report(AgentStep value) => Steps.Add(value);
    }

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

    /// <summary>
    /// Bileşik birincil anahtarlı şema — iki kolon birlikte PK.
    ///
    /// Eski kapı (LinterService) bunu "multiple primary keys" hatası sayıyordu,
    /// oysa bu kod tabanının kendi golden fixture'ı (03-composite-key) onu meşru
    /// sayıyor ve üreticiler tek bir bileşik PK kısıtı yazıyor.
    /// </summary>
    private static DatabaseSchema CompositeKey()
    {
        var schema = new DatabaseSchema
        {
            Name = "join",
            Tables =
            {
                new SchemaTable
                {
                    Id = "t_o", Name = "orders",
                    Columns = { new SchemaColumn { Id = "c_o_id", Name = "id", Type = "INT", IsPK = true } },
                },
                new SchemaTable
                {
                    Id = "t_p", Name = "products",
                    Columns = { new SchemaColumn { Id = "c_p_id", Name = "id", Type = "INT", IsPK = true } },
                },
                new SchemaTable
                {
                    Id = "t_op", Name = "order_products",
                    Columns =
                    {
                        new SchemaColumn { Id = "c_op_o", Name = "order_id", Type = "INT", IsPK = true, IsFK = true },
                        new SchemaColumn { Id = "c_op_p", Name = "product_id", Type = "INT", IsPK = true, IsFK = true },
                    },
                },
            },
        };

        schema.Relations.Add(new SchemaRelation
        {
            Id = "r1", Type = "OneToMany",
            SourceTableId = "t_op", SourceColumnId = "c_op_o",
            TargetTableId = "t_o", TargetColumnId = "c_o_id",
        });
        schema.Relations.Add(new SchemaRelation
        {
            Id = "r2", Type = "OneToMany",
            SourceTableId = "t_op", SourceColumnId = "c_op_p",
            TargetTableId = "t_p", TargetColumnId = "c_p_id",
        });

        return schema;
    }

    // ── Plan turu ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_plan_turn_runs_when_the_budget_allows_it()
    {
        var source = new FakeSource(Healthy());

        await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 3);

        Assert.Equal(1, source.PlanCalls);
    }

    [Fact]
    public async Task The_plan_turn_is_skipped_when_the_budget_is_tight()
    {
        // İki tur = taslak + bir onarım. Plan turunu buraya sıkıştırmak,
        // kullanıcının tek düzeltme hakkını plan uğruna harcamak olurdu.
        var source = new FakeSource(Healthy());

        await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 2);

        Assert.Equal(0, source.PlanCalls);
    }

    [Fact]
    public async Task The_plan_is_handed_to_the_draft_turn()
    {
        // Plan üretilip taslağa BAĞLANMAZSA tur boşa harcanmış olur.
        var source = new FakeSource(Healthy()) { PlanText = "1. users table" };

        await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 3);

        Assert.Equal("1. users table", source.SeenPlan);
    }

    [Fact]
    public async Task The_plan_turn_counts_as_a_round()
    {
        // Plan bir AI çağrısıdır; sayılmazsa kullanıcı bütçesini göremediği
        // bir yerden harcamış olur.
        var source = new FakeSource(Healthy());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 3);

        Assert.Equal(2, result.Rounds);
    }

    // ── İlerleme mesajları ───────────────────────────────────────────────────

    [Fact]
    public async Task Repair_progress_counts_repair_attempts_not_total_pipeline_rounds()
    {
        // Plan turu çalışınca "rounds" taslaktan sonra 2'den başlıyor. İlerleme
        // mesajı yine de "1. ONARIM denemesi" demeli, "2. pipeline turu" değil —
        // kullanıcı ekranda "round 2/3" görüp bir tur atlanmış sanmamalı.
        var progress = new CapturingProgress();
        var source = new FakeSource(Broken(), Healthy());

        await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4, progress: progress);

        var repairStep = Assert.Single(progress.Steps, s => s.Kind == "repair");
        Assert.Equal("Repairing (round 1/2)…", repairStep.Message);
    }

    [Fact]
    public async Task Repair_progress_is_still_correct_when_the_plan_turn_is_skipped()
    {
        // Bütçe plan turuna yetmiyorsa taslaktan sonra rounds=1 olur; bu durumda
        // sayaç zaten doğruydu — bu test onu regresyona karşı kilitliyor.
        var progress = new CapturingProgress();
        var source = new FakeSource(Broken(), Healthy());

        await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 2, progress: progress);

        var repairStep = Assert.Single(progress.Steps, s => s.Kind == "repair");
        Assert.Equal("Repairing (round 1/1)…", repairStep.Message);
    }

    // ── Temel akış ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Composite_primary_keys_are_not_treated_as_a_finding()
    {
        // Bileşik anahtar meşrudur. Hata sayılırsa, bu şemayı üreten her
        // kullanıcı kapatılamayan bir bulguyla tüm bütçesini yakar.
        var source = new FakeSource(CompositeKey());

        var result = await Pipeline(source).RunAsync("ara tablo", DatabaseType.PostgreSQL);

        Assert.True(result.Clean,
            "Bileşik PK bulgu üretmemeli: " + string.Join(" | ", result.RemainingFindings));
        Assert.Equal(0, source.RepairCalls);
    }

    [Fact]
    public async Task A_foreign_key_with_a_mismatched_type_is_reported_with_its_rule_code()
    {
        var schema = Healthy();
        schema.Tables.Add(new SchemaTable
        {
            Id = "t2", Name = "orders",
            Columns =
            {
                new SchemaColumn { Id = "c_o_id", Name = "id", Type = "INT", IsPK = true },
                // Tip uyuşmuyor: VARCHAR → INT
                new SchemaColumn { Id = "c_o_user", Name = "user_id", Type = "VARCHAR", Length = 50, IsFK = true },
            },
        });
        schema.Relations.Add(new SchemaRelation
        {
            Id = "r1", Type = "OneToMany",
            SourceTableId = "t2", SourceColumnId = "c_o_user",
            TargetTableId = "t1", TargetColumnId = "c1",
        });

        var source = new FakeSource(schema);

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL);

        Assert.False(result.Clean);
        Assert.Contains(result.RemainingFindings, f => f.Contains("NSL004"));
    }

    [Fact]
    public async Task A_clean_draft_needs_no_repair_round()
    {
        // Sorunsuz bir taslakta ikinci bir çağrı yapmak, kullanıcının bütçesini
        // hiçbir şey için harcamak olurdu.
        var source = new FakeSource(Healthy());

        var result = await Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL);

        Assert.True(result.Clean);
        // Varsayılan bütçede plan turu da çalışıyor: plan + taslak = 2 tur.
        Assert.Equal(2, result.Rounds);
        Assert.Equal(0, source.RepairCalls);
    }

    [Fact]
    public async Task A_broken_draft_is_repaired_and_the_result_is_clean()
    {
        var source = new FakeSource(Broken(), Healthy());

        var result = await Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL);

        Assert.True(result.Clean);
        // plan + taslak + bir düzeltme.
        Assert.Equal(3, result.Rounds);
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
        // yalnızca bütçe harcar. (plan + taslak + tek düzeltme denemesi)
        Assert.Equal(3, result.Rounds);
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
