# Namines Flow — Spec

**Durum:** Onaylandı (brainstorming sürecinde bölüm bölüm onaylandı) — kullanıcı incelemesi bekleniyor.

**Sorun:** Canvas'ta (`frontend/store/useSchemaStore.ts`) `addTable`/
`deleteTable` gibi işlemler doğrudan senkron Zustand mutasyonu — dinleyen
bir event/hook sistemi yok. Backend'de de bunu dinleyen bir trigger/queue
yok; tek gerçek zamanlı mekanizma SignalR'ın kaba `ReceiveSchema`
broadcast'i (tüm şemayı gönderiyor, "hangi tablo eklendi/silindi" bilgisini
taşımıyor). Kullanıcı canvas üzerinden "şu olursa şunu çalıştır" (tablo
eklenince webhook çağır, tablo silinince uyar, vb.) tanımlayabilmeli.

**Hedef:** Hem istemci tarafında anlık tepkiler (toast, görsel ipucu) hem
sunucu tarafında kalıcı aksiyonlar (webhook, DBA kontrolü, örnek veri
üretimi) veren, canvas üzerinde görsel olarak tanımlanan bir otomasyon
sistemi.

**Mimari karar:** Hibrit yaklaşım (Yaklaşım A) — istemci tarafı olaylar
anında, ağa çıkmadan tetiklenir; sunucu tarafı aksiyonlar mevcut 30
saniyelik bulut senkronu sırasında, sunucunun **kendi hesapladığı**
diff'e göre tetiklenir (istemcinin "şunu yaptım" iddiasına güvenilmez).
Alternatif olarak değerlendirilen "tam istemci güdümlü, anında" (B)
güvenlik gerekçesiyle (sunucu doğrulamasız istemci iddiasına güvenir),
"gerçek DB trigger/CDC" (C) ise aşırı mühendislik gerekçesiyle reddedildi
— kod tabanı zaten JSON-diff desenini kullanıyor (`SchemaDiffResult`).

**Kritik bulgu:** `IMigrationService.CalculateDiffAsync(oldSchema,
newSchema, engine)` zaten var ve `AddedTables`, `RemovedTables`,
`AddedColumns`/`RemovedColumns`/`ModifiedColumns` (tablo başına),
`AddedRelations`/`RemovedRelations` üretiyor — altı tetikleyici tipinin
tamamı. Yeni bir diff motoru yazılmayacak, bu servis yeniden kullanılacak.
`SsrfGuard` (`backend/Namines.Core/Security/SsrfGuard.cs`) webhook URL
doğrulaması için hazır — yeni bir SSRF koruması yazılmayacak.

---

## Bölüm 1: Event Kontratı

### İstemci tarafı (anında tepkiler)

Yeni dosya: `frontend/lib/flowEventBus.ts` — basit bir pub/sub
(bağımlılık eklemeden, ~20 satır `Map<string, Set<Listener>>`).

```ts
export type FlowEvent =
  | { type: "TableAdded"; tableId: string; tableName: string }
  | { type: "TableDeleted"; tableId: string; tableName: string }
  | { type: "ColumnAdded"; tableId: string; columnId: string; columnName: string }
  | { type: "ColumnDeleted"; tableId: string; columnId: string }
  | { type: "ColumnChanged"; tableId: string; columnId: string }
  | { type: "RelationAdded"; relationId: string }
  | { type: "RelationDeleted"; relationId: string };
```

`useSchemaStore.ts`'teki `addTable`, `deleteTable` ve yeni eklenecek
kolon/ilişki mutasyon fonksiyonlarının **sonunda** (state `set()`
edildikten hemen sonra) ilgili olay `flowEventBus.emit(...)` ile
yayınlanır. Mevcut davranış (state şekli, undo/redo) değişmez; event
emisyonu yan etki olarak eklenir.

Bu olaylar **yalnızca** aynı tarayıcı oturumundaki anlık tepkiler için
kullanılır (toast, `AutomationNode` üzerinde kısa "tetiklendi"
animasyonu) — ağa hiç çıkmaz.

### Sunucu tarafı (kalıcı aksiyonlar)

