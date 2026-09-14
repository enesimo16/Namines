using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Hat, bir turun patlamasıyla elde duranı KAYBETMEMELİ.
///
/// Taslak üretildikten sonra bir onarım turunun hız sınırına takılması, o ana
/// kadar üretilmiş (ve kullanıcının kotasından ödenmiş) geçerli şemanın çöpe
/// gitmesi anlamına gelmemeli — hattın kendi sözü de bu:
/// "Elde kalan şema — hatalı olsa bile döner."
/// </summary>
public class SchemaAgentResilienceTests
{
    private sealed class ThrowingSource : ISchemaDraftSource
    {
        private readonly DatabaseSchema _draft;
        private readonly Exception _failure;
        private readonly bool _failPlan;
        private readonly bool _failDraft;

        public ThrowingSource(
            DatabaseSchema draft, Exception failure, bool failPlan = false, bool failDraft = false)
        {
            _draft = draft;
            _failure = failure;
            _failPlan = failPlan;
            _failDraft = failDraft;
        }

        public int DraftCalls { get; private set; }

        public Task<string?> PlanAsync(string prompt, DatabaseType engine, CancellationToken ct = default) =>
            _failPlan ? throw _failure : Task.FromResult<string?>("plan");

        public Task<DatabaseSchema> DraftAsync(
            string prompt, DatabaseType engine, string? plan, CancellationToken ct = default)
        {
            DraftCalls++;
            return _failDraft ? throw _failure : Task.FromResult(_draft);
        }

        public Task<DatabaseSchema> RepairAsync(
            DatabaseSchema schema, IReadOnlyList<string> findings, DatabaseType engine,
            CancellationToken ct = default) => throw _failure;
    }

    /// <summary>FK tipi uyuşmayan şema — onarım turu tetikler.</summary>
    private static DatabaseSchema NeedsRepair()
    {
        var schema = new DatabaseSchema { Name = "shop" };

        var users = new SchemaTable { Id = "t1", Name = "users" };
        users.Columns.Add(new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true });

        var orders = new SchemaTable { Id = "t2", Name = "orders" };
        orders.Columns.Add(new SchemaColumn { Id = "c2", Name = "id", Type = "INT", IsPK = true });
        orders.Columns.Add(new SchemaColumn
        {
            Id = "c3", Name = "user_id", Type = "VARCHAR", Length = 50, IsFK = true
        });

        schema.Tables.Add(users);
        schema.Tables.Add(orders);
        schema.Relations.Add(new SchemaRelation
        {
            Id = "r1",
            Type = "OneToMany",
            SourceTableId = "t2",
            SourceColumnId = "c3",
            TargetTableId = "t1",
            TargetColumnId = "c1",
        });

        return schema;
    }

    private static SchemaAgentPipeline Pipeline(ISchemaDraftSource source) =>
        new(source, new DdlGeneratorFactory(), NullLogger<SchemaAgentPipeline>.Instance);

    [Fact]
    public async Task A_failed_repair_round_still_returns_the_draft()
    {
        var source = new ThrowingSource(NeedsRepair(), new InvalidOperationException("rate limited"));

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4);

        Assert.NotNull(result.Schema);
        Assert.Equal(2, result.Schema.Tables.Count);
        Assert.False(result.Clean);
    }

    [Fact]
    public async Task The_failure_is_reported_as_a_finding_not_hidden()
    {
        var source = new ThrowingSource(NeedsRepair(), new InvalidOperationException("rate limited"));

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4);

        Assert.Contains(result.RemainingFindings, f => f.Contains("rate limited"));
    }

    [Fact]
    public async Task A_failed_draft_round_still_fails_the_request()
    {
        // Elde HİÇBİR ŞEY yokken "kısmi sonuç" döndürmek yalan olurdu.
        var source = new ThrowingSource(
            NeedsRepair(), new InvalidOperationException("boom"), failDraft: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4));
    }

    [Fact]
    public async Task A_failed_plan_round_does_not_stop_the_run()
    {
        // Plan turu bir İYİLEŞTİRME; onsuz da şema üretilebiliyor. Planın
        // patlaması yüzünden üretimi düşürmek, opsiyonel bir adımı zorunlu
        // kılmak olurdu.
        var source = new ThrowingSource(
            NeedsRepair(), new InvalidOperationException("plan boom"), failPlan: true);

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4);

        Assert.Equal(1, source.DraftCalls);
        Assert.NotNull(result.Schema);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_as_a_finding()
    {
        // Kullanıcı iptali bir HATA değil; bulguya çevrilirse çağıran onu
        // normal bir sonuç sanar ve iptal edilmiş bir işi tamamlanmış gösterir.
        var source = new ThrowingSource(NeedsRepair(), new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4));
    }
}
