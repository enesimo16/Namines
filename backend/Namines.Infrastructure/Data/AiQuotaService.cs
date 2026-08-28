using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namines.Core.Analysis;
using Namines.Core.Models.Auth;

namespace Namines.Infrastructure.Data;

/// <summary>
/// Havuzun doluluk baskısı — büyütme kararını İNSANIN vermesi için üretilen
/// veri (bkz. <see cref="AiQuotaService.PoolPressureAsync"/>).
/// </summary>
/// <param name="CurrentPool">Şu anki günlük havuz.</param>
/// <param name="MaxPool">Otomatik büyümenin sert tavanı.</param>
/// <param name="DaysObserved">Kaç günlük veri var.</param>
/// <param name="DaysFull">Bu günlerin kaçında havuz %95+ doldu.</param>
/// <param name="ShouldGrow">Eşik aşıldı mı — bir üst kademe önerilir mi.</param>
/// <param name="SuggestedPool">Önerilen yeni havuz; büyüme gerekmiyorsa mevcut değer.</param>
public sealed record PoolPressure(
    long CurrentPool,
    long MaxPool,
    int DaysObserved,
    int DaysFull,
    bool ShouldGrow,
    long SuggestedPool);

/// <summary>Kota kontrolünün sonucu.</summary>
public enum AiQuotaDecision
{
    /// <summary>Bütçe var.</summary>
    Allowed,

    /// <summary>Paylaşılan günlük havuz doldu.</summary>
    PoolExhausted,

    /// <summary>Kullanıcının günlük tavanı doldu.</summary>
    UserExhausted,

    /// <summary>
    /// Ekibin (organizasyonun) günlük ortak havuzu doldu.
    ///
    /// Kullanıcının kendi tavanından ayrı: kişi hakkını bitirmemiş olabilir ama
    /// ekip arkadaşları havuzu tüketmiş olabilir. Ayrı bir durum olması şart —
    /// "kendi hakkın dolu" demek yanlış olurdu ve kullanıcı sebebi anlamazdı.
    /// </summary>
    TeamExhausted,
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
        long.TryParse(_configuration["AiPool:DailyTokenPool"], out var value) ? value : 500_000;

    /// <summary>
    /// Havuzun çıkabileceği en yüksek değer — otomatik büyümenin SERT tavanı.
    ///
    /// <b>Neden ayrı bir tavan:</b> havuz doğrudan aylık gider demek. Talebe
    /// göre kendiliğinden büyüyen bir havuz, bir kötüye kullanım dalgasında ya
    /// da viral bir günde faturayı da kendiliğinden büyütür. Otomasyonun
    /// faydası (elle müdahale gerekmemesi) ancak bir üst sınırla birlikte
    /// güvenli.
    /// </summary>
    public long MaxDailyPool =>
        long.TryParse(_configuration["AiPool:MaxDailyTokenPool"], out var value) ? value : 2_000_000;

    /// <summary>
    /// Havuzun kaç GÜN üst üste dolmasından sonra bir üst kademeye çıkılacağı.
    ///
    /// Tek bir yoğun gün büyümeyi tetiklememeli — o gün bir kampanya, bir
    /// paylaşım ya da tek seferlik bir dalga olabilir. Üst üste dolması
    /// "talep kalıcı" demek.
    /// </summary>
    public int GrowthAfterFullDays =>
        int.TryParse(_configuration["AiPool:GrowthAfterFullDays"], out var value) && value > 0 ? value : 3;

    /// <summary>
    /// Havuzun günde KAÇ ücretsiz kullanıcıyı taşıyacak şekilde bölüneceği.
    ///
    /// Bu sayı bir tahmin değil, bir <b>taahhüt</b>: havuz bu kadar kişiye
    /// bölünüyor ve kimse payının iki katından fazlasını alamıyor.
    /// </summary>
    public int MinDailyFreeUsers =>
        int.TryParse(_configuration["AiPool:MinDailyFreeUsers"], out var value) && value > 0 ? value : 100;

