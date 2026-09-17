using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// "Gelişmiş" kapalıyken (tek tur) hattın davranışı.
///
/// <b>Kapalı mod denetimsiz DEĞİL, otomatik onarımsız.</b> Bu ayrım ürünün
/// vaadi: kullanıcı hızlı sonuç alıyor ama şemanın bozuk olduğunu yine de
/// öğreniyor. Denetim de kapansaydı, sessizce çalışmayan DDL üretirdik.
///
/// Testler bu SÖZLEŞMEYİ kilitliyor: ileride biri plan turunun bütçe eşiğini
/// düşürür ya da onarım koşulunu gevşetirse, varsayılan mod sessizce
/// pahalılaşır — ve bunu kullanıcı faturasında görür, burada değil.
/// </summary>
public class SchemaAgentBudgetTests
{
    private sealed class CountingSource : ISchemaDraftSource
    {
        private readonly DatabaseSchema _schema;

        public CountingSource(DatabaseSchema schema) => _schema = schema;

        public int PlanCalls { get; private set; }
        public int RepairCalls { get; private set; }

        public Task<string?> PlanAsync(string prompt, DatabaseType engine, CancellationToken ct = default)
        {
            PlanCalls++;
            return Task.FromResult<string?>("plan");
        }

        public Task<DatabaseSchema> DraftAsync(
            string prompt, DatabaseType engine, string? plan, CancellationToken ct = default) =>
            Task.FromResult(_schema);

        public Task<DatabaseSchema> DraftChunkAsync(
            string prompt, DatabaseType engine, Namines.Core.Analysis.SchemaChunk chunk,
            System.Collections.Generic.IReadOnlyList<string> allTableNames, CancellationToken ct = default) =>
            throw new System.NotImplementedException("bu test parçalı üretimi kullanmıyor");

        public Task<DatabaseSchema> RepairAsync(
            DatabaseSchema schema, IReadOnlyList<string> findings, DatabaseType engine, CancellationToken ct = default)
        {
            RepairCalls++;
            return Task.FromResult(_schema);
        }

        public Task<int> EffectiveMaxOutputTokensAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>FK tipi uyuşmayan şema — NSL004 hatası üretir.</summary>
    private static DatabaseSchema WithBrokenForeignKey()
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
    public async Task A_single_round_budget_skips_both_the_plan_and_the_repair_turn()
    {
        var source = new CountingSource(WithBrokenForeignKey());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 1);

        Assert.Equal(0, source.PlanCalls);
        Assert.Equal(0, source.RepairCalls);
        Assert.Equal(1, result.Rounds);
    }

    [Fact]
    public async Task A_single_round_budget_still_inspects_and_reports_the_findings()
    {
        // Kapalı modun bütün değeri bu: hızlı, ama sessiz değil.
        var source = new CountingSource(WithBrokenForeignKey());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 1);

        Assert.False(result.Clean);
        Assert.Contains(result.RemainingFindings, f => f.Contains("NSL004"));
    }

    [Fact]
    public async Task A_zero_round_budget_is_refused_rather_than_silently_producing_nothing()
    {
        var source = new CountingSource(WithBrokenForeignKey());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 0));
    }

    [Fact]
    public async Task The_plan_turn_needs_three_rounds_not_two()
    {
        // Eşiği düşürmek, varsayılan modun sessizce plan turu harcamasına yol
        // açardı — kullanıcı bunu yalnızca kota tükenince fark ederdi.
        var source = new CountingSource(WithBrokenForeignKey());

        await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 2);

        Assert.Equal(0, source.PlanCalls);
    }
}
