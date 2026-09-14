# Namines Flow — Bölüm 3 (Backend Depolama ve Orkestrasyon) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Namines Flow kurallarını (`AutomationRule`) kalıcı hale getirmek ve
sunucunun kendi hesapladığı şema diff'ine göre webhook/DBA-kontrolü/örnek-veri
aksiyonlarını gerçekten çalıştırmak — istemci tarafı (Bölüm 1-2) hâlâ anlık,
sunucu tarafı artık kalıcı ve doğrulanmış.

**Architecture:** `AuthController.SyncProjects`, kaydetmeden önce eski/yeni
şemayı `IMigrationService.CalculateDiffAsync` ile karşılaştırır; kayıt
başarılı olduktan SONRA bir `AutomationJob`'ı sınırlı kapasiteli bir
`Channel<T>` kuyruğuna atar (mevcut `IVaultJobQueue`/`VaultBackupWorker`
deseninin birebir kopyası — kendi DI kapsamını açan tek işçili
`BackgroundService`). Saf bir `AutomationRuleMatcher`, diff'i etkin kurallarla
eşleştirir; `AutomationExecutor` eşleşen her kuralın aksiyonunu kendi
try/catch'i içinde çalıştırır ve `AutomationRunLog`'a yazar. Yeni bir
`AutomationController`, node ekleme/silme/düzenleme için throttle'a girmeyen
CRUD sağlar. `useAutomationStore.ts` iyimser kalır; arka planda gerçek API'yi
çağırır ve yeni bir `loadRules` ile açılışta sunucudan hidratlanır.

**Tech Stack:** ASP.NET Core, EF Core (Code-First migration), `System.Threading.Channels`,
xUnit + `Microsoft.EntityFrameworkCore.InMemory` (mevcut desen), Next.js/TypeScript,
Zustand, Vitest.

**Spec:** [docs/superpowers/specs/2026-09-14-namines-flow-design.md](../specs/2026-09-14-namines-flow-design.md)
(Bölüm 3 + "Bölüm 3 Eki" bölümleri — bu plan onları birebir uygular)

## Global Constraints

- Sunucu istemcinin "şunu yaptım" iddiasına GÜVENMEZ — diff'i her zaman
  kendisi, `oldSchema`/`newSchema`'dan hesaplar.
- Bir kuralın aksiyonu başarısız olursa (webhook 5xx, AI hatası, kota
  reddi) diğer kurallar ve `SyncProjects`'in kendi yanıtı ASLA etkilenmez —
  her kural kendi try/catch'i içinde çalışır.
- `DbaCheck`/`SeedData` her çalıştırmadan önce `AiQuotaService.TryReserveAsync`
  ile kota rezerve eder; reddedilirse kural atlanır (`Status=Skipped`),
  hata fırlatılmaz.
