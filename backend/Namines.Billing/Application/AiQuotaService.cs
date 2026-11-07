using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namines.Billing.Domain;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Analysis;

namespace Namines.Billing.Application;

/// <summary>
/// AI bütçesinin TEK sahibi (22 §5).
///
/// Aynı hesap iki yerden harcanıyor — <c>AIQuotaMiddleware</c> (Studio, JWT ile) ve Gateway'in <c>/query/nl</c>
/// ucu (API anahtarı ile). İki kopya yazılsaydı biri havuzu, tavanı ya da gün sıfırlamasını farklı hesaplar ve
/// kullanıcı hangi yoldan geldiğine göre farklı bütçe görürdü.
///
/// Üç sınır birlikte uygulanır: paylaşılan günlük havuz (yalnızca Free), kullanıcı tavanı ve Team'in ortak
/// havuzu. <see cref="TryReserveAsync"/> üçünü TEK işlemde koşullu artırır: "bak, sonra yaz" yarışı
/// (iki istek aynı boşluğu görüp ikisi de geçer) kapalıdır, biri tutmazsa hiçbiri yazılmaz.
/// </summary>
internal sealed class AiQuotaService : IAiQuota
{
    private readonly IBillingData _data;
    private readonly IConfiguration _configuration;
    private readonly PlanResolver _plans;
    private readonly IIdentityDirectory _identity;

    public AiQuotaService(IBillingData data, IConfiguration configuration, IIdentityDirectory identity)
    {
        _data = data;
        _configuration = configuration;
        _identity = identity;
        _plans = new PlanResolver(data, identity);
    }

    public long DailyPool =>
        long.TryParse(_configuration["AiPool:DailyTokenPool"], out var value) ? value : 500_000;

    public long MaxDailyPool =>
        long.TryParse(_configuration["AiPool:MaxDailyTokenPool"], out var value) ? value : 2_000_000;

    public int GrowthAfterFullDays =>
        int.TryParse(_configuration["AiPool:GrowthAfterFullDays"], out var value) && value > 0 ? value : 3;

    public int MinDailyFreeUsers =>
        int.TryParse(_configuration["AiPool:MinDailyFreeUsers"], out var value) && value > 0 ? value : 100;

    public int MinUsefulDailyTokens =>
        int.TryParse(_configuration["AiPool:MinUsefulDailyTokens"], out var value) && value > 0 ? value : 8_000;

    /// <summary>
    /// Kullanıcı başına günlük token tavanı. Free için plan tavanı havuzdan adil paya göre daraltılır
    /// (bkz. <see cref="CalculateFreeUserCap"/>); ücretli planlar katalogdaki değeri alır, Team koltuk
    /// başına payın iki katına kadar patlayabilir (ortak havuz asıl sınır).
    /// </summary>
    public async Task<int> PerUserCapAsync(string userId, CancellationToken ct = default) =>
        CapFor(await TierAsync(userId, ct));

    private int CapFor(PlanTier tier)
    {
        if (tier == PlanTier.Free)
        {
            var planCap = int.TryParse(_configuration["AiPool:PerUserDailyTokens"], out var configured)
                ? configured
                : PlanQuotas.For(PlanTier.Free).DailyAiTokens;
            return CalculateFreeUserCap(planCap, DailyPool, MinDailyFreeUsers, MinUsefulDailyTokens);
        }

        var perSeat = PlanQuotas.For(tier).DailyAiTokens;
        return tier == PlanTier.Team ? perSeat * 2 : perSeat;
    }

    public async Task<PoolPressure> PoolPressureAsync(int lookbackDays = 7, CancellationToken ct = default)
    {
        var since = LocalDate(DateTime.UtcNow).AddDays(-lookbackDays);

        var recent = await _data.GlobalAiUsages.AsNoTracking()
            .Where(g => g.Date >= since)
            .Select(g => g.TokensUsed)
            .ToListAsync(ct);

        var threshold = (long)(DailyPool * 0.95);
        var fullDays = recent.Count(t => t >= threshold);

        var shouldGrow = fullDays >= GrowthAfterFullDays && DailyPool < MaxDailyPool;
        var suggested = shouldGrow ? Math.Min(DailyPool * 2, MaxDailyPool) : DailyPool;

        return new PoolPressure(DailyPool, MaxDailyPool, recent.Count, fullDays, shouldGrow, suggested);
    }

