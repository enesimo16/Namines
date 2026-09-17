using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="SchemaAgentPipeline.RunAsync"/>'in büyük planları
/// (&gt;12 tablo) birkaç paralel parça çağrısına bölüp birleştirdiğini kilitler.
///
/// <see cref="FakeSchemaDraftSource"/> kullanılıyor — gerçek AI YOK. Her
/// tabloya bir PK veriliyor ki <c>NslValidator</c> bulgu üretmesin: aksi
/// hâlde hat onarım döngüsüne girer ve test parçalamayı değil, döngüyü ölçer.
/// </summary>
public class SchemaAgentPipelinePartitionTests
{
    // 10 tablo, tek alan — bölme eşiğinin (12) ALTINDA.
    private const string SmallPlanJson = """
        {
          "schemaName": "shop",
          "domains": [
            { "name": "Catalog", "tables": [
              "products", "categories", "suppliers", "reviews", "brands",
              "warehouses", "inventory", "tags", "product_tags", "images"
            ] }
          ]
        }
        """;

    // 14 tablo, üç alan — bölme eşiğinin (12) ÜSTÜNDE. TargetTablesPerChunk=9
    // olduğundan açgözlü algoritma Identity(4)+Catalog(4)=8 tabloyu bir parçada
    // toplar, Orders(6) kendi parçasını açar (8+6=14>9).
    private const string LargePlanJson = """
        {
          "schemaName": "shop",
          "domains": [
            { "name": "Identity", "tables": ["users", "roles", "permissions", "sessions"] },
            { "name": "Catalog", "tables": ["products", "categories", "suppliers", "reviews"] },
            { "name": "Orders", "tables": [
              "orders", "order_items", "payments", "shipments", "carts", "coupons"
            ] }
          ]
        }
        """;

    private static SchemaAgentPipeline Pipeline(ISchemaDraftSource source) =>
        new(source, new DdlGeneratorFactory(), NullLogger<SchemaAgentPipeline>.Instance);

    private sealed class CapturingProgress : IProgress<AgentStep>
    {
        public List<AgentStep> Steps { get; } = new();
        public void Report(AgentStep value) => Steps.Add(value);
    }

    // ── Senaryo 1: ≤12 tablo → tek çağrı, parça YOK ─────────────────────────

    [Fact]
    public async Task Small_plan_uses_the_single_draft_call_not_chunks()
    {
        var source = new FakeSchemaDraftSource { PlanResponse = SmallPlanJson };

        await Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL);