- Webhook URL'i HER çalıştırmada `SsrfGuard.IsUrlSafe` ile yeniden doğrulanır
  (DNS rebinding — kaydedilmiş bir URL zamanla farklı bir IP'ye çözülebilir).
- `AutomationRule`/`AutomationRunLog` → `CloudProjects` FK'ı `OnDelete(DeleteBehavior.Cascade)`
  (proje silinince kuralları/loglar da silinir) — `Branch.cs`'teki mevcut
  Fluent API deseniyle birebir aynı stil.
- Kuyruk SINGLETON, işçi KENDİ DI kapsamını açar (`IServiceScopeFactory`) —
  istek kapsamındaki `DbContext`'e arka planda dokunmak `ObjectDisposedException`
  demek.
- `Toast` aksiyonu sunucuda HİÇ işlenmez — yalnızca istemci `naminesFlow`
  event bus'ında dinlenir.
- Örnek veri (`SeedData`) v1'de otomatik veritabanına YAZILMAZ — yalnızca
  üretilip `AutomationRunLog.ResultSummary`'ye kaydedilir.
- `useAutomationStore.ts`'in dışa açık imzaları (satır 11-29) DEĞİŞMEZ —
  yalnızca gövdeleri arka planda API çağırır ve yeni `loadRules` eklenir.

---

### Task 1: `AutomationRule`/`AutomationRunLog` modelleri ve migration

**Files:**
- Create: `backend/Namines.Core/Models/AutomationRule.cs`
- Modify: `backend/Namines.Infrastructure/Data/AuthDbContext.cs`
- Create: EF Core migration (dotnet-ef ile üretilir, elle yazılmaz)
- Test: `backend/Namines.Tests/Data/AutomationRuleContextTests.cs`

**Interfaces:**
- Produces: `AutomationRule` (Id, ProjectId, ScopeTableId, TriggerType,
  ActionType, ActionConfigJson, Enabled, CreatedAt), `AutomationRunLog`
  (Id, RuleId, TriggeredAt, Status, ErrorMessage, ResultSummary) — Task 2+
  bunları tüketir.

- [ ] **Step 1: Modelleri yaz**

`backend/Namines.Core/Models/AutomationRule.cs`:

```csharp
using System;

namespace Namines.Core.Models;

/// <summary>
/// Namines Flow kuralının sunucu tarafı kaydı (Bölüm 3).
///
/// İstemcideki <c>useAutomationStore.ts</c>'in kalıcı karşılığı — ScopeTableId
/// istemcinin stabil <c>SchemaTable.Id</c>'sini tutar, TABLO ADINI DEĞİL
/// (bkz. spec "Bölüm 3 Eki #2" — SchemaDiffResult ada göre çalışır, eşleştirme
/// bu ikisini AutomationRuleMatcher içinde birbirine çevirir).
/// </summary>
public class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Null = proje genelinde. Doluysa, o TABLO'ya ait diff girdileriyle sınırlı.</summary>
    public string? ScopeTableId { get; set; }

    /// <summary>"TableAdded" | "TableDeleted" | "ColumnAdded" | "ColumnDeleted" | "ColumnChanged" | "RelationAdded" | "RelationDeleted"</summary>
    public string TriggerType { get; set; } = string.Empty;

    /// <summary>"Webhook" | "DbaCheck" | "SeedData" | "Toast"</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>Örn. {"url": "https://..."}. Serbest JSON — aksiyon tipine göre yorumlanır.</summary>
    public string ActionConfigJson { get; set; } = "{}";

    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Bir kuralın tek bir çalıştırmasının kaydı — teşhis ve kullanıcıya sonuç göstermek için.</summary>
public class AutomationRunLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RuleId { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;

    /// <summary>"Success" | "Failed" | "Skipped"</summary>
    public string Status { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    /// <summary>DBA/seed sonucunun kısa özeti. Webhook/Toast için null.</summary>
    public string? ResultSummary { get; set; }
}
```

- [ ] **Step 2: `AuthDbContext`'e kaydet**

`AuthDbContext.cs`'e `DbSet` ekle (mevcut `Branches` satırının yanına):

```csharp
public DbSet<Namines.Core.Models.AutomationRule> AutomationRules { get; set; } = null!;
public DbSet<Namines.Core.Models.AutomationRunLog> AutomationRunLogs { get; set; } = null!;
```

`OnModelCreating` içine, `Branch`/`SchemaVersion` bloklarının hemen ardından
(Branch.cs'teki FK deseniyle BİREBİR aynı stil):

```csharp
// ── Namines Flow — AutomationRule / AutomationRunLog (Bölüm 3) ──

builder.Entity<Namines.Core.Models.AutomationRule>()
    .HasOne<CloudProject>()
    .WithMany()
    .HasForeignKey(r => r.ProjectId)
    .OnDelete(DeleteBehavior.Cascade);

builder.Entity<Namines.Core.Models.AutomationRule>()
    .HasIndex(r => new { r.ProjectId, r.Enabled });

builder.Entity<Namines.Core.Models.AutomationRunLog>()
    .HasOne<Namines.Core.Models.AutomationRule>()
    .WithMany()
    .HasForeignKey(l => l.RuleId)
    .OnDelete(DeleteBehavior.Cascade);

builder.Entity<Namines.Core.Models.AutomationRunLog>()
    .HasIndex(l => l.RuleId);
```

- [ ] **Step 3: Failing test yaz (kayıt + cascade delete doğrulanıyor)**

`backend/Namines.Tests/Data/AutomationRuleContextTests.cs` (mevcut
in-memory `AuthDbContext` test deseniyle — `VaultJobQueueTests.cs`'in
kardeşi, `Namines.Tests` içindeki başka bir `*ContextTests.cs`'e bakarak
DbContextOptions kurulumunu birebir kopyala):

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Xunit;

namespace Namines.Tests.Data;

public class AutomationRuleContextTests
{
    private static AuthDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    private static async Task<CloudProject> SeedProjectAsync(AuthDbContext db)
    {
        var project = new CloudProject
        {
            Id = Guid.NewGuid().ToString(),
            Name = "p",
            DbType = "PostgreSQL",
            SchemaJson = "{}",
            NodePositionsJson = "{}",
            UserId = "u1",
        };
        db.CloudProjects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    [Fact]
    public async Task Kural_kaydedilip_geri_okunabiliyor()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db);

        db.AutomationRules.Add(new AutomationRule
        {
            ProjectId = project.Id,
            ScopeTableId = "t1",
            TriggerType = "TableDeleted",
            ActionType = "Webhook",
            ActionConfigJson = "{\"url\":\"https://example.com/hook\"}",
        });
        await db.SaveChangesAsync();

        var saved = await db.AutomationRules.SingleAsync();
        Assert.Equal("TableDeleted", saved.TriggerType);
        Assert.True(saved.Enabled);
    }

    [Fact]
    public async Task Proje_silinince_kurallari_ve_loglari_da_siliniyor()
    {
        await using var db = NewContext();
        var project = await SeedProjectAsync(db);

        var rule = new AutomationRule { ProjectId = project.Id, TriggerType = "TableAdded", ActionType = "Toast" };
        db.AutomationRules.Add(rule);
        await db.SaveChangesAsync();

        db.AutomationRunLogs.Add(new AutomationRunLog { RuleId = rule.Id, Status = "Success" });
        await db.SaveChangesAsync();

        db.CloudProjects.Remove(project);
        await db.SaveChangesAsync();

        Assert.Empty(db.AutomationRules);
        Assert.Empty(db.AutomationRunLogs);
    }
}
```

- [ ] **Step 4: Testi çalıştırıp FAIL ettiğini doğrula**

Run: `dotnet test backend/Namines.Tests --filter AutomationRuleContextTests`
Expected: derleme hatası (modeller/DbSet henüz yoksa) veya `FAIL` — bu adımı
Step 1-2'den ÖNCE çalıştırıp gerçekten kırıldığını görmek istiyorsan
Step 1-2'yi geçici olarak atla; aksi halde bu adım modelleri yazdıktan sonra
zaten PASS eder, o da kabul edilebilir (model+context tek bir birim).

- [ ] **Step 5: Testi çalıştırıp PASS ettiğini doğrula**

Run: `dotnet test backend/Namines.Tests --filter AutomationRuleContextTests -v normal`
Expected: 2/2 PASS

- [ ] **Step 6: EF Core migration üret**

```bash
cd backend/Namines.API
dotnet ef migrations add AddNaminesFlowAutomation --project ../Namines.Infrastructure --startup-project .
```

Üretilen migration dosyasını gözden geçir: `AutomationRules` ve
`AutomationRunLogs` tablolarının, `CascadeDelete` FK'larının ve
Step 2'deki indexlerin doğru üretildiğini doğrula.

- [ ] **Step 7: Commit**

```bash
git add backend/Namines.Core/Models/AutomationRule.cs backend/Namines.Infrastructure/Data/AuthDbContext.cs backend/Namines.Tests/Data/AutomationRuleContextTests.cs backend/Namines.Infrastructure/Migrations/
git commit -m "feat: add AutomationRule/AutomationRunLog models and migration"
```

---

### Task 2: `AutomationRuleMatcher` — saf eşleştirme mantığı

**Files:**
- Create: `backend/Namines.Core/Analysis/AutomationRuleMatcher.cs`
- Test: `backend/Namines.Tests/Analysis/AutomationRuleMatcherTests.cs`

**Interfaces:**
- Consumes: `SchemaDiffResult` (`AddedTables`, `RemovedTables`,
  `ModifiedTables[].TableName/.AddedColumns/.RemovedColumns/.ModifiedColumns`,
  `AddedRelations`, `RemovedRelations` — `Namines.Core.Models.SchemaDiffResult`),
  `DatabaseSchema.Tables` (`Id`, `Name` — `oldSchema`/`newSchema`'dan
  Id→Name sözlüğü kurmak için), `AutomationRule` listesi.
- Produces: `IReadOnlyList<AutomationRule> Match(SchemaDiffResult diff,
  DatabaseSchema oldSchema, DatabaseSchema newSchema, IReadOnlyList<AutomationRule> rules)`
  — Task 4 (`AutomationExecutor`) bunu tüketir.

**Neden ayrı, saf bir sınıf:** Spec Bölüm 4 test stratejisi açıkça bunu
istiyor — "gerçek DB/HTTP gerektirmeden birim testlerle kilitlenir". Ayrıca
spec eki #2'deki tablo-id↔ad çevirisi burada, tek bir yerde çözülüyor;
`AutomationExecutor`'ın kendisi bu detayı hiç bilmiyor.

- [ ] **Step 1: Failing test yaz**

```csharp
using System.Collections.Generic;
using Namines.Core.Analysis;
using Namines.Core.Models;
using Xunit;

namespace Namines.Tests.Analysis;

public class AutomationRuleMatcherTests
{
    private static DatabaseSchema SchemaWith(params (string Id, string Name)[] tables)
    {
        var schema = new DatabaseSchema();
        foreach (var (id, name) in tables)
            schema.Tables.Add(new SchemaTable { Id = id, Name = name });
        return schema;
    }

    [Fact]
    public void Tablo_silinince_scopeTableId_ile_eslesen_kural_tetikleniyor()
    {
        var oldSchema = SchemaWith(("t1", "orders"));
        var newSchema = SchemaWith();
        var diff = new SchemaDiffResult { RemovedTables = { "orders" } };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = "t1", TriggerType = "TableDeleted", ActionType = "Webhook", Enabled = true },
        };

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);

        Assert.Single(matched);
        Assert.Equal("r1", matched[0].Id);
    }

    [Fact]
    public void Baska_tabloya_ait_kural_tetiklenmiyor()
    {
        var oldSchema = SchemaWith(("t1", "orders"), ("t2", "users"));
        var newSchema = SchemaWith(("t2", "users"));
        var diff = new SchemaDiffResult { RemovedTables = { "orders" } };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = "t2", TriggerType = "TableDeleted", ActionType = "Webhook", Enabled = true },
        };

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);

        Assert.Empty(matched);
    }

    [Fact]
    public void Devre_disi_kural_tetiklenmiyor()
    {
        var oldSchema = SchemaWith(("t1", "orders"));
        var newSchema = SchemaWith();
        var diff = new SchemaDiffResult { RemovedTables = { "orders" } };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = "t1", TriggerType = "TableDeleted", ActionType = "Webhook", Enabled = false },
        };

        Assert.Empty(AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules));
    }

    [Fact]
    public void Proje_geneli_kural_scopeTableId_null_herhangi_bir_tablo_eklenince_tetikleniyor()
    {
        var oldSchema = SchemaWith();
        var newSchema = SchemaWith(("t1", "orders"));
        var diff = new SchemaDiffResult { AddedTables = { "orders" } };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = null, TriggerType = "TableAdded", ActionType = "Toast", Enabled = true },
        };

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);

        Assert.Single(matched);
    }

    [Fact]
    public void Kolon_eklenince_tabloya_bagli_kural_tetikleniyor()
    {
        var oldSchema = SchemaWith(("t1", "orders"));
        var newSchema = SchemaWith(("t1", "orders"));
        var diff = new SchemaDiffResult
        {
            ModifiedTables = { new TableDiffDetail { TableName = "orders", AddedColumns = { "total" } } },
        };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = "t1", TriggerType = "ColumnAdded", ActionType = "DbaCheck", Enabled = true },
        };

        Assert.Single(AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules));
    }

    [Fact]
    public void Farkli_tetikleyici_tipindeki_kural_eslesmiyor()
    {
        var oldSchema = SchemaWith(("t1", "orders"));
        var newSchema = SchemaWith();
        var diff = new SchemaDiffResult { RemovedTables = { "orders" } };
        var rules = new List<AutomationRule>
        {
            new() { Id = "r1", ScopeTableId = "t1", TriggerType = "TableAdded", ActionType = "Webhook", Enabled = true },
        };

        Assert.Empty(AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules));
    }
}
```

- [ ] **Step 2: Testleri çalıştırıp FAIL ettiğini doğrula**

Run: `dotnet test backend/Namines.Tests --filter AutomationRuleMatcherTests`
Expected: derleme hatası (`AutomationRuleMatcher` yok)

- [ ] **Step 3: `AutomationRuleMatcher`'ı yaz**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// Bir <see cref="SchemaDiffResult"/>'ı etkin <see cref="AutomationRule"/>'larla
/// eşleştiren saf, deterministik fonksiyon (Bölüm 3 Eki #2).
///
/// <b>Neden Id→Name sözlüğü:</b> <see cref="SchemaDiffResult"/> tabloları AD ile
/// tanımlar (<c>SchemaTable.Name</c>), <see cref="AutomationRule.ScopeTableId"/>
/// ise istemcinin stabil <c>SchemaTable.Id</c>'sini tutar. Silinen bir tablo
/// yalnızca <paramref name="oldSchema"/>'da, eklenen bir tablo yalnızca
/// <paramref name="newSchema"/>'da bulunur — bu yüzden sözlük İKİSİNİN
/// BİRLEŞİMİNDEN kurulur, tek taraflı olsaydı silinen tabloya bağlı kurallar
/// hiç bulunamazdı.
/// </summary>
public static class AutomationRuleMatcher
{
    public static IReadOnlyList<AutomationRule> Match(
        SchemaDiffResult diff,
        DatabaseSchema oldSchema,
        DatabaseSchema newSchema,
        IReadOnlyList<AutomationRule> rules)
    {
        var idToName = new Dictionary<string, string>();
        foreach (var t in oldSchema.Tables) idToName[t.Id] = t.Name;
        foreach (var t in newSchema.Tables) idToName[t.Id] = t.Name;

        var addedTableNames = new HashSet<string>(diff.AddedTables, StringComparer.Ordinal);
        var removedTableNames = new HashSet<string>(diff.RemovedTables, StringComparer.Ordinal);
        var addedColumnTables = new HashSet<string>(
            diff.ModifiedTables.Where(m => m.AddedColumns.Count > 0).Select(m => m.TableName), StringComparer.Ordinal);
        var removedColumnTables = new HashSet<string>(
            diff.ModifiedTables.Where(m => m.RemovedColumns.Count > 0).Select(m => m.TableName), StringComparer.Ordinal);
        var changedColumnTables = new HashSet<string>(
            diff.ModifiedTables.Where(m => m.ModifiedColumns.Count > 0).Select(m => m.TableName), StringComparer.Ordinal);
        var hasRelationAdded = diff.AddedRelations.Count > 0;
        var hasRelationRemoved = diff.RemovedRelations.Count > 0;

        bool Matches(AutomationRule rule)
        {
            if (!rule.Enabled) return false;

            // Proje geneli kural (ScopeTableId == null): hangi tablo olduğuna
            // bakmadan, o TÜR olayın hiç olup olmadığına bakar.
            var scopeName = rule.ScopeTableId is null
                ? null
                : idToName.TryGetValue(rule.ScopeTableId, out var n) ? n : null;

            // ScopeTableId dolu ama sözlükte yoksa (hiç var olmamış bir tablo id'si)
            // eşleşme imkansız.
            if (rule.ScopeTableId is not null && scopeName is null) return false;

            bool InScope(HashSet<string> tableNames) =>
                scopeName is null ? tableNames.Count > 0 : tableNames.Contains(scopeName);

            return rule.TriggerType switch
            {
                "TableAdded" => InScope(addedTableNames),
                "TableDeleted" => InScope(removedTableNames),
                "ColumnAdded" => InScope(addedColumnTables),
                "ColumnDeleted" => InScope(removedColumnTables),
                "ColumnChanged" => InScope(changedColumnTables),
                "RelationAdded" => scopeName is null && hasRelationAdded,
                "RelationDeleted" => scopeName is null && hasRelationRemoved,
                _ => false,
            };
        }

        return rules.Where(Matches).ToList();
    }
}
```