Yeni bir kanal açılmıyor; mevcut proje senkron uç noktası
(`AuthController`'daki proje sync akışı, `/api/auth/projects`)
genişletiliyor. Akış zaten `existing.SchemaJson`'ı (eski) `projDto`
(yeni) ile değiştirmeden önce elinde tutuyor:

```csharp
var oldSchema = JsonSerializer.Deserialize<DatabaseSchema>(existing.SchemaJson);
var newSchema = JsonSerializer.Deserialize<DatabaseSchema>(projDto.SchemaJson);
var diff = await _migrationService.CalculateDiffAsync(oldSchema, newSchema, engine);
// ... existing.SchemaJson = projDto.SchemaJson; SaveChangesAsync(); ...
await _automationExecutor.RunAsync(existing.Id, diff, ct);  // SaveChangesAsync SONRASI, fire-and-forget
```

**Kural:** Sunucu, istemcinin "şunu ekledim" demesine güvenmiyor — kendi
diff'ini kendisi hesaplıyor. İstemci yalnızca kuralın kendisini (hangi
tabloya, hangi tetikleyici, hangi aksiyon) tanımlıyor.

**Bilinen taviz:** Sunucu tarafı aksiyonlar (webhook/DBA/seed) mevcut
30 saniyelik `CLOUD_SYNC_THROTTLE_MS` içinde tetiklenir — en fazla ~30sn
gecikme. İstemci tarafı tepkiler (toast) anındadır. Kullanıcı onayladı:
bu gecikme kabul edilebilir, throttle bypass edilmeyecek.

---

## Bölüm 2: Canvas UI — `AutomationNode`

**Yeni node tipi:** `frontend/components/canvas/nodes/AutomationNode.tsx`
— `frontend/app/canvas/page.tsx:335`'teki `nodeTypes` haritasına
`automationNode: AutomationNode` olarak eklenir. Sarı/amber bir ⚡
kutucuğu, ilgili `TableNode`'a düz, kesikli bir çizgiyle bağlanır (yeni
bir edge tipi — `RelationEdge`'den görsel olarak ayrışmalı, veritabanı
ilişkisiyle karıştırılmamalı).

**Ekleme yolu:** Kullanıcı bir tabloyu seçip sağ tık context menüsünden
"Add automation" der (mevcut `canvas-context-menu` deseniyle tutarlı) —
yeni bir `automationNode` tabloya bağlı olarak canvas'a eklenir.

