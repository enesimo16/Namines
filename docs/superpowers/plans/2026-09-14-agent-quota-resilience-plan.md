# Agent Kotası ve Dayanıklılık (Spec Bölüm 4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (a) Onarım turu sayısını plana bağlamak — bugün Free ile Pro
aynı derinliği alıyor, (b) onarım turundaki bir AI hatasının elde duran
geçerli şemayı çöpe atmasını durdurmak.

**Architecture:** Kota muhasebesi (rezerve → ölç → uzlaştır → iade),
hata sınıfları ve `RemainingFindings` ile "en iyi son hali döndür"
davranışı ZATEN var. Bu plan iki gerçek boşluğu kapatır ve yeni bir
muhasebe yolu açmaz.

**Tech Stack:** .NET 8 / C#, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-14-agent-prompt-engine-design.md`
(Bölüm 4)

## Global Constraints

- Token muhasebesi DEĞİŞMİYOR: `AiUsageTracker` zaten `PostAsync`
  üzerinden her çağrıyı (araç turları dahil) ölçüyor ve `SettleAsync`
  ölçümü tahmine tercih ediyor. Tur sayısı yalnızca DERİNLİK sınırıdır,
  fatura değil.
- Taslak turu hatası hâlâ isteği düşürür: elde hiçbir şey yokken
  "kısmi sonuç" döndürmek yalan olurdu.
- Kullanıcı iptali (`OperationCanceledException`) hata DEĞİLDİR; asla
  bulguya çevrilmez, olduğu gibi yukarı çıkar.

---

### Task 1: Onarım turu tavanını plana bağla

**Neden:** `PlanLimits` içinde `DailyAiTokens` plana göre değişiyor ama
onarım derinliği değişmiyor. `AffordableRoundsAsync` herkesi
`DefaultTotalRounds`'a sabitliyor, yani Pro'nun tek farkı daha büyük bir
token havuzu — agent yine aynı noktada pes ediyor.

**Files:**
- Modify: `backend/Namines.Core/Analysis/PlanQuotas.cs`
- Modify: `backend/Namines.Infrastructure/Services/SchemaAgentPipeline.cs`
- Modify: `backend/Namines.API/Controllers/SchemaController.cs`
- Test: `backend/Namines.Tests/Analysis/PlanQuotaAgentRoundsTests.cs` (yeni)

**Interfaces:**
- Produces: `PlanLimits.MaxAgentRepairTurns` (`int`),
  `SchemaAgentPipeline.FixedRounds` (`const int` = 2, plan + taslak).

- [ ] **Step 1: Write the failing test**

```csharp
using Namines.Core.Analysis;
using Xunit;

namespace Namines.Tests.Analysis;

/// <summary>
/// Onarım derinliği plana bağlı olmalı.
///
/// <b>Neden kota tablosunda, çağıranda değil:</b> tavan çağıranda sabitlenirse
/// (eskiden öyleydi) ödeyen kullanıcı yalnızca daha büyük bir token havuzu alır
/// ama agent aynı noktada pes eder — yani parasının karşılığını almaz.
/// </summary>
public class PlanQuotaAgentRoundsTests
{
    [Fact]
    public void A_free_plan_gets_the_shallow_repair_budget()
    {
        Assert.Equal(2, PlanQuotas.For(PlanTier.Free).MaxAgentRepairTurns);
    }

    [Fact]
    public void Paid_plans_repair_deeper_than_free()
    {
        var free = PlanQuotas.For(PlanTier.Free).MaxAgentRepairTurns;

        Assert.True(PlanQuotas.For(PlanTier.Pro).MaxAgentRepairTurns > free);
        Assert.True(PlanQuotas.For(PlanTier.Team).MaxAgentRepairTurns > free);
        Assert.True(PlanQuotas.For(PlanTier.Enterprise).MaxAgentRepairTurns > free);
    }

    [Fact]
    public void Pro_and_team_share_the_same_repair_depth()
    {
        // Bir Team koltuğu bir Pro hesabıyla aynı hakkı taşır; ekip olmak
        // kimseyi kısıtlamaz (bkz. DailyAiTokens'daki aynı gerekçe).
        Assert.Equal(
            PlanQuotas.For(PlanTier.Pro).MaxAgentRepairTurns,
            PlanQuotas.For(PlanTier.Team).MaxAgentRepairTurns);
    }

    [Fact]
    public void The_owner_account_repairs_at_least_as_deep_as_any_paid_plan()
    {
        Assert.True(
            PlanQuotas.For(PlanTier.Dev).MaxAgentRepairTurns >=
            PlanQuotas.For(PlanTier.Enterprise).MaxAgentRepairTurns);
    }

    [Fact]
    public void Every_plan_can_repair_at_least_once()
    {
        // Sıfır onarım turu, agent hattını tek çağrıya indirger ve denetimi
        // anlamsızlaştırırdı — bulgu bulunur ama hiç düzeltilmez.
        foreach (var tier in new[]
                 { PlanTier.Free, PlanTier.Pro, PlanTier.Team, PlanTier.Enterprise, PlanTier.Dev })
            Assert.True(PlanQuotas.For(tier).MaxAgentRepairTurns >= 1, $"{tier} repairs zero times.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/Namines.Tests --filter PlanQuotaAgentRoundsTests`