    /// <summary>
    /// Free kullanıcının tavanı: havuzun adil payının iki katı (patlama payı) ile plan tavanından küçük olanı.
    /// Tavan havuzdan büyük olursa tek kullanıcı herkesin hakkını tüketirdi.
    /// </summary>
    public static int CalculateFreeUserCap(int planCap, long dailyPool, int minDailyFreeUsers, int minUsefulDailyTokens)
    {
        if (minDailyFreeUsers <= 0) return planCap;

        var fairShare = (int)Math.Min(int.MaxValue, dailyPool / minDailyFreeUsers);
        var burstable = Math.Max(fairShare * 2, minUsefulDailyTokens);

        return Math.Min(planCap, burstable);
    }

    private async Task<(string OrgId, long Limit)?> TeamPoolAsync(string userId, PlanTier tier, CancellationToken ct)
    {
        if (tier != PlanTier.Team) return null;

        var orgId = await _identity.GetPoolOrganizationIdAsync(userId, ct);
        if (orgId is null) return null;

        var seats = PlanQuotas.For(PlanTier.Team).TeamSeats;
        var perSeat = PlanQuotas.For(PlanTier.Team).DailyAiTokens;
        return (orgId, (long)perSeat * seats);
    }

    public Task<PlanTier> TierAsync(string userId, CancellationToken ct = default) => _plans.TierAsync(userId, ct);

    /// <summary>Gün sınırı Türkiye saatine göre (UTC+3): ürünün asıl kitlesi için "bugün" gece yarısı sıfırlanır.</summary>
    private static DateTime LocalDate(DateTime utc) => utc.AddHours(3).Date;

    public async Task<AiQuotaStatus> EnsureAsync(string userId, CancellationToken ct = default)
    {
        var quota = await EnsureQuotaAsync(userId, ct);
        return await StatusFromAsync(userId, quota, ct);
    }

    public async Task<AiQuotaStatus> GetStatusAsync(string userId, CancellationToken ct = default)
    {
        // GET idempotent kalmalı: yazma yok. Gün sıfırlaması hesaplanır, kalıcılaşması ilk AI çağrısında.
        var quota = await _data.UserAIQuotas.AsNoTracking().FirstOrDefaultAsync(q => q.UserId == userId, ct);
        var tier = await TierAsync(userId, ct);
        var cap = CapFor(tier);

        var lastReset = quota?.LastResetDate ?? DateTime.UtcNow;
        var resetDue = LocalDate(lastReset) < LocalDate(DateTime.UtcNow);
        var used = quota is null || resetDue ? 0 : quota.DailyUsageCount;

        var today = DateTime.UtcNow.Date;
        var poolUsed = await _data.GlobalAiUsages.AsNoTracking()
            .Where(g => g.Date == today).Select(g => (long?)g.TokensUsed).FirstOrDefaultAsync(ct) ?? 0;

        return new AiQuotaStatus(tier, cap, used, lastReset, DailyPool, poolUsed);
    }

    private async Task<AiQuotaStatus> StatusFromAsync(string userId, UserAIQuota quota, CancellationToken ct)
    {
        var tier = await TierAsync(userId, ct);
        var today = DateTime.UtcNow.Date;
        var poolUsed = await _data.GlobalAiUsages.AsNoTracking()
            .Where(g => g.Date == today).Select(g => (long?)g.TokensUsed).FirstOrDefaultAsync(ct) ?? 0;
        return new AiQuotaStatus(tier, quota.DailyLimit, quota.DailyUsageCount, quota.LastResetDate, DailyPool, poolUsed);
    }

