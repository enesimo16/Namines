# F0 — Kaynak Kayıt Defteri ve `+` Menüsü — Uygulama Planı

> **Ajan çalışanlar için:** Bu planı görev görev uygulamak için
> `superpowers:subagent-driven-development` (önerilen) veya
> `superpowers:executing-plans` alt-skill'ini kullan. Adımlar takip için
> checkbox (`- [ ]`) sözdizimiyle yazıldı.

**Hedef:** Şema kaynaklarını sunucudan gelen tek bir kayıt defterine bağlamak
ve prompt kutusundaki dağınık ikonları tek bir `+ Add source` menüsünde
toplamak — davranış değişmeden.

**Mimari:** Sunucuda salt-okunur bir katalog (`ISchemaSourceCatalog`) her
kaynağı bir `SchemaSourceDescriptor` ile tanımlar (kimlik, grup, yetenekler,
tahmin üretip üretmediği). `GET /api/sources` bunu **string** alanlarla
yayınlar. Frontend menüyü bu listeden üretir; hangi kaynağın ne yapabildiğini
istemcide bir daha yazmaz.

**Teknoloji:** .NET 8 (`Namines.Core` → `Namines.Infrastructure` →
`Namines.API`), xUnit; Next.js 16 + React 19 + Zustand, Vitest +
@testing-library/react.

