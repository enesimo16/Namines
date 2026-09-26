# Agent UI Entegrasyonu (Spec Bölüm 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (a) Bölüm 2'de eklenen `plan` adımının ön yüzü çökertmesini
engellemek, (b) agent'ın kalan bulgularını kullanıcıya göstermek —
bugün backend özenle döndürüyor, UI sessizce çöpe atıyor, (c) modelin
yanına "Gelişmiş" toggle'ı koyup varsayılanı tek AI turuna çekmek.

**Architecture:** SSE altyapısı, canlı ilerleme ekranı ve hedef motor
seçicisi ZATEN var. Bu plan yeni bir akış kurmaz; var olan üçünün
eksiklerini kapatır. "Gelişmiş" toggle'ı yeni bir kod yolu AÇMIYOR —
mevcut hattın `budgetRounds` parametresini değiştiriyor:

| Mod | budgetRounds | Plan turu | Denetim | Onarım turu |
|---|---|---|---|---|
| Varsayılan (toggle kapalı) | 1 | atlanır (<3) | **çalışır** | yok (`rounds < budgetRounds` yanlış) |
| Gelişmiş (toggle açık) | kotaya göre, en çok `DefaultTotalRounds` | çalışır | çalışır | çalışır |

Yani varsayılanda da şema NSL kurallarından ve gerçek DDL derlemesinden
geçiyor; fark yalnızca modelin bulguları düzeltmek için tur harcamaması.

**Tech Stack:** ASP.NET Core (SSE), Next.js/TypeScript, Tailwind.

**Spec:** `docs/superpowers/specs/2026-09-14-agent-prompt-engine-design.md`
(Bölüm 3)

## Global Constraints

- **Bulgular gizlenmez.** `BuildResultPayload`'ın yazılı kuralı bu; UI de
  ona uymalı. "Çalışıyor gibi görünen" bir şema, hiç vermemekten kötüdür.
- Toggle KAPALIYKEN de denetim çalışır. Kapalı mod "denetimsiz" değil,
  "otomatik onarımsız" demektir — kullanıcıya bu şekilde anlatılmalı.
- Kota davranışı korunur: bütçesi hiç olmayan kullanıcı her iki modda da
  429 almalı (bu yüzden varsayılan `1` sabit değil,
  `Math.Min(affordable, 1)`).