    /// <summary>
    /// Kota satırı: yoksa açar, sınırı güncel plana göre düzeltir, gün değiştiyse sayacı sıfırlar.
    /// Eşzamanlı ilk çağrılarda ikinci ekleme birincil anahtar çakışmasıyla düşer, kazananın satırı okunur.
    /// </summary>
    public async Task<UserAIQuota> EnsureQuotaAsync(string userId, CancellationToken ct = default)
    {
        var cap = await PerUserCapAsync(userId, ct);
        var quota = await _data.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == userId, ct);

        if (quota is null)
        {
            quota = new UserAIQuota { UserId = userId, DailyLimit = cap, DailyUsageCount = 0, LastResetDate = DateTime.UtcNow };
            await _data.UserAIQuotas.AddAsync(quota, ct);
            try
            {
                await _data.SaveChangesAsync(ct);
                return quota;
            }
            catch (DbUpdateException) when (_data.Database.CurrentTransaction is null)
            {
                _data.Entry(quota).State = EntityState.Detached;
                return await _data.UserAIQuotas.FirstAsync(q => q.UserId == userId, ct);
            }
        }

        var changed = false;
        if (quota.DailyLimit != cap)
        {
            quota.DailyLimit = cap;
            changed = true;
        }

        if (LocalDate(quota.LastResetDate) < LocalDate(DateTime.UtcNow))
        {
            quota.DailyUsageCount = 0;
            quota.LastResetDate = DateTime.UtcNow;
            changed = true;
        }

