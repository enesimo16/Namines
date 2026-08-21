using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Namines.Core.Enums;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Services;
using Namines.Tests.Fixtures;

namespace Namines.Tests.Analysis;

/// <summary>
/// G9 — <see cref="MigrationService.CalculateDiffAsync"/>'in artık dağınık ad-hoc
/// "HasBreakingChanges = true" atamaları yerine <see cref="Namines.Core.Analysis.SchemaImpactAnalyzer"/>'ı
/// tek doğruluk kaynağı olarak kullandığının kanıtı. Bkz. new-phase/11-MIGRATIONS-BRANCHING.md §2.
///
/// <see cref="MigrationService"/>, <see cref="GroqAIService"/>'e bağımlı ama
/// <c>CalculateDiffAsync</c> onu hiç çağırmıyor — bu yüzden testte gerçek bir API
/// anahtarı gerekmeden, boş/minimal bağımlılıklarla inşa edilebiliyor.
/// </summary>
public class MigrationServiceRiskTests
{
    private static MigrationService CreateService()
    {
        var httpClient = new HttpClient();
        var configuration = new ConfigurationBuilder().Build();
        var httpContextAccessor = new HttpContextAccessor();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var groqService = new GroqAIService(httpClient, configuration, httpContextAccessor, cache);
        return new MigrationService(groqService);
    }

    [Fact]
    public async Task Identical_schemas_are_safe_and_not_breaking()
    {
        var service = CreateService();
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();

        var result = await service.CalculateDiffAsync(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.False(result.HasBreakingChanges);
        Assert.Equal(RiskLevel.Safe, result.OverallRisk);
        Assert.NotNull(result.Impact);
        Assert.Empty(result.Impact!.BreakingChanges);
    }

    [Fact]
    public async Task Dropping_a_column_is_reported_as_breaking_via_impact_analyzer()
    {
        var service = CreateService();
        var oldSchema = SchemaFixtures.ECommerce();
        var newSchema = SchemaFixtures.ECommerce();
        var users = newSchema.Tables.Single(t => t.Id == "t_users");
        users.Columns.RemoveAll(c => c.Id == "c_u_created");

        var result = await service.CalculateDiffAsync(oldSchema, newSchema, DatabaseType.PostgreSQL);

        Assert.True(result.HasBreakingChanges);
        Assert.Equal(RiskLevel.Breaking, result.OverallRisk);
        Assert.Contains("CreatedAt", result.ModifiedTables.Single(t => t.TableName == "Users").RemovedColumns);
        Assert.NotNull(result.Impact);
        Assert.Contains(result.Impact!.DataLossRisks, d => d.ColumnName == "CreatedAt");
    }

    [Fact]
    public async Task Multi_cascade_path_reports_mssql_specific_message_when_mssql_engine_requested()
    {
        var service = CreateService();
        var oldSchema = SchemaFixtures.MultiCascadePath();
        var newSchema = SchemaFixtures.MultiCascadePath();
        foreach (var rel in newSchema.Relations)
            rel.OnDelete = ReferentialAction.Cascade;

        var result = await service.CalculateDiffAsync(oldSchema, newSchema, DatabaseType.MSSQL);

        Assert.True(result.HasBreakingChanges);
        Assert.Contains(result.Impact!.BreakingChanges, b => b.Description.Contains("Msg 1785"));
    }

    [Fact]
    public async Task Defaults_to_postgres_engine_when_not_specified()
    {
        var service = CreateService();
        var oldSchema = SchemaFixtures.MultiCascadePath();
        var newSchema = SchemaFixtures.MultiCascadePath();
        foreach (var rel in newSchema.Relations)
            rel.OnDelete = ReferentialAction.Cascade;

        var result = await service.CalculateDiffAsync(oldSchema, newSchema);

        Assert.DoesNotContain(result.Impact!.BreakingChanges, b => b.Description.Contains("Msg 1785"));
    }
}