    /// <summary>
    /// Bir kullanıcıya verilmesi anlamlı olan en küçük günlük bütçe —
    /// kabaca tek bir şema üretimi.
    ///
    /// <b>Neden gerekli:</b> havuzu çok fazla kişiye bölmek, herkese hiçbir işe
    /// yaramayacak kadar küçük bir pay vermek olur. 1.000 token'la kullanıcı bir
    /// şema üretemez; "bütçen var" deyip sonra ortasında kesmek, hiç
    /// başlatmamaktan daha kötü bir deneyim. Bu tabanın altına düşmek yerine
    /// daha AZ kullanıcıya düzgün hizmet veriliyor ve geri kalanına havuzun
    /// dolduğu dürüstçe söyleniyor.
    /// </summary>
    public int MinUsefulDailyTokens =>
        int.TryParse(_configuration["AiPool:MinUsefulDailyTokens"], out var value) && value > 0 ? value : 8_000;

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

        if (tier == PlanTier.Free)
        {
            var planCap = int.TryParse(_configuration["AiPool:PerUserDailyTokens"], out var configured)
                ? configured
                : PlanQuotas.For(PlanTier.Free).DailyAiTokens;

            return FreeUserCap(planCap);
        }

        var perSeat = PlanQuotas.For(tier).DailyAiTokens;