**Spec:** [`06-EKLENTI-MIMARISI.md`](06-EKLENTI-MIMARISI.md) (sözleşme ve menü
tasarımı), [`07-FAZ-PLANI.md`](07-FAZ-PLANI.md) (F0'ın kapsamı).

## Kapsam kararları (spec'ten sapmalar — bilerek)

1. **`ISchemaSourceConnector` (çalıştırılabilir sözleşme) F0'a GİRMİYOR.**
   06 §3 onu tanımlıyor, ama F0'da hiçbir şey onu implemente etmeyecek: mevcut
   kaynakların hepsinin kendi ucu var ve frontend onları doğrudan çağırıyor.
   Implementorü olmayan bir arayüz, ilk gerçek implementor (GitHub, F1)
   geldiğinde yeniden yazılır. F0 yalnızca **tanımlayıcı** katalog.
2. **`ProjectRepository` modeli F1'e taşındı.** 07'de F0'a yazılmıştı; ama
   kullanıcısı olmayan bir control-DB tablosu eklemek, migration'ı bugün
   yazıp yarın değiştirmek demek. F1'de tarama ucu onu gerçekten yazacağı
   anda gelir.
3. **Ses (Whisper) menüye girmiyor.** Ses bir kaynak değil, prompt metnine
   giriş yöntemi — şema üretmiyor, cümle üretiyor. Mikrofon düğmesi yerinde
   kalır.

## Global kısıtlar

- **Yorumlar sadece NEDEN için**, Türkçe, mevcut stille tutarlı (AGENTS.md).
- **Frontend işi `FRONTEND.md`'ye tabidir:** renk paleti sabit, token'lar
  kullanılır (`text-content-*`, `glass-*`, `var(--radius-*)`), ham hex yasak.
  Görsel karar öncesi `ui-ux-pro-max` skill'i sorgulanır (FRONTEND.md §0).
- **Enum'lar sınırdan STRING olarak geçer.** Backend enum'unu frontend'de
  sayıyla eşlemek bu projede bir kez kırıldı (gözlem #3): API `kind` ve
  `capabilities` alanlarını string/string[] olarak döner.
- `npm run check:design` ve `npm run lint` frontend görevlerinde yeşil olmalı.
- **Commit'i kullanıcı onaylamadan atma** (AGENTS.md). Plandaki commit
  adımları hazırlanır, onay alındıktan sonra çalıştırılır.
- Yeni markdown dosyalarının `git status`'ta göründüğü doğrulanır.

---

### Görev 1: Katalog sözleşmesi ve statik katalog

> ✅ **Bitmiştir.** Katalog yazıldı; `Namines.Tests/Sources/SchemaSourceCatalogTests.cs` kapsıyor.

**Dosyalar:**
- Oluştur: `backend/Namines.Core/Sources/SchemaSourceDescriptor.cs`
- Oluştur: `backend/Namines.Core/Sources/ISchemaSourceCatalog.cs`
- Oluştur: `backend/Namines.Infrastructure/Services/StaticSchemaSourceCatalog.cs`
- Test: `backend/Namines.Tests/Sources/SchemaSourceCatalogTests.cs`

**Arayüzler:**
- Kullanır: —
- Üretir: `SchemaSourceDescriptor(string Id, string DisplayName, string Description,
  SchemaSourceKind Kind, SchemaSourceCapability Capabilities, bool ProducesGuess)`;
  `enum SchemaSourceKind { Connect, Import, Starter }`;
  `[Flags] enum SchemaSourceCapability { None, Import, Compare, Watch, WriteBack }`;
  `ISchemaSourceCatalog.All() → IReadOnlyList<SchemaSourceDescriptor>`.

- [ ] **Adım 1: Başarısız testi yaz**

`backend/Namines.Tests/Sources/SchemaSourceCatalogTests.cs`:

```csharp
using System.Linq;
using Namines.Core.Sources;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Sources;

/// <summary>github/06-EKLENTI-MIMARISI.md — katalog sözleşmesi.</summary>
public class SchemaSourceCatalogTests
{
    private static readonly ISchemaSourceCatalog Catalog = new StaticSchemaSourceCatalog();

    [Fact]
    public void Catalog_lists_every_source_the_product_has_today()
    {
        var ids = Catalog.All().Select(s => s.Id).ToArray();

        Assert.Contains("dbconnect", ids);
        Assert.Contains("openapi", ids);
        Assert.Contains("code", ids);
        Assert.Contains("jsonshape", ids);
        Assert.Contains("image", ids);
        Assert.Contains("starter", ids);
    }

    [Fact]
    public void Voice_is_not_a_source()
    {
        // Ses prompt METNİ üretiyor, şema değil. Menüye koymak, kullanıcıya
        // seçtiğinde şema geleceğini vaat etmek olurdu.
        Assert.DoesNotContain("voice", Catalog.All().Select(s => s.Id));
    }

    [Fact]
    public void Watching_a_source_requires_connecting_to_it()
    {
        // 06 §4'ün "Bağla" / "İçe aktar" ayrımı: drift takibi ancak sürekli
        // bir ilişki varsa mümkün. Tek seferlik bir içe aktarmada izlenecek
        // bir şey yok — bu testin koruduğu şey menünün gruplaması değil,
        // kullanıcıya verilen sözün tutarlılığı.
        foreach (var source in Catalog.All().Where(s => s.Capabilities.HasFlag(SchemaSourceCapability.Watch)))
            Assert.Equal(SchemaSourceKind.Connect, source.Kind);
    }

    [Fact]
    public void Inferred_sources_are_marked_as_guesses()
    {
        // second-phase/06-VERI-KAYNAKLARI.md: "çıkarım olduğu her ekranda
        // söylenmeli". Bayrak katalogdan geliyor ki UI unutamasın.
        Assert.True(Catalog.All().Single(s => s.Id == "openapi").ProducesGuess);
        Assert.True(Catalog.All().Single(s => s.Id == "jsonshape").ProducesGuess);
        Assert.True(Catalog.All().Single(s => s.Id == "image").ProducesGuess);

        Assert.False(Catalog.All().Single(s => s.Id == "dbconnect").ProducesGuess);
        Assert.False(Catalog.All().Single(s => s.Id == "code").ProducesGuess);
    }

    [Fact]
    public void Every_source_can_be_shown_to_a_user()
    {
        foreach (var source in Catalog.All())
        {
            Assert.False(string.IsNullOrWhiteSpace(source.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(source.Description));
            Assert.True(source.Capabilities.HasFlag(SchemaSourceCapability.Import));
        }
    }
}
```

- [ ] **Adım 2: Testi çalıştır, başarısız olduğunu gör**

Çalıştır: `dotnet test backend/Namines.Tests --filter SchemaSourceCatalogTests`
Beklenen: DERLENMEZ — `Namines.Core.Sources` ad alanı yok.

- [ ] **Adım 3: Sözleşmeyi yaz**

`backend/Namines.Core/Sources/SchemaSourceDescriptor.cs`:

```csharp
using System;

namespace Namines.Core.Sources;

/// <summary>
/// Kaynağın kullanıcıyla kurduğu ilişki türü (github/06-EKLENTI-MIMARISI.md §4).
///
/// <b>Ayrım kozmetik değil:</b> <see cref="Connect"/> sürekli bir ilişkidir ve
/// arkadan drift takip edilebilir; <see cref="Import"/> tek seferliktir. Menüde
/// ayrı gruplanmalarının sebebi bu — kullanıcı sonradan "neden drift bildirimi
/// almıyorum" diye sormamalı.
/// </summary>
public enum SchemaSourceKind
{
    Connect,
    Import,
    Starter,
}

/// <summary>
/// Kaynağın ne yapabildiği. UI hiçbir yerde kaynağı ADIYLA tanıyıp yetenek
/// varsaymaz; bayrağa bakar. Yeni kaynak eklendiğinde menü kendiliğinden
/// doğru davranır.
/// </summary>
[Flags]
public enum SchemaSourceCapability
{
    None = 0,
    Import = 1,
    Compare = 2,
    Watch = 4,
    WriteBack = 8,
}

/// <param name="Id">Kararlı kimlik; istemci bunu eylemle eşler.</param>
/// <param name="ProducesGuess">
/// Üretilen şema bir çıkarım mı. <b>Katalogda tutulmasının sebebi:</b>
/// "tahmin olduğunu her ekranda söyle" kuralı bugün her kaynağın kendi
/// ekranında ayrı ayrı hatırlanıyor; buradan gelince UI'ın unutma ihtimali
/// kalmıyor.
/// </param>
public sealed record SchemaSourceDescriptor(
    string Id,
    string DisplayName,
    string Description,
    SchemaSourceKind Kind,
    SchemaSourceCapability Capabilities,
    bool ProducesGuess);
```

`backend/Namines.Core/Sources/ISchemaSourceCatalog.cs`:

```csharp
using System.Collections.Generic;

namespace Namines.Core.Sources;

/// <summary>
/// Ürünün bildiği şema kaynaklarının tek listesi.
///
/// <b>Arayüz, kaynakların çalıştırılmasını DEĞİL tanıtılmasını kapsıyor</b>
/// (github/07-FAZ-PLANI.md F0 kapsam kararı 1): her kaynağın bugün kendi ucu
/// var ve istemci onları doğrudan çağırıyor. Çalıştırılabilir ortak sözleşme,
/// ilk gerçek implementorü (GitHub, F1) geldiğinde yazılacak — implementorü
/// olmayan bir arayüz, o geldiğinde yeniden yazılırdı.
/// </summary>
public interface ISchemaSourceCatalog
{
    IReadOnlyList<SchemaSourceDescriptor> All();
}
```

`backend/Namines.Infrastructure/Services/StaticSchemaSourceCatalog.cs`:

```csharp
using System.Collections.Generic;
using Namines.Core.Sources;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Bugünkü kaynaklar. Liste sabit çünkü kaynaklar koda gömülü — kullanıcı
/// başına değişen bir şey yok. Kullanıcıya göre değişen ilk kaynak (bağlı
/// GitHub deposu) F1'de geldiğinde bu sınıf onu ayrı bir yoldan ekleyecek.
/// </summary>
public sealed class StaticSchemaSourceCatalog : ISchemaSourceCatalog
{
    private static readonly IReadOnlyList<SchemaSourceDescriptor> Sources = new[]
    {
        new SchemaSourceDescriptor(
            "dbconnect", "Database connection",
            "Read the schema straight from a live database.",
            SchemaSourceKind.Connect,
            SchemaSourceCapability.Import | SchemaSourceCapability.Compare | SchemaSourceCapability.Watch,
            ProducesGuess: false),

        new SchemaSourceDescriptor(
            "openapi", "OpenAPI / GraphQL URL",
            "Infer a data model from an API specification.",
            SchemaSourceKind.Import,
            SchemaSourceCapability.Import | SchemaSourceCapability.Compare,
            ProducesGuess: true),

        new SchemaSourceDescriptor(
            "code", "Code files",
            "Prisma schema, EF Core entities or raw SQL.",
            SchemaSourceKind.Import,
            SchemaSourceCapability.Import | SchemaSourceCapability.Compare,
            ProducesGuess: false),

        new SchemaSourceDescriptor(
            "jsonshape", "Sample JSON response",
            "Infer entities from the shape of API responses.",
            SchemaSourceKind.Import,
            SchemaSourceCapability.Import,
            ProducesGuess: true),

        new SchemaSourceDescriptor(
            "image", "Image",
            "Read a diagram or screenshot of a schema.",
            SchemaSourceKind.Import,
            SchemaSourceCapability.Import,
            ProducesGuess: true),

        new SchemaSourceDescriptor(
            "starter", "Starter schemas",
            "Five ready-made schemas to begin from.",
            SchemaSourceKind.Starter,
            SchemaSourceCapability.Import,
            ProducesGuess: false),
    };

    public IReadOnlyList<SchemaSourceDescriptor> All() => Sources;
}
```

- [ ] **Adım 4: Testi çalıştır, geçtiğini gör**

Çalıştır: `dotnet test backend/Namines.Tests --filter SchemaSourceCatalogTests`
Beklenen: 5 test PASS.

- [ ] **Adım 5: Commit (onay sonrası)**

```bash
git add backend/Namines.Core/Sources backend/Namines.Infrastructure/Services/StaticSchemaSourceCatalog.cs backend/Namines.Tests/Sources
git commit -m "feat: add schema source catalog contract"
```

---

### Görev 2: `GET /api/sources` ucu

> ✅ **Bitmiştir.** `GET /api/sources` canlı; `Controllers/SourcesControllerTests.cs` kapsıyor.

**Dosyalar:**
- Oluştur: `backend/Namines.API/Controllers/SourcesController.cs`
- Değiştir: `backend/Namines.API/Extensions/ServiceCollectionExtensions.cs` (DI kaydı)
- Test: `backend/Namines.Tests/Controllers/SourcesControllerTests.cs`

**Arayüzler:**
- Kullanır: Görev 1'in `ISchemaSourceCatalog`, `SchemaSourceDescriptor`.
- Üretir: `GET /api/sources` → `[{ id, displayName, description, kind, capabilities[], producesGuess }]`
  — `kind` ve `capabilities` **string**.

- [ ] **Adım 1: Başarısız testi yaz**

`backend/Namines.Tests/Controllers/SourcesControllerTests.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Namines.API.Controllers;
using Namines.Core.Sources;

namespace Namines.Tests.Controllers;

public class SourcesControllerTests
{
    private sealed class OneSourceCatalog : ISchemaSourceCatalog
    {
        public IReadOnlyList<SchemaSourceDescriptor> All() => new[]
        {
            new SchemaSourceDescriptor(
                "dbconnect", "Database connection", "Read the schema straight from a live database.",
                SchemaSourceKind.Connect,
                SchemaSourceCapability.Import | SchemaSourceCapability.Watch,
                ProducesGuess: false),
        };
    }

    [Fact]
    public void Kind_and_capabilities_cross_the_boundary_as_strings()
    {
        // Backend enum'unu istemcide sayıyla eşlemek bu projede bir kez
        // kırıldı: sunucuya yeni bir değer eklenince istemcideki birebir
        // kopya sessizce yanlış değeri gösterdi. Sözleşme string olursa
        // eklenen değer istemcide TANINMAZ olur — yanlış olmaz.
        var result = new SourcesController(new OneSourceCatalog()).List() as OkObjectResult;

        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result!.Value);

        Assert.Contains("\"kind\":\"connect\"", json);
        Assert.Contains("\"capabilities\":[\"import\",\"watch\"]", json);
        Assert.Contains("\"producesGuess\":false", json);
    }
}
```

- [ ] **Adım 2: Testi çalıştır, başarısız olduğunu gör**

Çalıştır: `dotnet test backend/Namines.Tests --filter SourcesControllerTests`
Beklenen: DERLENMEZ — `SourcesController` yok.

- [ ] **Adım 3: Controller'ı yaz**

`backend/Namines.API/Controllers/SourcesController.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Sources;

namespace Namines.API.Controllers;

/// <summary>
/// github/06-EKLENTI-MIMARISI.md — `+ Add source` menüsünün veri kaynağı.
///
/// <b>Anonim:</b> liste sabit, kullanıcıya göre değişmiyor ve hiçbir şey
/// harcamıyor. Menüyü görebilmek için giriş istemek, henüz hesabı olmayan
/// kullanıcıdan ürünün ne yapabildiğini gizlemek olurdu.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class SourcesController : ControllerBase
{
    private readonly ISchemaSourceCatalog _catalog;

    public SourcesController(ISchemaSourceCatalog catalog) => _catalog = catalog;

    [HttpGet]
    public IActionResult List() => Ok(_catalog.All().Select(s => new
    {
        id = s.Id,
        displayName = s.DisplayName,
        description = s.Description,
        kind = s.Kind.ToString().ToLowerInvariant(),
        capabilities = CapabilityNames(s.Capabilities),
        producesGuess = s.ProducesGuess,
    }));

    /// <summary>
    /// Bayrakları isimlere çevirir. <c>None</c> listeye girmez — "hiçbiri"
    /// bir yetenek değil, yeteneklerin yokluğu.
    /// </summary>
    private static IReadOnlyList<string> CapabilityNames(SchemaSourceCapability capabilities) =>
        Enum.GetValues<SchemaSourceCapability>()
            .Where(c => c != SchemaSourceCapability.None && capabilities.HasFlag(c))
            .Select(c => c.ToString().ToLowerInvariant())
            .ToArray();
}
```

- [ ] **Adım 4: DI kaydını ekle**

`backend/Namines.API/Extensions/ServiceCollectionExtensions.cs` içinde, diğer
tekil servislerin yanına:

```csharp
// Katalog durumsuz ve sabit — her istekte yeniden kurmanın anlamı yok.
services.AddSingleton<ISchemaSourceCatalog, StaticSchemaSourceCatalog>();
```

- [ ] **Adım 5: Testleri çalıştır**

Çalıştır: `dotnet test backend/Namines.Tests --filter SourcesControllerTests`
Beklenen: PASS.

Sonra tüm paket: `dotnet test backend/Namines.Tests`
Beklenen: mevcut testlerin hepsi hâlâ yeşil (yeni testler dahil).

- [ ] **Adım 6: Uygulamayı GERÇEKTEN ayağa kaldır**

```bash
dotnet run --project backend/Namines.API
```
Sonra başka bir kabukta: `curl http://localhost:5000/api/sources`
Beklenen: 6 kaynaklık JSON, `kind` ve `capabilities` string.

> Bu adım atlanamaz. Bu projede 857 test yeşilken uygulama hiç başlamıyordu
> (AGENTS.md); DI kaydı eksik/yanlış olan bir servis tam olarak böyle görünür.

- [ ] **Adım 7: Commit (onay sonrası)**

```bash
git add backend/Namines.API/Controllers/SourcesController.cs backend/Namines.API/Extensions/ServiceCollectionExtensions.cs backend/Namines.Tests/Controllers/SourcesControllerTests.cs
git commit -m "feat: expose the schema source catalog at GET /api/sources"
```

---

### Görev 3: Frontend tipleri ve servis çağrısı

> ✅ **Bitmiştir.** Yazıldı — ama ayrı bir `sourceService.ts` yerine `frontend/services/api.ts` içindeki `/sources` çağrısı olarak; tipler `frontend/types/source.ts`'te.

**Dosyalar:**
- Oluştur: `frontend/types/source.ts`
- Değiştir: `frontend/services/api.ts` (yeni `sourceService`)
- Test: `frontend/services/sourceService.test.ts`

**Arayüzler:**
- Kullanır: `GET /api/sources` (Görev 2).
- Üretir: `SchemaSourceDescriptor` tipi ve `sourceService.catalog(): Promise<SchemaSourceDescriptor[]>`.

- [ ] **Adım 1: Başarısız testi yaz**

`frontend/services/sourceService.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { isKnownSourceKind } from '../types/source';

describe('source catalog contract', () => {
  it('recognises the kinds the server sends today', () => {
    expect(isKnownSourceKind('connect')).toBe(true);
    expect(isKnownSourceKind('import')).toBe(true);
    expect(isKnownSourceKind('starter')).toBe(true);
  });

  it('treats an unknown kind as unknown instead of guessing a group', () => {
    // Sunucu yarın yeni bir grup eklerse, istemci onu YANLIŞ bir gruba
    // koymaktansa tanımadığını bilmeli. Sessiz yanlış gruplama, kullanıcıya
    // "bu kaynak drift takip eder" demek olurdu.
    expect(isKnownSourceKind('webhook')).toBe(false);
  });
});
```

- [ ] **Adım 2: Testi çalıştır, başarısız olduğunu gör**

Çalıştır: `cd frontend && npx vitest run services/sourceService.test.ts`
Beklenen: FAIL — `../types/source` bulunamadı.

- [ ] **Adım 3: Tipleri yaz**

`frontend/types/source.ts`:

```ts
/**
 * github/06-EKLENTI-MIMARISI.md — sunucudaki kaynak kataloğunun istemci yüzü.
 *
 * Alanlar STRING: backend enum'unu sayıyla eşlemek bu projede bir kez kırıldı
 * (backend'e yeni bir değer eklendi, istemcideki birebir kopya güncellenmedi).
 * String sözleşmede tanınmayan değer YANLIŞ değil, bilinmeyen olur.
 */
export const SOURCE_KINDS = ['connect', 'import', 'starter'] as const;
export type SchemaSourceKind = (typeof SOURCE_KINDS)[number];

export const SOURCE_CAPABILITIES = ['import', 'compare', 'watch', 'writeback'] as const;
export type SchemaSourceCapability = (typeof SOURCE_CAPABILITIES)[number];

export interface SchemaSourceDescriptor {
  id: string;
  displayName: string;
  description: string;
  kind: string;
  capabilities: string[];
  producesGuess: boolean;
}

export const isKnownSourceKind = (kind: string): kind is SchemaSourceKind =>
  (SOURCE_KINDS as readonly string[]).includes(kind);
```

- [ ] **Adım 4: Servis çağrısını ekle**

`frontend/services/api.ts` — dosyanın tepesindeki import bloğuna
`import type { SchemaSourceDescriptor } from '../types/source';` eklenir,
`schemaService`'in hemen ardına:

```ts
export const sourceService = {
  /** `+ Add source` menüsünün içeriği. Anonim uç; giriş gerektirmiyor. */
  catalog: async (): Promise<SchemaSourceDescriptor[]> => {
    const response = await api.get<SchemaSourceDescriptor[]>('/sources');
    return response.data;
  },
};
```

- [ ] **Adım 5: Testi çalıştır, geçtiğini gör**

Çalıştır: `cd frontend && npx vitest run services/sourceService.test.ts`
Beklenen: 2 test PASS.

- [ ] **Adım 6: Commit (onay sonrası)**

```bash
git add frontend/types/source.ts frontend/services/api.ts frontend/services/sourceService.test.ts
git commit -m "feat: add the source catalog client contract"
```

---

### Görev 4: `SourceMenu` bileşeni

> ✅ **Bitmiştir.** Yazıldı — bileşen `SourceMenu` yerine `frontend/components/prompt/SourceStrip.tsx` adıyla indi; `SourceStrip.test.tsx` kapsıyor.

**Dosyalar:**
- Oluştur: `frontend/components/prompt/SourceMenu.tsx`
- Test: `frontend/components/prompt/SourceMenu.test.tsx`

**Arayüzler:**
- Kullanır: Görev 3'ün `SchemaSourceDescriptor`.
- Üretir: `<SourceMenu sources={...} onSelect={(id: string) => void} disabled?: boolean />`

**Görsel karar öncesi (FRONTEND.md §0 — zorunlu):**

```bash
python ".claude/skills/ui-ux-pro-max/scripts/search.py" "dropdown menu grouped actions" --domain ux
```
Sonucu göreve not düş; renk için sonucu kullanma (palet FRONTEND.md §2'de sabit).

- [ ] **Adım 1: Başarısız testi yaz**

`frontend/components/prompt/SourceMenu.test.tsx`:

```tsx
import { describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { SourceMenu } from './SourceMenu';
import type { SchemaSourceDescriptor } from '../../types/source';

const sources: SchemaSourceDescriptor[] = [
  { id: 'dbconnect', displayName: 'Database connection', description: 'Live database.',
    kind: 'connect', capabilities: ['import', 'watch'], producesGuess: false },
  { id: 'openapi', displayName: 'OpenAPI / GraphQL URL', description: 'From a spec.',
    kind: 'import', capabilities: ['import'], producesGuess: true },
  { id: 'starter', displayName: 'Starter schemas', description: 'Ready-made.',
    kind: 'starter', capabilities: ['import'], producesGuess: false },
  { id: 'webhook', displayName: 'Something new', description: 'Unknown group.',
    kind: 'webhook', capabilities: ['import'], producesGuess: false },
];

describe('SourceMenu', () => {
  it('stays closed until it is opened', () => {
    render(<SourceMenu sources={sources} onSelect={() => {}} />);
    expect(screen.queryByText('Database connection')).toBeNull();
  });

  it('groups sources by kind', () => {
    render(<SourceMenu sources={sources} onSelect={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: /add source/i }));

    expect(screen.getByText('Connect')).toBeTruthy();
    expect(screen.getByText('Import')).toBeTruthy();
    expect(screen.getByText('Start from')).toBeTruthy();
  });

  it('shows a guess badge only on sources that infer', () => {
    render(<SourceMenu sources={sources} onSelect={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: /add source/i }));

    // "Çıkarım olduğu her ekranda söylenmeli" (second-phase/06).
    expect(screen.getAllByText('guess')).toHaveLength(1);
  });

  it('puts a source of an unknown kind in Import rather than dropping it', () => {
    // Sunucu yeni bir grup eklerse kaynak KAYBOLMAMALI; en muhafazakâr grup
    // "tek seferlik içe aktarma" — sürekli ilişki VAAT ETMEYEN grup.
    render(<SourceMenu sources={sources} onSelect={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: /add source/i }));

    expect(screen.getByText('Something new')).toBeTruthy();
  });

  it('reports the chosen source and closes', () => {
    const onSelect = vi.fn();
    render(<SourceMenu sources={sources} onSelect={onSelect} />);
    fireEvent.click(screen.getByRole('button', { name: /add source/i }));
    fireEvent.click(screen.getByText('OpenAPI / GraphQL URL'));

    expect(onSelect).toHaveBeenCalledWith('openapi');
    expect(screen.queryByText('Database connection')).toBeNull();
  });

  it('closes on Escape', () => {
    render(<SourceMenu sources={sources} onSelect={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: /add source/i }));
    fireEvent.keyDown(document, { key: 'Escape' });

    expect(screen.queryByText('Database connection')).toBeNull();
  });
});
```

- [ ] **Adım 2: Testi çalıştır, başarısız olduğunu gör**

Çalıştır: `cd frontend && npx vitest run components/prompt/SourceMenu.test.tsx`
Beklenen: FAIL — `./SourceMenu` yok.

- [ ] **Adım 3: Bileşeni yaz**

`frontend/components/prompt/SourceMenu.tsx`:

```tsx
'use client';

import React, { useEffect, useRef, useState } from 'react';
import { Plus } from 'lucide-react';
import type { SchemaSourceDescriptor } from '../../types/source';

interface SourceMenuProps {
  sources: SchemaSourceDescriptor[];
  onSelect: (id: string) => void;
  disabled?: boolean;
}

/**
 * Grup başlıkları. "Connect" ile "Import" ayrımı kullanıcıya bir SÖZ veriyor:
 * bağlanan kaynak izlenir, içe aktarılan tek seferliktir
 * (github/06-EKLENTI-MIMARISI.md §4).
 */
const GROUPS: { kind: string; title: string }[] = [
  { kind: 'connect', title: 'Connect' },
  { kind: 'import', title: 'Import' },
  { kind: 'starter', title: 'Start from' },
];

export function SourceMenu({ sources, onSelect, disabled }: SourceMenuProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    const onClick = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onClick);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onClick);
    };
  }, [open]);

  // Tanınmayan bir `kind` DÜŞÜRÜLMEZ, "Import" altına alınır: sunucu yeni bir
  // grup eklediğinde kaynağın kaybolması, yanlış grupta görünmesinden daha
  // kötü — ve "import" hiçbir süreklilik vaat etmeyen en muhafazakâr grup.
  const groupOf = (kind: string) =>
    GROUPS.some(g => g.kind === kind) ? kind : 'import';

  return (
    <div className="relative" ref={containerRef}>
      <button
        type="button"
        disabled={disabled}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
        className="flex items-center gap-1.5 h-7 px-2 rounded-[var(--radius-control)] glass-button text-content-muted hover:text-content-primary transition-all disabled:opacity-50 disabled:cursor-not-allowed"
      >
        <Plus className="w-3.5 h-3.5" />
        <span className="text-xs font-medium">Add source</span>
      </button>

      {open && (
        <div
          role="menu"
          className="absolute left-0 bottom-full mb-2 w-[300px] rounded-[var(--radius-card)] border border-line-strong bg-surface-800/98 backdrop-blur-xl p-2 shadow-2xl z-50 flex flex-col gap-1 animate-dropdown-in"
        >
          {GROUPS.map(group => {
            const items = sources.filter(s => groupOf(s.kind) === group.kind);
            if (items.length === 0) return null;
            return (
              <div key={group.kind} className="flex flex-col">
                <span className="px-2 pt-1.5 pb-1 text-[10px] uppercase tracking-wide text-content-muted">
                  {group.title}
                </span>
                {items.map(source => (
                  <button
                    key={source.id}
                    type="button"
                    role="menuitem"
                    onClick={() => { onSelect(source.id); setOpen(false); }}
                    className="flex flex-col items-start gap-0.5 px-2 py-1.5 rounded-[var(--radius-control)] text-left hover:bg-surface-700/50 transition-colors"
                  >
                    <span className="flex items-center gap-1.5">
                      <span className="text-sm text-content-primary">{source.displayName}</span>
                      {/* Tahmin etiketi katalogdan geliyor; UI'ın hangi kaynağın
                          çıkarım yaptığını ayrıca bilmesi gerekmiyor. */}
                      {source.producesGuess && (
                        <span className="text-[9px] px-1 py-px rounded border border-line-strong text-content-muted">
                          guess
                        </span>
                      )}
                    </span>
                    <span className="text-[11px] text-content-muted">{source.description}</span>
                  </button>
                ))}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Adım 4: Testi çalıştır, geçtiğini gör**

Çalıştır: `cd frontend && npx vitest run components/prompt/SourceMenu.test.tsx`
Beklenen: 6 test PASS.

- [ ] **Adım 5: Tasarım kurallarını doğrula**

Çalıştır: `cd frontend && npm run check:design && npm run lint`
Beklenen: ikisi de temiz.

- [ ] **Adım 6: Commit (onay sonrası)**

```bash
git add frontend/components/prompt/SourceMenu.tsx frontend/components/prompt/SourceMenu.test.tsx
git commit -m "feat: add the add-source menu component"
```

---

### Görev 5: `/new` sayfasına bağlama

> ✅ **Bitmiştir.** `/new` sayfasına bağlandı.

**Dosyalar:**
- Değiştir: `frontend/app/new/page.tsx` (textarea'nın altındaki düğme satırı)

**Arayüzler:**
- Kullanır: `sourceService.catalog()` (Görev 3), `<SourceMenu>` (Görev 4).
- Üretir: — (sayfa içi)

**Davranış sözleşmesi — DEĞİŞMEYECEK olanlar:**

| Menü seçimi | Bugünkü davranışın aynısı |
|---|---|
| `openapi` | `setShowUrlInput(true)` — aynı URL alanı, aynı "bu bir tahmin" notu |
| `image` | `fileInputRef.current?.click()` — aynı dosya seçici, aynı önizleme |
| `code` / `dbconnect` / `jsonshape` / `starter` | Bu ekranda karşılığı yok; canvas'ta var. Toast ile söylenir |

- [ ] **Adım 1: Katalog çağrısını ekle**

`models` useEffect'inin hemen altına:

```tsx
const [sources, setSources] = useState<SchemaSourceDescriptor[]>([]);

// Kaynak listesi SUNUCUDAN: hangi kaynağın hangi grupta olduğunu istemcide
// tekrar yazmak, iki kaynağın ayrışması demekti (model listesiyle aynı
// gerekçe). Liste alınamazsa menü gizleniyor ve eski düğmeler yerinde
// kalıyor — menüyü kaybetmek şema üretimini engellememeli.
useEffect(() => {
  let cancelled = false;
  sourceService.catalog()
    .then(list => { if (!cancelled) setSources(list); })
    .catch(() => { /* sessiz: menü olmadan da üretim çalışıyor */ });
  return () => { cancelled = true; };
}, []);
```

- [ ] **Adım 2: Seçim yönlendiricisini ekle**

```tsx
const handleSourceSelect = (id: string) => {
  if (id === 'openapi') { setShowUrlInput(true); return; }
  if (id === 'image') { fileInputRef.current?.click(); return; }
  // Geri kalanı canvas'ta yaşıyor. Sessizce hiçbir şey yapmamak, menüyü
  // bozuk gösterirdi.
  showToast('Available on the canvas — open a schema first.', 'info');
};
```

- [ ] **Adım 3: Düğme satırını değiştir**

`absolute bottom-2.5 right-2.5` kabındaki **link** ve **image** düğmelerini
kaldır; yerine `SourceMenu` gelir, `VoiceRecorder` KALIR:

```tsx
<div className="absolute bottom-2.5 left-2.5 right-2.5 flex items-center justify-between gap-1.5">
  {sources.length > 0
    ? <SourceMenu sources={sources} onSelect={handleSourceSelect} disabled={isGenerating} />
    : <span />}
  <VoiceRecorder
    disabled={isGenerating}
    onTranscription={(text) => setPrompt(prev => prev ? `${prev} ${text}` : text)}
  />
</div>
```

`fileInputRef`'e bağlı gizli `<input type="file">` **silinmez**, bu kabın
dışına (form'un içine) taşınır — menü onu tetiklemeye devam ediyor.

- [ ] **Adım 4: Testleri ve kontrolleri çalıştır**

```bash
cd frontend && npx vitest run && npm run check:design && npm run lint
```
Beklenen: hepsi yeşil.

- [ ] **Adım 5: CANLI doğrula**

```bash
cd frontend && npm run dev
```
`/new` sayfasında sırayla:

1. `+ Add source` açılıyor, üç grup görünüyor.
2. `OpenAPI / GraphQL URL` → URL alanı açılıyor, "it's a guess" notu duruyor.
3. Bir URL girip şema üret → **eskisiyle aynı sonuç**.
4. `Image` → dosya seçici açılıyor, önizleme çıkıyor, üretim çalışıyor.
5. Mikrofon düğmesi hâlâ çalışıyor.
6. `Code files` → toast çıkıyor.
7. `Esc` ve dışarı tıklama menüyü kapatıyor.
8. 375px genişlikte: `Generate` düğmesi hâlâ ekranda ve tıklanabilir
   (bu satır bir kez taşmıştı — page.tsx'teki `flex-wrap` yorumuna bak).

Ekran görüntüsü al; 2, 4 ve 8 kanıtlanmadan görev bitmiş sayılmaz.

- [ ] **Adım 6: Commit (onay sonrası)**

```bash
git add frontend/app/new/page.tsx
git commit -m "feat: replace the prompt icon row with the add-source menu"
```

---

## Öz-denetim

- **Spec kapsamı:** 06 §3'ün tanımlayıcı kısmı → Görev 1; §4'ün menüsü ve
  grup ayrımı → Görev 4-5; "tahmin etiketi her ekranda" → Görev 1 (bayrak) +
  Görev 4 (rozet). §3'ün çalıştırılabilir sözleşmesi bilerek F1'e bırakıldı
  (yukarıdaki kapsam kararı 1).
- **Yer tutucu taraması:** yok — her adımda çalıştırılacak komut ve yazılacak
  kod tam hâliyle duruyor.
- **Tip tutarlılığı:** alan adları backend (Görev 1) → JSON (Görev 2) → TS
  (Görev 3) → bileşen (Görev 4) boyunca aynı: `id`, `displayName`,
  `description`, `kind`, `capabilities`, `producesGuess`.