Not: `RelationAdded`/`RelationDeleted` için `scopeName is null` şartı
bilinçli — `SchemaRelation`'ın hangi tabloya ait olduğunu diff bu seviyede
ayırt etmiyor (yalnızca `AddedRelations`/`RemovedRelations` listesi var),
bu yüzden v1'de ilişki tetikleyicileri yalnızca PROJE GENELİ kurallarda
desteklenir — tabloya özel bir ilişki kuralı asla tetiklenmez. Bu, spec'in
"Kapsam Dışı" bölümündeki "kolon/ilişki tetikleyicileri için ayrı görsel
gösterim yok" kararıyla tutarlı bir sınırlama; ayrıca bir spec notu olarak
eklenmeli ama bu planın kapsamında kod davranışını değiştirmiyor.

- [ ] **Step 4: Testleri çalıştırıp PASS ettiğini doğrula**

Run: `dotnet test backend/Namines.Tests --filter AutomationRuleMatcherTests -v normal`
Expected: 6/6 PASS

- [ ] **Step 5: Commit**

```bash
git add backend/Namines.Core/Analysis/AutomationRuleMatcher.cs backend/Namines.Tests/Analysis/AutomationRuleMatcherTests.cs
git commit -m "feat: add pure AutomationRuleMatcher for diff-to-rule matching"
```

---

### Task 3: `IAutomationJobQueue` — bounded iş kuyruğu

**Files:**
- Create: `backend/Namines.Infrastructure/Services/AutomationJobQueue.cs`
- Test: `backend/Namines.Tests/Services/AutomationJobQueueTests.cs`

**Interfaces:**
- Produces: `AutomationJob(string ProjectId, SchemaDiffResult Diff, DatabaseSchema OldSchema, DatabaseSchema NewSchema)`,
  `IAutomationJobQueue.TryEnqueue(AutomationJob)`, `DequeueAsync(CancellationToken)`,
  `PendingCount` — Task 4 (`AutomationExecutorWorker`) ve Task 5
  (`AuthController`) bunu tüketir.

**Neden `VaultJobQueue`'dan farklı davranıyor:** Vault kuyruğu dolunca
istek 503 döner (kullanıcı "şimdi meşgulüz" görür). Otomasyon tetikleme
`SyncProjects`'in ASIL işi değil, yan etkisi — kuyruk dolarsa istek yine
`200 OK` dönmeli, iş sessizce (ama LOGLANARAK) düşürülmeli. Bu yüzden
`TryEnqueue`'un false dönmesi `AuthController`'da hataya değil, bir
`ILogger.LogWarning`'e yol açacak (Task 5).

- [ ] **Step 1: Failing test yaz**

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

public class AutomationJobQueueTests
{
    private static AutomationJob Job(string projectId) =>
        new(projectId, new SchemaDiffResult(), new DatabaseSchema(), new DatabaseSchema());

    [Fact]
    public async Task Kuyruga_eklenen_is_sirayla_geri_aliniyor()
    {
        var queue = new AutomationJobQueue();

        Assert.True(queue.TryEnqueue(Job("p1")));
        Assert.True(queue.TryEnqueue(Job("p2")));

        Assert.Equal("p1", (await queue.DequeueAsync(CancellationToken.None)).ProjectId);
        Assert.Equal("p2", (await queue.DequeueAsync(CancellationToken.None)).ProjectId);
    }

    [Fact]
    public void Kuyruk_dolunca_BEKLEMEDEN_reddediyor()
    {
        var queue = new AutomationJobQueue();

        var accepted = Enumerable.Range(0, 100).Count(i => queue.TryEnqueue(Job($"p{i}")));

        Assert.True(accepted < 100);
        Assert.False(queue.TryEnqueue(Job("bir-fazla")));
    }

    [Fact]
    public void Null_is_reddediliyor()
    {
        var queue = new AutomationJobQueue();
        Assert.Throws<ArgumentNullException>(() => queue.TryEnqueue(null!));
    }
}
```

- [ ] **Step 2: Testi çalıştırıp FAIL ettiğini doğrula**

Run: `dotnet test backend/Namines.Tests --filter AutomationJobQueueTests`
Expected: derleme hatası

- [ ] **Step 3: `AutomationJobQueue`'yu yaz**

`VaultJobQueue.cs`'in birebir kopyası, tip ve kapasite farklı:

```csharp
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Namines.Core.Models;

namespace Namines.Infrastructure.Services;

/// <summary>Kuyruğa alınmış bir otomasyon-tetikleme işi.</summary>
public sealed record AutomationJob(
    string ProjectId, SchemaDiffResult Diff, DatabaseSchema OldSchema, DatabaseSchema NewSchema);

/// <summary>
/// Namines Flow'un sunucu tarafı aksiyon kuyruğu (Bölüm 3).
///
/// <see cref="VaultJobQueue"/> ile aynı gerekçeler (istek kapsamlı DbContext,
/// Task.Run yerine kuyruk) — TEK FARK: kuyruk dolduğunda çağıran BEKLEMEDEN
/// false alır ve bunu SESSİZCE loglar, isteği 503'e ÇEVİRMEZ. Otomasyon
/// tetikleme, senkron isteğin (proje kaydetme) yan etkisidir — asıl iş kuyruk
/// yüzünden asla başarısız görünmemeli.
/// </summary>
public interface IAutomationJobQueue
{
    bool TryEnqueue(AutomationJob job);
    ValueTask<AutomationJob> DequeueAsync(CancellationToken ct);
    int PendingCount { get; }
}

