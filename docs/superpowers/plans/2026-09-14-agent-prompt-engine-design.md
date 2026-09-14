# Namines Agent Prompt Engine — Tasarım Dokümanı

> Bu bir bite-sized TDD implementasyon planı değil; önce mimari kararların
> netleşmesi için yazılmış bir tasarım dokümanıdır. Onaylandıktan sonra
> `superpowers:writing-plans` ile gerçek görev-bazlı plana dönüştürülür.

**Sorun:** `GroqAIService.cs` ve `backend/Namines.Core/Prompts/*` altındaki
tüm üretim tek turlu (system+user → tek istek → JSON parse → kör retry).
Şema kontratı (`tables/columns/relations`) computed column, view, trigger,
stored procedure, composite index gibi "üst düzey" DDL kavramlarını hiç
taşımıyor — model bunları üretmek istese bile format izin vermiyor.

**Hedef:** İki bağımsız iyileştirme — (A) şema kontratını üst düzey DDL
kavramlarına genişletmek, (B) tek turlu üretimi plan→üret→doğrula→onar
döngüsü olan bir agent orkestrasyonuna çevirmek.

## A) Şema Kontratı Genişletmesi

**Dosyalar:**
- Modify: `backend/Namines.Core/Prompts/SchemaPromptBuilder.cs` — sistem
  promptuna yeni alanların JSON şeması ve üretim kuralları eklenir.
- Modify: `backend/Namines.Core/Models/*` (şemanın C# karşılığı — Explore
  agent'ının bulduğu `SchemaTable`/`SchemaColumn` tiplerinin bulunduğu
  dosya; gerçek yol implementasyon sırasında doğrulanmalı).
- Modify: `frontend/types/flow.ts`, `frontend/lib/schemaToFlow.ts`,
  `frontend/lib/flowToSchema.ts` — yeni alanları canvas'a yansıtmak için.
- Modify: `backend/Namines.Infrastructure/Generators/DdlGenerator/*` —
  yeni JSON alanlarını gerçek DDL'e (CREATE VIEW, computed column syntax,
  composite INDEX, CHECK constraint) çeviren deterministik üretim mantığı.

**Yeni şema alanları (öneri):**
```jsonc
{
  "tables": [{
    "columns": [{
      // mevcut alanlar +
      "computedExpression": "string | null",   // örn. "price * quantity"
      "isPersisted": "bool"                     // computed ise
    }],
    "checkConstraints": [{ "id", "expression" }],
    "indexes": [{ "id", "columnIds": [], "isUnique": "bool" }]
  }],
  "views": [{ "id", "name", "selectDefinition" }],
  // triggers/stored procedures: v2'ye ertelenir — DB motoru başına sözdizimi
  // farkı çok büyük (Postgres PL/pgSQL vs MSSQL T-SQL vs MySQL); önce
  // computed column + index + view ile başlanmalı.
}
```

**Neden triggers/stored procedures v1'de yok:** Namines 5 farklı DB motorunu
hedefliyor (Postgres/MySQL/MSSQL/MariaDB/Oracle/SQLite). Trigger/SP
sözdizimi motorlar arasında o kadar farklı ki LLM'in tek promptta güvenilir
üretmesi düşük olasılıklı; ilk sürümde motor-agnostik olan computed column,
index, check constraint, view'a odaklanmak daha az riskli.

**Token bütçesi:** `GroqAIService.CalculateMaxTokens` mevcut sınırları
(4096/6000) yeni alanlarla birlikte yeniden ölçülmeli — computed
expression ve view definition'lar token maliyetini artırır.

## B) Agent Orkestrasyon Katmanı

**Mevcut akış:** `system+user prompt → tek istek → JSON parse → (hata ise)
aynı promptu temperature artırarak tekrar gönder, max 2 deneme`.

**Hedef akış (plan → üret → doğrula → onar):**

