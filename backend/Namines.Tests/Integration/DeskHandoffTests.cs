using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Namines.Core.Models.Auth;
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

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        await _container.StartAsync();

        _options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(_container.GetConnectionString()).Options;

        await using var context = new AuthDbContext(_options);
        await context.Database.MigrateAsync();
        await context.Users.AddAsync(new ApplicationUser { Id = "u1", UserName = "u1" });
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync() => DockerAvailable.Value ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

    private AuthDbContext Context() => new(_options);

    [RequiresDockerFact]
    public async Task A_freshly_created_token_exchanges_for_its_owner()
    {
        await using var context = Context();
        var raw = await context.CreateDeskHandoffTokenAsync("u1");

        await using var exchangeContext = Context();
        var userId = await exchangeContext.ExchangeDeskHandoffTokenAsync(raw);

        Assert.Equal("u1", userId);
    }

    [RequiresDockerFact]
    public async Task The_raw_token_is_never_stored()
    {
        // TeamInvite ile aynı gerekçe: ham saklansaydı veritabanı yedeğine
        // erişen herkes birinin oturumunu Desk'te açabilirdi.
        await using var context = Context();
        var raw = await context.CreateDeskHandoffTokenAsync("u1");

        var stored = await context.DeskHandoffTokens.AsNoTracking().FirstAsync();
        Assert.DoesNotContain(raw, stored.TokenHash);
        Assert.NotEqual(raw, stored.TokenHash);
    }

    [RequiresDockerFact]
    public async Task Using_the_same_token_twice_only_works_once()
    {
        await using var context = Context();
        var raw = await context.CreateDeskHandoffTokenAsync("u1");

        await using var first = Context();
        var firstResult = await first.ExchangeDeskHandoffTokenAsync(raw);

        await using var second = Context();
        var secondResult = await second.ExchangeDeskHandoffTokenAsync(raw);

        Assert.Equal("u1", firstResult);
        Assert.Null(secondResult);
    }

    [RequiresDockerFact]
    public async Task An_unknown_token_exchanges_for_nothing()
    {
        await using var context = Context();
        var userId = await context.ExchangeDeskHandoffTokenAsync("this-was-never-issued");
        Assert.Null(userId);
    }

    [RequiresDockerFact]
    public async Task An_expired_token_no_longer_works()
    {
        await using var context = Context();
        var raw = await context.CreateDeskHandoffTokenAsync("u1");

        // 30 saniyelik ömrü elle geçmişe çekiyoruz — gerçekten 30 sn beklemek
        // yerine, ama AYNI IsUsable kontrolünü tetikliyor.
        var entity = await context.DeskHandoffTokens.FirstAsync();
        entity.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await context.SaveChangesAsync();

        await using var exchangeContext = Context();
        var userId = await exchangeContext.ExchangeDeskHandoffTokenAsync(raw);
        Assert.Null(userId);
    }

    [RequiresDockerFact]
    public async Task Concurrent_exchanges_of_the_same_token_only_let_one_through()
    {
        // ASIL kanıt: iki istek TAM OLARAK AYNI ANDA aynı jetonu kullanmayı
        // dener. Yarış koruması olmasaydı ikisi de "kullanılmamış" okuyup
        // ikisi de başarılı dönerdi — bir jeton çalınıp yeniden oynatılabilir
        // (replay) olurdu.
        await using var context = Context();
        var raw = await context.CreateDeskHandoffTokenAsync("u1");

        var task1 = Task.Run(async () =>
        {
            await using var c = Context();
            return await c.ExchangeDeskHandoffTokenAsync(raw);
        });
        var task2 = Task.Run(async () =>
        {
            await using var c = Context();
            return await c.ExchangeDeskHandoffTokenAsync(raw);
        });

        var results = await Task.WhenAll(task1, task2);

        // Tam olarak biri kazanmalı, sıfırı ya da ikisi değil.
        Assert.Single(results, r => r == "u1");
        Assert.Single(results, r => r is null);
    }
}