public sealed class AutomationJobQueue : IAutomationJobQueue
{
    private const int Capacity = 64;

    private readonly Channel<AutomationJob> _channel =
        Channel.CreateBounded<AutomationJob>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    private int _pending;

    public int PendingCount => Volatile.Read(ref _pending);

    public bool TryEnqueue(AutomationJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!_channel.Writer.TryWrite(job)) return false;
        Interlocked.Increment(ref _pending);
        return true;
    }

    public async ValueTask<AutomationJob> DequeueAsync(CancellationToken ct)
    {
        var job = await _channel.Reader.ReadAsync(ct);
        Interlocked.Decrement(ref _pending);
        return job;
    }
}
```

- [ ] **Step 4: Testi çalıştırıp PASS ettiğini doğrula**

Run: `dotnet test backend/Namines.Tests --filter AutomationJobQueueTests -v normal`
Expected: 3/3 PASS

- [ ] **Step 5: Commit**

```bash
git add backend/Namines.Infrastructure/Services/AutomationJobQueue.cs backend/Namines.Tests/Services/AutomationJobQueueTests.cs
git commit -m "feat: add bounded AutomationJobQueue mirroring VaultJobQueue"
```

---

### Task 4: `AutomationExecutor` + `AutomationExecutorWorker`

**Files:**
- Create: `backend/Namines.Infrastructure/Services/AutomationExecutor.cs`
- Create: `backend/Namines.Infrastructure/Services/AutomationExecutorWorker.cs`
- Test: `backend/Namines.Tests/Services/AutomationExecutorTests.cs`

**Interfaces:**
- Consumes: `AutomationRuleMatcher.Match(...)` (Task 2), `IAutomationJobQueue` (Task 3),
  `AiQuotaService.TryReserveAsync(userId, estimatedTokens, ct) : Task<AiQuotaDecision>`,
  `GroqAIService.AnalyzeSchemaDbaAsync(schema, dbType) : Task<List<DbaIssue>>`,
  `IAIService.GenerateMockDataAsync(schema) : Task<string>`,
  `SsrfGuard.IsUrlSafe(url) : bool`, `IHttpClientFactory.CreateClient("AutomationWebhook")`.
- Produces: `IAutomationExecutor.RunAsync(string projectId, SchemaDiffResult diff,
  DatabaseSchema oldSchema, DatabaseSchema newSchema, CancellationToken ct)` —
  Task 5 (`AuthController`, kuyruk üzerinden dolaylı) bunu tüketir.

**Interface + implementasyon aynı adımda tanımlanıyor** çünkü bu görev DI
kayıtlı somut bir servis (`BackgroundService` onu enjekte edecek), test ise
`IAiUsageTracker`/`AiQuotaService` gibi bağımlılıkları sahte (fake)
implementasyonlarla değiştirerek çalışacak.

- [ ] **Step 1: Failing test yaz — kota reddi kuralı atlıyor, diğerini engellemiyor**

```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

public class AutomationExecutorTests
{
    private static AuthDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(System.Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    private static async Task<(AuthDbContext Db, CloudProject Project)> SeedAsync(params AutomationRule[] rules)
    {
        var db = NewContext();
        var project = new CloudProject
        {
            Id = "proj-1", Name = "p", DbType = "PostgreSQL",
            SchemaJson = "{}", NodePositionsJson = "{}", UserId = "user-1",
        };
        db.CloudProjects.Add(project);
        foreach (var r in rules) { r.ProjectId = project.Id; db.AutomationRules.Add(r); }
        await db.SaveChangesAsync();
        return (db, project);
    }

    [Fact]
    public async Task Webhook_kurali_guvenli_olmayan_URL_icin_atlaniyor_ve_loglaniyor()
    {
        var (db, project) = await SeedAsync(new AutomationRule
        {
            ScopeTableId = null, TriggerType = "TableAdded", ActionType = "Webhook",
            ActionConfigJson = "{\"url\":\"http://127.0.0.1/hook\"}", Enabled = true,
        });

        var executor = new AutomationExecutor(
            db, new StubQuota(Core.Data.AiQuotaDecision.Allowed),
            groqDba: null!, aiService: null!,
            httpClientFactory: new StubHttpClientFactory(HttpStatusCode.OK),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationExecutor>.Instance);

        var diff = new SchemaDiffResult { AddedTables = { "orders" } };
        var schema = new DatabaseSchema { Tables = { new SchemaTable { Id = "t1", Name = "orders" } } };

        await executor.RunAsync(project.Id, diff, new DatabaseSchema(), schema, CancellationToken.None);

        var log = await db.AutomationRunLogs.SingleAsync();
        Assert.Equal("Skipped", log.Status);
    }

    [Fact]
    public async Task Bir_kuralin_basarisizligi_digerini_engellemiyor()
    {
        var (db, project) = await SeedAsync(
            new AutomationRule { ScopeTableId = null, TriggerType = "TableAdded", ActionType = "Webhook", ActionConfigJson = "{\"url\":\"https://example.com/hook\"}", Enabled = true },
            new AutomationRule { ScopeTableId = null, TriggerType = "TableAdded", ActionType = "Toast", Enabled = true });

        var executor = new AutomationExecutor(
            db, new StubQuota(Core.Data.AiQuotaDecision.Allowed),
            groqDba: null!, aiService: null!,
            httpClientFactory: new StubHttpClientFactory(HttpStatusCode.InternalServerError),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationExecutor>.Instance);

        var diff = new SchemaDiffResult { AddedTables = { "orders" } };
        var schema = new DatabaseSchema { Tables = { new SchemaTable { Id = "t1", Name = "orders" } } };

        // Patlamamalı — Toast sunucuda hiç işlenmediği için tek log satırı
        // (Webhook) beklenir, Toast için log YAZILMAZ.
        await executor.RunAsync(project.Id, diff, new DatabaseSchema(), schema, CancellationToken.None);

        var logs = await db.AutomationRunLogs.ToListAsync();
        Assert.Single(logs);
        Assert.Equal("Failed", logs[0].Status);
    }
}
```

Test dosyasının başında iki küçük yardımcı sınıf gerekir (aynı dosyanın
altına, `internal` olarak eklenebilir):

```csharp
internal sealed class StubQuota : AiQuotaService
{
    // NOT: AiQuotaService sealed olduğu için doğrudan alt sınıf çıkarılamaz —
    // bu, Step 3'te AutomationExecutor'ın AiQuotaService yerine dar bir
    // arayüz (IQuotaReserver gibi) beklemesi gerektiğini gösteren bir
    // TASARIM SİNYALİDİR. Aşağıdaki Step 3'te bu netleştirilmiştir.
}
```

**Not (kritik, Step 1'i yazarken karşına çıkacak):** `AiQuotaService`
`sealed class` (bkz. `AiQuotaService.cs:69`) — doğrudan sahte alt sınıf
üretilemez. Bu yüzden `AutomationExecutor`, somut `AiQuotaService` yerine
yeni, dar bir arayüz bekleyecek: `IAiQuotaReserver { Task<AiQuotaDecision>
TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct); }`.
Bunu Step 2'de tanımla, `AiQuotaService`'e `: IAiQuotaReserver` ekle (tek
satır, mevcut davranışı değiştirmez), test içinde sahte bir
`IAiQuotaReserver` implementasyonu (`StubQuota`) kullan. Yukarıdaki
`internal sealed class StubQuota : AiQuotaService` satırını SİL, yerine:

```csharp
internal sealed class StubQuota : Namines.Core.Interfaces.IAiQuotaReserver
{
    private readonly AiQuotaDecision _decision;
    public StubQuota(AiQuotaDecision decision) => _decision = decision;
    public Task<AiQuotaDecision> TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct)
        => Task.FromResult(_decision);
}

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpStatusCode _status;
    public StubHttpClientFactory(HttpStatusCode status) => _status = status;
    public HttpClient CreateClient(string name) =>
        new(new StubHandler(_status)) { Timeout = System.TimeSpan.FromSeconds(5) };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        public StubHandler(HttpStatusCode status) => _status = status;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_status));
    }
}
```

- [ ] **Step 2: `IAiQuotaReserver` arayüzünü çıkar**

`backend/Namines.Core/Interfaces/IAiQuotaReserver.cs` (yeni):

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Core.Interfaces;

/// <summary>
/// <c>AiQuotaService.TryReserveAsync</c>'in dar arayüzü — yalnızca
/// <c>AutomationExecutor</c>'ın ihtiyaç duyduğu tek metod (Bölüm 3 Eki #1).
/// <c>AiQuotaService</c> sealed olduğu için testte sahte bir kota kararı
/// vermek doğrudan alt sınıflamayla mümkün değil; bu arayüz o boşluğu kapatır.
/// </summary>
public interface IAiQuotaReserver
{
    Task<Namines.Core.Data.AiQuotaDecision> TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct = default);
}
```