        // Team'de üye başına tavan, payından YÜKSEK ama havuzun tamamından DÜŞÜK.
        //
        // Tam payına (200K) eşitlemek, ortak havuzu bölünmüş kotaya çevirir ve
        // boşta duran üyenin payını çöpe atardı. Havuzun tamamına (600K) açmak
        // ise bir üyenin hepsini tüketip diğerlerine hiçbir şey bırakmamasına
        // izin verirdi. İkiye katlamak ortası: yoğun çalışan biri payının iki
        // katını kullanabiliyor, ekibe her hâlükârda bir pay kalıyor.
        return tier == PlanTier.Team ? perSeat * 2 : perSeat;
    }

    /// <summary>
    /// Ücretsiz bir kullanıcının günlük tavanı — planın verdiği hak ile havuzdan
    /// düşen ADİL PAY'ın küçüğü.
    ///
    /// <b>Çözdüğü sorun:</b> plan tavanı 20.000, havuz 100.000'di. Yani günün
    /// ilk BEŞ kullanıcısı havuzun tamamını tüketebiliyordu ve altıncıdan
    /// itibaren gelen herkes, kendi hakkı hiç dolmamışken "havuz doldu" duvarına
    /// çarpıyordu. Ücretsiz katmanın vaadi ilk gelenlere değil, gelen herkese.
    ///
    /// <b>Kural:</b> pay = havuz / hedef kullanıcı sayısı. Kullanıcı payının
    /// <b>iki katına</b> kadar çıkabiliyor — Team havuzundaki ile aynı gerekçe:
    /// tam paya kilitlemek boşta duran payı çöpe atar, tamamına açmak birinin
    /// hepsini yemesine izin verir.
    ///
    /// <b>Taban:</b> pay <see cref="MinUsefulDailyTokens"/>'ın altına düşerse
    /// tabana çekiliyor. Bu, hedeften daha az kullanıcıya hizmet vermek demek —
    /// ama herkese işe yaramaz bir kırıntı vermektense daha az kişiye çalışan
    /// bir ürün vermek doğru olan.
    /// </summary>
    private int FreeUserCap(int planCap) =>
        CalculateFreeUserCap(planCap, DailyPool, MinDailyFreeUsers, MinUsefulDailyTokens);

    /// <summary>
    /// Havuzun son <paramref name="lookbackDays"/> gündeki doluluk baskısı ve
    /// önerilen bir sonraki kademe.
    ///
    /// <b>Öneriyor, UYGULAMIYOR.</b> Havuzu büyütmek doğrudan para harcamak
    /// demek ve bu kararı bir sayaç veremez: aynı doluluk, "ürün tutuyor,
    /// büyüt" de olabilir "biri kötüye kullanıyor, önce ona bak" da. Karar
    /// insanın; bu metot yalnızca kararı verecek sayıyı üretiyor.
    ///
    /// Kademe iki katına çıkıyor (500K → 1M → 2M) ve
    /// <see cref="MaxDailyPool"/>'u asla aşmıyor.
    /// </summary>
    public async Task<PoolPressure> PoolPressureAsync(int lookbackDays = 7, CancellationToken ct = default)
    {
        var since = LocalDate(DateTime.UtcNow).AddDays(-lookbackDays);

        var recent = await _context.GlobalAiUsages.AsNoTracking()
            .Where(g => g.Date >= since)
            .Select(g => g.TokensUsed)
            .ToListAsync(ct);

        // "Dolu" için %95 eşiği: havuz tam tavana değmeden de pratikte
        // tükenmiş sayılır — son birkaç bin token kimseye bir üretim yaptırmaz.
        var threshold = (long)(DailyPool * 0.95);
        var fullDays = recent.Count(t => t >= threshold);

        var shouldGrow = fullDays >= GrowthAfterFullDays && DailyPool < MaxDailyPool;
        var suggested = shouldGrow ? Math.Min(DailyPool * 2, MaxDailyPool) : DailyPool;

        return new PoolPressure(
            CurrentPool: DailyPool,
            MaxPool: MaxDailyPool,
            DaysObserved: recent.Count,
            DaysFull: fullDays,
            ShouldGrow: shouldGrow,
            SuggestedPool: suggested);
    }

    /// <summary>
    /// <see cref="FreeUserCap"/>'in saf hâli — veritabanına ve yapılandırmaya
    /// bağlı olmayan politika hesabı.
    ///
    /// Ayrı durması bilinçli: bu bir <b>iş kuralı</b> ve ucuz birim testlerle
    /// kanıtlanabilmeli. Örnek bir servis kurup sahte bir DbContext bağlamak,
    /// aslında matematiği test eden bir teste altyapı maliyeti yüklerdi.
    /// </summary>
    public static int CalculateFreeUserCap(int planCap, long dailyPool, int minDailyFreeUsers, int minUsefulDailyTokens)
    {
        if (minDailyFreeUsers <= 0) return planCap;

        var fairShare = (int)Math.Min(int.MaxValue, dailyPool / minDailyFreeUsers);
        var burstable = Math.Max(fairShare * 2, minUsefulDailyTokens);

        return Math.Min(planCap, burstable);
    }

    /// <summary>
    /// Kullanıcının ekip havuzu — Team planında organizasyonun günlük toplam hakkı.
    /// Team değilse <c>null</c> (ekip havuzu kontrolü hiç yapılmıyor).
    /// </summary>
    private async Task<(string OrgId, long Limit)?> TeamPoolAsync(string userId, CancellationToken ct)
    {
        if (await TierAsync(userId, ct) != PlanTier.Team) return null;

        // Kullanıcının üyesi olduğu, kişisel OLMAYAN organizasyon; yoksa kendi
        // kişisel org'u. TeamController'daki ActiveOrgAsync ile aynı kural —
        // ikisi ayrışırsa kullanıcı bir yerde ekipte, başka yerde tek başına
        // görünürdü.
        var org = await _context.OrganizationMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Organization.IsPersonal ? 1 : 0)
            .ThenBy(m => m.JoinedAt)
            .Select(m => new { m.OrganizationId, m.Organization.CreatedByUserId })
            .FirstOrDefaultAsync(ct);

        if (org is null) return null;

        var seats = PlanQuotas.For(PlanTier.Team).TeamSeats;
        var perSeat = PlanQuotas.For(PlanTier.Team).DailyAiTokens;

        return (org.OrganizationId, (long)perSeat * seats);
    }

    /// <summary>
    /// Kullanıcının planı. Kullanıcı bulunamazsa <see cref="PlanTier.Free"/> —
    /// bilinmeyen bir kimliğe ücretli hak vermek, faturalama tarafında sessizce
    /// para kaybı demektir.
    /// </summary>
    public async Task<PlanTier> TierAsync(string userId, CancellationToken ct = default)
    {
        var row = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.SubscriptionStatus, u.PlanCode, u.IsDev })
            .FirstOrDefaultAsync(ct);

        return PlanQuotas.Resolve(row?.SubscriptionStatus, row?.PlanCode, row?.IsDev ?? false);
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
        // Sahip hesabı hiçbir kapıya takılmıyor — ne kendi tavanına, ne paylaşılan
        // havuza. Havuz kontrolünün de atlanması bilinçli: havuz "ücretsiz
        // kullanıcılar toplamda şu kadar harcasın" demek, geliştiricinin kendi
        // ürününü deneyemez hâle gelmesi değil.
        //
        // Harcama YİNE DE kaydediliyor (bkz. ConsumeAsync): sınırsız olmak,
        // maliyetin görünmez olması anlamına gelmemeli.
        if (await TierAsync(userId, ct) == PlanTier.Dev) return AiQuotaDecision.Allowed;

        var quota = await EnsureQuotaAsync(userId, ct);

        var today = DateTime.UtcNow.Date;
        var used = await _context.GlobalAiUsages
            .Where(g => g.Date == today)
            .Select(g => (long?)g.TokensUsed)
            .FirstOrDefaultAsync(ct) ?? 0;

        // Paylaşılan havuz YALNIZCA ücretsiz kullanıcıları bağlıyor.
        //
        // Önceden herkesi bağlıyordu ve bu, planların vaadini sessizce yalanlıyordu:
        // havuz 100.000/gün, oysa Pro'ya 200.000, bir Team'e 600.000 satılıyor.
        // Yani parasını ödemiş bir müşteri, ücretsiz kullanıcıların tükettiği bir
        // tavana takılıp aldığı hakkı hiç kullanamıyordu. Havuzun varlık sebebi
        // "bedava kullanımın maliyetini sınırlamak"; ücretli kullanım zaten
        // gelirle karşılanıyor, onu aynı tavana sokmak ters.
        var tier = await TierAsync(userId, ct);
        if (tier == PlanTier.Free && used + estimatedTokens > DailyPool)
            return AiQuotaDecision.PoolExhausted;

        if (quota.DailyUsageCount + estimatedTokens > quota.DailyLimit) return AiQuotaDecision.UserExhausted;

        // Ekip havuzu: Team'de organizasyonun TOPLAM günlük hakkı. Kullanıcının
        // kendi tavanı dolmamış olsa bile ekibin hakkı bitmiş olabilir — o zaman
        // reddediliyor, aksi hâlde üç kişi ayrı ayrı tavanına kadar harcayıp
        // ekibin toplam bütçesini ikiye katlardı.
        var team = await TeamPoolAsync(userId, ct);
        if (team is not null)
        {
            var teamUsed = await _context.OrgAiUsages.AsNoTracking()
                .Where(o => o.OrganizationId == team.Value.OrgId && o.Date == today)
                .Select(o => (long?)o.TokensUsed)
                .FirstOrDefaultAsync(ct) ?? 0;

            if (teamUsed + estimatedTokens > team.Value.Limit) return AiQuotaDecision.TeamExhausted;
        }

        return AiQuotaDecision.Allowed;
    }

    /// <summary>
    /// Bütçeyi ÖNCEDEN düşer ve izin verir; yer yoksa reddeder.
    ///
    /// <b>Çözdüğü sorun (yarış koşulu):</b> <see cref="CheckAsync"/> sayaçlara
    /// dokunmuyor, <see cref="ConsumeAsync"/> ise iş BİTTİKTEN sonra düşüyordu.
    /// Aradaki pencerede eşzamanlı gönderilen N istek kontrolü hep birlikte
    /// geçiyor, hepsi sağlayıcıya gidiyor ve tavan tek seferde deliniyordu.
    /// Üretim ucunda hız sınırı da olmadığı için bu teorik değil, tek satırlık
    /// bir betikle sömürülebilir bir açıktı.
    ///
    /// Rezervasyon, kontrolü ve düşmeyi TEK atomik adımda yapıyor: ikinci istek
    /// birincinin düşüşünü görüyor.
    ///
    /// <b>Tahmin fazlaysa iş bitince iade ediliyor</b> (bkz.
    /// <see cref="ReconcileAsync"/>) — kullanıcı kullanmadığı bütçeyi
    /// kaybetmemeli.
    /// </summary>
    public async Task<AiQuotaDecision> TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct = default)
    {
        var decision = await CheckAsync(userId, estimatedTokens, ct);
        if (decision != AiQuotaDecision.Allowed) return decision;

        // Dev hesabı sayaçlara yazılıyor ama sınıra takılmıyor (CheckAsync'teki
        // gerekçe): sınırsız olmak, maliyetin görünmez olması demek değil.
        await ConsumeAsync(userId, estimatedTokens, ct);
        return AiQuotaDecision.Allowed;
    }

    /// <summary>
    /// Rezerve edilen tahmini, sağlayıcının bildirdiği GERÇEK kullanımla
    /// değiştirir.
    ///
    /// Gerçek kullanım tahminden büyükse fark düşülür, küçükse iade edilir.
    /// Ölçüm alınamadıysa (<paramref name="actualTokens"/> ≤ 0) hiçbir şey
    /// yapılmaz — rezervasyon olduğu gibi kalır, yani ölçemediğimizde
    /// kullanıcının LEHİNE değil, bütçenin lehine hata yapıyoruz.
    /// </summary>
    public async Task ReconcileAsync(string userId, int reservedTokens, int actualTokens, CancellationToken ct = default)
    {
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

    /// <summary>
    /// Kullanılmayan rezervasyonu geri verir. Sayaçlar sıfırın altına
    /// DÜŞMÜYOR — negatif bir sayaç, ertesi günün bütçesini şişirirdi.
    ///
    /// Çağrı hiç iş yapmadan başarısız olduğunda (sağlayıcı hatası, hız sınırı)
    /// rezervasyonun TAMAMI buradan iade ediliyor.
    /// </summary>
    public async Task RefundAsync(string userId, int tokens, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;

        await _context.UserAIQuotas
            .Where(q => q.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                q => q.DailyUsageCount,
                q => q.DailyUsageCount - tokens < 0 ? 0 : q.DailyUsageCount - tokens), ct);

        await _context.GlobalAiUsages
            .Where(g => g.Date == today)
            .ExecuteUpdateAsync(s => s.SetProperty(
                g => g.TokensUsed,
                g => g.TokensUsed - tokens < 0 ? 0 : g.TokensUsed - tokens), ct);

        var team = await TeamPoolAsync(userId, ct);
        if (team is not null)
        {
            await _context.OrgAiUsages
                .Where(o => o.OrganizationId == team.Value.OrgId && o.Date == today)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    o => o.TokensUsed,
                    o => o.TokensUsed - tokens < 0 ? 0 : o.TokensUsed - tokens), ct);
        }
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

        // Ekip sayacı da artıyor. Aynı yarış-güvenli desen: önce güncelle, satır
        // yoksa ekle, ekleme çakışırsa güncellemeye dön.
        var team = await TeamPoolAsync(userId, ct);
        if (team is not null)
        {
            var orgRows = await _context.OrgAiUsages
                .Where(o => o.OrganizationId == team.Value.OrgId && o.Date == today)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.TokensUsed, o => o.TokensUsed + estimatedTokens), ct);

            if (orgRows == 0)
            {
                try
                {
                    await _context.OrgAiUsages.AddAsync(new OrgAiUsage
                    {
                        OrganizationId = team.Value.OrgId,
                        Date = today,
                        TokensUsed = estimatedTokens,
                    }, ct);
                    await _context.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    await _context.OrgAiUsages
                        .Where(o => o.OrganizationId == team.Value.OrgId && o.Date == today)
                        .ExecuteUpdateAsync(s => s.SetProperty(o => o.TokensUsed, o => o.TokensUsed + estimatedTokens), ct);
                }
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
