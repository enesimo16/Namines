using Namines.Core.Enums;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;

namespace Namines.Tests.RunTests;

/// <summary>
/// G12 — "Run Tests" (new-phase/29-DATABASE-CHANGE-REVIEW.md §4). GERÇEK, ephemeral
/// container'lara karşı — Impact Analysis'in tahminini kanıta çeviren adımın ta kendisi.
/// Bu projenin neden izole olduğu için bkz. Namines.Tests.RunTests.csproj.
/// </summary>
[Collection("Docker")]
public class BranchTestRunnerServiceTests
{
    private static BranchTestRunnerService CreateRunner() => new(new DdlGeneratorFactory());

    [RequiresDockerFact]
    public async Task Valid_schema_succeeds_against_real_postgres()
    {
        using var runner = CreateRunner();
        var result = await runner.RunAsync(MinimalSchemas.Simple(), DatabaseType.PostgreSQL);

        Assert.True(result.Supported);
        Assert.True(result.Success, result.EngineMessage);
        Assert.Null(result.EngineMessage);
        Assert.True(result.DurationMs > 0);
    }

    [RequiresDockerFact]
    public async Task Valid_schema_succeeds_against_real_mssql()
    {
        using var runner = CreateRunner();
        var result = await runner.RunAsync(MinimalSchemas.Simple(), DatabaseType.MSSQL);

        Assert.True(result.Supported);
        Assert.True(result.Success, result.EngineMessage);
    }

    [RequiresDockerFact]
    public async Task Multi_cascade_path_is_rejected_by_real_mssql_with_raw_engine_message()
    {
        using var runner = CreateRunner();
        var result = await runner.RunAsync(MinimalSchemas.MultiCascadePathAllCascade(), DatabaseType.MSSQL);

        Assert.True(result.Supported);
        Assert.False(result.Success, result.EngineMessage);
        Assert.Contains("1785", result.EngineMessage);
    }

    [RequiresDockerFact]
    public async Task Valid_schema_succeeds_against_real_mysql()
    {
        using var runner = CreateRunner();
        var result = await runner.RunAsync(MinimalSchemas.Simple(), DatabaseType.MySQL);

        Assert.True(result.Supported);
        Assert.True(result.Success, result.EngineMessage);
    }

    [Fact]
    public async Task Sqlite_runs_without_docker()
    {
        using var runner = CreateRunner();
        var result = await runner.RunAsync(MinimalSchemas.Simple(), DatabaseType.SQLite);

        Assert.True(result.Supported);
        Assert.True(result.Success, result.EngineMessage);
    }

    [Fact]
    public async Task Unsupported_engine_is_reported_honestly_not_as_a_failure()
    {
        using var runner = CreateRunner();
        var result = await runner.RunAsync(MinimalSchemas.Simple(), DatabaseType.Oracle);

        Assert.False(result.Supported);
        Assert.False(result.Success);
        Assert.Contains("isn't available", result.EngineMessage);
    }
}