1. **Plan adımı:** Kullanıcı isteğini alıp modele "önce hangi tabloları,
   hangi ilişkileri, hangi üst-düzey yapıları (view/computed/index)
   üreteceğini kısa bir plan olarak listele" dedirten ayrı, ucuz bir istem
   (küçük model / düşük token).
2. **Üretim adımı:** Plana göre tam şema JSON'u üretilir (mevcut
   `SchemaPromptBuilder` akışına benzer, ama plan bağlamı da mesaj
   geçmişine eklenir — yani artık **çok mesajlı** bir `messages` dizisi
   gönderiliyor, tek system+user değil).
3. **Doğrulama adımı (AI-dışı, deterministik):** Yeni bir
   `SchemaValidator` sınıfı — FK referansları var mı, computed expression
   referans verdiği kolonlar tabloda mevcut mu, isim çakışması var mı,
   3NF ihlali var mı gibi kontrolleri **kod ile** (LLM'e sormadan) yapar.
   Bu adım hızlı ve deterministik olduğu için ayrı bir LLM turu gerekmez.
4. **Onarım adımı:** Doğrulama hata verirse, hatalar modele "bu hataları
   düzelt" promptuyla geri gönderilir (mevcut mesaj geçmişine eklenerek —
   modelin önceki çıktısını görmesi sağlanır, şu anki kör retry'nin
   aksine). Max 3 tur.
5. **Sonlandırma:** Doğrulama geçerse şema kaydedilir.

**Yeni dosyalar:**
- Create: `backend/Namines.Infrastructure/AI/Agent/SchemaAgentOrchestrator.cs`
  — plan→üret→doğrula→onar döngüsünü yöneten sınıf. `IAIService`'i
  (mevcut arayüz) kullanır, değiştirmez.
- Create: `backend/Namines.Infrastructure/AI/Agent/SchemaValidator.cs` —
  deterministik, AI-dışı doğrulama kuralları.
- Create: `backend/Namines.Core/Prompts/AgentPlanPromptBuilder.cs` — adım 1
  için ayrı, kısa bir prompt builder.
- Modify: `backend/Namines.Infrastructure/AI/GroqAIService.cs` — tek
  system+user yerine `List<ChatMessage> messages` alan bir overload
  eklenir (mevcut tek-turlu metodlar geriye dönük uyumlu kalır, yeni bir
  `GenerateSchemaAgenticAsync(string prompt, IAIProgressReporter? reporter)`
  metodu eklenir).
- Modify: `backend/Namines.API/Controllers/*SchemaController*` — yeni
  endpoint (`POST /api/schema/generate-agentic`) veya mevcut endpoint'e
  `mode=agentic` query parametresi.

**Kota/gözlemlenebilirlik:** `AiUsageTracker` çok-turlu akış için de
kullanılmalı — her tur ayrı bir "AI call" olarak sayılmalı, toplam tur
sayısı plan seviyesinde (`PlanQuotas`) sınırlanmalı (örn. ücretsiz planda
max 2 onarım turu, ücretli planda max 4).

**Frontend etkisi:** `frontend/services/*` altındaki şema üretim
çağrısına, ilerleme göstermek için bir "streaming/polling" mekanizması
eklenmeli (agent birden fazla tur attığı için tek istek-cevap yeterli
değil) — SignalR zaten mevcut (`CanvasHub`), bu iş için yeni bir
`AgentProgressHub` ya da mevcut hub'a yeni bir metod eklenebilir.

## Kapsam Dışı (v1 için)

- Gerçek tool-calling (Groq/OpenAI `tools` parametresi) — plan→üret→doğrula
  döngüsü zaten çoğu değeri veriyor; tool-calling'i v2'ye ertelemek
  karmaşıklığı azaltır.
- Trigger/Stored Procedure üretimi (yukarıda gerekçelendirildi).
- Çoklu-agent (agent-to-agent) mimarisi — tek agent'ın plan→üret→doğrula
  döngüsü yeterli; gerçek ihtiyaç ortaya çıkmadan çoklu-agent
  orkestrasyonu eklemek YAGNI ihlali olur.
