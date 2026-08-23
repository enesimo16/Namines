using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

/// <summary>Kota kontrolünün sonucu.</summary>
public enum AiQuotaDecision
{
    /// <summary>Bütçe var.</summary>
    Allowed,

    /// <summary>Paylaşılan günlük havuz doldu.</summary>
    PoolExhausted,

    /// <summary>Kullanıcının günlük tavanı doldu.</summary>
    UserExhausted,
}

/// <summary>
/// AI bütçesinin TEK sahibi (22 §5).
///
/// <b>Neden ayrı bir servis:</b> aynı hesap iki yerden harcanıyor —
/// <c>AIQuotaMiddleware</c> (Studio, JWT ile) ve Gateway'in <c>/query/nl</c> ucu
/// (müşteri uygulaması, API anahtarı ile). İkinci yer kendi kopyasını yazdığında
/// üç şeyi birden yanlış yaptı ve hiçbiri testlerde görünmedi:
/// <list type="bullet">
/// <item>Kota <b>token</b> sayıyor, çağrı değil — kopya 1 artırıyordu, yani
/// 20.000'lik tavan pratikte hiç dolmuyordu.</item>
/// <item>Paylaşılan günlük havuza (<see cref="GlobalAiUsage"/>) hiç
/// dokunmuyordu.</item>
/// <item>Gün sınırı UTC'ye göreydi; middleware ise TR saatine (UTC+3) göre
/// sıfırlıyor. Aynı sayaç iki farklı güne bölünüyordu.</item>
/// </list>
/// Kural tek yerde olduğu sürece bu üçü de tek bir yerde doğru.
/// </summary>
public sealed class AiQuotaService
{
    private readonly AuthDbContext _context;
    private readonly IConfiguration _configuration;

    public AiQuotaService(AuthDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    /// <summary>Paylaşılan günlük token havuzu.</summary>
    public long DailyPool =>
        long.TryParse(_configuration["AiPool:DailyTokenPool"], out var value) ? value : 100_000;

    /// <summary>
    /// Kullanıcının PLANINA göre günlük token tavanı.
    ///
    /// <b>Eskiden yapılandırmadan tek bir sayıydı</b> — yani ücretli kullanıcı da
    /// ücretsiz kullanıcı da aynı bütçeyi alıyordu. Abonelik bilgisi veritabanında
    /// duruyor ama hiçbir sınırı etkilemiyordu: para ödeyen karşılığını almıyor,
    /// ödemeyen de kısıtlanmıyordu.
    ///
    /// Yapılandırmadaki değer artık yalnızca <see cref="PlanTier.Free"/> için bir
    /// override; diğer planlar <see cref="PlanQuotas"/>'dan geliyor ki sayılar
    /// tek yerde dursun.
    /// </summary>
    public async Task<int> PerUserCapAsync(string userId, CancellationToken ct = default)
    {
        var tier = await TierAsync(userId, ct);

        if (tier == PlanTier.Free &&
            int.TryParse(_configuration["AiPool:PerUserDailyTokens"], out var configured))
            return configured;

        return PlanQuotas.For(tier).DailyAiTokens;
    }

    /// <summary>
    /// Kullanıcının planı. Kullanıcı bulunamazsa <see cref="PlanTier.Free"/> —
    /// bilinmeyen bir kimliğe ücretli hak vermek, faturalama tarafında sessizce
    /// para kaybı demektir.
    /// </summary>
    public async Task<PlanTier> TierAsync(string userId, CancellationToken ct = default)
    {
        var status = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.SubscriptionStatus)
            .FirstOrDefaultAsync(ct);

        return PlanQuotas.Resolve(status);
    }

    /// <summary>
    /// Gün sınırı <b>TR saatine (UTC+3)</b> göre.
    ///
    /// İki yerin farklı gün sınırı kullanması, aynı sayacın bazı saatlerde
    /// sıfırlanmış bazı saatlerde sıfırlanmamış görünmesi demekti.
    /// </summary>
    private static DateTime LocalDate(DateTime utc) => utc.AddHours(3).Date;

