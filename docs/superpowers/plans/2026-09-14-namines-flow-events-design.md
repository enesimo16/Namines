# Namines Flow — Event Sistemi Tasarım Dokümanı

> Bite-sized TDD planı değil; mimari kararlar netleşsin diye yazılan
> tasarım dokümanı. Onaydan sonra `superpowers:writing-plans` ile gerçek
> plana dönüştürülür.

**Sorun:** Canvas'ta (`frontend/store/useSchemaStore.ts`) `addTable`/
`deleteTable` gibi işlemler doğrudan senkron Zustand mutasyonu — dinleyen
bir event/hook sistemi yok. Backend'de de bunu dinleyen bir trigger/queue
yok; tek gerçek zamanlı mekanizma SignalR'ın kaba `ReceiveSchema` broadcast'i
(tüm şemayı gönderiyor, "hangi tablo eklendi/silindi" bilgisini taşımıyor).

**Hedef:** "Tabloya eklenti yapıldığında şunu çalıştır, silinince şunu
çalıştır" türü kurallar — hem canvas üzerinden tanımlanabilir hem de
sistemsel (backend) olarak güvenilir şekilde tetiklenir olsun.

## Event Kontratı (ortak, frontend+backend)

**Yeni dosya:** `frontend/types/flowEvents.ts` (frontend) ve
`backend/Namines.Core/Events/SchemaEvents.cs` (backend) — aynı event
tiplerinin iki dildeki karşılığı, elle senkron tutulur (kod üretimi ile
otomatikleştirmek v2 konusu):

```ts
type SchemaEvent =
  | { type: "TableAdded"; tableId: string; table: SchemaTable }
  | { type: "TableDeleted"; tableId: string; tableName: string }
  | { type: "TableUpdated"; tableId: string; changes: Partial<SchemaTable> }
  | { type: "ColumnAdded"; tableId: string; columnId: string; column: SchemaColumn }
  | { type: "ColumnDeleted"; tableId: string; columnId: string }
  | { type: "RelationAdded"; relationId: string; relation: SchemaRelation }
  | { type: "RelationDeleted"; relationId: string };
```

## 1) Canvas Tarafı — Event Bus + Otomasyon Kuralları

**Neden mevcut store'u kırmadan:** `useSchemaStore` zaten undo/redo
(`_past`/`_future`) ile çalışıyor; event emisyonunu store mutasyonlarının
**içine** eklemek (ayrı bir yeniden yazım değil) en düşük riskli yol.

**Yeni dosya:** `frontend/lib/flowEventBus.ts` — basit bir pub/sub
(bağımlılık eklemeden, ~20 satır `Map<string, Set<Listener>>`).

**Modify:** `frontend/store/useSchemaStore.ts` — `addTable`, `deleteTable`,
ve benzer mutasyon fonksiyonlarının **sonunda** (state `set()` edildikten
hemen sonra) ilgili event `flowEventBus.emit(...)` ile yayınlanır. Mevcut
davranış (state şekli, undo/redo) değişmez; sadece yan etki olarak event
eklenir.

**Yeni dosya:** `frontend/store/useAutomationStore.ts` — kullanıcının
canvas üzerinde tanımladığı kuralları tutan ayrı bir Zustand store:

```ts
interface AutomationRule {
  id: string;
  trigger: "TableAdded" | "TableDeleted" | "ColumnAdded" | "ColumnDeleted";
  scope: { tableId?: string };  // boşsa global (her tabloda tetiklenir)
  action:
    | { type: "RunSeedGeneration" }
    | { type: "RunDbaCheck" }
    | { type: "CallWebhook"; url: string }
    | { type: "ShowNotification"; message: string };
  enabled: boolean;
}
```

**Yeni UI:** `frontend/components/canvas/AutomationPanel.tsx` — sağ panelde
"Bu tabloya eklenti/silme olursa..." kuralları tanımlama arayüzü. Var olan
sağ panel bileşenlerinin (`frontend/components/canvas/*`) yanına eklenir,
mevcut layout'u bozmaz.

**Bağlayıcı:** `frontend/hooks/useAutomationRunner.ts` — `flowEventBus`'a
subscribe olur, gelen her event için `useAutomationStore`'daki eşleşen
kuralları bulur ve action'ı çalıştırır (örn. webhook için `fetch`,
notification için mevcut toast sistemi).

## 2) Backend Tarafı — Gerçek Event Emisyonu ve Kalıcı Otomasyonlar