Expected: FAIL — `MaxAgentRepairTurns` yok (derleme hatası).

- [ ] **Step 3: Write the implementation**

`PlanQuotas.cs` — add the parameter to `PlanLimits` (sona, varsayılanla —
mevcut çağıranların hiçbiri kırılmaz):

```csharp
/// <param name="MaxAgentRepairTurns">
/// Şema ajanının kaç kez DÜZELTME turu atabileceği (plan + taslak turları
/// bunun DIŞINDA — bkz. SchemaAgentPipeline.FixedRounds).
///
/// <b>Neden plana bağlı:</b> önceden tavan çağıranda sabitti, yani ödeyen
/// kullanıcının tek farkı daha büyük bir token havuzuydu — agent yine aynı
/// noktada pes ediyordu. Derinlik, ücretli planın somut karşılığı.
///
/// <b>Neden token bütçesinden AYRI bir sınır:</b> token havuzu "bugün ne
/// kadar harcayabilirsin", bu ise "tek bir şemada ne kadar ısrar edilsin"
/// sorusunu cevaplıyor. Yalnızca token'a bakmak, havuzu bol bir kullanıcının
/// tek istekte çözülemeyen bir bulgu için turlarca dönmesine izin verirdi.
/// </param>
public sealed record PlanLimits(
    int BranchDatabases,
    int EphemeralRunsPerDay,
    int ByodbConnections,
    int DailyAiTokens = 20_000,
    int GatewayRequestsPerMinute = 60,
    int TeamSeats = 1,
    int CrossDatabaseRelations = 3,
    int ManagedDatabases = 0,
    long ManagedDatabaseStorageWarningBytes = -1,
    int MaxAgentRepairTurns = 2);
```

and set it per tier in `For(...)`:
- `Free` → `MaxAgentRepairTurns: 2` (bugünküyle aynı)
- `Pro` → `MaxAgentRepairTurns: 4`
- `Team` → `MaxAgentRepairTurns: 4`
- `Enterprise` → `MaxAgentRepairTurns: 6`
- `Dev` → `MaxAgentRepairTurns: 6`

`SchemaAgentPipeline.cs` — name the fixed part so the `+2` stops being magic:

```csharp
    /// <summary>
    /// Düzeltme turlarının DIŞINDA kalan sabit turlar: plan + taslak.
    ///
    /// Ayrı bir sabit olması, kota tavanını hesaplayan çağıranın (bkz.
    /// SchemaController.AffordableRoundsAsync) aritmetiği kendi başına
    /// yapmasını engelliyor — plan turu eklendiğinde tam olarak o hata
    /// yapılmıştı ve bir düzeltme hakkı sessizce yenmişti.
    /// </summary>
    public const int FixedRounds = 2;

    public const int DefaultRepairRounds = 2;

    public const int DefaultTotalRounds = FixedRounds + DefaultRepairRounds;
```

(`DefaultTotalRounds`'un eski `DefaultRepairRounds + 2` tanımı bununla
birebir aynı değeri üretir; davranış değişmiyor.)

`SchemaController.AffordableRoundsAsync` — tier-aware ceiling:

```csharp
    private async Task<int> AffordableRoundsAsync(string? userId)
    {
        if (_quota is null || string.IsNullOrEmpty(userId))
            return SchemaAgentPipeline.DefaultTotalRounds;

        var quota = await _quota.EnsureQuotaAsync(userId, HttpContext.RequestAborted);
        var remaining = Math.Max(0, quota.DailyLimit - quota.DailyUsageCount);

        var affordable = remaining / SchemaRoundTokenEstimate;

        // Tavan PLANA bağlı. Önceden herkes aynı tavandaydı: ödeyen kullanıcının
        // tek farkı daha büyük bir token havuzuydu, agent yine aynı noktada pes
        // ediyordu — yani derinlik için ödenen para bir karşılık üretmiyordu.
        var tier = await _quota.TierAsync(userId);
        var ceiling = PlanQuotas.For(tier).MaxAgentRepairTurns + SchemaAgentPipeline.FixedRounds;

        return Math.Min(affordable, ceiling);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/Namines.Tests --filter PlanQuotaAgentRoundsTests`
Expected: PASS, and `dotnet build backend/Namines.sln` clean.

- [ ] **Step 5: Commit**

```bash
git add backend/Namines.Core/Analysis/PlanQuotas.cs \
        backend/Namines.Infrastructure/Services/SchemaAgentPipeline.cs \
        backend/Namines.API/Controllers/SchemaController.cs \
        backend/Namines.Tests/Analysis/PlanQuotaAgentRoundsTests.cs
git commit -m "feat: make agent repair depth a plan-tier limit"
```

---

### Task 2: Onarım turu hatası eldeki şemayı çöpe atmasın

**Neden:** `RunAsync` içinde try/catch yok. Taslak başarıyla üretildikten
sonra bir onarım turu hız sınırına takılırsa istisna yukarı çıkıyor,
controller bütçeyi iade edip hata dönüyor — ve kullanıcı ELİNDEKİ GEÇERLİ
taslağı da kaybediyor. Oysa `SchemaAgentResult.Schema`'nın kendi dokümanı
"Elde kalan şema — hatalı olsa bile döner" diyor.

**Files:**
- Modify: `backend/Namines.Infrastructure/Services/SchemaAgentPipeline.cs`
- Test: `backend/Namines.Tests/Services/SchemaAgentResilienceTests.cs` (yeni)

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Hat, bir turun patlamasıyla elde duranı KAYBETMEMELİ.
///
/// Taslak üretildikten sonra bir onarım turunun hız sınırına takılması, o ana
/// kadar üretilmiş (ve kullanıcının kotasından ödenmiş) geçerli şemanın çöpe
/// gitmesi anlamına gelmemeli — hattın kendi sözü de bu:
/// "Elde kalan şema — hatalı olsa bile döner."
/// </summary>
public class SchemaAgentResilienceTests
{
    private sealed class ThrowingSource : ISchemaDraftSource
    {
        private readonly DatabaseSchema _draft;
        private readonly Exception _failure;
        private readonly bool _failPlan;
        private readonly bool _failDraft;

        public ThrowingSource(
            DatabaseSchema draft, Exception failure, bool failPlan = false, bool failDraft = false)
        {
            _draft = draft;
            _failure = failure;
            _failPlan = failPlan;
            _failDraft = failDraft;
        }

        public int DraftCalls { get; private set; }

        public Task<string?> PlanAsync(string prompt, DatabaseType engine, CancellationToken ct = default) =>
            _failPlan ? throw _failure : Task.FromResult<string?>("plan");

        public Task<DatabaseSchema> DraftAsync(
            string prompt, DatabaseType engine, string? plan, CancellationToken ct = default)
        {
            DraftCalls++;
            return _failDraft ? throw _failure : Task.FromResult(_draft);
        }

        public Task<DatabaseSchema> RepairAsync(
            DatabaseSchema schema, IReadOnlyList<string> findings, DatabaseType engine,
            CancellationToken ct = default) => throw _failure;
    }