        if (changed) await _data.SaveChangesAsync(ct);
        return quota;
    }

    /// <summary>
    /// Okuma amaçlı karar (rezervasyon yapmaz). Middleware "AI'ya mı, yerel yedeğe mi" kararını bundan verir;
    /// bütçeyi harcayan yol <see cref="TryReserveAsync"/>.
    /// </summary>
    public async Task<AiQuotaDecision> CheckAsync(string userId, int estimatedTokens, CancellationToken ct = default)
    {
        var tier = await TierAsync(userId, ct);
        if (tier == PlanTier.Dev) return AiQuotaDecision.Allowed;

        var quota = await EnsureQuotaAsync(userId, ct);

        var today = DateTime.UtcNow.Date;
        var used = await _data.GlobalAiUsages.AsNoTracking()
            .Where(g => g.Date == today)
            .Select(g => (long?)g.TokensUsed)
            .FirstOrDefaultAsync(ct) ?? 0;

        if (tier == PlanTier.Free && used + estimatedTokens > DailyPool)
            return AiQuotaDecision.PoolExhausted;

        if (quota.DailyUsageCount + estimatedTokens > quota.DailyLimit) return AiQuotaDecision.UserExhausted;

        var team = await TeamPoolAsync(userId, tier, ct);
        if (team is not null)
        {
            var teamUsed = await _data.OrgAiUsages.AsNoTracking()
                .Where(o => o.OrganizationId == team.Value.OrgId && o.Date == today)
                .Select(o => (long?)o.TokensUsed)
                .FirstOrDefaultAsync(ct) ?? 0;

            if (teamUsed + estimatedTokens > team.Value.Limit) return AiQuotaDecision.TeamExhausted;
        }

        return AiQuotaDecision.Allowed;
    }

    /// <summary>Bu kapsamda en az bir rezervasyon yapıldı mı: middleware aynı işi ikinci kez düşmesin.</summary>
    public bool HasReservedInScope { get; private set; }

    public async Task<AiQuotaDecision> TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct = default)
    {
        var decision = await TryReserveCoreAsync(userId, estimatedTokens, ct);
        if (decision == AiQuotaDecision.Allowed) HasReservedInScope = true;
        return decision;
    }

    private async Task<AiQuotaDecision> TryReserveCoreAsync(string userId, int estimatedTokens, CancellationToken ct)
    {
        var tier = await TierAsync(userId, ct);

        // Geliştirici hesabı hiçbir sınıra takılmaz ama harcaması yine de sayılır: havuz ölçümü gerçeği göstersin.
        if (tier == PlanTier.Dev)
        {
            await EnsureQuotaAsync(userId, ct);
            await ConsumeAsync(userId, estimatedTokens, ct);
            return AiQuotaDecision.Allowed;
        }

        await EnsureQuotaAsync(userId, ct);

        var today = DateTime.UtcNow.Date;
        var team = await TeamPoolAsync(userId, tier, ct);

        // Sayaç satırları işlemden ÖNCE açılır: PostgreSQL'de işlem içindeki çakışma hatası işlemi bozardı.
        await EnsurePoolRowAsync(today, ct);
        if (team is not null) await EnsureTeamRowAsync(team.Value.OrgId, today, ct);

        var ambient = _data.Database.CurrentTransaction;
        if (ambient is not null)
            return await ReserveStepsAsync(userId, estimatedTokens, tier, team, today, ambient, savepoint: true, ct);

        var strategy = _data.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _data.ChangeTracker.Clear();
            await using var tx = await _data.Database.BeginTransactionAsync(ct);
            var result = await ReserveStepsAsync(userId, estimatedTokens, tier, team, today, tx, savepoint: false, ct);
            if (result == AiQuotaDecision.Allowed) await tx.CommitAsync(ct);
            else await tx.RollbackAsync(ct);
            return result;
        });
    }

    /// <summary>
    /// Sıra, <see cref="CheckAsync"/>'in karar önceliğiyle aynı: havuz, kullanıcı, ekip. Her adım koşullu
    /// güncelleme; etkilenen satır 0 ise aynı işlemdeki önceki adımlar geri alınır.
    /// </summary>
    private async Task<AiQuotaDecision> ReserveStepsAsync(
        string userId, int tokens, PlanTier tier, (string OrgId, long Limit)? team, DateTime today,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, bool savepoint, CancellationToken ct)
    {
        const string name = "ai_reserve";
        if (savepoint) await tx.CreateSavepointAsync(name, ct);

        async Task<AiQuotaDecision> Fail(AiQuotaDecision decision)
        {
            if (savepoint) await tx.RollbackToSavepointAsync(name, ct);
            return decision;
        }

        var pool = DailyPool;
        if (tier == PlanTier.Free)
        {
            var rows = await _data.GlobalAiUsages
                .Where(g => g.Date == today && g.TokensUsed + tokens <= pool)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.TokensUsed, g => g.TokensUsed + tokens), ct);
            if (rows == 0) return await Fail(AiQuotaDecision.PoolExhausted);
        }
        else
        {
            // Ücretli planlar paylaşılan havuzdan sınırlanmaz ama tüketimleri havuz ölçümüne yazılır.
            await _data.GlobalAiUsages.Where(g => g.Date == today)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.TokensUsed, g => g.TokensUsed + tokens), ct);
        }

        var user = await _data.UserAIQuotas
            .Where(q => q.UserId == userId && q.DailyUsageCount + tokens <= q.DailyLimit)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.DailyUsageCount, q => q.DailyUsageCount + tokens), ct);
        if (user == 0) return await Fail(AiQuotaDecision.UserExhausted);

        if (team is not null)
        {
            var org = team.Value.OrgId;
            var limit = team.Value.Limit;
            var rows = await _data.OrgAiUsages
                .Where(o => o.OrganizationId == org && o.Date == today && o.TokensUsed + tokens <= limit)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.TokensUsed, o => o.TokensUsed + tokens), ct);
            if (rows == 0) return await Fail(AiQuotaDecision.TeamExhausted);
        }

        if (savepoint) await tx.ReleaseSavepointAsync(name, ct);

        // ExecuteUpdate izlenen varlığı güncellemez; izleyicide kalmış eski örnek bayat sayaç göstermesin.
        var tracked = _data.UserAIQuotas.Local.FirstOrDefault(q => q.UserId == userId);
        if (tracked is not null) await _data.Entry(tracked).ReloadAsync(ct);
        return AiQuotaDecision.Allowed;
    }

    public async Task ReconcileAsync(string userId, int reservedTokens, int actualTokens, CancellationToken ct = default)
    {
        // Sağlayıcı kullanım bilgisi vermediyse tahmin geçerli kalır: iade edip bedavaya saymak açığa yol açardı.
        if (actualTokens <= 0) return;

        var delta = actualTokens - reservedTokens;
        if (delta == 0) return;

        if (delta > 0)
        {
            await ConsumeAsync(userId, delta, ct);
            return;
        }

        await RefundAsync(userId, -delta, ct);
    }

    public async Task RefundAsync(string userId, int tokens, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var tier = await TierAsync(userId, ct);

        // Sıfırın altına inmez: aynı rezervasyon iki kez iade edilse bile sayaç negatife geçip bedava hak doğurmaz.
        await _data.UserAIQuotas
            .Where(q => q.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                q => q.DailyUsageCount,
                q => q.DailyUsageCount - tokens < 0 ? 0 : q.DailyUsageCount - tokens), ct);

        await _data.GlobalAiUsages
            .Where(g => g.Date == today)
            .ExecuteUpdateAsync(s => s.SetProperty(
                g => g.TokensUsed,
                g => g.TokensUsed - tokens < 0 ? 0 : g.TokensUsed - tokens), ct);

        var team = await TeamPoolAsync(userId, tier, ct);
        if (team is not null)
        {
            await _data.OrgAiUsages
                .Where(o => o.OrganizationId == team.Value.OrgId && o.Date == today)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    o => o.TokensUsed,
                    o => o.TokensUsed - tokens < 0 ? 0 : o.TokensUsed - tokens), ct);
        }
    }

    /// <summary>
    /// Harcamayı yazar, reddetmez: işlem zaten yapıldı (ya da geliştirici hesabı). Kullanıcı sayacı tavanda
    /// kırpılır, havuz ve ekip sayaçları gerçek tüketimi gösterir.
    /// </summary>
    public async Task ConsumeAsync(string userId, int estimatedTokens, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var trackedQuota = _data.UserAIQuotas.Local.FirstOrDefault(q => q.UserId == userId);
        var tier = await TierAsync(userId, ct);

        await _data.UserAIQuotas
            .Where(q => q.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                q => q.DailyUsageCount,
                q => q.DailyUsageCount + estimatedTokens > q.DailyLimit
                    ? q.DailyLimit
                    : q.DailyUsageCount + estimatedTokens), ct);

        await EnsurePoolRowAsync(today, ct);
        await _data.GlobalAiUsages.Where(g => g.Date == today)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.TokensUsed, g => g.TokensUsed + estimatedTokens), ct);

        var team = await TeamPoolAsync(userId, tier, ct);
        if (team is not null)
        {
            await EnsureTeamRowAsync(team.Value.OrgId, today, ct);
            await _data.OrgAiUsages
                .Where(o => o.OrganizationId == team.Value.OrgId && o.Date == today)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.TokensUsed, o => o.TokensUsed + estimatedTokens), ct);
        }

        if (trackedQuota is not null) await _data.Entry(trackedQuota).ReloadAsync(ct);
    }

    /// <summary>Gün başına tek satır: eşzamanlı ilk istekler çakışmadan aynı satırda buluşur.</summary>
    private Task EnsurePoolRowAsync(DateTime today, CancellationToken ct) =>
        _data.Database.IsNpgsql()
            ? _data.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO billing."GlobalAiUsages" ("Date","TokensUsed") VALUES ({today},0) ON CONFLICT ("Date") DO NOTHING""", ct)
            : _data.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "GlobalAiUsages" ("Date","TokensUsed") VALUES ({today},0) ON CONFLICT ("Date") DO NOTHING""", ct);

    private Task EnsureTeamRowAsync(string organizationId, DateTime today, CancellationToken ct) =>
        _data.Database.IsNpgsql()
            ? _data.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO billing."OrgAiUsages" ("OrganizationId","Date","TokensUsed") VALUES ({organizationId},{today},0) ON CONFLICT ("OrganizationId","Date") DO NOTHING""", ct)
            : _data.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "OrgAiUsages" ("OrganizationId","Date","TokensUsed") VALUES ({organizationId},{today},0) ON CONFLICT ("OrganizationId","Date") DO NOTHING""", ct);
}
