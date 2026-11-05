using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Namines.Core.Models.Auth;
using Namines.Desk.Application;
using Namines.Desk.Infrastructure.Persistence;
using Namines.Desk.Services;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Integration;

/// <summary>
/// Namines Desk v2 §E1.2 — SSO devir jetonu, GERÇEK PostgreSQL'e karşı.
///
/// <b>Bu testlerin varlık sebebi asıl olarak yarış korumasıdır.</b> Bellek içi
/// sağlayıcı EF'in <c>ExecuteUpdateAsync</c>'ini gerçek bir atomik UPDATE olarak
/// çalıştırmaz (tek işlemli test veritabanında yarış zaten kurulamaz) — iki
/// eşzamanlı "aynı jetonu kullan" isteğinin YALNIZCA BİRİNİN kazanması, gerçek
/// bir veritabanının kilitleme davranışı olmadan kanıtlanamaz.
/// </summary>
[Collection("Docker")]
public class DeskHandoffTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private DbContextOptions<AuthDbContext> _options = null!;
    private DbContextOptions<DeskDbContext> _deskOptions = null!;

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();
        PostgresTestPools.Reset();

        var connStr = _container.GetConnectionString();
        _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connStr).Options;
        _deskOptions = new DbContextOptionsBuilder<DeskDbContext>()
            .UseNpgsql(connStr).Options;

        await using var context = new AuthDbContext(_options);
        await context.Database.MigrateAsync();
        await context.Users.AddAsync(new ApplicationUser { Id = "u1", UserName = "u1" });
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync() => DockerAvailable.Value ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

    private DeskDbContext DeskContext() => new(_deskOptions);

    private DeskApplicationService DeskApp(DeskDbContext db) =>
        new(db, new DeskHandoffTokenValidator());

    [RequiresDockerFact]
    public async Task A_freshly_created_token_exchanges_for_its_owner()
    {
        await using var context = DeskContext();
        var raw = await DeskApp(context).CreateHandoffTokenAsync("u1");

        await using var exchangeContext = DeskContext();
        var userId = await DeskApp(exchangeContext).ExchangeHandoffTokenAsync(raw);

        Assert.Equal("u1", userId);
    }

    [RequiresDockerFact]
    public async Task The_raw_token_is_never_stored()
    {
        await using var context = DeskContext();
        var raw = await DeskApp(context).CreateHandoffTokenAsync("u1");

        var stored = await context.DeskHandoffTokens.AsNoTracking().FirstAsync();
        Assert.DoesNotContain(raw, stored.TokenHash);
        Assert.NotEqual(raw, stored.TokenHash);
    }

    [RequiresDockerFact]
    public async Task Using_the_same_token_twice_only_works_once()
    {
        await using var context = DeskContext();
        var raw = await DeskApp(context).CreateHandoffTokenAsync("u1");

        await using var first = DeskContext();
        var firstResult = await DeskApp(first).ExchangeHandoffTokenAsync(raw);

        await using var second = DeskContext();
        var secondResult = await DeskApp(second).ExchangeHandoffTokenAsync(raw);

        Assert.Equal("u1", firstResult);
        Assert.Null(secondResult);
    }

    [RequiresDockerFact]
    public async Task An_unknown_token_exchanges_for_nothing()
    {
        await using var context = DeskContext();
        var userId = await DeskApp(context).ExchangeHandoffTokenAsync("this-was-never-issued");
        Assert.Null(userId);
    }

    [RequiresDockerFact]
    public async Task An_expired_token_no_longer_works()
    {
        await using var context = DeskContext();
        var raw = await DeskApp(context).CreateHandoffTokenAsync("u1");

        var entity = await context.DeskHandoffTokens.FirstAsync();
        entity.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await context.SaveChangesAsync();

        await using var exchangeContext = DeskContext();
        var userId = await DeskApp(exchangeContext).ExchangeHandoffTokenAsync(raw);
        Assert.Null(userId);
    }

    [RequiresDockerFact]
    public async Task Concurrent_exchanges_of_the_same_token_only_let_one_through()
    {
        await using var context = DeskContext();
        var raw = await DeskApp(context).CreateHandoffTokenAsync("u1");

        var task1 = Task.Run(async () =>
        {
            await using var c = DeskContext();
            return await DeskApp(c).ExchangeHandoffTokenAsync(raw);
        });
        var task2 = Task.Run(async () =>
        {
            await using var c = DeskContext();
            return await DeskApp(c).ExchangeHandoffTokenAsync(raw);
        });

        var results = await Task.WhenAll(task1, task2);

        Assert.Single(results, r => r == "u1");
        Assert.Single(results, r => r is null);
    }
}