    /// <summary>FK tipi uyuşmayan şema — onarım turu tetikler.</summary>
    private static DatabaseSchema NeedsRepair()
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
            Id = "r1", Type = "OneToMany",
            SourceTableId = "t2", SourceColumnId = "c3",
            TargetTableId = "t1", TargetColumnId = "c1",
        });

        return schema;
    }

    private static SchemaAgentPipeline Pipeline(ISchemaDraftSource source) =>
        new(source, new DdlGeneratorFactory(), NullLogger<SchemaAgentPipeline>.Instance);

    [Fact]
    public async Task A_failed_repair_round_still_returns_the_draft()
    {
        var source = new ThrowingSource(NeedsRepair(), new InvalidOperationException("rate limited"));

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4);

        Assert.NotNull(result.Schema);
        Assert.Equal(2, result.Schema.Tables.Count);
        Assert.False(result.Clean);
    }

    [Fact]
    public async Task The_failure_is_reported_as_a_finding_not_hidden()
    {
        var source = new ThrowingSource(NeedsRepair(), new InvalidOperationException("rate limited"));

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4);

        Assert.Contains(result.RemainingFindings, f => f.Contains("rate limited"));
    }

    [Fact]
    public async Task A_failed_draft_round_still_fails_the_request()
    {
        // Elde HİÇBİR ŞEY yokken "kısmi sonuç" döndürmek yalan olurdu.
        var source = new ThrowingSource(
            NeedsRepair(), new InvalidOperationException("boom"), failDraft: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4));
    }

    [Fact]
    public async Task A_failed_plan_round_does_not_stop_the_run()
    {
        // Plan turu bir İYİLEŞTİRME; onsuz da şema üretilebiliyor. Planın
        // patlaması yüzünden üretimi düşürmek, opsiyonel bir adımı zorunlu
        // kılmak olurdu.
        var source = new ThrowingSource(
            NeedsRepair(), new InvalidOperationException("plan boom"), failPlan: true);

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4);

        Assert.Equal(1, source.DraftCalls);
        Assert.NotNull(result.Schema);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_as_a_finding()
    {
        // Kullanıcı iptali bir HATA değil; bulguya çevrilirse istek iptal
        // edilmiş gibi görünmez ve çağıran onu normal bir sonuç sanar.
        var source = new ThrowingSource(NeedsRepair(), new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 4));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/Namines.Tests --filter SchemaAgentResilienceTests`
Expected: FAIL — onarım istisnası yukarı çıktığı için ilk iki test düşer;
plan turu istisnası da üretimi düşürdüğü için dördüncü test düşer.

- [ ] **Step 3: Write the implementation**

In `RunAsync`, wrap the plan turn:

```csharp
        if (budgetRounds >= 3)
        {
            progress?.Report(AgentStep.Plan("Planning…"));
            try
            {
                plan = await _source.PlanAsync(prompt, engine, cancellationToken);
                rounds++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Plan bir İYİLEŞTİRME, zorunluluk değil: onsuz da şema
                // üretilebiliyor. Üretimi burada düşürmek, opsiyonel bir adımı
                // zorunlu kılmak olurdu.
                _logger.LogWarning(ex, "Schema agent plan turn failed; continuing without a plan.");
                plan = null;
            }
        }
```

and the repair turn inside the loop, replacing the bare call:

```csharp
            DatabaseSchema repaired;
            try
            {
                repaired = await _source.RepairAsync(schema, findings, engine, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Kullanıcı iptali hata DEĞİL; bulguya çevrilirse çağıran onu
                // normal bir sonuç sanar.
                throw;
            }
            catch (Exception ex)
            {
                // Elde GEÇERLİ bir şema var ve kullanıcı onun bedelini zaten
                // ödedi. Turun patlaması yüzünden onu da çöpe atmak, hattın
                // kendi sözünü ("elde kalan şema döner") bozmak olurdu.
                _logger.LogWarning(
                    ex, "Schema agent repair round {Round} failed; returning the best schema so far.", rounds);

                findings = findings
                    .Append($"[agent] Repair round {rounds} could not run: {ex.Message}")
                    .ToList();
                break;
            }

            rounds++;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/Namines.Tests --filter SchemaAgentResilienceTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/Namines.Infrastructure/Services/SchemaAgentPipeline.cs \
        backend/Namines.Tests/Services/SchemaAgentResilienceTests.cs
git commit -m "fix: keep the best schema when a repair round fails"
```

---

### Task 3: Tam doğrulama

- [ ] **Step 1:** `dotnet test backend/Namines.Tests` — yalnızca Docker
  gerektiren 8 `Integration` testi düşmeli.
- [ ] **Step 2:** `dotnet test backend/Namines.Tests --filter "FullyQualifiedName~DdlGoldenTests"`
  — 192/192; DDL üretimi bu planda değişmedi.
- [ ] **Step 3:** `cd frontend && npx vitest run && npx tsc --noEmit` — temiz.
