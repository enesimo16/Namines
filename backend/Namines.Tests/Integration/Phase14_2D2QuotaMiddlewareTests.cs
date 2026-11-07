using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.API.Middleware;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Tests.TestSupport;
using Xunit;

namespace Namines.Tests.Integration;

/// <summary>
/// M-3: ana AI yolu (AIQuotaMiddleware) gerçek PostgreSQL'de, sağlayıcı yerine yavaş bir sahte uçla. Eski sürümde bütçe
/// yanıttan SONRA düşülüyordu: bütçe tükenmeden gelen N paralel istek hepsi izin alırdı. Şimdi atomik rezervasyon.
/// </summary>
[Collection("Docker")]
public sealed class Phase14_2D2QuotaMiddlewareTests : IClassFixture<Phase14_2BPostgresFixture>
{
    private readonly Phase14_2BPostgresFixture _pg;

    public Phase14_2D2QuotaMiddlewareTests(Phase14_2BPostgresFixture pg) => _pg = pg;

    private static AuthDbContext Open(string cs) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(cs).Options);

    private async Task<string> NewDatabaseAsync(string name, params string[] users)
    {
        PostgresTestPools.Reset();
        var cs = await _pg.CreateDatabaseAsync(name);
        await using var db = Open(cs);
        await db.MigrateAllModulesAsync();
        foreach (var id in users) db.Users.Add(new ApplicationUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
        await db.SaveChangesAsync();
        return cs;
    }

    private static IConfiguration Config(int perUserCap, long pool) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiPool:PerUserDailyTokens"] = perUserCap.ToString(),
            ["AiPool:DailyTokenPool"] = pool.ToString(),
        }).Build();

    /// <summary>Bir isteği gerçek ara katmandan geçirir; "sağlayıcı" 80 ms bekler. Sonuç: yerel yedeğe düştü mü, kaç kez sağlayıcıya gidildi.</summary>
    private static async Task<(bool Fallback, bool ReachedProvider)> RequestAsync(string cs, IConfiguration config, string userId, string path, SemaphoreSlim gate, int status = 200)
    {
        await gate.WaitAsync();
        try
        {
            await using var db = Open(cs);
            var kit = TestModules.For(db);
            var services = new ServiceCollection()
                .AddSingleton(kit.Identity)
                .AddSingleton<IAiQuota>(kit.AiQuota(config))
                .AddSingleton<IAiPolicies>(kit.Policies)
                .BuildServiceProvider();
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Path = path;
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test"));
            var reached = false;
            var middleware = new AIQuotaMiddleware(async c =>
            {
                if (!c.Items.ContainsKey("FallbackToLocal")) reached = true;
                await Task.Delay(80);
                c.Response.StatusCode = status;
            });
            await middleware.InvokeAsync(context);
            return (context.Items.ContainsKey("FallbackToLocal"), reached);
        }
        finally { gate.Release(); }
    }

    private static async Task<(bool Fallback, bool ReachedProvider)[]> TogetherAsync(string cs, IConfiguration config, IReadOnlyList<string> userIds, string path, int parallel, int status = 200)
    {
        var gate = new SemaphoreSlim(parallel);
        var start = new TaskCompletionSource();
        var tasks = userIds.Select(async id => { await start.Task; return await RequestAsync(cs, config, id, path, gate, status); }).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    private static async Task<long> PoolUsedAsync(string cs)
    {
        await using var db = Open(cs);
        return await db.Billing().GlobalAiUsages.SumAsync(g => g.TokensUsed);
    }

    [RequiresDockerFact]
    public async Task Parallel_requests_never_exceed_the_user_cap_through_the_middleware()
    {
        const int estimate = 3_500; // /api/aidba
        var cs = await NewDatabaseAsync("mw_user", "u1");
        var config = Config(perUserCap: estimate * 3, pool: 100_000_000);

        var results = await TogetherAsync(cs, config, Enumerable.Repeat("u1", 12).ToList(), "/api/aidba/analyze", parallel: 12);

        Assert.Equal(3, results.Count(r => !r.Fallback && r.ReachedProvider));
        Assert.Equal(9, results.Count(r => r.Fallback));
        await using var verify = Open(cs);
        Assert.Equal(estimate * 3, (await verify.Billing().UserAIQuotas.AsNoTracking().SingleAsync(q => q.UserId == "u1")).DailyUsageCount);
        Assert.Equal(estimate * 3, await PoolUsedAsync(cs));
    }

    [RequiresDockerFact]
    public async Task Parallel_requests_never_overspend_the_free_pool_through_the_middleware()
    {
        const int estimate = 3_500;
        var ids = Enumerable.Range(0, 24).Select(i => "f" + i).ToList();
        var cs = await NewDatabaseAsync("mw_pool", ids.ToArray());
        var config = Config(perUserCap: 20_000, pool: estimate * 10);

        var results = await TogetherAsync(cs, config, ids, "/api/aidba/analyze", parallel: 24);

        Assert.Equal(10, results.Count(r => !r.Fallback && r.ReachedProvider));
        Assert.Equal(estimate * 10L, await PoolUsedAsync(cs));
        await using var verify = Open(cs);
        Assert.Equal(estimate * 10L, (await verify.Billing().UserAIQuotas.AsNoTracking().ToListAsync()).Sum(q => (long)q.DailyUsageCount));
    }

    [RequiresDockerFact]
    public async Task Parallel_requests_never_overspend_the_team_pool_through_the_middleware()
    {
        const int estimate = 6_000; // /api/coderai
        var team = PlanQuotas.For(PlanTier.Team);
        var limit = (long)team.DailyAiTokens * team.TeamSeats;
        var allowedCalls = (int)(limit / estimate);
        var ids = Enumerable.Range(0, 8).Select(i => "t" + i).ToList();
        var cs = await NewDatabaseAsync("mw_team", ids.ToArray());
        await using (var seed = Open(cs))
        {
            seed.Organizations.Add(new Organization { Id = "team-org", Name = "Team", IsPersonal = false, CreatedByUserId = ids[0] });
            await seed.SaveChangesAsync();
            seed.OrganizationMembers.AddRange(ids.Select(id => new OrganizationMember { OrganizationId = "team-org", UserId = id, Role = OrgRole.Editor }));
            await seed.SaveChangesAsync();
            foreach (var id in ids) await seed.SeedSubscriptionAsync(id, "active", "team");
        }

        // 8 üye x 16 çağrı = 128 istek; ekip havuzu yalnızca 100 çağrıya yeter.
        var requests = ids.SelectMany(id => Enumerable.Repeat(id, 16)).ToList();
        var results = await TogetherAsync(cs, Config(20_000, 100_000_000), requests, "/api/coderai/generate", parallel: 32);

        Assert.Equal(allowedCalls, results.Count(r => !r.Fallback && r.ReachedProvider));
        await using var verify = Open(cs);
        var teamUsed = await verify.Billing().OrgAiUsages.Where(o => o.OrganizationId == "team-org").SumAsync(o => o.TokensUsed);
        Assert.Equal((long)allowedCalls * estimate, teamUsed);
        Assert.True(teamUsed <= limit);
    }

    [RequiresDockerFact]
    public async Task A_failed_response_refunds_the_reservation()
    {
        const int estimate = 3_500;
        var cs = await NewDatabaseAsync("mw_refund", "u1");
        var config = Config(perUserCap: estimate * 2, pool: 100_000_000);

        var failed = await TogetherAsync(cs, config, new[] { "u1", "u1" }, "/api/aidba/analyze", parallel: 2, status: 500);
        Assert.All(failed, r => Assert.False(r.Fallback));
        await using var verify = Open(cs);
        // Başarısız çağrı kullanıcıya hiçbir şey vermedi: sayaçlar sıfıra döner ve sonraki istekler yine izin alır.
        Assert.Equal(0, (await verify.Billing().UserAIQuotas.AsNoTracking().SingleAsync()).DailyUsageCount);
        Assert.Equal(0, await PoolUsedAsync(cs));
        var next = await TogetherAsync(cs, config, new[] { "u1", "u1" }, "/api/aidba/analyze", parallel: 2);
        Assert.All(next, r => Assert.False(r.Fallback));
    }
}