**Sorun:** Backend şemayı sadece "tüm şema" olarak alıyor/kaydediyor; hangi
tablonun eklendiğini/silindiğini bilmiyor. Bunu bilmesi için **diff'leme**
gerekiyor.

**Yeni dosya:** `backend/Namines.Core/Diffing/SchemaDiffer.cs` — iki şema
snapshot'ı (`SchemaBefore`, `SchemaAfter`) alıp `List<SchemaEvent>` üretir
(zaten `MigrationPromptBuilder`'ın "eski/yeni şema diff'i" için benzer bir
ihtiyacı olduğu görüldü — muhtemelen kısmen yeniden kullanılabilir, bu
implementasyon sırasında doğrulanmalı).

**Tetiklenme noktası:** Şema kaydetme endpoint'i (canvas'tan "save" veya
otomatik kaydetme çağrısı) — kaydetmeden önce eski şema ile yeni şema
`SchemaDiffer`'dan geçirilir, üretilen event'ler bir **iç event bus'a**
yayınlanır.

**Yeni dosya:** `backend/Namines.Infrastructure/Events/SchemaEventBus.cs`
— basit in-process pub/sub (MediatR eklemek istenirse `INotification`
pattern'i de kullanılabilir; proje şu an MediatR kullanmıyor, bu yüzden
başta minimal bir in-process çözüm önerilir, MediatR'a geçiş v2).

**Yeni dosya:** `backend/Namines.Core/Models/AutomationRule.cs` — EF Core
entity, canvas'taki `AutomationRule` tipinin backend kalıcı karşılığı:
`Id, ProjectId, Trigger, ScopeTableId (nullable), ActionType, ActionConfigJson, Enabled`.

**Migration:** Yeni `AutomationRules` tablosu için EF Core migration
(`backend/Namines.Infrastructure/Migrations/` altına).

**Yeni dosya:** `backend/Namines.Infrastructure/Automation/AutomationExecutor.cs`
— `SchemaEventBus`'a subscribe olur, gelen her event için ilgili
projedeki `AutomationRule`'ları DB'den çeker, eşleşenleri **arka planda**
(mevcut altyapıda hosted service / background job yapısı varsa onu
kullanır, yoksa `IHostedService` tabanlı basit bir kuyruk) çalıştırır:
webhook çağrısı, `MockDataPromptBuilder` üzerinden seed üretimi tetikleme,
`DbaPromptBuilder` üzerinden analiz tetikleme, ya da SignalR üzerinden
kullanıcıya bildirim.

**Loglama:** Çalıştırılan her otomasyon `AutomationRunLog` (ProjectId,
RuleId, TriggeredAt, Status, ErrorMessage) olarak kaydedilir — Desk'in
zaten var olan "Logs" sekmesinde gösterilebilir (Explore agent'ının
bulduğu `namines_desk` dokümanlarına göre bu sekme zaten planlanmış).

## Sıralama Önerisi

1. Event kontratını tanımla (frontend+backend tipleri) — kod yazmadan
   önce iki tarafın da aynı sözleşmeye uyması gerekiyor.
2. Canvas event bus + store'a entegrasyon (izole, backend'e dokunmaz,
   hemen test edilebilir).
3. Canvas otomasyon UI + runner (frontend-only, webhook/notification
   action'larıyla uçtan uca demo edilebilir).
4. Backend `SchemaDiffer` + `SchemaEventBus` (izole, henüz UI'a bağlı
   değil, unit test edilebilir).
5. `AutomationRule` persistence + `AutomationExecutor` + loglama.
6. Desk "Logs" sekmesine `AutomationRunLog` gösterimi.

Her adım kendi başına test edilebilir/demo edilebilir bir teslim üretir —
bu yüzden bunu tek plan yerine yukarıdaki 6 adımı ayrı görevler olarak
bite-sized plana dökmek daha güvenli.

## Kapsam Dışı (v1 için)

- Gerçek zamanlı dağıtık kuyruk (Kafka/RabbitMQ) — proje ölçeği için
  in-process event bus + basit background job yeterli; harici kuyruk
  eklemek şu an YAGNI.
- Action tiplerinde kod çalıştırma (arbitrary script execution) — güvenlik
  riski yüksek, kapsam dışı bırakılmalı; sadece önceden tanımlı action
  tipleri (webhook, seed, dba-check, notification) desteklenmeli.
