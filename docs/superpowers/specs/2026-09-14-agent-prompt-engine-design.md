# Namines Agent Prompt Engine — Spec

**Durum:** Onaylandı (brainstorming sürecinde bölüm bölüm onaylandı) — kullanıcı incelemesi bekleniyor.

> ✅ **Bitmiştir** — ama plandaki isimlerle değil. Burada öngörülen ayrı
> `SchemaAgentOrchestrator` ve `SchemaValidator` sınıfları yazılmadı; aynı iş
> `backend/Namines.Infrastructure/Services/SchemaAgentPipeline.cs` (plan →
> taslak → doğrula → onar hattı) ve doğrulama kapısı olarak mevcut
> `NslValidator` ile yapıldı. Plan turu `AgentPlanPromptBuilder`, araçlar
> `Infrastructure/AI/Agent/AgentTools.cs`, tool-calling `IAgentChatClient`.
> Uygulama planı: [`../plans/2026-09-14-agent-orchestration-plan.md`](../plans/2026-09-14-agent-orchestration-plan.md).
> (2026-09-26'da koddan doğrulandı.)

**Sorun:** `GroqAIService.cs` ve `backend/Namines.Core/Prompts/*` altındaki tüm
şema üretimi tek turlu (system+user → tek istek → JSON parse → kör retry).
Şema kontratı (`tables/columns/relations`) computed column, view, trigger,
stored procedure, composite index, check constraint gibi "üst düzey" DDL
kavramlarını hiç taşımıyor — model bunları üretmek istese bile format izin
vermiyor.

**Hedef:** Şema kontratını üst düzey DDL kavramlarına genişletmek ve tek
turlu üretimi, LLM'in kendi aracılarını (tool-calling) kullanarak kendi
çıktısını doğrulayıp onardığı bir agent döngüsüne çevirmek. Kapsam: yeni
şema üretimi **ve** mevcut şemanın revizyonu (örn. "şu tabloya trigger
ekle").

**Mimari karar:** Tek agent, tam mesaj geçmişi + tool-calling (Yaklaşım A —
alternatif olarak değerlendirilen "durum makinesi, tool-calling yok" (B) ve
"çoklu-agent, yapı/DDL-sözdizimi ayrımı" (C) YAGNI gerekçesiyle
reddedildi: B, kullanıcının istediği tool-calling esnekliğini sağlamıyor;
C, tek agent + tool-calling'in zaten karşıladığı bir ayrımı gereksiz yere
çoğaltıyor).

---

## Bölüm 1: Şema Kontratı ve Veri Modeli

### Yeni JSON alanları

`SchemaPromptBuilder`/`RevisionPromptBuilder` çıktısı ve backend
`SchemaTable`/`SchemaColumn` modellerine eklenir:

```jsonc
{
  "tables": [{
    "columns": [{
      // mevcut: id, name, type, length, isPK, isFK, isNullable, defaultValue
      "computedExpression": "string | null",  // örn. "price * quantity"
      "isPersisted": "bool"
    }],
    "checkConstraints": [{ "id", "expression" }],   // örn. "age >= 0"
    "indexes": [{ "id", "columnIds": [], "isUnique": "bool" }]
  }],
  "triggers": [{
    "id", "tableId", "timing": "Before|After", "event": "Insert|Update|Delete",
    "targetEngine": "Postgres|MySQL|MSSQL|MariaDB|Oracle|SQLite",
    "body": "string"
  }],
  "storedProcedures": [{
    "id", "name", "targetEngine", "parameters": [{ "name", "type" }], "body": "string"
  }]
}
```

### Motor kısıtı

`triggers`/`storedProcedures` kullanıcı hedef DB motorunu seçmeden
**üretilmez**. Sistem promptu, motor bilgisi yoksa bu alanları boş bırakır
ve kullanıcıya "trigger/SP üretmek için önce hedef veritabanı motorunu
seçin" uyarısı döner. Bu kısıt, agentic mod toggle'ı (Bölüm 3) ile
birleşerek: toggle açıkken hedef motor seçimi **zorunlu** hale gelir.

### Etkilenen dosyalar

- Modify: `backend/Namines.Core/Prompts/SchemaPromptBuilder.cs`,
  `RevisionPromptBuilder.cs`
- Modify: `backend/Namines.Core/Models/*` (SchemaTable/SchemaColumn — gerçek
  dosya adı implementasyon sırasında doğrulanacak)
- Modify: `frontend/types/flow.ts`, `frontend/lib/schemaToFlow.ts`,
  `frontend/lib/flowToSchema.ts`
- Modify: `backend/Namines.Infrastructure/Generators/DdlGenerator/*` — yeni
  alanları gerçek DDL'e çevirme (computed column sözdizimi, composite
  INDEX, CHECK, trigger/SP motor-özgü şablonlar)

---

## Bölüm 2: Orkestrasyon Akışı (Mesaj Geçmişi + Tool-Calling)

### Akış

1. **Plan turu:** Kullanıcı isteği → kısa bir prompt ile "hangi tablolar,
   hangi üst-düzey yapılar (computed/index/trigger/SP) üretilecek" planı
   çıkarılır. Bu, mesaj geçmişinin ilk asistan mesajı olur.
2. **Üretim turu:** Plan bağlamıyla birlikte tam şema JSON'u istenir
   (Bölüm 1'deki genişletilmiş format).
3. **Agent kendi doğrulamasını yapar (tool-calling):** Model, üretim
   sonrası kendi isteğiyle şu araçları çağırabilir:
   - `validate_schema(schema)` → deterministik `SchemaValidator`'ı
     çalıştırır, hata listesi döner
   - `get_column_info(tableId, columnId)` → bir kolonun var olup
     olmadığını/tipini sorgular (computed expression/FK yazarken
     halüsinasyonu azaltır)
   - `preview_ddl(schema, targetEngine)` → `DdlGeneratorFactory`'den
     geçirip gerçek DDL önizlemesi + sözdizimi hatası döner
4. **Onarım turu:** `validate_schema` veya `preview_ddl` hata dönerse,
   model bu sonuçları (tool response olarak) görür ve düzeltilmiş şemayı
   üretir. Mesaj geçmişine eklenerek devam eder — model kendi önceki
   hatasını **görür** (mevcut kör-retry'nin aksine).
5. **Tur sınırı:** Onarım turu kotası (Bölüm 4) dolarsa, **en son üretilen
   şema + kalan doğrulama hataları** kullanıcıya döner
   (`CompletedWithWarnings`).
6. **Güvence katmanı:** Tool-calling isteğe bağlıdır — model tool
   çağırmasa bile orkestratör her üretim/onarım turu sonrası
   `SchemaValidator`'ı **otomatik olarak** çalıştırır. Tool-calling tek
   güvence noktası değildir.

### Yeni dosyalar

- `backend/Namines.Infrastructure/AI/Agent/SchemaAgentOrchestrator.cs` —
  döngüyü yönetir, `IAIService`'i tool-calling destekli yeni bir metod
  üzerinden çağırır.
- `backend/Namines.Infrastructure/AI/Agent/SchemaValidator.cs` —
  deterministik kontroller (FK/isim çakışması/computed referans/motor
  uyumluluğu).
- `backend/Namines.Infrastructure/AI/Agent/AgentTools.cs` — tool tanımları
  (JSON schema) + `validate_schema`/`get_column_info`/`preview_ddl`
  handler'ları.
- `backend/Namines.Core/Prompts/AgentPlanPromptBuilder.cs` — plan turu için
  ayrı, kısa prompt.

### Modify

- `backend/Namines.Infrastructure/AI/GroqAIService.cs` — `List<ChatMessage>
  messages` + `tools` parametresi destekleyen yeni overload
  (`GenerateSchemaAgenticAsync`). Mevcut tek-turlu metodlar değişmeden
  kalır (geriye dönük uyumluluk).

### Not: Gemini proxy uyumluluğu

Groq'un OpenAI-uyumlu API'si `tools`/`tool_choice` destekliyor. Gemini
proxy yolunda (mevcut kod model adına göre yönlendiriyor) aynı format
kullanılabilir, ancak Groq'un Gemini proxy'si bu parametreyi sessizce yok
sayabilir — implementasyon sırasında ayrıca doğrulanmalı. Doğrulanamazsa,
agentic mod (v1'de) sadece Groq-native modellerle sınırlanır.

---

## Bölüm 3: API / Frontend Entegrasyonu

### Backend endpoint

- Modify: mevcut şema üretim controller'ına yeni endpoint:
  `POST /api/schema/generate-agentic` ve `POST /api/schema/revise-agentic`
  (Bölüm 1'de onaylanan kapsam gereği hem üretim hem revizyon agentic modu
  destekler).
- İstek gövdesi mevcut endpoint'le aynı (kullanıcı isteği + hedef DB
  motoru), farkı sadece agentic döngüyü tetiklemesi.

### Gerçek zamanlı ilerleme (SignalR)

- Yeni Hub metodu (mevcut `CanvasHub`'a eklenir ya da ayrı
  `AgentProgressHub` — implementasyon sırasında `CanvasHub`'ın sorumluluk
  sınırı değerlendirilip karar verilir): orkestratör her adımda
  (`PlanStarted`, `Generating`, `Validating`, `Repairing (tur N/M)`,
  `Completed`, `CompletedWithWarnings`) bir event yayınlar.
- `SchemaAgentOrchestrator` her adım geçişinde `IAgentProgressReporter`
  (yeni arayüz) üzerinden bildirim gönderir — bu arayüz Hub'a bağımlı
  değildir, test edilebilir kalır.

### Frontend

- Modify: şema üretim formunun bulunduğu bileşen — "Gelişmiş
  (trigger/computed/index)" etiketli bir **toggle** eklenir. Toggle
  açıkken istek `generate-agentic`/`revise-agentic` endpoint'ine gider ve
  hedef DB motoru seçimi **zorunlu** hale gelir.
- Yeni dosya: `frontend/components/schema/AgentProgressIndicator.tsx` —
  SignalR event'lerini dinleyip adım adım ilerleme gösterir
  (`Planlanıyor → Üretiliyor → Doğrulanıyor → Onarılıyor (2/4) →
  Tamamlandı`).
- Yeni dosya: `frontend/hooks/useAgentProgress.ts` — Hub bağlantısını ve
  event state'ini yönetir (mevcut `useMultiplayer.ts` deseniyle tutarlı).
- Sonuç `CompletedWithWarnings` ile dönerse, kalan doğrulama hataları
  UI'da açıkça listelenir — sessizce yutulmaz.

---

## Bölüm 4: Kota / Hata Yönetimi ve Test Stratejisi

### Kota

- Yeni alan: `PlanQuotas.MaxAgentRepairTurns` (ücretsiz plan: 2, ücretli
  plan: 4).
- Her tur (plan/üretim/onarım/tool-call) `AiUsageTracker`'a ayrı bir "AI
  call" olarak işlenir.
- Kota **sadece onarım turlarını** sınırlar — plan turu, ilk üretim turu ve
  tool-call'lar sabit maliyet olarak kabul edilir, kullanıcı sürpriz
  kesintiyle karşılaşmaz.

### Hata sınıfları ve davranışları

| Durum | Davranış |
|---|---|
| Doğrulama hatası, kota dolmadı | Otomatik onarım turu tetiklenir |
| Doğrulama hatası, kota doldu | `CompletedWithWarnings` + en son şema + hata listesi döner |
| LLM API hatası (ağ/5xx/rate-limit) | Mevcut `GroqAIService` genel hata yönetimi korunur; agentic akışta ek olarak "hangi turda başarısız oldu" loglanır |
| Tool-calling formatı bozuk / model tool çağırmayı reddetti | Orkestratör kendi doğrulamasını her üretim sonrası otomatik tetikler (Bölüm 2, madde 6) |

### Test stratejisi

- `SchemaValidator`: saf unit test — FK referans hatası, computed
  expression'da olmayan kolon, isim çakışması, yanlış motor için trigger
  gibi senaryolar; LLM çağrısı gerektirmez.
- `SchemaAgentOrchestrator`: `IAIService`'in mock/fake implementasyonuyla
  test edilir — "ilk üretim hatalı dönsün, ikinci turda düzelsin" gibi
  senaryolar simüle edilir; gerçek API çağrısı yapılmaz.
- `AgentTools` handler'ları: her tool bağımsız unit test edilir
  (`validate_schema` çağrısının `SchemaValidator`'ı doğru çağırdığı,
  `preview_ddl`'in `DdlGeneratorFactory`'yi doğru çağırdığı).
- Uçtan uca (opsiyonel, manuel/nightly, gerçek API anahtarıyla): "trigger'lı
  bir sipariş şeması üret" isteği atıp sonucun geçerli DDL'e çevrildiğini
  doğrulayan entegrasyon testi — maliyetli olduğu için her PR'da
  çalıştırılmaz.
- Frontend: `AgentProgressIndicator`/`useAgentProgress` için SignalR
  mock'lu component test — event sırasının doğru UI state'e yansıdığını
  doğrular.

---

## Kapsam Dışı (v1 için)

- `search_similar_schemas` tool'u (embedding/arama altyapısı gerektirir —
  v2 adayı).
- MockData/Migration/DBA prompt builder'larının agentic döngüye
  bağlanması (kapsam sadece şema üretimi + revizyon).
- Çoklu-agent mimarisi (Yaklaşım C, yukarıda gerekçelendirilerek
  reddedildi).
- Sandbox DB'de gerçek DDL dry-run (sadece deterministik + tool-calling
  doğrulaması; gerçek DB'de çalıştırma v2 adayı).