**Dikkat — `AiQuotaDecision` namespace kontrolü:** Yukarıdaki mevcut kod
taramasında `AiQuotaDecision` enum'ı `Namines.Infrastructure.Data`
namespace'inde tanımlı (`AiQuotaService.cs` dosyasının başı,
`namespace Namines.Infrastructure.Data;`), `Namines.Core.Data` DEĞİL.
`IAiQuotaReserver`, `Namines.Core` projesindeyken `AiQuotaDecision`
`Namines.Infrastructure`'da olduğu için bağımlılık yönü TERS düşer
(`Core`, `Infrastructure`'a bağımlı olamaz — mevcut proje referansları
tersini varsayıyor, `Infrastructure` `Core`'a bağımlı). Bu, arayüzü
`Namines.Core.Interfaces` yerine `Namines.Infrastructure.Services`
içine, `AutomationExecutor` ile AYNI ad alanına koymayı gerektirir —
`AiQuotaService`'in zaten yaşadığı proje. Yukarıdaki dosyayı SİL, yerine:

`backend/Namines.Infrastructure/Services/IAiQuotaReserver.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

/// <summary>
/// <see cref="AiQuotaService.TryReserveAsync"/>'in dar arayüzü — yalnızca
/// <see cref="AutomationExecutor"/>'ın ihtiyaç duyduğu tek metod (Bölüm 3 Eki #1).
/// <see cref="AiQuotaService"/> sealed olduğu için testte sahte bir kota
/// kararı vermek doğrudan alt sınıflamayla mümkün değil; bu arayüz o boşluğu
/// kapatır. Aynı projede (Infrastructure) tanımlı çünkü AiQuotaDecision de
/// burada yaşıyor — Core projesine taşımak gereksiz bir bağımlılık ters
/// çevirmesi olurdu.
/// </summary>
public interface IAiQuotaReserver
{
    Task<AiQuotaDecision> TryReserveAsync(string userId, int estimatedTokens, CancellationToken ct = default);
}
```

`AiQuotaService.cs`'te sınıf tanımına ekle (`AiQuotaService.cs:69` civarı):

```csharp
public sealed class AiQuotaService : IAiQuotaReserver
```

(Mevcut hiçbir üye imzası değişmiyor — `TryReserveAsync` zaten bu imzaya sahip.)

Test dosyasındaki `StubQuota`'yı `Namines.Core.Interfaces.IAiQuotaReserver`
yerine `Namines.Infrastructure.Services.IAiQuotaReserver` ve
`Namines.Infrastructure.Data.AiQuotaDecision` kullanacak şekilde düzelt.

- [ ] **Step 3: Testleri çalıştırıp FAIL ettiğini doğrula**

Run: `dotnet test backend/Namines.Tests --filter AutomationExecutorTests`
Expected: derleme hatası (`AutomationExecutor` yok)

- [ ] **Step 4: `AutomationExecutor`'ı yaz**

```csharp
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Security;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

public interface IAutomationExecutor
{
    Task RunAsync(string projectId, SchemaDiffResult diff, DatabaseSchema oldSchema, DatabaseSchema newSchema, CancellationToken ct = default);
}

/// <summary>
/// Namines Flow'un sunucu tarafı aksiyonlarını çalıştırır (Bölüm 3).
///
/// Bir kuralın aksiyonu başarısız olursa (webhook 5xx, AI hatası, kota reddi)
/// DİĞER KURALLAR etkilenmez — her kural kendi try/catch'i içinde çalışır ve
/// sonucu <see cref="AutomationRunLog"/>'a yazar.
/// </summary>
public sealed class AutomationExecutor : IAutomationExecutor
{
    /// <summary>DBA/seed için sabit tahmin — SchemaController.SchemaRoundTokenEstimate ile aynı büyüklük mertebesi.</summary>
    private const int AutomationTokenEstimate = 2500;

    private readonly AuthDbContext _db;
    private readonly IAiQuotaReserver _quota;
    private readonly GroqAIService _groqDba;
    private readonly IAIService _aiService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AutomationExecutor> _logger;

    public AutomationExecutor(
        AuthDbContext db, IAiQuotaReserver quota, GroqAIService groqDba, IAIService aiService,
        IHttpClientFactory httpClientFactory, ILogger<AutomationExecutor> logger)
    {
        _db = db;
        _quota = quota;
        _groqDba = groqDba;
        _aiService = aiService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task RunAsync(string projectId, SchemaDiffResult diff, DatabaseSchema oldSchema, DatabaseSchema newSchema, CancellationToken ct = default)
    {
        var project = await _db.CloudProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return; // Proje bu arada silinmiş olabilir — iş düşer.

        var rules = await _db.AutomationRules.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.Enabled).ToListAsync(ct);
        if (rules.Count == 0) return;

        var matched = AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);
        if (matched.Count == 0) return;

        var engine = Enum.TryParse<DatabaseType>(project.DbType, ignoreCase: true, out var parsedEngine)
            ? parsedEngine : DatabaseType.PostgreSQL;

        foreach (var rule in matched)
        {
            // "Toast" sunucuda HİÇ işlenmiyor — istemci kendi event bus'ından
            // dinliyor. Log dahi yazılmıyor: sunucunun hiç bilmediği bir şeyin
            // "çalıştı" kaydı tutması yanıltıcı olurdu.
            if (rule.ActionType == "Toast") continue;

            try
            {
                await RunOneAsync(rule, project.UserId, newSchema, engine, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Namines Flow: {RuleId} kurali calistirilirken beklenmeyen hata.", rule.Id);
                await LogAsync(rule.Id, "Failed", ex.Message, null, ct);
            }
        }
    }

    private async Task RunOneAsync(AutomationRule rule, string userId, DatabaseSchema schema, DatabaseType engine, CancellationToken ct)
    {
        switch (rule.ActionType)
        {
            case "Webhook":
                await RunWebhookAsync(rule, ct);
                return;
            case "DbaCheck":
                await RunDbaCheckAsync(rule, userId, schema, engine, ct);
                return;
            case "SeedData":
                await RunSeedDataAsync(rule, userId, schema, ct);
                return;
            default:
                await LogAsync(rule.Id, "Failed", $"Unknown action type: {rule.ActionType}", null, ct);
                return;
        }
    }

    private async Task RunWebhookAsync(AutomationRule rule, CancellationToken ct)
    {
        var url = ExtractUrl(rule.ActionConfigJson);

        // HER ÇALIŞTIRMADA yeniden doğrulanıyor — kaydedilen bir URL zamanla
        // farklı bir IP'ye çözülebilir (DNS rebinding).
        if (url is null || !SsrfGuard.IsUrlSafe(url))
        {
            await LogAsync(rule.Id, "Skipped", "Webhook URL is missing or not a safe public target.", null, ct);
            return;
        }

        var client = _httpClientFactory.CreateClient("AutomationWebhook");
        var response = await client.PostAsJsonAsync(url, new
        {
            trigger = rule.TriggerType,
            table = rule.ScopeTableId,
            timestamp = DateTime.UtcNow,
        }, ct);

        if (response.IsSuccessStatusCode)
            await LogAsync(rule.Id, "Success", null, null, ct);
        else
            await LogAsync(rule.Id, "Failed", $"Webhook returned {(int)response.StatusCode}.", null, ct);
    }

    private async Task RunDbaCheckAsync(AutomationRule rule, string userId, DatabaseSchema schema, DatabaseType engine, CancellationToken ct)
    {
        var decision = await _quota.TryReserveAsync(userId, AutomationTokenEstimate, ct);
        if (decision != AiQuotaDecision.Allowed)
        {
            await LogAsync(rule.Id, "Skipped", "Quota exceeded.", null, ct);
            return;
        }

        var issues = await _groqDba.AnalyzeSchemaDbaAsync(schema, engine);
        var summary = issues.Count == 0
            ? "No issues found."
            : $"{issues.Count} issue(s) found.";
        await LogAsync(rule.Id, "Success", null, summary, ct);
    }

    private async Task RunSeedDataAsync(AutomationRule rule, string userId, DatabaseSchema schema, CancellationToken ct)
    {
        var decision = await _quota.TryReserveAsync(userId, AutomationTokenEstimate, ct);
        if (decision != AiQuotaDecision.Allowed)
        {
            await LogAsync(rule.Id, "Skipped", "Quota exceeded.", null, ct);
            return;
        }

        // v1'de otomatik veritabanına YAZILMAZ — yalnızca üretilip özetlenir.
        var sql = await _aiService.GenerateMockDataAsync(schema);
        var summary = sql.Length > 500 ? sql[..500] + "…" : sql;
        await LogAsync(rule.Id, "Success", null, summary, ct);
    }

    private static string? ExtractUrl(string actionConfigJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(actionConfigJson);
            return doc.RootElement.TryGetProperty("url", out var v) ? v.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task LogAsync(string ruleId, string status, string? error, string? summary, CancellationToken ct)
    {
        _db.AutomationRunLogs.Add(new AutomationRunLog
        {
            RuleId = ruleId, Status = status, ErrorMessage = error, ResultSummary = summary,
        });
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Testleri çalıştırıp PASS ettiğini doğrula**

Run: `dotnet test backend/Namines.Tests --filter AutomationExecutorTests -v normal`
Expected: 2/2 PASS

- [ ] **Step 6: `AutomationExecutorWorker`'ı yaz**

`VaultBackupWorker.cs`'in birebir kopyası — kendi DI kapsamını açan tek işçili `BackgroundService`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Kuyruğa alınmış Namines Flow tetiklemelerini çalıştırır (Bölüm 3).
///
/// KENDİ DI kapsamını açıyor — <see cref="VaultBackupWorker"/> ile aynı
/// gerekçe: HTTP isteğinin DbContext'i yanıt döndüğünde atılıyor.
/// </summary>
public sealed class AutomationExecutorWorker : BackgroundService
{
    private readonly IAutomationJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutomationExecutorWorker> _logger;

    public AutomationExecutorWorker(IAutomationJobQueue queue, IServiceScopeFactory scopeFactory, ILogger<AutomationExecutorWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            AutomationJob job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var executor = scope.ServiceProvider.GetRequiredService<IAutomationExecutor>();
                await executor.RunAsync(job.ProjectId, job.Diff, job.OldSchema, job.NewSchema, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Namines Flow: {ProjectId} icin otomasyon calistirilirken beklenmeyen hata; isci devam ediyor.", job.ProjectId);
            }
        }
    }
}
```

- [ ] **Step 7: Commit**

```bash
git add backend/Namines.Infrastructure/Services/AutomationExecutor.cs backend/Namines.Infrastructure/Services/AutomationExecutorWorker.cs backend/Namines.Infrastructure/Services/IAiQuotaReserver.cs backend/Namines.Infrastructure/Data/AiQuotaService.cs backend/Namines.Tests/Services/AutomationExecutorTests.cs
git commit -m "feat: add AutomationExecutor and background worker"
```

---

### Task 5: DI kaydı + `AuthController.SyncProjects` entegrasyonu

**Files:**
- Modify: `backend/Namines.API/Extensions/ServiceCollectionExtensions.cs`
- Modify: `backend/Namines.API/Controllers/AuthController.cs`
- Test: `backend/Namines.Tests/Controllers/AuthControllerAutomationTests.cs` (yoksa yeni; varsa mevcut `AuthController` test dosyasına ekle — önce `Grep` ile ara)

**Interfaces:**
- Consumes: `IAutomationJobQueue.TryEnqueue` (Task 3), `IMigrationService.CalculateDiffAsync` (mevcut).
- Produces: yok (uç nokta davranışı).

- [ ] **Step 1: DI kayıtlarını ekle**

`ServiceCollectionExtensions.cs`'e, `AddNaminesVault()` bloğunun hemen
altına (aynı kuyruk+işçi deseni burada tekrarlanıyor):

```csharp
// Namines Flow — otomasyon tetikleme kuyruğu (Bölüm 3). Vault kuyruğuyla
// aynı gerekçe (istek kapsamlı DbContext, kendi DI kapsamı); TEK FARK:
// kuyruk dolunca istek 503'e DÜŞMÜYOR, sessizce loglanıp atlanıyor —
// otomasyon tetikleme SyncProjects'in yan etkisi, asıl işi değil.
services.AddSingleton<IAutomationJobQueue, AutomationJobQueue>();
services.AddScoped<IAutomationExecutor, AutomationExecutor>();
services.AddHostedService<AutomationExecutorWorker>();

// Webhook aksiyonu için adlandırılmış istemci — kısa timeout: kullanıcının
// üçüncü taraf ucu yavaşsa/düşükse bu, işçiyi TIKAMAMALI.
services.AddHttpClient("AutomationWebhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
```

- [ ] **Step 2: `AuthController`'a bağımlılıkları enjekte et**

`AuthController.cs`'in constructor'ını bul (özet: `UserManager<ApplicationUser>
userManager, AuthDbContext context, IConfiguration configuration`) ve şunları ekle:

```csharp
private readonly IMigrationService _migrationService;
private readonly IAutomationJobQueue _automationQueue;
private readonly ILogger<AuthController> _logger;

public AuthController(
    UserManager<ApplicationUser> userManager, AuthDbContext context, IConfiguration configuration,
    IMigrationService migrationService, IAutomationJobQueue automationQueue, ILogger<AuthController> logger)
{
    _userManager = userManager;
    _context = context;
    _configuration = configuration;
    _migrationService = migrationService;
    _automationQueue = automationQueue;
    _logger = logger;
}
```

(Mevcut alan adları farklıysa — `_userManager`/`_context`/`_configuration` —
gerçek dosyadaki adları kullan, yukarıdakiler placeholder değil, önceki
keşifte doğrulanan gerçek imzaya dayanıyor ama alan adlarını Read ile
teyit et.)

- [ ] **Step 3: `SyncProjects` içine diff hesaplama + kuyruğa atma ekle**

`existing.SchemaJson = projDto.SchemaJson;` satırından ÖNCE eski/yeni
şemayı yakala, döngü SONRASI (yani `SaveChangesAsync()`'ten SONRA) diff'i
hesapla ve kuyruğa at. `AuthController.cs:352-363` civarındaki `if
(existing != null)` bloğunu şu şekilde genişlet:

```csharp
// Diff, kayıt GÜNCELLENMEDEN ÖNCEKİ haliyle hesaplanmalı — bu yüzden eski
// JSON, üzerine yazılmadan burada yakalanıyor.
string? oldSchemaJsonForDiff = existing.SchemaJson;
string newSchemaJsonForDiff = projDto.SchemaJson;
string dbTypeForDiff = existing.DbType;
string projectIdForDiff = existing.Id;

// Update existing record owned by this user
existing.Name = projDto.Name;
existing.DbType = projDto.DbType;
existing.SchemaJson = projDto.SchemaJson;
existing.NodePositionsJson = projDto.NodePositionsJson;
existing.UpdatedAt = DateTime.UtcNow;
existing.OrganizationId ??= personalOrg.Id;
_context.CloudProjects.Update(existing);

_pendingDiffs.Add((projectIdForDiff, dbTypeForDiff, oldSchemaJsonForDiff, newSchemaJsonForDiff));
```

Döngüden önce `var _pendingDiffs = new List<(string ProjectId, string DbType,
string OldJson, string NewJson)>();` tanımla. `await
_context.SaveChangesAsync();` satırından HEMEN SONRA, `return Ok(...)`'tan
ÖNCE:

```csharp
// Kaydetme başarılı OLDUKTAN SONRA — sunucu, istemcinin "şunu değiştirdim"
// iddiasına değil, KENDİ hesapladığı diff'e güveniyor (bkz. spec "Kritik
// bulgu"). Kuyruk dolarsa iş sessizce loglanıp atlanır — bu, senkron
// isteğin (proje kaydetme) başarısını ASLA etkilemez.
foreach (var (pid, dbType, oldJson, newJson) in _pendingDiffs)
{
    try
    {
        var oldSchema = string.IsNullOrWhiteSpace(oldJson)
            ? new DatabaseSchema()
            : JsonSerializer.Deserialize<DatabaseSchema>(oldJson, SchemaJsonOptions.Default) ?? new DatabaseSchema();
        var newSchema = JsonSerializer.Deserialize<DatabaseSchema>(newJson, SchemaJsonOptions.Default) ?? new DatabaseSchema();

        var engine = Enum.TryParse<DatabaseType>(dbType, ignoreCase: true, out var parsedEngine)
            ? parsedEngine : DatabaseType.PostgreSQL;
        var diff = await _migrationService.CalculateDiffAsync(oldSchema, newSchema, engine);

        if (!_automationQueue.TryEnqueue(new AutomationJob(pid, diff, oldSchema, newSchema)))
            _logger.LogWarning("Namines Flow: {ProjectId} icin otomasyon kuyrugu dolu, tetikleme atlandi.", pid);
    }
    catch (Exception ex)
    {
        // Diff hesaplama patlarsa (örn. bozuk JSON) senkron isteği ASLA
        // düşürmemeli — kullanıcı projesini kaydedebilmeli, otomasyon
        // tetiklenmemesi ikincil bir kayıp.
        _logger.LogError(ex, "Namines Flow: {ProjectId} icin diff hesaplanamadi.", pid);
    }
}
```

Yeni projeler (`else` dalı, `AddAsync`) için diff hesaplanmaz — eski şema
yok, `TableAdded` tetikleyicisi yine de "her şey yeni eklendi" anlamına
gelmez (proje henüz otomasyon kuralına sahip olamaz, `AutomationRule`
oluşturmak için önce proje var olmalı). Bu, spec'te veya ekte
belirtilmemiş küçük bir netleştirme — kod davranışını sadeleştirir,
kapsam dışına çıkarmaz.

- [ ] **Step 4: Failing test yaz — kuyruk dolu olsa bile SyncProjects 200 dönüyor**

Mevcut `AuthController` test dosyasını `Grep` ile bul (`AuthControllerTests`
gibi); yoksa yeni `AuthControllerAutomationTests.cs` oluştur, mevcut
controller test kurulum deseninden (`WebApplicationFactory` veya doğrudan
controller örnekleme — hangisi kullanılıyorsa) kopyala. Asgari senaryo:

```csharp
[Fact]
public async Task Kuyruk_dolu_olsa_bile_SyncProjects_basariyla_donuyor()
{
    // Arrange: bir proje kaydet, sonra SchemaJson'ı değiştirip tekrar sync
    // çağır; IAutomationJobQueue.TryEnqueue her zaman false dönen bir sahte
    // ile enjekte et. Assert: yanıt yine 200 OK / "Sync successful.".
}
```

(Gerçek dosya yapısına bakmadan tam kurulum kodu yazmak "placeholder"
sayılır — bu adımı yürüten kişi önce mevcut `AuthController` test
dosyasını okuyup aynı kurulumu kullanmalı.)

- [ ] **Step 5: Testi FAIL/PASS döngüsüyle doğrula, sonra tüm `Namines.Tests`'i çalıştır**

Run: `dotnet test backend/Namines.Tests`
Expected: tüm testler PASS (regresyon yok)

- [ ] **Step 6: Commit**

```bash
git add backend/Namines.API/Extensions/ServiceCollectionExtensions.cs backend/Namines.API/Controllers/AuthController.cs backend/Namines.Tests/Controllers/
git commit -m "feat: wire Namines Flow diff computation and job enqueue into SyncProjects"
```

---

### Task 6: `AutomationController` — CRUD uç noktaları

**Files:**
- Create: `backend/Namines.API/Controllers/AutomationController.cs`
- Test: `backend/Namines.Tests/Controllers/AutomationControllerTests.cs`

**Interfaces:**
- Produces: `GET /api/automation/rules?projectId=...`, `POST /api/automation/rules`,
  `DELETE /api/automation/rules/{id}` — Task 7 (frontend `useAutomationStore`)
  bunu tüketir.

- [ ] **Step 1: Failing test yaz**

Mevcut basit bir controller testinin (`Grep` ile `Namines.Tests/Controllers/`
altında ara, örn. `VaultController` testi varsa onun kurulumunu kopyala)
desenini izleyerek üç senaryo: (1) proje sahibi olmayan kullanıcı 403/404
alır, (2) kural oluşturma 201 döner ve DB'de bulunur, (3) silme kuralı
kaldırır.

- [ ] **Step 2: `AutomationController`'ı yaz**

```csharp
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Infrastructure.Data;

namespace Namines.API.Controllers;

public class CreateAutomationRuleRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string? ScopeTableId { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string ActionConfigJson { get; set; } = "{}";
}

[ApiController]
[Route("api/automation")]
[Authorize]
public class AutomationController : ControllerBase
{
    private readonly AuthDbContext _context;

    public AutomationController(AuthDbContext context) => _context = context;

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private async Task<bool> OwnsProjectAsync(string projectId, string userId, CancellationToken ct)
    {
        // Bölüm 3 v1: "sahiplik" = kullanıcının bu projeye erişimi olan bir
        // organizasyonun üyesi olması. AuthController.GetProjects'teki
        // myOrgIds desenini tekrar kullanmak yerine burada en dar hâliyle
        // (proje.UserId == userId) tutuluyor — takım paylaşımlı düzenleme
        // yetkisi bu planın kapsamı dışında, spec de bunu ayırt etmiyor.
        return await _context.CloudProjects.AsNoTracking()
            .AnyAsync(p => p.Id == projectId && p.UserId == userId, ct);
    }

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules([FromQuery] string projectId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await OwnsProjectAsync(projectId, userId, ct)) return NotFound();

        var rules = await _context.AutomationRules.AsNoTracking()
            .Where(r => r.ProjectId == projectId).ToListAsync(ct);
        return Ok(rules);
    }

    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule([FromBody] CreateAutomationRuleRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await OwnsProjectAsync(request.ProjectId, userId, ct)) return NotFound();

        var rule = new AutomationRule
        {
            ProjectId = request.ProjectId,
            ScopeTableId = request.ScopeTableId,
            TriggerType = request.TriggerType,
            ActionType = request.ActionType,
            ActionConfigJson = request.ActionConfigJson,
        };
        _context.AutomationRules.Add(rule);
        await _context.SaveChangesAsync(ct);
        return Ok(rule);
    }

    [HttpDelete("rules/{id}")]
    public async Task<IActionResult> DeleteRule(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var rule = await _context.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();
        if (!await OwnsProjectAsync(rule.ProjectId, userId, ct)) return NotFound();

        _context.AutomationRules.Remove(rule);
        await _context.SaveChangesAsync(ct);
        return Ok(new { deleted = true });
    }
}
```

- [ ] **Step 3-4: Testleri FAIL→PASS döngüsüyle doğrula**

Run: `dotnet test backend/Namines.Tests --filter AutomationControllerTests -v normal`

- [ ] **Step 5: Commit**

```bash
git add backend/Namines.API/Controllers/AutomationController.cs backend/Namines.Tests/Controllers/AutomationControllerTests.cs
git commit -m "feat: add AutomationController CRUD endpoints"
```

---

### Task 7: Frontend — `useAutomationStore` gerçek API'ye bağlanıyor

**Files:**
- Create: `frontend/lib/automationApi.ts`
- Modify: `frontend/store/useAutomationStore.ts`
- Test: `frontend/store/useAutomationStore.test.ts` (mevcutsa genişlet; yoksa oluştur)

**Interfaces:**
- Consumes: `GET/POST/DELETE /api/automation/rules` (Task 6).
- Produces: `useAutomationStore`'a EKLENEN `loadRules(projectId: string): Promise<void>`
  — mevcut `addRule`/`updateRule`/`deleteRule`/`deleteRulesForTable`/`rulesForTable`/
  `setSelectedRuleId` imzaları DEĞİŞMEZ (bkz. Global Constraints).

- [ ] **Step 1: API istemcisini yaz**

`frontend/lib/automationApi.ts` (mevcut bir `lib/*Api.ts` dosyasının fetch
deseninden — auth header'ı nasıl ekleniyorsa aynı şekilde; bu adımı yürüten
kişi önce `frontend/lib/` altındaki mevcut bir API çağrı dosyasını okuyup
aynı base-URL/auth deseninden kopyalamalı, aşağıdaki iskelet o desene göre
tamamlanır):

```ts
import type { AutomationRule, AutomationActionType } from '../store/useAutomationStore';
import type { NaminesFlowEvent } from './naminesFlowEventBus';

export async function fetchAutomationRules(projectId: string): Promise<AutomationRule[]> {
  // GET /api/automation/rules?projectId=... — mevcut fetch/auth deseniyle.
  throw new Error('not implemented — copy the existing lib/*Api.ts auth pattern here');
}

export async function createAutomationRule(
  projectId: string, scopeTableId: string,
  triggerType: NaminesFlowEvent['type'], actionType: AutomationActionType,
): Promise<AutomationRule> {
  throw new Error('not implemented — copy the existing lib/*Api.ts auth pattern here');
}

export async function deleteAutomationRule(id: string): Promise<void> {
  throw new Error('not implemented — copy the existing lib/*Api.ts auth pattern here');
}
```

**Not:** Bu adımın iskelet bırakılması bilinçli — planın yazıldığı anda
`frontend/lib/` altındaki gerçek auth/fetch deseni (hangi header, hangi
base URL, token nereden okunuyor) doğrulanmadı. Bu görevi yürüten kişi
İLK ADIM olarak `frontend/lib/` içinde mevcut bir API dosyasını (örn. proje
sync'i yapan dosya) okuyup yukarıdaki üç fonksiyonu o gerçek desenle
doldurmalı — placeholder kalıcı değil, bu görevin ilk alt-adımı.

- [ ] **Step 2: Store'a `loadRules` ekle ve mevcut aksiyonları arka plan çağrısıyla genişlet**

`useAutomationStore.ts`'i şu şekilde değiştir (imzalar AYNI kalıyor,
gövdeler arka planda API çağırıyor):

```ts
import { create } from 'zustand';
import type { NaminesFlowEvent } from '../lib/naminesFlowEventBus';
import { fetchAutomationRules, createAutomationRule, deleteAutomationRule } from '../lib/automationApi';

// ... genId, AutomationActionType, AutomationRule, AutomationStoreState (DEĞİŞMEDİ) ...

interface AutomationStoreState {
  rules: AutomationRule[];
  selectedRuleId: string | null;
  loadRules: (projectId: string) => Promise<void>;
  addRule: (scopeTableId: string, triggerType: NaminesFlowEvent['type'], actionType: AutomationActionType) => string;
  updateRule: (id: string, patch: Partial<Pick<AutomationRule, 'triggerType' | 'actionType' | 'actionConfig' | 'enabled'>>) => void;
  deleteRule: (id: string) => void;
  deleteRulesForTable: (tableId: string) => void;
  rulesForTable: (tableId: string) => AutomationRule[];
  setSelectedRuleId: (id: string | null) => void;
}

export const useAutomationStore = create<AutomationStoreState>((set, get) => ({
  rules: [],
  selectedRuleId: null,

  loadRules: async (projectId) => {
    try {
      const rules = await fetchAutomationRules(projectId);
      set({ rules });
    } catch {
      // Ağ hatası: mevcut (muhtemelen boş) state korunur — kullanıcı
      // sayfayı yenileyince tekrar dener. Sessiz başarısızlık burada
      // kabul edilebilir çünkü kural DÜZENLEME hâlâ iyimser çalışır.
    }
  },

  addRule: (scopeTableId, triggerType, actionType) => {
    const id = genId();
    set(state => ({
      rules: [...state.rules, { id, scopeTableId, triggerType, actionType, actionConfig: {}, enabled: true }],
    }));
    // İyimser: yerel state anında güncellendi, sunucuya arka planda bildiriliyor.
    // NOT: gerçek sunucu id'si burada göz ardı ediliyor (v1 tavizi) — yerel
    // id kalıcı olarak kullanılmaya devam eder çünkü store'un dışa açık
    // sözleşmesi senkron bir id döndürmek zorunda.
    void createAutomationRule(get().rules.find(r => r.id === id)!.scopeTableId, scopeTableId, triggerType, actionType);
    return id;
  },

  updateRule: (id, patch) => {
    set(state => ({
      rules: state.rules.map(r => r.id === id ? { ...r, ...patch } : r),
    }));
    // v1 tavizi: PATCH ucu bu planın kapsamında değil (spec CRUD listesi
    // yalnızca POST/DELETE tanımlıyor) — güncelleme şimdilik yalnızca
    // istemcide kalıcı, sayfa yenilenince sunucudaki eski hâline döner.
    // Bu, spec'in "throttle'a girmez" sözünü bozmuyor çünkü kayıt zaten
    // hiç sunucuya yazılmıyor; sonraki bir bölümde PATCH eklenebilir.
  },

  deleteRule: (id) => {
    set(state => ({
      rules: state.rules.filter(r => r.id !== id),
      selectedRuleId: state.selectedRuleId === id ? null : state.selectedRuleId,
    }));
    void deleteAutomationRule(id);
  },

  deleteRulesForTable: (tableId) => {
    const toDelete = get().rules.filter(r => r.scopeTableId === tableId);
    set(state => ({ rules: state.rules.filter(r => r.scopeTableId !== tableId) }));
    toDelete.forEach(r => void deleteAutomationRule(r.id));
  },

  rulesForTable: (tableId) => get().rules.filter(r => r.scopeTableId === tableId),

  setSelectedRuleId: (id) => set({ selectedRuleId: id }),
}));
```

**Bilinen v1 taviz (yukarıda kod içinde de belirtildi):** `updateRule`
sunucuya hiç yazmıyor çünkü spec'in Bölüm 3 CRUD listesi yalnızca
`POST`/`DELETE` tanımlıyor, `PATCH` yok. Bu, planın spec'e sadık kalması
gereği bilinçli bir sınırlama — `AutomationRuleDrawer`'daki düzenlemeler
sayfa yenilenince kaybolur. Kullanıcı bunu fark ederse ayrı bir görev
olarak `PATCH /api/automation/rules/{id}` eklenebilir; bu planın kapsamı
dışında.

- [ ] **Step 3: Test — `loadRules` sunucudan gelen kuralları state'e yazıyor**

`frontend/store/useAutomationStore.test.ts`'e (mevcut dosyanın en altına,
`vi.mock('../lib/automationApi', ...)` ile):

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useAutomationStore } from './useAutomationStore';

