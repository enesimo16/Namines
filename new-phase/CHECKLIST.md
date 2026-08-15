# Namines — İlerleme Checklist'i

> Her madde **doğrulanmadan** işaretlenmez. Doğrulama = çalışan komut + görülen çıktı.
> Format: `- [ ]` yapılmadı · `- [x]` yapıldı ve doğrulandı · `- [~]` başlandı, bitmedi
>
> **"G" = Görev grubu, gün değil.** G0, G1, G2… sıralı iş paketleridir. Bir günde birkaç görev
> grubu bitebilir; bir görev grubu birkaç güne yayılabilir. Sıra önemlidir, takvim değil.

**Aktif faz:** Faz 0 — Temeli Sağlamlaştırma (G7 disk alanı bekliyor) → G8'den itibaren
Lifecycle Pivot ([27-LIFECYCLE-PIVOT.md](27-LIFECYCLE-PIVOT.md))
**Son güncelleme:** 2026-08-10

---

## G0 — Kurulum & Temel Doğrulama

- [x] **Ortam kontrolü** — .NET 9 SDK + .NET 8 runtime (8.0.21), Node v22.20.0, Docker 29.6.1, Git 2.51
  - Doğrulama: `dotnet --list-runtimes` → `Microsoft.AspNetCore.App 8.0.21` mevcut ✔
- [x] **Yerel çalışma kopyası** — `C:\Users\Enes Yel\Desktop\namines` artık repo kökü, branch `main`
  - Doğrulama: `git log --oneline -1` → `4a323d0` ✔
  - Not: `new-phase/` klasörü repo içinde, untracked
- [x] **Backend derleniyor**
  - Doğrulama: `dotnet build` → `0 Hata, 2 Uyarı` ✔
- [x] **Backend ayağa kalkıyor**
  - Doğrulama: `curl /health` → `HTTP 200` + `{"status":"Healthy"}` (sqlite + memory check yeşil) ✔
  - ⚠️ Bulgu: `launchSettings.json` portu **5117**'ye zorluyor, `Program.cs` ise `5000` diyor. Yerel geliştirmede kafa karıştırıcı — G-ekstra'ya alındı.
- [ ] **Frontend derleniyor**
  - Doğrulama: `npm install && npm run build` → hatasız

---

## G1 — Hızlı Güvenlik & Altyapı Düzeltmeleri ✅ TAMAMLANDI

- [x] **G1.1 — `docker.sock` mount kaldırıldı** *(Etki 9 / Zorluk 2)*
  - Sorun: `/var/run/docker.sock` container'a bağlanması = host'ta root eşdeğeri yetki
  - Dosya: `docker-compose.yml`, `DockerSweeperBackgroundService.cs`
  - Doğrulama: `grep -rn "docker.sock" *.yml` → sadece açıklama satırı ✔
  - Ek: Sweeper artık `Sandbox:Enabled` bayrağıyla kontrol ediliyor (varsayılan **false**) ve 3 ardışık hatadan sonra kendini kapatıyor → socket yokken log/alert gürültüsü yok
  - Log kanıtı: `Docker sandbox sweeper devre dışı (Sandbox:Enabled=false)` ✔
- [x] **G1.2 — Serilog dosya sink'i kapatılabilir yapıldı** *(Etki 6 / Zorluk 1)*
  - Sorun: `logs/*.log` yerel diske yazıyor; PaaS'te her deploy'da uçuyor, çok instance'ta parçalı, PII filtresi yok
  - Dosya: `backend/Namines.API/Program.cs` (hem bootstrap hem host logger)
  - Doğrulama A (varsayılan): çalıştırıldı → `logs/` klasörü **oluşmadı** ✔
  - Doğrulama B (bayrak): `Serilog__WriteTo__File=true` → `logs/namines-20260808.log` **oluştu** ✔
  - Not: özellik silinmedi, opt-in yapıldı
- [x] **G1.3 — Startup migration kontrol altına alındı** *(Etki 7 / Zorluk 1)*
  - Sorun: `Database.Migrate()` her instance başlangıcında → yatay ölçeklemede yarış koşulu
  - Dosya: `backend/Namines.API/Program.cs`
  - Doğrulama A: `dotnet run -- --migrate` → 10 migration uygulandı, **web sunucusu başlamadan exit 0** ✔
  - Doğrulama B: normal başlatma → migration çalıştı, uygulama ayağa kalktı ✔
  - Davranış değişikliği YOK: `Database:MigrateOnStartup` varsayılanı `true`. Production'da `true` ise uyarı basıyor.