- Adım tipi (`AgentStep.Kind`) iki dilde ayrı yazılı; birini değiştiren
  diğerini aynı değişiklikte güncellemek zorunda (bkz. gözlem #3).

---

### Task 1: Backend — `Advanced` bayrağı ve bütçe kapısı

> ✅ **Bitmiştir.** `GenerateRequest.Advanced` bayrağı ve bütçe kapısı yazıldı; `SchemaAgentBudgetTests.cs` kapsıyor.

**Files:**
- Modify: `backend/Namines.Core/Models/GenerateRequest.cs`
- Modify: `backend/Namines.API/Controllers/SchemaController.cs`
- Test: `backend/Namines.Tests/Services/SchemaAgentBudgetTests.cs` (yeni)

**Interfaces:**
- Produces: `GenerateRequest.Advanced` (`bool`, varsayılan `false`).

- [ ] **Step 1: Write the failing test**

Bu davranışın özü hattın kendisinde: bütçe 1 olduğunda plan ve onarım
turlarının ikisi de atlanmalı ama bulgular DÖNMELİ. Controller'ı ayağa
kaldırmadan bunu doğrulamak mümkün (ve daha hızlı):

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
/// "Gelişmiş" kapalıyken (tek tur) hattın davranışı.
///
/// <b>Kapalı mod denetimsiz DEĞİL, otomatik onarımsız.</b> Bu ayrım ürünün
/// vaadi: kullanıcı hızlı sonuç alıyor ama şemanın bozuk olduğunu yine de
/// öğreniyor. Denetim de kapansaydı, sessizce çalışmayan DDL üretirdik.
/// </summary>
public class SchemaAgentBudgetTests
{
    private sealed class CountingSource : ISchemaDraftSource
    {
        private readonly DatabaseSchema _schema;

        public CountingSource(DatabaseSchema schema) => _schema = schema;

        public int PlanCalls { get; private set; }
        public int RepairCalls { get; private set; }

        public Task<string?> PlanAsync(string prompt, DatabaseType engine, CancellationToken ct = default)
        {
            PlanCalls++;
            return Task.FromResult<string?>("plan");
        }

        public Task<DatabaseSchema> DraftAsync(
            string prompt, DatabaseType engine, string? plan, CancellationToken ct = default) =>
            Task.FromResult(_schema);

        public Task<DatabaseSchema> RepairAsync(
            DatabaseSchema schema, IReadOnlyList<string> findings, DatabaseType engine, CancellationToken ct = default)
        {
            RepairCalls++;
            return Task.FromResult(_schema);
        }
    }

    /// <summary>FK tipi uyuşmayan şema — NSL004 hatası üretir.</summary>
    private static DatabaseSchema WithBrokenForeignKey()
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
    public async Task A_single_round_budget_skips_both_the_plan_and_the_repair_turn()
    {
        var source = new CountingSource(WithBrokenForeignKey());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 1);

        Assert.Equal(0, source.PlanCalls);
        Assert.Equal(0, source.RepairCalls);
        Assert.Equal(1, result.Rounds);
    }

    [Fact]
    public async Task A_single_round_budget_still_inspects_and_reports_the_findings()
    {
        // Kapalı modun bütün değeri bu: hızlı, ama sessiz değil.
        var source = new CountingSource(WithBrokenForeignKey());

        var result = await Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 1);

        Assert.False(result.Clean);
        Assert.Contains(result.RemainingFindings, f => f.Contains("NSL004"));
    }

    [Fact]
    public async Task A_zero_round_budget_is_refused_rather_than_silently_producing_nothing()
    {
        var source = new CountingSource(WithBrokenForeignKey());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Pipeline(source).RunAsync("x", DatabaseType.PostgreSQL, budgetRounds: 0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/Namines.Tests --filter SchemaAgentBudgetTests`
Expected: derlenir ve **geçer** — hat bu davranışı zaten destekliyor.
Bu görevde test, eklenen davranışı değil, toggle'ın dayandığı SÖZLEŞMEYİ
kilitliyor: ileride biri plan turunun bütçe eşiğini düşürürse ya da
onarım koşulunu gevşetirse, varsayılan mod sessizce pahalılaşır ve bu
testler patlar. (Geçen bir testle başlamak burada bilinçli; `Advanced`
bayrağının kendisi Step 3'te ekleniyor ve onun testi Task 4'teki uçtan
uca doğrulama.)

- [ ] **Step 3: Write the implementation**

`backend/Namines.Core/Models/GenerateRequest.cs` — add:

```csharp
    /// <summary>
    /// Gelişmiş mod: plan turu + otomatik onarım turları + araçlar.
    ///
    /// <b>Varsayılan <c>false</c> ve bu "denetimsiz" demek DEĞİL:</b> kapalı
    /// modda da şema NSL kurallarından ve gerçek DDL derlemesinden geçiyor,
    /// bulgular kullanıcıya gösteriliyor. Fark, modelin o bulguları düzeltmek
    /// için ek tur (yani kota) harcamaması.
    /// </summary>
    public bool Advanced { get; set; }
```

`SchemaController.GenerateSchema` — replace the budget line:

```csharp
        // Kaç tur harcayabileceğimizi BÜTÇE söylüyor, hat değil. Bir kullanıcının
        // günlük hakkı bitmişken üç tur çalıştırmak, ona hiçbir şey vermeden
        // parasını harcamak olurdu.
        var affordableRounds = await AffordableRoundsAsync(userId);

        // Gelişmiş kapalıyken TEK tur: taslak üretilir ve denetimden geçer, ama
        // plan turu ve otomatik onarım turu çalışmaz — bulgular kullanıcıya
        // gösterilir, model onları düzeltmek için kota harcamaz.
        //
        // Sabit 1 DEĞİL, Math.Min: bütçesi hiç kalmamış kullanıcı kapalı modda
        // da 429 almalı, sessizce bedava tur kazanmamalı.
        var budgetRounds = request.Advanced
            ? affordableRounds
            : Math.Min(affordableRounds, 1);
```

- [ ] **Step 4: Run the test again**

Run: `dotnet test backend/Namines.Tests --filter SchemaAgentBudgetTests`
Expected: PASS (ve `dotnet build backend/Namines.sln` hatasız).

- [ ] **Step 5: Commit**

```bash
git add backend/Namines.Core/Models/GenerateRequest.cs \
        backend/Namines.API/Controllers/SchemaController.cs \
        backend/Namines.Tests/Services/SchemaAgentBudgetTests.cs
git commit -m "feat: gate plan and repair rounds behind an advanced request flag"
```

---

### Task 2: Frontend sözleşmesi — `plan` adımı ve `advanced` alanı

> ✅ **Bitmiştir.** `sseSchemaStream.ts` ve `services/api.ts` `plan` adımını ve `advanced` alanını taşıyor.

**Neden acil:** `ProductionScreen` `KIND_ICON[step.kind]` ile ikon
seçiyor. `plan` anahtarı yokken değer `undefined` oluyor ve React onu
bileşen olarak render etmeye çalışıp ÇÖKÜYOR. Bölüm 2'de backend'e
eklenen adım, bugün ön yüzü kırıyor.

**Files:**
- Modify: `frontend/lib/sseSchemaStream.ts`
- Modify: `frontend/services/api.ts`

- [ ] **Step 1: Add `plan` to the step union**

In `sseSchemaStream.ts`:

```ts
export interface AgentStepEvent {
  /**
   * Backend `AgentStep.Kind` ile BİREBİR. İki dilde ayrı yazılı olduğu için
   * aralarında derleyici bağı yok: backend'e yeni bir tür eklendiğinde
   * burası da aynı değişiklikte güncellenmeli, yoksa tüketici çalışma
   * anında patlar (ikon tablosu `undefined` döner).
   */
  kind: 'plan' | 'draft' | 'inspect' | 'finding' | 'repair' | 'clean';
  message: string;
}
```

- [ ] **Step 2: Send the advanced flag**

In `frontend/services/api.ts`, change the signature and body:

```ts
  buildGenerateFormData: (
    prompt: string,
    dbType: string,
    naiModel: string,
    image?: File | null,
    apiSpecUrl?: string,
    answers?: Record<string, string>,
    advanced?: boolean,
  ): FormData => {
    const formData = new FormData();
    formData.append('Prompt', prompt);
    formData.append('DbType', dbType);
    formData.append('AIProvider', 'Groq');
    formData.append('ModelName', naiModel);
    // Gelişmiş mod: plan turu + otomatik onarım. Kapalıyken de şema
    // denetimden geçiyor, yalnızca onarım turu harcanmıyor.
    formData.append('Advanced', advanced ? 'true' : 'false');
    if (image) formData.append('Image', image);
    if (apiSpecUrl) formData.append('ApiSpecUrl', apiSpecUrl);
    if (answers && Object.keys(answers).length > 0) formData.append('Answers', JSON.stringify(answers));
    return formData;
  },
```

- [ ] **Step 3: Verify it compiles**

Run: `cd frontend && npx tsc --noEmit`
Expected: **FAIL** — `KIND_ICON` artık `plan` anahtarını eksik bırakıyor
ve `Record<AgentStepEvent['kind'], ...>` bunu derleme hatası yapıyor.
Bu istenen sonuç: tip sistemi Task 3'ü zorunlu kılıyor.

- [ ] **Step 4: Commit with Task 3** (tek başına derlenmediği için birlikte)

---

### Task 3: `ProductionScreen` — plan ikonu ve bulgu özeti

> ✅ **Bitmiştir.** `ProductionScreen.tsx` plan ikonu ve bulgu özetini gösteriyor.

**Files:**
- Modify: `frontend/components/landing/ProductionScreen.tsx`

**Interfaces:**
- Consumes: `AgentResultEvent['agent']` (Task 4 bunu geçirir).
- Produces: `ProductionScreen` yeni `summary` prop'u
  (`AgentResultEvent['agent'] | null`).

- [ ] **Step 1: Add the plan icon**

```tsx
import { Check, Loader2, AlertTriangle, X, ListTodo } from 'lucide-react';

const KIND_ICON: Record<AgentStepEvent['kind'], typeof Check> = {
  plan: ListTodo,
  draft: Loader2,
  inspect: Loader2,
  finding: AlertTriangle,
  repair: Loader2,
  clean: Check,
};
```

and include `plan` in the spinner condition:

```tsx
            const spinning =
              isRunning &&
              isLast &&
              (step.kind === 'plan' || step.kind === 'draft' || step.kind === 'inspect' || step.kind === 'repair');
```

- [ ] **Step 2: Accept and render the summary**

Extend the props:

```tsx
import { AgentStepEvent, AgentResultEvent } from '../../lib/sseSchemaStream';

interface Props {
  steps: AgentStepEvent[];
  /** Akış hâlâ devam ediyor mu — false olunca "kapat" görünür, otomatik kapanmaz. */
  isRunning: boolean;
  /**
   * Hat bittiğinde dönen özet; akış sürerken null.
   *
   * <b>Neden gösterilmek ZORUNDA:</b> sunucu kalan bulguları bilerek
   * döndürüyor ("çalışıyor gibi görünen bir şema, hiç vermemekten kötüdür").
   * Bunu burada yutmak, o kararı sessizce geri almak olurdu — kullanıcı
   * bozuk şemayı ancak veritabanı reddedince öğrenirdi.
   */
  summary: AgentResultEvent['agent'] | null;
  onClose: () => void;
}
```

and render it between the step list and the Continue button:

```tsx
        {summary && (
          <div className="mt-4 pt-4 border-t border-line-strong flex flex-col gap-3">
            {summary.clean ? (
              <div className="flex items-start gap-2.5 text-xs">
                <Check className="w-3.5 h-3.5 mt-0.5 shrink-0 text-success-text" />
                <span className="text-content-primary">
                  Schema compiled with no errors
                  {summary.rounds > 1 ? ` after ${summary.rounds} rounds` : ''}.
                </span>
              </div>
            ) : (
              <div className="flex flex-col gap-2">
                <div className="flex items-start gap-2.5 text-xs">
                  <AlertTriangle className="w-3.5 h-3.5 mt-0.5 shrink-0 text-warning" />
                  <span className="text-content-primary font-medium">
                    {summary.findings.length} unresolved{' '}
                    {summary.findings.length === 1 ? 'problem' : 'problems'} — the schema is
                    loaded, but fix these before using it.
                  </span>
                </div>
                <ul className="flex flex-col gap-1 pl-6 max-h-32 overflow-y-auto">
                  {summary.findings.map((finding, i) => (
                    <li key={i} className="text-[11px] text-content-secondary leading-snug">
                      {finding}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {/* Taşınabilirlik notları bulgu DEĞİL: kullanıcı bu motoru seçti,
                diğerlerinde takılması onun sorunu değil. Katlanmış duruyor ki
                "yarın MySQL'e taşıyabilir miyim" sorusu cevapsız kalmasın ama
                bugünkü işi de gölgelemesin. */}
            {summary.portability.length > 0 && (
              <details className="text-[11px]">
                <summary className="cursor-pointer text-content-muted hover:text-content-secondary">
                  Works on this engine, but {summary.portability.length} issue
                  {summary.portability.length === 1 ? '' : 's'} on other engines
                </summary>
                <ul className="flex flex-col gap-1 mt-1.5 pl-3 max-h-28 overflow-y-auto">
                  {summary.portability.map((note, i) => (
                    <li key={i} className="text-content-muted leading-snug">{note}</li>
                  ))}
                </ul>
              </details>
            )}
          </div>
        )}
```

- [ ] **Step 3: Commit with Task 4**

---

### Task 4: Üretim sayfası — "Gelişmiş" toggle'ı ve özetin bağlanması

> ✅ **Bitmiştir.** `app/new/page.tsx` "Gelişmiş" toggle'ını taşıyor.

**Files:**
- Modify: `frontend/app/new/page.tsx`

- [ ] **Step 1: Hold the toggle and the summary in state**

```tsx
  const [advanced, setAdvanced] = useState(false);
  const [agentSummary, setAgentSummary] = useState<AgentResultEvent['agent'] | null>(null);
```

(import `AgentResultEvent` alongside `AgentStepEvent`.)

- [ ] **Step 2: Thread them through the run**

In `runGeneration`:

```tsx
    setProductionSteps([]);
    setAgentSummary(null);
    setShowProduction(true);

    const formData = schemaService.buildGenerateFormData(
      prompt, dbType, naiModel, image, apiSpecUrl, answers, advanced,
    );
```

and in `onResult`, before `setIsGenerating(false)`:

```tsx
      onResult: (result) => {
        loadFromSchema(result.schema as DatabaseSchema);
        useSchemaStore.getState().recordGenerationSource(prompt, answers);
        // Kalan bulgular kullanıcıya gösterilecek (bkz. ProductionScreen).
        // Bunu atlamak, sunucunun "bulguları gizleme" kararını sessizce
        // geri almak olurdu.
        setAgentSummary(result.agent);
        setIsGenerating(false);
      },
```

and pass it down:

```tsx
        <ProductionScreen
          steps={productionSteps}
          summary={agentSummary}
```

- [ ] **Step 3: Add the toggle next to the model selector**

Immediately after the model-selector block (the `{models.length > 0 && (…)}`
element) inside the options row:

```tsx
              {/* Gelişmiş mod — modelin yanında, çünkü ikisi de aynı soruyu
                  cevaplıyor: bu üretim ne kadar bütçe harcasın.

                  Kapalıyken şema YİNE denetimden geçiyor; kapalı olan tek şey
                  modelin bulguları düzeltmek için ek tur harcaması. Etiketin
                  "denetimi kapat" gibi okunmaması bu yüzden önemli. */}
              <button
                type="button"
                disabled={isGenerating}
                onClick={() => setAdvanced(!advanced)}
                aria-pressed={advanced}
                title={
                  advanced
                    ? 'The agent plans, then fixes what the rule engine and the real DDL compiler report. Uses more of your budget.'
                    : 'One pass. The schema is still checked, but problems are reported instead of fixed automatically.'
                }
                className={`flex items-center gap-1.5 rounded-[var(--radius-control)] px-3 h-[38px] text-sm font-medium transition-colors shrink-0 disabled:opacity-50 disabled:cursor-not-allowed ${
                  advanced
                    ? 'bg-accent/20 text-accent-text border border-accent/40'
                    : 'glass-input text-content-muted hover:text-content-primary'
                }`}
              >
                <Sparkles className="w-3.5 h-3.5" />
                <span>Advanced</span>
              </button>
```

(add `Sparkles` to the existing `lucide-react` import.)

- [ ] **Step 4: Verify the frontend compiles**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 5: Verify in the browser**

Start the dev server and confirm, on `/new`:
1. The **Advanced** toggle renders next to the model selector and toggles
   its active styling.
2. The page still builds and the options row does not overflow at phone
   width (375px) — the row is `flex-wrap`, the new button is `shrink-0`.

- [ ] **Step 6: Commit**

```bash
git add frontend/lib/sseSchemaStream.ts frontend/services/api.ts \
        frontend/components/landing/ProductionScreen.tsx frontend/app/new/page.tsx
git commit -m "feat: surface agent findings and add an advanced-mode toggle"
```

---

### Task 5: Tam doğrulama

> ✅ **Bitmiştir.** Doğrulandı.

- [ ] **Step 1:** `dotnet test backend/Namines.Tests` — yalnızca Docker
  gerektiren 8 `Integration` testi düşmeli, başka hiçbir şey.
- [ ] **Step 2:** `cd frontend && npx tsc --noEmit` — temiz.
- [ ] **Step 3:** `dotnet test backend/Namines.Tests --filter "FullyQualifiedName~DdlGoldenTests"`
  — 192 test geçmeli; bu planda DDL üretimi değişmedi, snapshot değişirse
  REGRESYONDUR.