**Yapılandırma:** Node'a tıklanınca `TableEditorDrawer` deseniyle
tutarlı bir çekmece açılır:
- Tetikleyici tipi (Bölüm 1'deki 6 tip, tabloya özel olanlar için tablo
  zaten node'un bağlı olduğu tablo — ayrıca seçtirilmez)
- Aksiyon tipi (Webhook / DBA kontrolü / Örnek veri üretimi / Bildirim)
- Aksiyona özel alanlar (webhook için URL alanı)

Node üzerinde özet metin durur (örn. "On Delete → Webhook").

**Silme:** Node silinince bağlı `AutomationRule` kaydı da silinir —
throttle'a takılmadan, Bölüm 3'teki ayrı CRUD uç noktası üzerinden
anında.

---

## Bölüm 3: Backend Depolama ve Orkestrasyon

### Veri modeli

Yeni dosya: `backend/Namines.Core/Models/AutomationRule.cs`

```csharp
public class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProjectId { get; set; } = string.Empty;    // FK → CloudProjects.Id, ON DELETE CASCADE
    public string? ScopeTableId { get; set; }                 // null = proje genelinde
    // Kolon/ilişki tetikleyicilerinde ScopeTableId yalnızca "hangi TABLO"yu
    // süzer, "hangi KOLON/İLİŞKİ"yi değil — o tabloya eklenen/silinen HER
    // kolon/ilişki bu kuralı tetikler. Tek bir kolonu hedefleyen kural v1
    // kapsamı dışında (bkz. Kapsam Dışı).
    public string TriggerType { get; set; } = string.Empty;   // "TableAdded" | "TableDeleted" | "ColumnAdded" | "ColumnDeleted" | "ColumnChanged" | "RelationAdded" | "RelationDeleted"
    public string ActionType { get; set; } = string.Empty;    // "Webhook" | "DbaCheck" | "SeedData" | "Toast"
    public string ActionConfigJson { get; set; } = "{}";       // örn. {"url": "https://..."}
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AutomationRunLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RuleId { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = string.Empty;   // "Success" | "Failed" | "Skipped"
    public string? ErrorMessage { get; set; }
    public string? ResultSummary { get; set; }            // DBA/seed sonucunun kısa özeti
}
```

EF Core migration ile `AutomationRules` ve `AutomationRunLogs` tabloları
eklenir (`CloudProjects` FK'lı, `ON DELETE CASCADE`).

### CRUD

Yeni `AutomationController`:
- `GET /api/automation/rules?projectId=...`
- `POST /api/automation/rules` (node eklenince/düzenlenince)
- `DELETE /api/automation/rules/{id}` (node silinince)

Bu uç noktalar throttle'a girmez — node ekleme/silme/düzenleme kullanıcı
deneyiminde **anında** yansımalı (kuralın kendisi, kuralın tetiklenmesi
değil).

### Yürütme motoru

Yeni dosya: `backend/Namines.Infrastructure/Services/AutomationExecutor.cs`

```csharp
public interface IAutomationExecutor
{
    Task RunAsync(string projectId, SchemaDiffResult diff, CancellationToken ct = default);
}
```

`AuthController`'ın sync akışından, `SaveChangesAsync()` başarılı
olduktan **sonra**, isteğin yanıtını bloklamadan (mevcut arka plan
görev deseni varsa o kullanılır; yoksa basit bir kuyruklanmış
`IHostedService`) çağrılır.

Diff'teki her `AddedTables`/`RemovedTables`/`ModifiedTables[].AddedColumns`/
... girdisi, o projenin **etkin** (`Enabled = true`) `AutomationRule`'larıyla
`TriggerType` + (varsa) `ScopeTableId` üzerinden eşleştirilir. Eşleşen her
kural için:

- **Webhook:** `SsrfGuard.IsUrlSafe(url)` ile **her çalıştırmada yeniden**
  doğrulanır (kaydedilen bir URL zamanla farklı bir IP'ye çözülebilir —
  DNS rebinding). Geçmezse kural atlanır, `AutomationRunLog`'a
  `Status=Skipped` yazılır. Geçerse 5 saniye timeout'lu bir `HttpClient`
  POST'u: gövde `{ trigger, table, timestamp }`.
- **DBA kontrolü:** `GroqAIService.AnalyzeSchemaDbaAsync(schema, dbType)`
  çağrılır, sonuç `AutomationRunLog.ResultSummary`'ye özetlenerek yazılır.
- **Örnek veri üretimi:** `IAIService.GenerateMockDataAsync(schema)`
  çağrılır, üretilen SQL `AutomationRunLog`'a yazılır. **v1'de otomatik
  veritabanına yazılmaz** — kullanıcı sonucu görür, isterse elle uygular
  (Vault/Ground'a otomatik yazmak, kullanıcının onayı olmadan üretim
  verisini değiştirmek riski taşırdı).
- **Bildirim (Toast):** Sunucuda **hiç işlenmez** — bu aksiyon türü
  yalnızca istemci `flowEventBus`'ında dinlenir, kaydı/çalıştırması
  backend'e hiç düşmez.

Bir kuralın aksiyonu başarısız olursa (webhook 5xx, AI hatası) diğer
kurallar etkilenmez — her kural kendi try/catch'i içinde çalışır ve
hata `AutomationRunLog`'a yazılır, tüm senkron isteğini düşürmez.

---

## Bölüm 4: Test Stratejisi

- **Eşleştirme mantığı** (`SchemaDiffResult` → hangi kurallar tetiklenir)
  saf, deterministik bir fonksiyona ayrılır (`AutomationRuleMatcher`
  veya benzeri) ve gerçek DB/HTTP gerektirmeden birim testlerle
  kilitlenir.
- **SsrfGuard entegrasyonu:** özel/loopback bir webhook URL'inin HER
  ZAMAN reddedildiğini doğrulayan testler (mevcut
  `DbHostAccessPolicyTests.cs` deseniyle tutarlı stil).
- **`AutomationExecutor` dayanıklılığı:** sahte `IAIService`/`HttpClient`
  ile "bir kuralın webhook'u başarısız olursa diğer kurallar yine de
  çalışır" senaryosu.
- **Frontend:** `flowEventBus`'ın `addTable`/`deleteTable` çağrıldığında
  doğru olayı yaydığını doğrulayan `useSchemaStore.test.ts` genişlemesi;
  `AutomationNode` için `ProductionScreen.test.tsx` deseniyle tutarlı bir
  bileşen testi.

---

## Kapsam Dışı (v1 için)

- Throttle bypass — sunucu tarafı aksiyonlar için ~30sn gecikme kabul
  edildi (kullanıcı onayı).
- Örnek verinin otomatik olarak gerçek veritabanına yazılması — yalnızca
  üretilip gösteriliyor.
- Kolon/ilişki tetikleyicileri için canvas'ta ayrı bir görsel gösterim
  (yalnızca tablo bazlı `AutomationNode` var; kolon/ilişki tetikleyicileri
  aynı node üzerinden, tetikleyici tipi seçilerek tanımlanır — ayrı bir
  "kolon node'u" yok).
- Sunucu tarafı "Toast" işleme — bu aksiyon tamamen istemci tarafı.