- [x] **G1.4 — `ForwardedHeaders` KnownNetworks düzeltmesi** *(Etki 6 / Zorluk 1)*
  - Sorun: `KnownNetworks.Clear()` + `KnownProxies.Clear()` koşulsuz → X-Forwarded-For herkes tarafından sahte doldurulabilir → **rate limit atlatılabilir**
  - Dosya: `backend/Namines.API/Program.cs`
  - Doğrulama: 2 geçerli + 1 geçersiz CIDR verildi →
    `ForwardedHeaders: 2 güvenilen ağ/proxy tanımlandı` + `'GECERSIZ' ayrıştırılamadı, yok sayıldı` ✔
  - Tanımlı değilse Production'da açık uyarı basıyor (sessiz risk yok)

**G1 doğrulama özeti:** `dotnet build` → **0 Hata** · `/health` → **200 Healthy** · 4/4 madde çalışan kanıtla doğrulandı

---

## G2 — Test Altyapısı (baseline) ✅ TAMAMLANDI

- [x] **G2.1 — Test projesi oluşturuldu** (xUnit 2.9.3 + Verify.Xunit 31.12.5 + Shouldly 4.3)
  - `backend/Namines.Tests/`, solution'a eklendi, Core + Infrastructure referanslı
  - Not: Verify 31.x xunit 2.9.3 gerektiriyor (şablonun 2.5.3'ü NU1107 çakışması veriyordu)
  - Not: FluentAssertions v8+ ticari lisanslı → **Shouldly** tercih edildi
  - Doğrulama: `dotnet build` → 0 Hata ✔
- [x] **G2.2 — 5 fixture şeması eklendi**
  - `01-minimal`, `02-ecommerce`, `03-composite-key`, `04-self-referencing`, `05-multi-cascade-path`
  - `StableUuid` açıkça veriliyor (model varsayılanı `Guid.NewGuid()` — aksi halde snapshot'lar anlamsız olurdu)
- [x] **G2.3 — Golden-file baseline kaydedildi** — 5 fixture × 6 motor = **30 dosya**
  - `Namines.Tests/Golden/{MSSQL,PostgreSQL,MySQL,MariaDB,SQLite,Oracle}/*.verified.sql`
  - Ek 90 değişmez testi: determinizm, boş-değil, tablo-kaybı-yok
  - Doğrulama: `dotnet test --filter "Category!=KnownIssue"` → **120/120 Başarılı** ✔
- [x] **G2.4 — Bilinen hata testleri yazıldı — ŞU AN KIRMIZI (kasıtlı)**
  - `Ddl/CascadePathTests.cs`, `[Trait("Category","KnownIssue")]`
  - Doğrulama: `dotnet test --filter "Category=KnownIssue"` → **8 test Başarısız** ✔
  - Teşhis çıktısı: `MSSQL: aynı tabloya birden fazla cascade yolu var — Msg 1785`
    `Orders → Users: 2 yol` · `Cascade kenarları: Addresses→Users, Orders→Users, Orders→Addresses`
  - Bu 8 test G3'te yeşile dönecek

**G2 doğrulama özeti:** ana paket **120/120 yeşil** · bilinen hata paketi **8/8 kırmızı (kanıt)** · toplam 128 test

⚠️ **Dürüstlük notu:** Bu testler DDL'in *metnini* doğruluyor, gerçekten çalıştığını değil.
Msg 1785 iddiası SQL Server dokümantasyonuna dayanıyor. Docker Desktop bu makinede çalışmadığı
için ampirik doğrulama yapılamadı — G5'te (Testcontainers) yapılacak.

---

## G3 — İlk Gerçek Düzeltme: `ON DELETE CASCADE` ✅ TAMAMLANDI

- [x] **G3.1 — `ReferentialAction` enum + `OnDelete`/`OnUpdate` alanları** *(varsayılan `NoAction`)*
  - `Namines.Core/Enums/ReferentialAction.cs` — NoAction · Restrict · Cascade · SetNull · SetDefault
  - `SchemaRelation.OnDelete` / `.OnUpdate`
  - **Geriye uyumlu:** eski JSON'larda alan yok → varsayılana düşer → mevcut şemalar otomatik güvenli hale gelir
  - Doğrulama: `Relation_default_is_no_action` testi ✔
- [x] **G3.2 — 6 DDL üreticisi güncellendi**
  - Ortak çevirici: `ReferentialActionSql.cs` — motor farklarını tek yerde topluyor
  - MSSQL/Oracle `RESTRICT` bilmez → NO ACTION'a düşer · Oracle `ON UPDATE` ve `SET DEFAULT` desteklemez
  - **Düşüş yönü kuralı:** hiçbir fallback CASCADE'e (veri kaybına) doğru değil
  - Doğrulama: `No_action_never_degrades_into_cascade` 6 motorda ✔
- [x] **G3.3 — `FkCascadeAnalyzer` yazıldı ve API'ye bağlandı**
  - `Namines.Core/Analysis/FkCascadeAnalyzer.cs` — 4 tür sorun tespit ediyor
  - MultipleCascadePaths · CascadeCycle · SetNullOnNotNullColumn · SetDefaultWithoutDefaultValue
  - `POST /api/compile/sql` artık `diagnostics[]` dönüyor (**bloklamıyor** — kullanıcı bilerek devam edebilir)
  - Doğrulama (canlı API): `'Orders' tablosundan 'Users' tablosuna 2 ayrı cascade yolu var... Msg 1785` ✔
  - Doğrulama (varsayılan): `diagnostics: []`, SQL'de 0 CASCADE ✔
- [x] **G3.4 — Golden dosyalar güncellendi, diff incelendi**
  - 24/30 golden dosya değişti (FK'sı olmayan `01-minimal` dokunulmadı)
  - **Baseline'da 54 `ON DELETE CASCADE` → yeni çıktıda 0**
  - CASCADE dışında beklenmedik hiçbir değişiklik yok (diff ile doğrulandı) ✔
- [x] **G3.5 — G2'deki 8 kırmızı test yeşile döndü**
  - Doğrulama: `Category=KnownIssue` → **8/8 Başarılı** ✔
  - Trait kaldırıldı; artık ana paketin regresyon koruması
- [x] **G3.6 — Frontend: FK davranış seçici**
  - `types/schema.ts` — `ReferentialAction` tipi + `onDelete`/`onUpdate`
  - **Kritik düzeltme:** `flowToSchema`/`schemaToFlow` round-trip. Bu yapılmasaydı kullanıcının
    seçtiği değer canvas'a her dokunulduğunda sessizce silinecekti.
  - `RelationEdge.tsx` — ilişki etiketine tıkla → 5 seçenekli menü, CASCADE'de "veri kaybı" uyarısı,
    NO ACTION dışındaki değerler etikette rozet olarak görünüyor
  - Doğrulama: `npx tsc --noEmit` → 0 hata · `npm run build` → başarılı ✔

**G3 doğrulama özeti:** `dotnet build` **0 Hata** · **176/176 test yeşil** (G2'de 128 idi, 48 yeni test)
· frontend build temiz · canlı API uyarı dönüyor

**Yeni test dosyaları:** `ReferentialActionTests.cs` (33 test) · `FkCascadeAnalyzerTests.cs` (13 test)

---

## G4 — Index + Unique + Check desteği ✅ TAMAMLANDI

- [x] Model: `SchemaIndex`/`SchemaUnique`/`SchemaCheck`, geriye uyumlu (eski kayıtlar boş liste)
- [x] `ConstraintSql.cs` — 6 motora bağlandı; desteklenmeyen özellik (kısmi index, INCLUDE)
      sessizce düşürülmüyor, açıklama satırı yazılıyor
- [x] Frontend: `TableEditorDrawer`'a index bölümü, FK-index-eksik uyarısı + tek tık düzeltme
- [x] Yeni fixture (`06-indexes-constraints`) + `IndexConstraintTests` (21 test)
- [x] Mevcut 30 golden dosya **değişmedi** — geriye uyumluluk kanıtı
- Doğrulama: 255/255 test yeşil · commit `89c5a9f`

## G5 — Testcontainers ile gerçek DB doğrulaması ✅ TAMAMLANDI

- [x] Testcontainers.PostgreSql/MsSql/MySql eklendi
- [x] `RequiresDockerFact/Theory` — Docker yoksa atlanır, kırmızı olmaz
- [x] **22/22 gerçek veritabanı testi yeşil** (PostgreSQL 17, SQL Server 2022, MySQL 8.4)
- [x] **Msg 1785 iddiası ampirik olarak kanıtlandı**: eski davranış (her FK'da CASCADE)
      gerçek SQL Server tarafından reddedildi; yeni varsayılan (NO ACTION) sorunsuz çalıştı
- [x] **5 önceden bilinmeyen gerçek hata bulundu ve düzeltildi** (golden-file testlerinin
      yapısal olarak yakalayamayacağı sınıftan — "SQL geçerli görünüyor ama motor reddediyor"):
      1. Tip eşleme yoktu (4/6 üretici) — `TypeSql.cs` yeni
      2. DEFAULT fonksiyon çevirisi yoktu — `DefaultValueSql.cs` yeni
      3. Bileşik PK'nın her iki kolonu da identity/auto-increment alıyordu (4 üretici)
      4. MySQL/MariaDB DEFAULT sözdizimi (parantez eksikti)
      5. Ham SQL ifadelerinde case-folding (test fixture düzeltmesi)
- [x] `InternalsVisibleTo` ile `TypeSql`/`DefaultValueSql` test projesine açıldı
- Doğrulama: 308/308 birim test + 22/22 integration test yeşil · commit `2e62d5f`
- ⚠️ Not: Docker Desktop bu makinede üç container'ı eşzamanlı çalıştırırken çöktü,
  yeniden başlatma gerekti. Integration testleri artık sıralı çalıştırılmalı
  (`xunit.parallelizeAssembly=false xunit.parallelizeTestCollections=false`)

## G6 — SignalR auth sertleştirme + Redis backplane ✅ TAMAMLANDI

- [x] **Kapsam düzeltmesi (tasarımı okuyunca):** Hub'a JWT'yi ZORUNLU kılmadım —
      kod içinde "Guest erişimi TASARIM GEREĞİDİR" yorumu var, `/canvas`'a giden
      her kullanıcı otomatik bir odaya katılıyor (girişsiz bile). Zorunlu JWT bu
      kasıtlı özelliği kırardı. Bunun yerine: kimliği doğrulanmışsa JWT claim'i
      kullanılıyor, anonim/guest davranışı birebir korunuyor.
- [x] **Kimlik taklidi sertleştirmesi** — `PresenceIdentity.ResolveDisplayName`
      (`Namines.Core/Realtime/`, SignalR'dan bağımsız saf fonksiyon): giriş yapmış
      kullanıcı için istemcinin gönderdiği serbest metne değil, JWT `ClaimTypes.Name`
      claim'ine güveniliyor — artık "Yönetici" yazıp başka birine bürünmek mümkün değil
- [x] **`IPresenceStore` soyutlaması** (`Namines.Core/Interfaces/`) — `static ConcurrentDictionary`
      DI singleton'ına taşındı (test edilebilir)
  - `InMemoryPresenceStore` — tek instance, Redis yoksa (varsayılan davranış korunuyor)
  - `RedisPresenceStore` — Redis varsa; TTL 8 saat (sunucu çökerse çöp anahtar birikmesin)
- [x] **SignalR Redis backplane** — `Redis:ConnectionString` yapılandırılmışsa
      `AddStackExchangeRedis`; yoksa uyarı + tek instance davranışı (davranış değişikliği yok)
- [x] **`MaximumReceiveMessageSize`**: 32 KB varsayılanından 512 KB'ye çıkarıldı —
      orta büyüklükte bir şema (30-40 tablo) eski limiti aşıp sessizce reddediliyordu
- [x] **Çok-instance senaryosu gerçek Redis'te kanıtlandı**: iki ayrı `RedisPresenceStore`
      + iki ayrı `ConnectionMultiplexer` (iki API instance'ını simüle eder) — biri yazıyor,
      diğeri okuyor. Bu tam olarak Faz 1'in kırık olduğu senaryo.
- Doğrulama: 321/321 non-integration + 5/5 Redis integration test yeşil
- ⚠️ Not: Bu doğrulama sırasında Docker'ın WSL2 disk katmanı önceki zorla kapanmadan
  "read-only file system" durumuna düştü — `wsl --shutdown` ile tam temiz kapanış
  gerekti. Kod regresyonu değildi.

## Sonraki

- [ ] **G7 — Control DB: SQLite → PostgreSQL** (tam geçiş, SQLite kaldırılacak)
      ⚠️ Disk alanı yüzünden bekliyor — Docker'a güvenmeden önce
      `Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"` ile boş alanı kontrol et.
      Artık sadece ölçekleme için değil, G9'daki sunucu-taraflı branch tablolarının
      **ön koşulu** olduğu için önceliği arttı.

## G8+ — Lifecycle Pivot (2026-08-10, [27](27-LIFECYCLE-PIVOT.md) kararına göre)

> Sıra kesin değil, bağımlılıklara göre esner. Detaylı spesifikasyonlar:
> [28-IMPACT-ANALYSIS-ENGINE.md](28-IMPACT-ANALYSIS-ENGINE.md) ·
> [29-DATABASE-CHANGE-REVIEW.md](29-DATABASE-CHANGE-REVIEW.md) ·
> [30-SERVER-SIDE-BRANCHING.md](30-SERVER-SIDE-BRANCHING.md)

- [ ] **G8 — `SchemaImpactAnalyzer`** — `FkCascadeAnalyzer`'ı genelleştir
      (Namines.Core/Analysis): `ImpactReport` modeli (AffectedTables/Relations/
      Indexes, BreakingChanges, DataLossRisks, LockRisks, IndexSuggestions,
      RollbackAssessment, OverallRisk = max() kuralı)
- [ ] **G9 — Migration risk sınıflandırması** — `MigrationService`'e ImpactReport
      entegrasyonu, [11-MIGRATIONS-BRANCHING.md §2](11-MIGRATIONS-BRANCHING.md)'deki
      risk tablosunun koda dökülmesi
- [ ] **G10 — Control DB: server-side `branches`/`schema_versions` tabloları**
      (G7'ye bağımlı — Postgres olmadan gerçek anlamda yapılamaz)
- [ ] **G11 — Database Change Review UI** — diff + impact + risk + test sonucu
      tek ekranda, onay/red mekanizması (control DB'de `pending_review`/`rejected`
      durumları)
- [ ] **G12 — "Run Tests" aksiyonu** — G5'in Testcontainers altyapısını runtime'a
      taşı (ephemeral branch DB, tek seferlik)
- [ ] **G13 — Etkilenen API/UI statik tahmini** — basit metin/AST taraması,
      "olası etki" olarak işaretlenir, kesin değil
- [ ] **G14 — Minimal Gateway** — şemadan otomatik salt-okunur REST (liste+detay)
- [ ] **G15 — AI Impact Explainer ajanı** — `ImpactReport`'u insan diline çevirir
- [ ] **G16 — Destructive işlem onay mekanizması** — control DB'de audit +
      approval tablosu, [29 §3](29-DATABASE-CHANGE-REVIEW.md)'teki 1-kişi/2-kişi kuralı
- [ ] **G17 — CanvasHub'ı branch_id'ye bağla** — G6'daki `IPresenceStore` aynı
      kalır, `roomId` kavramı `branch_id`'ye eşlenir ([30 §3 Adım 2](30-SERVER-SIDE-BRANCHING.md))

---

## G-ekstra — Yol boyunca bulunanlar

- [ ] `launchSettings.json` `applicationUrl`'i 5117; `Program.cs` 5000 diyor. Tek bir port belirle (5000 öneriliyor) — README ve docker-compose 5000 varsayıyor.
- [ ] `DatabaseExecutorController.cs:33,50` — CS8625 nullable uyarısı (2 adet). Küçük ama `TreatWarningsAsErrors` açmadan önce temizlenmeli.

---

## Kod dışı işler (sen yapacaksın)

- [ ] `C:\Users\Enes Yel` dizinindeki yanlış git deposunu düzelt (remote'u `automated-recruitment-pipeline`)
- [ ] Ödeme altyapısı araştırması (Stripe TR sınırlı → Paddle / LemonSqueezy)
- [ ] `namines.com` alan adı + marka taraması