    /// <summary>
    /// Kullanıcının kota satırını getirir; yoksa oluşturur, gün dönmüşse sıfırlar,
    /// tavanı güncel yapılandırmaya normalize eder.
    /// </summary>
    public async Task<UserAIQuota> EnsureQuotaAsync(string userId, CancellationToken ct = default)
    {
        var cap = await PerUserCapAsync(userId, ct);
        var quota = await _context.UserAIQuotas.FirstOrDefaultAsync(q => q.UserId == userId, ct);

        if (quota is null)
        {
            quota = new UserAIQuota
            {
                UserId = userId,
                DailyLimit = cap,
                DailyUsageCount = 0,
                LastResetDate = DateTime.UtcNow,
            };
            await _context.UserAIQuotas.AddAsync(quota, ct);
            await _context.SaveChangesAsync(ct);
            return quota;
        }

        var changed = false;

        // Tavan, PLANIN güncel hakkına çekiliyor. Plan değişince (yükseltme ya da
        // iptal) kullanıcının satırı kendiliğinden doğru sınıra gelir; ayrı bir
        // "planı senkronla" işi gerekmiyor ve unutulamıyor.
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

        if (changed) await _context.SaveChangesAsync(ct);

        return quota;
    }

    /// <summary>
    /// Bu kadar token harcanabilir mi?
    ///
    /// <b>Sayaçlara DOKUNMAZ.</b> Kontrol ile harcamayı ayırmak, başarısız bir
    /// çağrının bütçeden düşmemesini sağlıyor: dış bir servisin arızasını
    /// kullanıcının günlük hakkından kesmek yanlış olurdu.
    /// </summary>
    public async Task<AiQuotaDecision> CheckAsync(string userId, int estimatedTokens, CancellationToken ct = default)
    {
        var quota = await EnsureQuotaAsync(userId, ct);

        var today = DateTime.UtcNow.Date;
        var used = await _context.GlobalAiUsages
            .Where(g => g.Date == today)
            .Select(g => (long?)g.TokensUsed)
            .FirstOrDefaultAsync(ct) ?? 0;

        if (used + estimatedTokens > DailyPool) return AiQuotaDecision.PoolExhausted;
        if (quota.DailyUsageCount + estimatedTokens > quota.DailyLimit) return AiQuotaDecision.UserExhausted;

        return AiQuotaDecision.Allowed;
    }

    /// <summary>
    /// Harcamayı işler — kullanıcı sayacı ve paylaşılan havuz birlikte.
    ///
    /// <b>Atomik güncelleme kullanılıyor</b> (<c>ExecuteUpdate</c>): oku-değiştir-yaz
    /// yapsaydık eşzamanlı iki istek birbirinin artışını ezerdi ve tavan sessizce
    /// aşılırdı. Kullanıcı sayacı tavanda sabitleniyor — aşan bir değer, ertesi
    /// günün bütçesini de yemiş gibi görünürdü.
    /// </summary>
    public async Task ConsumeAsync(string userId, int estimatedTokens, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var trackedQuota = _context.UserAIQuotas.Local.FirstOrDefault(q => q.UserId == userId);

        await _context.UserAIQuotas
            .Where(q => q.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                q => q.DailyUsageCount,
                q => q.DailyUsageCount + estimatedTokens > q.DailyLimit
                    ? q.DailyLimit
                    : q.DailyUsageCount + estimatedTokens), ct);

        var rows = await _context.GlobalAiUsages
            .Where(g => g.Date == today)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.TokensUsed, g => g.TokensUsed + estimatedTokens), ct);

        if (rows == 0)
        {
            // Günün ilk harcaması: satır henüz yok. Yarışta ikinci istek de
            // buraya düşerse ekleme çakışır; o durumda güncellemeyi tekrar
            // deniyoruz — harcamayı sessizce kaybetmek, havuzu ölçülemez kılardı.
            try
            {
                await _context.GlobalAiUsages.AddAsync(
                    new GlobalAiUsage { Date = today, TokensUsed = estimatedTokens }, ct);
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                await _context.GlobalAiUsages
                    .Where(g => g.Date == today)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.TokensUsed, g => g.TokensUsed + estimatedTokens), ct);
            }
        }

        // ExecuteUpdate veritabanını günceller ama DEĞİŞİKLİK İZLEYİCİSİNİ
        // ATLAR: aynı kapsamda daha önce okunmuş bir kota nesnesi eski sayacı
        // taşımaya devam eder. Aynı istekte ikinci bir kontrol yapan çağıran
        // bayat bir değere bakar ve tavanı aşan bir harcamaya izin verebilir.
        if (trackedQuota is not null)
            await _context.Entry(trackedQuota).ReloadAsync(ct);
    }
}
