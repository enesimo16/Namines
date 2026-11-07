using Namines.Identity.Infrastructure.Persistence;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Namines.BuildingBlocks.Contracts;
using Namines.Hub.Application;
using Namines.Infrastructure.Data;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Namines.Tests.Integration;

/// <summary>
/// Denetim B-1: repo ayarı kaydı, arada yapılan şema kaydını sessizce eziyordu. Yarış penceresi
/// kesin açılır: ayar akışı Hub okumasında bekletilir, o sırada şema kaydı tamamlanır.
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2D1LostUpdateTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();
        PostgresTestPools.Reset();
    }

    public async Task DisposeAsync()
    {
        if (DockerAvailable.Value) await _container.DisposeAsync();
    }

    private async Task<string> UpgradedAsync(string name)
    {
        var admin = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
        await using (var conn = new NpgsqlConnection(admin.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        var cs = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name }.ConnectionString;
        await using (var ctx = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options))
            await ctx.GetService<IMigrator>().MigrateAsync(TestModules.Level14_2C);
        await TestModules.SeedLiveAsync(cs);
        await TestModules.MigrateAllAsync(cs);
        return cs;
    }

    private static ModuleKit Kit(string cs, params IInterceptor[] interceptors) =>
        new(new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options), cs, interceptors);

    private static async Task<string> SchemaOfAsync(string cs, string projectId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT \"SchemaJson\" FROM studio.projects WHERE \"Id\" = @id", conn);
        cmd.Parameters.AddWithValue("id", projectId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    [RequiresDockerFact]
    public async Task Repo_settings_save_does_not_overwrite_a_schema_saved_in_between()
    {
        var cs = await UpgradedAsync("lost_update");
        var pause = new PauseOnce(sql => sql.Contains("FROM hub.repositories", StringComparison.Ordinal));
        var settingsKit = Kit(cs, pause);

        var settings = Task.Run(() => settingsKit.HubRepositories(new NoDeletion())
            .UpdateSettingsAsync("u1", "p1", new RepoSettingsCommand(null, null, "new description", null, null, null, null, null, null)));

        // Ayar akışı projeyi okudu ve Hub okumasında bekliyor: tam bu anda şema kaydı tamamlanır.
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        const string savedSchema = "{\"tables\":[{\"id\":\"t1\",\"name\":\"kept\"}]}";
        var writer = Kit(cs);
        var header = (await writer.Projects.GetHeaderAsync("p1"))!;
        await writer.Projects.SaveAsync(new[]
        {
            new ProjectWrite("p1", header.Name, header.DbType, savedSchema, "{\"t1\":{}}", header.UserId, header.OrganizationId, header.RowVersion),
        });
        pause.Release.SetResult();

        var outcome = await settings.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(outcome.IsOk, outcome.Error);
        Assert.Equal(savedSchema, await SchemaOfAsync(cs, "p1"));
        Assert.Equal("new description", (await Kit(cs).HubService.GetRepositoryAsync("p1"))!.Description);
    }

    [RequiresDockerFact]
    public async Task Writing_an_existing_project_without_an_expected_version_is_refused()
    {
        var cs = await UpgradedAsync("no_version");
        var kit = Kit(cs);
        var header = (await kit.Projects.GetHeaderAsync("p1"))!;
        // Okunan sürüm taşınmadan yazmak, araya giren kaydı ezmenin tek yolu: sözleşme izin vermiyor.
        await Assert.ThrowsAsync<ArgumentException>(() => kit.Projects.SaveAsync(new[]
        {
            new ProjectWrite("p1", header.Name, header.DbType, "{}", "{}", header.UserId, header.OrganizationId),
        }));
    }

    [RequiresDockerFact]
    public async Task Download_key_rotation_keeps_the_old_key_when_the_new_one_cannot_be_written()
    {
        // Denetim m-3: iptal ve yeni anahtar ayrı commit'ti; ikincisi düşünce proje anahtarsız kalıyordu.
        var cs = await UpgradedAsync("rotate_atomic");
        var first = await Kit(cs).GatewayKeys.RotateDownloadKeyAsync("u1", "p1");
        Assert.True(first.IsOk, first.Error);

        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                CREATE FUNCTION pg_temp_fail() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN RAISE EXCEPTION 'injected'; END $f$;
                CREATE TRIGGER fail_new_key BEFORE INSERT ON gateway."GatewayApiKeys" FOR EACH ROW EXECUTE FUNCTION pg_temp_fail();
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAnyAsync<Exception>(() => Kit(cs).GatewayKeys.RotateDownloadKeyAsync("u1", "p1"));

        await using var check = new NpgsqlConnection(cs);
        await check.OpenAsync();
        await using var active = new NpgsqlCommand("""SELECT count(*) FROM gateway."GatewayApiKeys" WHERE "ProjectId" = 'p1' AND "Name" = 'Downloaded project' AND "RevokedAt" IS NULL""", check);
        Assert.Equal(1L, (long)(await active.ExecuteScalarAsync())!);
        Assert.NotNull(await Kit(cs).GatewayAccess.AuthenticateAsync(first.Value!.RawKey));
    }

    private sealed class NoDeletion : IProjectDeletion
    {
        public Task<ProjectDeletionResult> DeleteAsync(string projectId, string? actorUserId, CancellationToken ct, bool recordName = true) =>
            throw new NotSupportedException();
    }

    /// <summary>Eşleşen ilk okuma komutunu test serbest bırakana kadar bekletir.</summary>
    private sealed class PauseOnce : DbCommandInterceptor
    {
        private readonly Func<string, bool> _match;
        private int _hit;

        public PauseOnce(Func<string, bool> match) => _match = match;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (_match(command.CommandText) && Interlocked.Exchange(ref _hit, 1) == 0)
            {
                Reached.SetResult();
                await Release.Task;
            }
            return result;
        }
    }
}