vi.mock('../lib/automationApi', () => ({
  fetchAutomationRules: vi.fn(),
  createAutomationRule: vi.fn(),
  deleteAutomationRule: vi.fn(),
}));

import { fetchAutomationRules } from '../lib/automationApi';

describe('useAutomationStore.loadRules', () => {
  beforeEach(() => {
    useAutomationStore.setState({ rules: [], selectedRuleId: null });
    vi.mocked(fetchAutomationRules).mockReset();
  });

  it('sunucudan gelen kuralları state e yazıyor', async () => {
    vi.mocked(fetchAutomationRules).mockResolvedValue([
      { id: 'r1', scopeTableId: 't1', triggerType: 'TableDeleted', actionType: 'Webhook', actionConfig: {}, enabled: true },
    ]);

    await useAutomationStore.getState().loadRules('proj-1');

    expect(useAutomationStore.getState().rules).toHaveLength(1);
  });

  it('ağ hatasında mevcut state i koruyor', async () => {
    vi.mocked(fetchAutomationRules).mockRejectedValue(new Error('network'));
    useAutomationStore.setState({ rules: [{ id: 'r1', scopeTableId: 't1', triggerType: 'TableAdded', actionType: 'Toast', actionConfig: {}, enabled: true }], selectedRuleId: null });

    await useAutomationStore.getState().loadRules('proj-1');

    expect(useAutomationStore.getState().rules).toHaveLength(1);
  });
});
```

- [ ] **Step 4: Testleri FAIL→PASS döngüsüyle doğrula**

Run: `cd frontend && npx vitest run store/useAutomationStore.test.ts`

- [ ] **Step 5: `npx tsc --noEmit` ile tip hatası olmadığını doğrula**

- [ ] **Step 6: Commit**

```bash
git add frontend/lib/automationApi.ts frontend/store/useAutomationStore.ts frontend/store/useAutomationStore.test.ts
git commit -m "feat: connect useAutomationStore to the real backend, keeping its API surface stable"
```

---

### Task 8: Frontend — canvas açılışında kuralları yükle

**Files:**
- Modify: `frontend/app/canvas/page.tsx`

**Interfaces:**
- Consumes: `useAutomationStore.getState().loadRules(projectId)` (Task 7).

- [ ] **Step 1: `page.tsx`'te proje id'sinin okunduğu yeri bul**

`Grep` ile `useSchemaStore` veya proje id state'inin nereden geldiğini
(URL param / store) doğrula.

- [ ] **Step 2: Canvas mount olduğunda `loadRules` çağır**

Mevcut bir `useEffect` (örn. şema yükleme effect'i) varsa onun yanına,
proje id'si hazır olduğunda bir kez:

```tsx
useEffect(() => {
  if (!projectId) return;
  useAutomationStore.getState().loadRules(projectId);
}, [projectId]);
```

- [ ] **Step 3: Live browser doğrulaması**

`preview_start` ile dev server'ı aç, bir tabloya Namines Flow kuralı
ekle, sayfayı yenile, node'un ve kuralın hâlâ orada olduğunu (sunucudan
geldiğini) `read_page`/`screenshot` ile doğrula. Ağ isteklerini
`read_network_requests` ile kontrol et — `GET /api/automation/rules`
çağrısının gittiğini doğrula.

- [ ] **Step 4: Commit**

```bash
git add frontend/app/canvas/page.tsx
git commit -m "feat: hydrate Namines Flow rules from the server on canvas load"
```

---

## Kapsam Dışı (bu plan için, spec'in kendi "Kapsam Dışı"na ek olarak)

- `PATCH /api/automation/rules/{id}` — `updateRule`'un sunucuya yazması
  (yukarıda Task 7 Step 2'de not edildi).
- Takım paylaşımlı proje düzenleme yetkisi `AutomationController`'da
  (`OwnsProjectAsync` yalnızca `UserId` kontrolü yapıyor, organizasyon
  üyeliğini değil).
- İlişki tetikleyicilerinin (`RelationAdded`/`RelationDeleted`) tabloya
  özel eşleştirmesi — v1'de yalnızca proje geneli kurallarda çalışır
  (bkz. Task 2, `AutomationRuleMatcher` notu).