        Assert.Equal(1, source.DraftCalls);
        Assert.Empty(source.ChunkCalls);
    }

    // ── Senaryo 2: >12 tablo → yalnızca parça çağrıları ─────────────────────

    [Fact]
    public async Task Large_plan_uses_chunk_calls_not_the_single_draft_call()
    {
        var source = new FakeSchemaDraftSource { PlanResponse = LargePlanJson };

        await Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL);

        Assert.Equal(0, source.DraftCalls);
        Assert.Equal(2, source.ChunkCalls.Count);
        Assert.Contains(source.ChunkCalls, c => c.Label == "Identity + Catalog");
        Assert.Contains(source.ChunkCalls, c => c.Label == "Orders");
    }

    // ── Senaryo 3: parçalar birleşiyor, tablo sayısı toplamı veriyor ────────

    [Fact]
    public async Task All_chunks_merge_into_one_schema_whose_table_count_is_the_sum()
    {
        var source = new FakeSchemaDraftSource { PlanResponse = LargePlanJson };

        var result = await Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL);

        Assert.Equal(14, result.Schema.Tables.Count);
    }

    // ── Senaryo 4: bir parça patlarsa diğerleri yine döner + not üretilir ───

    [Fact]
    public async Task A_failing_chunk_does_not_cancel_the_others_and_produces_a_note()
    {
        var source = new FakeSchemaDraftSource { PlanResponse = LargePlanJson };
        source.FailingChunkLabels.Add("Orders");

        var result = await Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL);

        // Identity + Catalog parçası (8 tablo) yine döndü.
        Assert.Equal(8, result.Schema.Tables.Count);
        Assert.Contains(result.MergeNotes, n => n.Contains("Orders") && n.Contains("failed"));
    }

    [Fact]
    public async Task When_every_chunk_fails_the_pipeline_throws_instead_of_returning_an_empty_schema()
    {
        var source = new FakeSchemaDraftSource { PlanResponse = LargePlanJson };
        source.FailingChunkLabels.Add("Identity + Catalog");
        source.FailingChunkLabels.Add("Orders");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL));
    }

    // ── Senaryo 5: plan yok/bozuk → bugünkü plansız tek çağrı yolu ──────────

    [Fact]
    public async Task Missing_plan_falls_back_to_the_planless_single_draft_call()
    {
        // PlanResponse null (varsayılan) → SchemaScopePlan.TryParse null döner.
        var source = new FakeSchemaDraftSource();

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL);

        Assert.Equal(1, source.DraftCalls);
        Assert.Empty(source.ChunkCalls);
        Assert.Empty(result.MergeNotes);
    }

    [Fact]
    public async Task Malformed_plan_json_falls_back_to_the_planless_single_draft_call()
    {
        var source = new FakeSchemaDraftSource { PlanResponse = "not valid json, no braces at all" };

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL);

        Assert.Equal(1, source.DraftCalls);
        Assert.Empty(source.ChunkCalls);
        Assert.Empty(result.MergeNotes);
    }

    // ── Senaryo 6: ilerleme — her parça için bir AgentStep.Draft ────────────

    [Fact]
    public async Task Each_chunk_reports_its_own_draft_progress_step()
    {
        var progress = new CapturingProgress();
        var source = new FakeSchemaDraftSource { PlanResponse = LargePlanJson };

        await Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL, progress: progress);

        var draftSteps = progress.Steps.Where(s => s.Kind == "draft").ToList();

        // Bir parça başına bir "tamamlandı" mesajı — parça etiketini taşıyor.
        Assert.Contains(draftSteps, s => s.Message.Contains("Identity + Catalog"));
        Assert.Contains(draftSteps, s => s.Message.Contains("Orders"));

        // Kullanıcı birden çok alanın üretildiğini de görmeli (başlangıç mesajı).
        Assert.Contains(draftSteps, s => s.Message.Contains("domains"));
    }

    // ── Kapsam bildirimi (onScopePlanned) ───────────────────────────────────

    [Fact]
    public async Task OnScopePlanned_is_invoked_with_the_table_count_once_the_plan_is_parsed()
    {
        var source = new FakeSchemaDraftSource { PlanResponse = LargePlanJson };
        int? reportedCount = null;

        await Pipeline(source).RunAsync(
            "bir mağaza şeması", DatabaseType.PostgreSQL,
            onScopePlanned: (count, _) => { reportedCount = count; return Task.CompletedTask; });

        Assert.Equal(14, reportedCount);
    }

    [Fact]
    public async Task OnScopePlanned_is_not_invoked_when_there_is_no_parsed_plan()
    {
        var source = new FakeSchemaDraftSource();
        var invoked = false;

        await Pipeline(source).RunAsync(
            "x", DatabaseType.PostgreSQL,
            onScopePlanned: (_, _) => { invoked = true; return Task.CompletedTask; });

        Assert.False(invoked);
    }

    [Fact]
    public async Task A_throwing_scope_callback_stops_the_pipeline_before_any_draft_call()
    {
        var source = new FakeSchemaDraftSource { PlanResponse = LargePlanJson };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Pipeline(source).RunAsync(
            "bir mağaza şeması", DatabaseType.PostgreSQL,
            onScopePlanned: (_, _) => throw new InvalidOperationException("quota exceeded")));

        Assert.Empty(source.ChunkCalls);
        Assert.Equal(0, source.DraftCalls);
    }

    // ── İptal: bir parçanın OperationCanceledException'ı yutulmaz ──────────

    private sealed class CancelingChunkSource : ISchemaDraftSource
    {
        public Task<string?> PlanAsync(string prompt, DatabaseType engine, CancellationToken ct = default) =>
            Task.FromResult<string?>(LargePlanJson);

        public Task<DatabaseSchema> DraftAsync(
            string prompt, DatabaseType engine, string? plan, CancellationToken ct = default) =>
            throw new InvalidOperationException("bu test tekil taslak yolunu kullanmıyor");

        public Task<DatabaseSchema> DraftChunkAsync(
            string prompt, DatabaseType engine, SchemaChunk chunk,
            IReadOnlyList<string> allTableNames, CancellationToken ct = default)
        {
            if (chunk.Label == "Orders")
                throw new OperationCanceledException();

            var schema = new DatabaseSchema { Name = "Fake" };
            foreach (var name in chunk.OwnedTables)
            {
                schema.Tables.Add(new SchemaTable
                {
                    Id = SchemaIdConvention.TableId(name),
                    Name = name,
                    Columns = new List<SchemaColumn>
                    {
                        new() { Id = SchemaIdConvention.ColumnId(name, "id"), Name = "id", Type = "uuid", IsPK = true },
                    },
                });
            }
            return Task.FromResult(schema);
        }

        public Task<DatabaseSchema> RepairAsync(
            DatabaseSchema schema, IReadOnlyList<string> findings, DatabaseType engine, CancellationToken ct = default) =>
            Task.FromResult(schema);

        public Task<int> EffectiveMaxOutputTokensAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task A_canceled_chunk_propagates_cancellation_instead_of_being_absorbed_as_a_failure()
    {
        var source = new CancelingChunkSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Pipeline(source).RunAsync("bir mağaza şeması", DatabaseType.PostgreSQL));
    }
}
