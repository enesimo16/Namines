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
- [x] **Frontend derleniyor**
  - Doğrulama: `npm run build` (Next.js 16, Turbopack) → derleme + TypeScript + statik
    sayfa üretimi hatasız, 7 route (`/`, `/canvas`, `/compile`, `/review`, `/review/[id]`,
    `/share/[token]`) ✔ (G17 sonrası, bu oturumda doğrulandı — önceden hiç çalıştırılmamış
    stale bir G0 maddesiydi)

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

## G7 — Control DB: SQLite → PostgreSQL ✅ TAMAMLANDI

- [x] **Paket geçişi** — `Microsoft.EntityFrameworkCore.Sqlite` ve
      `AspNetCore.HealthChecks.Sqlite` kaldırıldı; `Npgsql.EntityFrameworkCore.PostgreSQL`
      8.0.8 + `AspNetCore.HealthChecks.NpgSql` 8.0.1 eklendi
  - ⚠️ Bulgu: `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.8` → `Npgsql 8.0.4` istiyordu,
    ama projede doğrudan `Npgsql 10.0.2` pinliydi (hedef-motor DDL çalıştırma için) —
    NuGet "en yakın kazanır" kuralıyla 10.0.2'yi seçip `HackyEnumTypeMapping` yükleme
    hatası veriyordu. `Npgsql`'i 8.0.7'ye indirerek çözüldü.
  - **Korunan ayrım:** `Microsoft.Data.Sqlite` paketi KALDIRILMADI —
    `DatabaseExecutorService`/`ScaffolderService`'teki kullanımı control DB değil,
    kullanıcının hedef motor olarak seçtiği SQLite desteği (6 motordan biri)
- [x] `Program.cs`: `UseSqlite` → `UseNpgsql`, health check `AddSqlite` → `AddNpgSql`
- [x] Migration'lar yeniden oluşturuldu — eski SQLite migration'ları silindi
      (provider'a özgü, taşınamaz), `InitialPostgres` olarak tek migration'da yeniden üretildi
- [x] **Gerçek PostgreSQL container'ına karşı doğrulandı** (Docker açıkken, disk
      alanı önce kontrol edildi: `Get-CimInstance Win32_LogicalDisk`)
  - `dotnet ef database update` → 13 tablo gerçekten oluştu (`\dt` ile doğrulandı)
  - `dotnet run -- --migrate` → "already up to date", exit 0
  - Normal başlatma → `/health` → `{"status":"Healthy","postgres-control-db":"Healthy"}`
- [x] **321/321 non-integration + 8/8 Postgres integration test yeşil** (Npgsql
      indirmesinin DDL-hedef Postgres çalıştırmasını bozmadığının kanıtı)
- [x] `docker-compose.yml` — `namines-control-db` servisi eklendi (postgres:17-alpine),
      backend'in `depends_on: condition: service_healthy` ile bağlandığı
- [x] `README.md`/`README.tr.md`/`deploy/backend.env.example` — control DB referansları
      PostgreSQL'e güncellendi (hedef-motor SQLite desteği metinleri dokunulmadan kaldı)
- ⚠️ Not: `.env.example` sandbox izniyle korunuyor, otomatik güncellenemedi —
  **elle kontrol et:** `ConnectionStrings__DefaultConnection` satırı SQLite formatındaysa
  Postgres formatına çevir (`Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=postgres`)
- ⚠️ Not: MSSQL/MySQL/Redis integration testleri bu turda TEKRAR ÇALIŞTIRILMADI —
  disk alanı tekrar daraldığı (1.25GB) için gereksiz risk alınmadı. Bu değişiklik
  o motorların koduna hiç dokunmadı, sadece control DB'yi ilgilendiriyor.

## Sonraki

## G8+ — Lifecycle Pivot (2026-08-10, [27](27-LIFECYCLE-PIVOT.md) kararına göre)

> Sıra kesin değil, bağımlılıklara göre esner. Detaylı spesifikasyonlar:
> [28-IMPACT-ANALYSIS-ENGINE.md](28-IMPACT-ANALYSIS-ENGINE.md) ·
> [29-DATABASE-CHANGE-REVIEW.md](29-DATABASE-CHANGE-REVIEW.md) ·
> [30-SERVER-SIDE-BRANCHING.md](30-SERVER-SIDE-BRANCHING.md)

- [x] **G8 — `SchemaImpactAnalyzer`** ✅ TAMAMLANDI — `FkCascadeAnalyzer`'ı genelleştir
      (Namines.Core/Analysis): `ImpactReport` modeli (AffectedTables/Relations/
      Indexes, BreakingChanges, DataLossRisks, LockRisks, IndexSuggestions,
      RollbackAssessment, OverallRisk = max() kuralı)
  - `Namines.Core/Enums/RiskLevel.cs` + `LockSeverity.cs` — `MigrationService`'in de
    (G9) paylaşacağı ortak sözlük
  - `Namines.Core/Models/ImpactReport.cs` — doc'taki record şeması birebir + `ChangeKind`/
    `BreakingChangeKind` enum'ları
  - `Namines.Core/Analysis/SchemaImpactAnalyzer.cs` — eşleştirme `StableUuid` üzerinden
    (rename ≠ add+remove); katman 1-5 (yapısal diff, cascade/FK — `FkCascadeAnalyzer`
    doğrudan devralınıyor, kilit/süre sınıfı, veri kaybı riski, eksik index önerisi)
    [28-IMPACT-ANALYSIS-ENGINE.md §3](28-IMPACT-ANALYSIS-ENGINE.md)'e göre uygulandı;
    katman 6 (etkilenen API/UI) bilinçli olarak G13'e bırakıldı (Gateway'siz sadece tahmin)
  - Risk sınıfları [11-MIGRATIONS-BRANCHING.md §2](11-MIGRATIONS-BRANCHING.md) tablosundan
    birebir: ADD COLUMN nullable→Safe, DROP COLUMN/TABLE→Breaking+Destructive+irreversible,
    RENAME→Breaking, tip daraltma→Breaking+DataLossRisk, CREATE INDEX→Blocking (CONCURRENTLY
    önerisiyle)
  - **Not:** doc'un test-planı metni "kolon silindi → Destructive" diyor ama doc'un kendi
    `BreakingChangeKind` enum'u `ColumnRemoved`'ı da içeriyor — ikisi birden tetiklenince
    §4'teki max() kuralı gereği sonuç `Breaking` (Destructive'den daha şiddetli). Enum
    şeklini prosa metninden daha otoriter kaynak sayıp buna göre uyguladım.
  - Doğrulama: `SchemaImpactAnalyzerTests.cs` — 14/14 yeşil (doc §6 test planındaki her
    senaryo + "max, ortalama değil" testi) · tam suite 362/362 yeşil, regresyon yok
- [x] **G9 — Migration risk sınıflandırması** ✅ TAMAMLANDI — `MigrationService`'e
      ImpactReport entegrasyonu, [11-MIGRATIONS-BRANCHING.md §2](11-MIGRATIONS-BRANCHING.md)'deki
      risk tablosunun koda dökülmesi
  - `MigrationService.CalculateDiffAsync` artık `engine` parametresi alıyor
    (varsayılan PostgreSQL) ve dağınık altı ayrı `HasBreakingChanges = true`
    atamasının yerine tek çağrıyla `SchemaImpactAnalyzer.Analyze(...)` çalıştırıp
    sonucu `SchemaDiffResult.Impact`'e (yeni alan) yazıyor
  - `SchemaDiffResult`'a `OverallRisk` (RiskLevel) + `Impact` (tam `ImpactReport`)
    eklendi; `HasBreakingChanges` **geriye uyumlu kalması için korundu** ama artık
    `Impact.BreakingChanges.Count > 0`'dan türetiliyor — ad-hoc tahmin değil
  - `SchemaDiffRequest.DbType` nullable eklendi (`MigrationController` `?? PostgreSQL`
    ile geçiriyor) — mevcut çağıranlar hiçbir şey göndermese de kırılmıyor
  - Frontend: `types/migration.ts`'e `RiskLevel`/`LockSeverity`/`ImpactReport` ve
    tüm alt tipleri eklendi (backend sözleşmesiyle bire bir, camelCase); `MigrationWizard.tsx`
    artık `calculateDiff`'e seçili `dbType`'ı geçiyor (motora özgü mesajlar için, ör. Msg 1785)
  - Doğrulama: `MigrationServiceRiskTests.cs` — 4/4 yeşil (wiring uçtan uca: aynı şema→Safe,
    kolon silme→Breaking+Impact dolu, MSSQL motoru→"Msg 1785" mesajı, motor belirtilmezse
    PostgreSQL varsayılan) · tam suite 366/366 yeşil · `npx tsc --noEmit` frontend'de temiz
- [x] **G10 — Control DB: server-side `branches`/`schema_versions` tabloları** ✅ TAMAMLANDI
      (G7'ye bağımlıydı — Postgres olmadan gerçek anlamda yapılamazdı)
  - Kapsam bilinçli olarak [30-SERVER-SIDE-BRANCHING.md §3 Adım 1](30-SERVER-SIDE-BRANCHING.md)
    ile sınırlı: sadece "sunucu branch'in varlığını biliyor" — CRDT bağlanması (Adım 2)
    ve ephemeral branch DB (Adım 3) G17'de
  - `Namines.Core/Models/Auth/Branch.cs` + `SchemaVersion.cs` — 18-CONTROL-PLANE-DDL.md'deki
    tasarımın Faz 0 karşılığı: `projects`/`users` yerine mevcut `CloudProject`/`ApplicationUser`'a
    bağlanıyor (Faz 2'nin ayrı ULID şeması yok — "yanına eklenir, değiştirmez" prensibi)
  - `AuthDbContext` — 2 yeni `DbSet`, FK'lar doc'taki §2 tablosuyla birebir: Project→Cascade,
    ParentBranch→SetNull, CreatedByUser→Restrict; unique (ProjectId,Name); kısmi unique
    (ProjectId WHERE IsDefault) — projede en fazla bir "main"; SchemaVersion (BranchId,Version) unique
  - `Namines.Infrastructure/Data/AuthDbContextFactory.cs` (yeni) — `dotnet ef` için tasarım-zamanı
    fabrika, Namines.API'yi (çalışan dev sunucusuyla dll kilidi çakışabiliyordu) startup project
    olarak gerektirmeden migration üretimini mümkün kılıyor
  - Migration `20260817172816_AddBranchesAndSchemaVersions` — **gerçek `namines-control-db`
    container'ına uygulandı**, `\d` ile tablo/index/FK'lar doğrulandı (G7 doğrulama bar'ıyla tutarlı)
  - `BranchController.cs` (yeni) — create/list branch, commit/list/get schema version;
    mevcut `AuthController` deseniyle tutarlı (ayrı servis katmanı yok, doğrudan `AuthDbContext`
    + `CloudProject.UserId` sahiplik kontrolü); checksum sunucuda SHA-256 ile hesaplanıyor,
    istemciden gelene güvenilmiyor; ikinci `IsDefault` isteği önce eskisini kapatıyor (DB'nin
    çıplak constraint-violation 500'ünü kullanıcıya göstermemek için)
  - Doğrulama: `BranchSchemaVersionTests.cs` — **gerçek Postgres'e karşı 7/7 yeşil**
    (Testcontainers, G5 deseniyle): branch adı benzersizliği, ikinci default branch DB
    tarafından reddediliyor (sadece EF değil), versiyon numarası çakışması reddediliyor,
    proje silinince branch+versiyon cascade siliniyor, ebeveyn branch silinince çocuğun
    ParentBranchId'si NULL'lanıyor (silinmiyor) · tam suite regresyonsuz
- [x] **G11 — Database Change Review UI** ✅ TAMAMLANDI (backend + frontend,
      canlı uçtan-uca tıklama testiyle doğrulandı — bkz. not) — diff + impact +
      risk tek ekranda, onay/red mekanizması
  - **Kapsam sadeleştirmesi (bilinçli):** doc, kullanıcının önce sunucu-taraflı bir
    branch açıp üzerinde çalıştığı tam Git-benzeri akış varsayıyor. Frontend'de böyle
    bir branch-yönetim UI'ı yok (canvas'taki `BranchControlPanel` hâlâ TAMAMEN
    istemci-taraflı eski model — G17'nin işi). Bunun yerine tek aksiyon: canvas
    toolbar'daki **"Request Review"** butonu — proje için sunucu-taraflı "main"
    branch'i yoksa oluşturur, mevcut şemayı yeni `SchemaVersion` olarak commit'ler,
    bir önceki versiyona karşı `SchemaImpactAnalyzer` çalıştırıp CR açar. Kullanıcı
    branch kavramıyla hiç uğraşmıyor ama altyapı G10'un gerçek tabloları üzerinde.
  - **"Run Tests" sekmesi bilinçli olarak yapılmadı** — doc'un kendi roadmap'i bunu
    ayrı bir iş olarak ayırıyor (G12: Testcontainers'ı runtime'a taşımak). "SQL"
    sekmesi de dürüstlük gereği ham ALTER-diff DDL değil, mevcut EF Core migration
    üreticisinin (`GenerateMigrationAsync`) çıktısı — ayrı bir DDL-diff motoru
    yok, var olmayan bir şeyi var gibi göstermemek için böyle etiketlendi
  - `Namines.Core/Enums/ChangeRequestStatus.cs` + `ApprovalDecision.cs`
  - `Namines.Core/Models/Auth/ChangeRequest.cs` + `ChangeRequestApproval.cs` —
    `HeadVersion`/`BaseVersion` (nullable — ilk versiyon boş şemayla kıyaslanır),
    `ImpactReportJson` (deterministik olduğu için saklanıyor, tekrar hesaplanmıyor)
  - `Namines.Core/Analysis/ChangeRequestApprovalPolicy.cs` (yeni) — onay iş kuralı
    (kim ne zaman onaylayabilir) bilinçli olarak controller'dan ayrı, saf fonksiyon
    olarak yazıldı — G8'deki `SchemaImpactAnalyzer` deseni: HTTP/DB'ye gömülen bir
    iş kuralı ucuz birim testlerle kanıtlanamaz. [29 §3](29-DATABASE-CHANGE-REVIEW.md):
    Safe/Risky→1 onay, Destructive/Breaking→2 onay + onaylayan yazarla aynı olamaz;
    tek bir "reddet" hemen kapatır
  - `ChangeRequestController.cs` (yeni) — `POST quick` (tek-tık review akışı),
    `GET {id}` (tam detay: impact + migration kodu + onaylar), `GET project/{id}`
    (liste), `POST {id}/decide` (onayla/reddet)
  - **Bulunan ve düzeltilen gerçek hata (canlı tarayıcı testinde yakalandı):**
    `BranchController`/`ChangeRequestController`'daki manuel `JsonSerializer.Deserialize
    <DatabaseSchema>` çağrıları `JsonStringEnumConverter` içermiyordu — frontend
    `ReferentialAction` gibi enum'ları string yazıyor (`"NoAction"`), dönüştürücüsüz
    deserializasyon `JsonException` fırlatıyordu. Golden-file/birim testleri bunu
    YAKALAYAMAZDI (ikisi de backend'in kendi ürettiği JSON'u kullanıyordu) — sadece
    gerçek frontend→backend isteği bunu ortaya çıkardı. Paylaşılan `SchemaJsonOptions`
    static alanıyla düzeltildi, her iki controller'da.
  - Migration `AddChangeRequests` — gerçek `namines-control-db`'ye uygulandı, `\d`
    ile doğrulandı
  - Frontend: `types/changeRequest.ts`, `services/api.ts` → `changeRequestService`,
    `ToolbarPanel.tsx`'e "Request Review" butonu, `app/review/page.tsx` (liste),
    `app/review/[id]/page.tsx` (tek ekran: risk metre + Schema Diff/Migration Code/
    Impact Analysis sekmeleri + onaylar + Approve/Reject)
  - Doğrulama: `ChangeRequestApprovalPolicyTests.cs` — 17/17 yeşil (saf iş kuralı,
    DB'siz) · `ChangeRequestIntegrationTests.cs` — gerçek Postgres'e karşı 4/4 yeşil
    (çoklu cascade yolu — Branch→ChangeRequest + Branch→SchemaVersion aynı silmede
    birlikte çalışıyor — , tekrar oy verememe, cascade delete) · tam suite 394/394 yeşil
  - **Not — ara kesinti ve kurtarma:** enum-deserialization hatası düzeltildikten
    sonra backend'i yeniden başlatırken makine 0 bayt boş disk alanına düştü
    (`C:` sürücüsü 233 GB'lık) ve Docker Desktop/WSL2 yanıt vermez oldu
    (`namines-control-db` container'ına erişilemedi) — CLAUDE.md'nin önceden bildiği
    kırılganlık ("disk alanı kritik olabilir", "Docker Desktop bu makinede kırılgan").
    Kullanıcı disk alanını temizledikten sonra `wsl --shutdown` + Docker Desktop
    process'lerini öldürüp yeniden başlatarak toparlandı; `namines-control-db`
    container'ı durmuş ama silinmemiş halde bulundu, `docker start` ile ayağa
    kaldırıldı.
  - **Canlı uçtan-uca doğrulama (tarayıcı, gerçek backend + gerçek Postgres):**
    canvas'ta şema yükle → "Request Review" → `POST /api/changerequest/quick`
    200 OK, `/review/{id}`'ye yönlendirdi → Schema Diff sekmesi 6 tabloyu da
    "ADDED" gösterdi → Impact Analysis sekmesi her yeni FK için "ADD FOREIGN KEY
    — BLOCKING" kilit riski ve eksik index önerilerini doğru listeledi → Migration
    Code sekmesi AI anahtarı yokken dürüst fallback mesajı gösterdi (hata değil) →
    Risk doğru şekilde RISKY hesaplandı (yeni FK'ler yüzünden) → **Approve** tıklandı,
    `POST .../decide` 200 OK, durum anında "1/1 approvals · APPROVED · enesimo
    approved this change" oldu → `/review` liste sayfası CR'ı doğru rozetlerle
    gösterdi. Konsol hatasız (sadece kesinti sırasındaki eski SignalR log'ları,
    yeni hata yok).
- [x] **G12 — "Run Tests" aksiyonu** ✅ TAMAMLANDI — G5'in Testcontainers altyapısı
      DEĞİL, ham `Docker.DotNet` (bkz. not) runtime'a taşındı: her CR için ephemeral,
      tek seferlik container (MSSQL/PostgreSQL/MySQL) + dosya-tabanlı SQLite.
  - **Testcontainers KULLANILMADI (mimari karar):** Testcontainers 4.x kendi
    "Docker.DotNet.Enhanced" forkunu getiriyor, bu fork gerçek `Docker.DotNet` paketiyle
    AYNI derlenmiş dosya adını (`Docker.DotNet.dll`) paylaşıyor. Bu proje zaten
    `DockerBackupService` üzerinden gerçek Docker.DotNet 3.125.15'e bağımlı (Docker
    Sandbox özelliği) — ikisi aynı process'te bir arada olunca NuGet'in sürüm çakışması
    çözümü sessizce yanlış DLL'i seçip DockerBackupService.cs'i derleme zamanında
    bozdu (CS0246/CS1061). Çözüm: Testcontainers'a hiç dokunma, `BranchTestRunnerService`
    zaten kanıtlanmış Docker.DotNet istemcisini ve `ContainerProfiles`'ı (Docker Sandbox'ın
    kullandığı) doğrudan yeniden kullanıyor. Test projesi de aynı nedenle AYRI:
    `Namines.Tests.RunTests/` — Namines.Tests Testcontainers'a bağımlı olduğu için
    aynı process'te TypeLoadException veriyordu, izole bir .csproj ile çözüldü.
  - **Canlı Docker'a karşı test sırasında 2 gerçek hata bulundu ve düzeltildi:**
    (1) `sqlcmd -i script.sql` `-b` bayrağı olmadan T-SQL hatalarında sessizce exit 0
    dönüyordu — Msg 1785 (multi-cascade path) testi bunu yakaladı, `-b` eklendi.
    `DockerBackupService.cs`'in KENDİ sqlcmd çağrıları da aynı eksikliğe sahip, ayrı
    bir görev olarak flaglendi (Docker Sandbox'ın DDL hatalarını sessizce yutabileceği
    anlamına gelir). (2) `mysqladmin ping` MySQL 8'in iki-aşamalı başlangıcındaki
    (init server → restart → gerçek server) geçici sunucuya karşı erken `0` dönüyordu
    — health check gerçek kimlik doğrulamalı bir `SELECT 1` sorgusuna çevrildi.
  - `Namines.Core/Models/TestRunResult.cs`, `IBranchTestRunner`, `BranchTestRunnerService`
    (Namines.Infrastructure/Services) — MSSQL/PostgreSQL/MySQL gerçek container,
    SQLite dosya-tabanlı, diğer 6 motor (Oracle/MariaDB/Db2/Firebird/Spanner/Redshift)
    `Supported=false` ile dürüstçe işaretleniyor (resmi container profili yok)
  - `ChangeRequestController.RunTests` (`POST {id}/run-tests`) — senkron, container
    açılışı dahil (~10-25sn), sonuç `ChangeRequest`'e kalıcı yazılıyor (TestRun* alanları,
    migration `AddChangeRequestTestRun`, gerçek control DB'ye uygulandı)
  - Frontend: review detay sayfasına "Run Tests" sekmesi — buton, container süresi,
    pass/fail, motorun HAM hatası
  - **Doğrulama — gerçek Docker'a karşı** (`Namines.Tests.RunTests/`, ayrı proje):
    6/6 yeşil — geçerli şema Postgres/MSSQL/MySQL'de gerçekten uygulandı, multi-cascade
    path gerçek MSSQL'de Msg 1785 ile reddedildi (ham mesaj doğrulandı), SQLite
    Docker'sız çalıştı, desteklenmeyen motor dürüstçe işaretlendi. Namines.Tests: 398/398
    yeşil (4 yeni: AffectedCodeScannerTests, G13).
  - **Canlı tarayıcı doğrulaması:** canvas'tan E-Commerce şablonu yüklendi → Request
    Review → CR açıldı → Run Tests tıklandı → GERÇEK MSSQL container'ı şablonun kendi
    hatasını yakaladı ("Could not create IDENTITY attribute on nullable column 'id'" —
    şablonun `id` kolonu yanlışlıkla nullable işaretli, PK olamaz) — bu tam olarak
    özelliğin vaadi: "tahmin değil, kanıt".
- [x] **G13 — Etkilenen API/UI statik tahmini** ✅ TAMAMLANDI — basit kelime-sınırı
      metin taraması (AST değil — doc'un izin verdiği ucuz seçenek), "olası etki"
      olarak işaretleniyor, kesin değil.
  - **Kapsam kararı:** doc'un "compile geçmişinden tara" önerisi bu projede karşılığı
    olmayan bir şeye dayanıyor — Namines üretilen kodu (EF Core/TypeScript) HİÇ
    saklamıyor (CompileController transient, DB'ye yazmıyor). Bunun yerine kullanıcı
    kendi uygulama dosyalarını (model/route/query) bu ekranda yapıştırır/yükler;
    tarama o dosyalara karşı çalışır. Kalıcı DEĞİL — her çağrıda yeniden hesaplanır,
    keyfi kullanıcı kaynak kodu DB'de saklanmıyor.
  - `Namines.Core/Models/AffectedCodeMatch.cs`, `Namines.Core/Analysis/AffectedCodeScanner.cs`
    (saf fonksiyon — `ChangeRequestApprovalPolicy`/`SchemaImpactAnalyzer` ile aynı desen).
    Aday kimlikler `ImpactReport.BreakingChanges` (en yüksek sinyal) +
    `AffectedTables`'ın Removed/RenamedFrom/Modified'ından çıkarılıyor, kelime-sınırlı
    regex ile satır satır aranıyor.
  - `ChangeRequestController.ScanAffectedCode` (`POST {id}/scan-affected-code`)
  - Frontend: review detay sayfasına "Affected Code" sekmesi — dosya yükleme, tarama,
    dosya:satır + eşleşen isim + satır metni listesi, sürekli "olası etki, kesin değil" uyarısı
  - Doğrulama: `AffectedCodeScannerTests.cs` — 4/4 yeşil (breaking-change çıkarımı,
    çok-dosyalı satır numarası eşleşmesi, kelime-sınırı — "Id" "ValidId" içinde
    eşleşmiyor, boş aday listesi = boş sonuç). Canlı tarayıcıda sekme render + upload/scan
    akışı doğrulandı.
- [x] **G14 — Minimal Gateway** ✅ TAMAMLANDI — şemadan otomatik salt-okunur REST
      (liste + detay), kullanıcının kendi canlı veritabanına karşı.
  - **Kapsam kararı:** connection string hiçbir yerde saklanmıyor —
    `DbIntrospectController`/`DatabaseExecutorController` ile AYNI güvenlik modeli
    (her istekte bir kez kullanılır, `Namines.Core.Security.SsrfGuard` ile
    localhost/private IP aralıkları reddedilir — bu yüzden bu servis kategorisi
    yerel Docker'a karşı test edilemiyor, `DbIntrospectionService`'in de zaten
    sahip olduğu önceden var olan bir sınır, yeni bir boşluk değil).
  - **Yazma yolu yok:** sadece SELECT üretilir. Tablo/kolon adları katı bir regex'ten
    (`^[A-Za-z_][A-Za-z0-9_]*$`) geçmeden asla SQL'e eklenmez, motora özgü quote
    (`[x]`/`"x"`/`` `x` ``) ile sarılır — DDL üreticilerindeki aynı `Quote()` deseni.
    WHERE değeri her zaman parametreli.
  - `Namines.Core/Models/GatewayModels.cs`, `IGatewayService`, `GatewayService`
    (Namines.Infrastructure) — MSSQL/PostgreSQL/MySQL/MariaDB/Oracle, her motor için
    doğru sayfalama sözdizimi (OFFSET/FETCH, LIMIT/OFFSET, LIMIT skip,take)
  - `GatewayController` (`POST /api/gateway/list`, `POST /api/gateway/detail`) —
    login zorunlu, rate-limit'li (DatabaseExecutorController ile aynı `"sensitive"` politika)
  - Frontend: `GatewayExplorerPanel.tsx` (yeni) — canvas toolbar'ında "Browse live
    data (read-only)" ikonu, DbConnectionPanel'in bağlantı formuyla aynı desen +
    şemadaki tablolardan seçim + sayfalı liste + satır tıklayınca detay görünümü
  - Doğrulama: `GatewayServiceTests.cs` — 24/24 yeşil (kimlik doğrulama/reddetme,
    motor başına quote karakteri, parametreli WHERE — SQL'de asla değer yok, sadece
    isim, her motor için sayfalama sözdizimi). Namines.Tests toplamı: 422/422 yeşil.
    Canlı tarayıcıda uçtan uca doğrulandı: E-Commerce şablonuyla Data Explorer açıldı,
    `users` tablosu seçildi, yerel bir bağlantı dizesiyle gönderildi → backend gerçekten
    `400 "Connection target is not allowed (private or reserved address)"` döndürdü ve
    UI bunu doğru gösterdi — SSRF koruması gerçek bir HTTP isteğiyle kanıtlandı.
- [x] **G15 — AI Impact Explainer ajanı** ✅ TAMAMLANDI — `ImpactReport`'u insan diline çevirir
  - **Kural (doc'un ilkesi, aynen uygulandı):** "motor kanıtladı, AI özetledi" — AI kendi
    başına yeni bulgu ÜRETMEZ, sistem talimatında açıkça yasaklandı ("NEVER invent").
    Sadece `ImpactReport`'un zaten içerdiği tablo/kolon adları, breaking change'ler, veri
    kaybı riskleri, kilit riskleri ve rollback durumunu düz metne çevirir.
  - `IAIService.ExplainImpactAsync(ImpactReport)` — hem `GroqAIService` hem `OllamaAIService`'te
    implemente edildi (projedeki tek iki `IAIService` implementasyonu).
  - `Namines.Core/Prompts/ImpactExplainerPromptBuilder.cs` (yeni, saf fonksiyon — mevcut
    `DbaPromptBuilder`/`StreamlitPromptBuilder` deseniyle aynı yerde) — sistem talimatı +
    yapılandırılmış bulgulardan üretilen kullanıcı prompt'u.
  - `ChangeRequestController.GetDetail`'e bağlandı — `Migration` alanıyla AYNI zaten var olan
    graceful-degradation deseni (AI anahtarı/servis yoksa `AiExplanation: null`, sekme
    boş görünür, hata fırlatmaz). Yeni `AIMode` kategorisi eklenmedi — mevcut "Documentation"
    kategorisi (`UserAIPolicy.Documentation`) yeniden kullanıldı, DocumentationController'ın
    kendi hassasiyetiyle tutarlı.
  - Frontend: review detay sayfası, Impact Analysis sekmesinin en üstünde "AI Summary" kutusu
    (yalnızca `aiExplanation` doluysa render olur) + "bu bağımsız bir bulgu değil, aşağıdaki
    yapısal analizden üretildi" notu.
  - Doğrulama: `ImpactExplainerPromptBuilderTests.cs` — 7/7 yeşil (icat yasağı sistem
    promptunda var, gerçek tablo/kolon adları ve mitigation metinleri prompt'a giriyor, güvenli/
    riskli durumlar doğru ayırt ediliyor). Namines.Tests toplamı: 429/429 yeşil. Canlı
    tarayıcıda uçtan uca doğrulandı: gerçek bir CR'ın Impact Analysis sekmesi açıldı, ağ
    yanıtında `aiExplanation` alanı doğru şekilde mevcut ve `null` (bu ortamda Groq API
    anahtarı yok — `migration` alanı da aynı sebeple `null`, tutarlı), UI hatasız/sessizce
    kutuyu gizliyor. Gerçek bir AI yanıtı üretme adımı (API anahtarı gerektirir) bu oturumda
    test edilemedi — Migration Code sekmesinin baştan beri sahip olduğu aynı sınır.
- [x] **G16 — Destructive işlem onay mekanizması** ✅ TAMAMLANDI — control DB'de audit +
      approval tablosu, [29 §3](29-DATABASE-CHANGE-REVIEW.md)'teki 1-kişi/2-kişi kuralı
  - **1-kişi/2-kişi kuralı zaten G11'de vardı** (`ChangeRequestApprovalPolicy`) — bu G'nin
    gerçek eksiği doc'un aynı tablosundaki **"Safe | Otomatik onaylanabilir (opt-in ayar)"**
    satırıydı: her risk seviyesi (Safe dahil) koşulsuz PendingReview'dan başlıyordu.
  - `CloudProject.AutoApproveSafeChanges` (bool, varsayılan false) — proje bazlı opt-in.
    Açıksa, `CreateQuick`'te Safe risk'li CR'lar insan onayı beklemeden direkt `Approved`
    açılır (`ResolvedAt` anında set edilir).
  - **Audit tablosu — bilinçli kapsam sadeleştirmesi:** doc'taki [18](18-CONTROL-PLANE-DDL.md)
    `audit_log` çok-kiracılı (org_id), genel amaçlı bir tablo — bu projede henüz bir
    Organization kavramı yok. Onun yerine `ChangeRequestAuditLog` — ChangeRequest'e özel,
    append-only bir zaman çizelgesi (Created/AutoApproved/Approved/Rejected). İnsan oyları
    zaten `ChangeRequestApproval`'da vardı (G11) — bu tablo onun YERİNE değil, sistem-güdümlü
    olayların (otomatik onay gibi insan aktörü olmayan olaylar, `ActorUserId=null`) da aynı
    zaman çizelgesinde görünmesi için var.
  - `ChangeRequestController`: `PUT project/{projectId}/auto-approve-safe` (proje sahibi
    toggle'lar), `GET {id}/audit` (append-only zaman çizelgesi okuma)
  - Migration `AddChangeRequestAuditLogAndAutoApprove` — gerçek control DB'ye uygulandı
  - Frontend: `/review` liste sayfasında proje-bazlı "Auto-approve Safe changes" toggle'ı
    (mevcut projenin ayarını `GET /api/auth/projects`'ten okuyup gösteriyor); CR detay
    sayfasında Approvals panelinin altında "History" bloğu (kim/ne zaman/otomatik mi)
  - Doğrulama: 3 yeni entegrasyon testi (gerçek Postgres'e karşı) — sistem-güdümlü olayda
    `ActorUserId=null` doğru kaydediliyor, branch silinince audit log cascade oluyor, zaman
    çizelgesi sırası korunuyor. Namines.Tests toplamı: 432/432 yeşil.
  - **Canlı uçtan uca doğrulama** (UI otomasyonu bu akış için kırılgan olduğundan gerçek bir
    HTTP isteğiyle doğrulandı): toggle açıldı (`PUT .../auto-approve-safe` → 200,
    `autoApproveSafeChanges:true`), Safe risk'li bir CR gerçek `/api/changerequest/quick`
    çağrısıyla oluşturuldu → **`status:"Approved"` döndü** (PendingReview değil),
    `/audit` iki doğru sıralı kayıt gösterdi ("Created" by g13tester, sonra "AutoApproved"
    `actorUserId:null`) — tarayıcıda `/review/{id}` sayfası bunu "APPROVED" rozeti + History
    bloğunda "Auto-approved (automatic) — Safe risk + project.AutoApproveSafeChanges enabled"
    olarak doğru render etti. Konsol hatasız (sadece backend restart'tan kalma eski
    SignalR yeniden bağlanma log'ları).
- [x] **G17 — CanvasHub'ı branch_id'ye bağla** ✅ TAMAMLANDI — `IPresenceStore`
      DEĞİŞMEDİ (doc'un öngördüğü tam olarak buydu), `roomId` kavramı `branch_id`'ye
      eşlendi ([30 §3 Adım 2](30-SERVER-SIDE-BRANCHING.md))
  - **Aşamalı, kırmayan geçiş:** rastgele `room-xxxx` roomId üretimi TAMAMEN kaldırılmadı —
    yalnızca kimliği doğrulanmış + aktif projesi olan kullanıcılar için branch ID'sine
    yönlendirildi. Guest/anonim kullanıcılar (proje yok) ve branch çözümlenemezse (ağ
    hatası vb.) eski "tahmin edilemez roomId" (capability-link) davranışına düşülür —
    guest erişimi tasarım gereği (CanvasHub.cs'in kendi yorumu), bu akış korunmalıydı.
  - `BranchController.GetOrCreateDefaultBranch` (`GET project/{id}/default`, yeni) —
    `ChangeRequestController.CreateQuick`'teki "yoksa oluştur" deseniyle aynı: proje
    senkronize edildiğinde henüz Branch satırı açılmıyor, ilk canlı işbirliği bağlantısında
    "main" branch'i bul-yoksa-oluştur.
  - `CanvasHub.cs` KOD OLARAK değişmedi — sadece dokümantasyon yorumu eklendi (hub, roomId'nin
    rastgele mi yoksa gerçek bir branch ID'si mi olduğunu bilmiyor/bilmesi gerekmiyor).
  - Frontend: `hooks/useMultiplayer.ts`'deki oda-çözümleme mantığı — URL'de roomId yoksa
    ve kullanıcı authenticated + `activeProjectId` varsa `branchService.getOrCreateDefault`
    çağrılır, dönen branch ID'si oda kimliği olarak URL'e yazılır.
  - Doğrulama: 2 yeni entegrasyon testi (gerçek Postgres) — find-or-create idempotency
    (`GetOrCreateDefaultBranch_returns_the_same_branch_on_repeated_calls`, ikinci çağrı
    yeni satır açmıyor). Namines.Tests toplamı: 434/434 yeşil. **Canlı tarayıcıda uçtan uca
    doğrulandı:** endpoint'e iki kez curl ile istek atıldı, ikisi de AYNI branch ID'sini
    döndürdü; canvas açıldığında tarayıcının URL'i `?roomId=0e4afe56-98f5-...` oldu — bu,
    G16 testinde oluşan "Yeni Proje" projesinin GERÇEK server-side "main" branch ID'si
    (rastgele `room-xxxx` DEĞİL), sayfa "We connected to the room" gösterdi. Konsol
    hatasız (sadece backend restart'lardan kalma eski SignalR log'ları).

---

- [x] **CI — derleme + test pipeline** ✅ TAMAMLANDI (`.github/workflows/ci.yml`)
  - Depoda daha önce **hiçbir pipeline build/test koşmuyordu** — tek workflow
    (`namines-schema-diff.yml`) bir PR yorumcusuydu ve dayandığı `namines-schema.json`
    repoda olmadığı için **hiç tetiklenmiyordu**. Yani her değişiklik doğrulanmamış gidiyordu.
  - 4 iş: **backend** (Release build + her iki test projesi, Docker'lı integration
    testleri dahil, CLAUDE.md'nin kuralı gereği sıralı), **frontend** (`npm ci` +
    `tsc --noEmit` + `build`), **design-tokens** (FRONTEND.md §4/§2 bekçisi: ham hex,
    saf beyaz/siyah, indigo/mor).
  - Doğrulama: bekçilerin geçtiği DEĞİL, **yakaladığı** kanıtlandı — kasıtlı ihlal
    içeren dosya konup üç ihlalin de yakalandığı görüldü, sonra silindi. `dotnet test`
    argüman sözdizimi ve **Release** yapılandırmasında tam solution derlemesi yerelde
    doğrulandı (hep Debug'da çalışıyorduk).
  - ⚠️ Not: `--filter "Category!=RequiresDocker"` daha önceki komutlarda **hiçbir şey
    filtrelemiyordu** — `RequiresDockerFact` yalnızca `Skip` set ediyor, Category trait
    eklemiyor. Raporlanan test sayıları zaten tüm paketti.

- [x] **G18 — Organizasyon / üyelik (05 §6 RBAC)** ✅ TAMAMLANDI
  - **Çözülen somut hata:** `Breaking`/`Destructive` risk taşıyan bir change request
    **hiçbir zaman onaylanamıyordu**. Kural (29 §3) "2 farklı kişi, yazar olamaz" diyor;
    ama yetki sınırı `CloudProject.UserId` olduğu için CR'a **yalnızca proje sahibi**
    erişebiliyordu ve sahibi de yazar olduğu için 403 alıyordu → kalıcı kilit.
    Canlı doğrulandı (fix öncesi: sahip onay denemesi → `403`).
    Birim testlerin kaçırma sebebi: sahte kullanıcı ID'leriyle saf politika testi
    yapıyorlar, sahiplik katmanına hiç dokunmuyorlar.
  - `Organization` + `OrganizationMember` (bileşik PK, `OrgRole`: Viewer/Editor/Admin/
    Owner/Billing) + `CloudProject.OrganizationId`.
  - `OrgAccess` (Infrastructure/Data) — yetki kontrolünün **tek kopyası**:
    `CanViewAsync` / `CanEditAsync` / `CanManageMembersAsync` / `GetOrCreatePersonalOrgAsync`.
    Önceden 6 controller'da `p.UserId == userId` kopyalanıyordu; SSRF regex'i ve branch
    find-or-create'te aynı kopyalama bize hata olarak dönmüştü.
  - `ProjectMemberController` — üye listele/ekle/rol değiştir/çıkar. Son Owner'ın
    düşürülmesi/çıkarılması engelli (org sahipsiz kalıp üye yönetimi kilitlenmesin).
  - Migration `AddOrganizationsAndMembers` + **idempotent backfill SQL**: her kullanıcıya
    kişisel org, projeler oraya taşındı. Doğrulandı: 4 kullanıcı → 4 org, 3 proje,
    **0 sahipsiz proje**.
  - **Kapsam sadeleştirmesi:** doc'taki `org_invites` (token + expiry + e-posta) akışı
    YAPILMADI — e-posta altyapısı yok. Üye doğrudan e-postayla eklenir, kullanıcının
    önceden kayıtlı olması gerekir. ULID/slug de ertelendi (mevcut GUID deseni korundu).
  - **Uçtan uca kanıt (gerçek HTTP):** ikinci kullanıcı ekibe eklendi → onayladı (`200`,
    hâlâ PendingReview çünkü 2 onay gerekiyor) → üçüncü kullanıcı onayladı →
    **`status: "Approved"`**. Audit zinciri eksiksiz: `Created by g13tester |
    Approved by reviewer | Approved by reviewer2` (ilk onaylayanın görünmesi G16
    düzeltmesinin karşılığı).
  - Doğrulama: `OrgAccessTests` 7/7 yeşil (rol matrisi, `billing`in yetki almadığı —
    sayısal olarak Owner'dan büyük olduğu için `>=` kullanılsaydı sessizce her yetkiyi
    alırdı —, org'suz eski projede geri-uyum, idempotent kişisel org, üye çıkarınca
    erişimin anında kesilmesi). Tam paket: **450 → 457 test yeşil**.
  - ⚠️ **Ürün kısıtı, bilinmeli:** kural gereği Breaking/Destructive için yazar hariç
    2 onay gerekiyor → böyle bir değişikliği geçirmek için ekipte **en az 3 kişi**
    olmalı. Tek/iki kişilik ekipte bu risk seviyesi geçirilemez. 29 §3'ün doğru okuması
    bu, ama ürün kararı olarak gözden geçirilmeli.

- [x] **G19 — Ekip büyüklüğüne duyarlı onay + üye yönetimi UI** ✅ TAMAMLANDI
  - G18'in sonundaki ⚠️ ürün kısıtının cevabı: kural "2 onay" derken ekipte 3 kişi
    olduğunu varsayıyordu. `ChangeRequestApprovalPolicy` artık ekip büyüklüğünü
    biliyor: `EffectiveRequiredApprovals(risk, teamSize)` ideali oy verebilecek
    kişi sayısıyla (`teamSize - 1`) sınırlar, tabanı 1'dir. 2 kişilik ekipte
    Breaking/Destructive **tek onayla** geçer; yazar-dışı şartı (`RequiresDistinctFromAuthor`)
    korunur, yani tek kişilik ekipte kendi değişikliğini kendin onaylayamama kuralı
    gevşemez — orada ikinci kişi eklemek gerekir.
  - Oy sayımı yalnızca **Editor/Admin/Owner**'ı sayar (`OrgAccess.CountVotingMembersAsync`);
    Viewer ve Billing ekip büyüklüğünü şişirip gereken onayı sahte yükseltmez.
  - `TeamPanel` (frontend/components/review) — üye listesi, rol değiştirme, ekleme,
    çıkarma; ekip büyüklüğüne göre "şu an kaç onay gerekiyor" mesajını gösterir.
  - ⚠️ **Bulunan sessiz hata:** `OrgRole` frontend'de sayısal enum olarak yazılmıştı,
    ama API global `JsonStringEnumConverter` ile `"Owner"` string'i döndürüyor —
    bütün rol karşılaştırmaları sessizce `false`'tu. String union'a çevrildi.

- [x] **G20 — MCP sunucusu, Faz 1** ✅ TAMAMLANDI ([33](33-MCP-AND-SKILL.md) §5)
  - `backend/Namines.Mcp/` — stdio MCP sunucusu (`namines-mcp`), resmî
    `ModelContextProtocol` paketi. Üç salt-okunur araç: `namines_pull_schema`,
    `namines_analyze_impact`, `namines_prove_migration`. **Yeni iş mantığı yok** —
    hepsi mevcut, test edilmiş servisleri sarar.
  - **Neden .NET, neden ayrı süreç:** barındırılan API SSRF koruması yüzünden
    kullanıcının `localhost`'undaki DB'ye ulaşamaz (33 §2). Sunucu kullanıcının
    kendi makinesinde çalıştığı için connection string ağdan hiç geçmez ve
    backend'in ayakta olmasına gerek yoktur.
  - **stdout protokol kanalıdır** — log'lar stderr'e yönlendirildi; oraya serbest
    metin yazan bir değişiklik JSON-RPC akışını bozar.
  - ⚠️ **Bulunan sessiz hata (bu iş boyunca en önemlisi):** camelCase şema JSON'u —
    ki `pull_schema`'nın *kendi çıktısı* camelCase — sessizce **boş şemaya** çözülüyordu.
    Sonuç: gerçek bir tablo eklemesi için analiz "Safe, hiçbir şey değişmemiş" diyordu.
    Yanlış güven veren analiz, analiz olmamasından kötüdür. `PropertyNameCaseInsensitive`
    eklendi; ayrıca `ParseSchema`'ya **sessiz boşalma koruması** kondu: girdi tablo
    taşıdığını söylüyor ama 0 tablo bağlandıysa, ya da tablolar adsız çözüldüyse,
    devam etmek yerine açık hatayla reddedilir.
  - Doğrulama: ham JSON-RPC ile protokol kanıtlandı (`initialize` → `namines-mcp`,
    3 araç listelendi); `ANALYZE(camelCase)` → `risk=Breaking, veri kaybı=users.email`;
    bozuk şekil reddedildi; `PROVE(SQLite)` → `supported=true success=true`.
    `NaminesToolsTests` **11/11 yeşil** (odak: iş mantığı değil, LLM'den gelen JSON'un
    sınır katmanında doğru bağlanması). Kurulum: `backend/Namines.Mcp/README.md`.
  - **Bilinçli sınır: yazma yolu YOK.** Faz 1'de hiçbir araç kullanıcının DB'sini
    değiştirmez — okur, analiz eder, kanıtlar, önerir (33 §7).

- [x] **G21 — MCP Faz 2 + CLI + Skill + dağıtım ("Faz A")** ✅ TAMAMLANDI
  - **MCP Faz 2 (33 §5):** `namines_generate_ddl` (deterministik, 6 lehçe, golden-file
    testli) ve `namines_open_change_request` (sunucuda inceleme açar; **yazan tek araç**,
    kullanıcının DB'sine yine dokunmaz, `NAMINES_API_TOKEN` ister). Toplam 5 araç.
  - ⚠️ **`generate_migration` bilinçli olarak YAPILMADI.** Mevcut
    `MigrationService.GenerateMigrationAsync` migration kodunu Groq'a yazdırıyor; onu
    araç olarak sunmak BAŞKA bir dil modelinin tahminini "Namines'in deterministik
    çıktısı" kılığında Claude'a geri vermek olurdu — 33 §3'ün tam tersi. Deterministik
    bir üretici (6 motor + golden-file) yazıldığında eklenebilir.
  - **CLI (`backend/Namines.Cli`, 11 §9):** `namines pull|diff|ddl|prove`. MCP araçlarının
    GÖVDESİNİ yeniden kullanır (Namines.Mcp'ye referans) — iki kopya yazmak, bu kod
    tabanının daha önce bedelini ödediği hata (6 controller'da tekrarlanan yetki kontrolü
    → `OrgAccess`). Çıkış kodları CI kapısı olacak şekilde ayrıştırıldı:
    `0` iyi · `1` hata · `2` destructive/breaking · `3` motor DDL'i reddetti.
  - **Skill (`skills/namines-schema-review/`, 33 §6):** politika katmanı. MCP "ne
    yapabilirim", Skill "ne zaman ve nasıl" — risk seviyesinin ne yapmayı zorunlu
    kıldığı araç tanımına gömülemez, çünkü bu politikadır.
  - **Dağıtım:** `packaging/npm` (`npx -y @namines/mcp` — .NET ŞARTI YOK, platforma
    uygun self-contained binary'yi indirir, checksum doğrular) + `PackAsTool`
    (`dotnet tool install -g Namines.Mcp` / `Namines.Cli`) + **Claude Code eklentisi**
    (`.claude-plugin/plugin.json` + `marketplace.json` + kök `.mcp.json`: tek
    `/plugin install` ile MCP sunucusu VE skill birlikte gelir — skill tek başına
    yarımdır, çünkü riski hesaplayacak aracı içermez) + `.github/workflows/release.yml`
    (6 RID, checksums.txt, etiketle tetiklenir). Binary 141 MB → **68 MB**
    (`EnableCompressionInSingleFile`; trimming YAPILMADI — ADO.NET sağlayıcıları
    reflection kullanıyor, trimming onları yalnızca belirli bir motora bağlanınca
    patlayan hâle getirirdi). Boyutun asıl sebebi `Namines.Infrastructure`'ın tek parça
    olması: MCP'nin hiç kullanmadığı QuestPDF (~12 MB), EF Core ve Kestrel de geliyor.
    Kalıcı çözüm Infrastructure'ı bölmek — ayrı bir iş.
  - ⚠️ **Bulunan hata (CLI duman testi bunun için yazıldı, ilk çalıştırmada yakaladı):**
    `StableUuid` hem model varsayılanında hem `DbIntrospectionService`'te
    `Guid.NewGuid()` idi. `SchemaImpactAnalyzer` tabloları bu alanla eşleştirip
    eşleşmeyeni "kaldırıldı + eklendi" saydığından, **aynı veritabanını iki kez çekip
    karşılaştırmak** — MCP/CLI'ın birincil akışı — hiçbir değişiklik yokken "tüm tablolar
    silinecek, veri kaybı, Breaking" diyordu. Adı "stable" olan alanın her çağrıda
    değişmesi zaten çelişkiydi; introspection'ın hafızası yok, canlı bir DB'de kimlik
    zaten isimdir. `SchemaIdentity` (Core/Analysis) eklendi: isimden türetilen,
    büyük/küçük harf duyarsız kimlik. Açıkça uuid veren kaynaklar (canvas) kendi
    değerlerini korur, böylece **rename tespiti bozulmadı** (rename = aynı uuid, farklı ad).
    Web canvas'ında görünmüyordu çünkü orada uuid'ler düzenlemeler boyunca yaşıyor.
  - Doğrulama: `SchemaIdentityTests` (aynı şema → Safe/boş; gerçek kolon silme → hâlâ
    Breaking + `email`; rename → `RenamedFrom` ve **veri kaybı yok**), `NaminesToolsTests`
    Faz 2 ile genişletildi. Tam paket **438 test yeşil, 0 başarısız**. npm sarmalayıcısı
    gerçek binary ile uçtan uca JSON-RPC üzerinden kanıtlandı: `initialize → namines-mcp`,
    5 araç listelendi, `generate_ddl` gerçek DDL döndürdü, log'lar stderr'de kaldı.
    CI'a CLI çıkış-kodu duman testi eklendi (yerelde çalıştırıldı, geçti).
  - ⚠️ `.gitignore`'un genel `*.md` kuralı `skills/**/SKILL.md`'yi sessizce yutuyordu
    (CLAUDE.md'nin uyardığı tuzak, ikinci kez). `!skills/**/*.md` istisnası eklendi,
    `git add -n` ile doğrulandı.

- [x] **G22 — Prisma eject (Faz B / [12](12-CODEGEN-EJECT.md))** ✅ TAMAMLANDI
  - `PrismaGeneratorService` + `PrismaTypeMap` + `PrismaNaming` (Infrastructure/Generators).
    Yüzeyler: `POST /api/compile/prisma` (önizleme + uyarılar), `/api/compile/prisma/zip`,
    `namines prisma` (CLI), `namines_generate_prisma` (MCP), `/compile` sayfasında
    **Prisma sekmesi**.
  - **Sessiz kayıp koruması, bu işin ana tasarım kararı:** Prisma CHECK kısıtlarını,
    kısmi index'leri ve INCLUDE'u ifade EDEMEZ. Bunları sessizce düşürmek, üretilen
    şemayı veritabanından daha gevşek yapar — ve kullanıcı o dosyadan `prisma db push`
    çalıştırırsa kısıt veritabanından DÜŞER. Bu yüzden üretici dosya değil,
    **dosya + uyarı** döndürür; uyarılar `schema.prisma`'nın BAŞINA (sonuna değil,
    görülmeden push edilmesin diye) yorum olarak da yazılır ve UI'da kod alanının
    üstünde, daraltılamaz biçimde gösterilir.
  - **Oracle reddedilir.** Prisma'nın Oracle provider'ı yok; sessizce `postgresql`
    yazmak ayrıştırılabilir ama tamamen yanlış bir dosya üretir ve kullanıcı bunu
    ancak canlıda fark ederdi. `NotSupportedException` → API 400, CLI exit 1.
  - **Ad eşleme:** model/alan adları PascalCase/camelCase olur ama `@@map`/`@map` ile
    gerçek adlar korunur — eşleme yazılmasaydı `prisma db push` tabloları yeniden
    adlandırırdı. Model adı tekilleştirilMEZ (`users` → `Users`): "address" → "addres",
    "status" → "statu" gibi düzensiz adlarda tahmin sessizce yanlış sonuç verir.
  - **Referans eylemleri her zaman açık yazılır.** Prisma'nın varsayılanı NoAction
    DEĞİL (zorunlu ilişkide `Restrict`, opsiyonelde `SetNull`); boş bırakmak
    veritabanındakinden farklı davranış üretirdi.
  - **Native tipler korunur** (`@db.VarChar(255)`): yalnızca `String` yazılsaydı
    MySQL'de `varchar(191)`'e düşerdi — sessiz tip değişikliği + veri kırpma riski.
    SQLite'ta native niteleyici şemayı geçersiz kıldığı için hiç yazılmaz.
  - **Frontend'de üretici TEKRARLANMADI.** `EfCorePreview` kendi C# kodunu istemcide
    üretiyor ve bedeli görünür: yalnızca ilk tabloyu gösteriyor, yani önizleme ile
    indirilen ZIP aynı şey değil. `PrismaPreview` arka uçtan çeker; `warnings` zaten
    yalnızca oradan gelebilir.
  - ⚠️ **Gerçek `prisma validate` iki hata buldu** (metin iddiaları yakalayamamıştı):
    (1) `map:` argümanı parantezin DIŞINA yazılıyordu → `@@unique([x]), map: "y"`
    Prisma'da "not a valid field or attribute definition" hatası veriyordu;
    (2) çoğullama `posts` → `postses` üretiyordu. G5'in dersinin tekrarı: makul
    görünen çıktı, kabul edilen çıktı değildir.
  - Doğrulama: `PrismaGeneratorTests` **21/21** — 4'ü `RequiresPrismaTheory` ile
    GERÇEK `prisma validate`'e karşı (PostgreSQL/MySQL/MSSQL/SQLite), ayrıca belirsiz
    ilişki ve bileşik PK senaryoları. Docker'daki `RequiresDockerFact` ile aynı desen:
    Prisma CLI yoksa atlanır, kırmızı olmaz. Tam paket **459 test yeşil**.
    CI duman testine `prisma` + Oracle-reddi eklendi (yerelde koştu, geçti).
  - ⚠️ **Doğrulanamayan tek şey:** `/compile` sayfasındaki Prisma sekmesinin canlı
    render'ı. Sayfa backend'e (SignalR odası) bağlı, backend Postgres kontrol DB'sine,
    o da Docker'a — bu makinede Docker Desktop bozuk durumda (API 500). Tip kontrolü
    temiz ve component mevcut `PanelKit` desenini izliyor, ama gözle görülmedi.

- [x] **G23 — Gateway: filtreleme + yazma yolu (Faz B / [08](08-GATEWAY-API.md))** ✅ TAMAMLANDI
  - Filtreleme (08 §2.1'in alt kümesi): `eq/neq/gt/gte/lt/lte/like/in/is-null/is-not-null`,
    ASC/DESC sıralama. **Operatör bir enum**, serbest metin değil — SQL'e yazılan
    karşılaştırma parçası da kullanıcı girdisi olmasın diye. Değerler her zaman parametreli.
    COUNT da AYNI filtrelerle çalışır; aksi hâlde sayfalama çubuğu filtrelenmiş listeyle
    çelişen bir toplam gösterirdi.
  - Yazma: `POST /api/gateway/create|update|delete`. **Üç maddelik güvenlik sözleşmesi:**
    (1) koşulsuz UPDATE/DELETE üretmenin yolu YOK — imza pk kolon/değerini zorunlu
    kılıyor, tek bir hata tüm tabloyu silemesin diye; (2) her yazma işlem içinde çalışır
    ve etkilenen satır sayısı doğrulanır, **1'den fazlaysa GERİ ALINIR** ("birincil
    anahtar" denen kolon gerçekte benzersiz değilse tek istek onlarca satırı ezerdi —
    bunu fark etmenin tek anı işlem hâlâ açıkken); (3) kolon adları katı doğrulamadan
    geçer. 0 satır hata değil, "kayıt yok" → 404.
  - `INSERT ... RETURNING` yalnızca PostgreSQL/SQLite'ta. SQL Server'ın
    `OUTPUT INSERTED.*`'ı **bilinçli kullanılmadı**: hedef tabloda trigger varsa Msg 334
    ile patlar, yani yazma tamamen çalışmaz hâle gelirdi. Satırı geri okuyamamak,
    yazmayı kırmaktan iyidir.
  - ⚠️ **ÖNCEDEN VAR OLAN HATA bulundu (gerçek Postgres testi yazınca):**
    `42883: operator does not exist: integer = text`. Gateway'in değerleri HTTP'den
    string geliyor; Npgsql bunları `text` bildirince Postgres — diğer motorların
    aksine — örtük dönüşüm yapmıyor ve sorguyu reddediyor. Bu yalnızca yeni yazma
    yolunu değil, **mevcut `DetailAsync`'i de** vuruyordu: tamsayı birincil anahtarlı
    bir tabloda gateway detay ucu hiç çalışmıyormuş. Testler yalnızca üretilen SQL
    METNİNİ doğruladığı için görünmemişti. Çözüm `::text` cast'i değil (index'i
    kullanılamaz hâle getirip her sorguyu tam taramaya çevirirdi) — parametre
    "tipsiz" bildiriliyor, Postgres değeri kolonun tipine göre çözüyor.
  - Doğrulama: `GatewayWriteTests` 28/28 (SQL metni) + `GatewayWriteExecutionTests`
    **12/12 GERÇEK PostgreSQL'e karşı** — iki geri-alma testi dahil: yinelenen
    anahtarlı bir tabloda UPDATE/DELETE denenince hiçbir satırın değişmediği fiilen
    doğrulanıyor. Ayrıca SQL taşıyan bir filtre değerinin veri olarak işlendiği ve
    tablonun hâlâ durduğu.
  - **Kapsam dışı, bilinçli:** GraphQL, API anahtarı/RBAC modeli (08 §4), OpenAPI
    üretimi, `expand` ile ilişki gömme, export/import, `/rpc`, `/query/nl`. Uçlar
    bugünkü güven modelini koruyor: `[Authorize]` + rate-limit, çağıran zaten bağlantı
    dizesine sahip. **Yazma için UI eklenmedi** — kullanıcının canlı veritabanına
    tarayıcıdan yazmak, henüz olmayan bir onay/geri-alma akışı ister; buton koymak
    korumaları kâğıt üstünde bırakırdı.

- [x] **G24 — Branch veritabanı sağlama (Faz B / [06](06-DATA-PLANE.md) §4)** ✅ TAMAMLANDI
  - `BranchDatabaseProvisioner` — branch başına GERÇEK, bağlanılabilir bir veritabanı.
    `POST/GET/DELETE /api/branch/{id}/database`. PostgreSQL, MySQL, SQL Server.
  - **`docker.sock` mount EDİLMİYOR** (CLAUDE.md kesin kuralı, 30 §5): servis host
    sürecinde çalışıp Docker API'sine oradan konuşuyor — `BranchTestRunnerService`
    ile aynı model.
  - Test koşucusundan farkı ömür, ve bu üç yeni sorumluluk getiriyor:
    (1) **Erişilebilirlik** — port yayımlanıyor ama YALNIZCA `127.0.0.1`'e; `0.0.0.0`
    bilinen kullanıcı adıyla çalışan bir veritabanını makinenin her ağına açardı.
    (2) **Kimlik** — test koşucusunun sabit parolası burada kabul edilemez, her branch
    kendi rastgele parolasını alıyor (yayımlanmış portla birleşince sabit parola gerçek
    bir açıklık). (3) **Ömür** — 8 saatlik TTL container ETİKETİNDE taşınıyor, böylece
    süpürme sunucu yeniden başlasa bile çalışıyor; durum bellekte olsaydı restart
    sonrası container'lar sahipsiz kalır, bulunamaz ama çalışmaya devam ederdi.
    `DockerSweeperBackgroundService` süresi dolanları temizliyor.
  - ⚠️ **Bulunan kararsızlık:** ilk sürüm `pg_isready` ile hazırlık kontrol ediyordu ve
    test bazen "şema uygulanamadı" ile düşüyordu. Sebep MySQL'de bir kez öğrenilen
    tuzağın aynısı: Postgres imajı önce yalnızca unix soketinde dinleyen GEÇİCİ bir
    sunucu başlatıyor, `pg_isready` ona "hazır" diyor, hemen ardından gerçek sunucu
    için yeniden başlıyor. Hazırlık kontrolü **host'tan gerçek bağlantıya** çevrildi —
    geçici sunucular TCP'de dinlemediği için bu sınıfın tamamını çözüyor, üstelik doğru
    soruyu soruyor: "kullanıcıya vereceğimiz bağlantı dizesi şu an çalışıyor mu?"
  - Doğrulama: `BranchDatabaseProvisionerTests` **15/15 gerçek Docker'a karşı** —
    sağlanan veritabanına host'tan bağlanılıyor, şemanın uygulandığı `SELECT`/`INSERT`
    ile kanıtlanıyor, aynı branch için ikinci çağrının yeni container açmadığı ve
    **yeni bir provisioner örneğinin durumu bulup gerçekten bağlanabildiği** (restart
    dayanıklılığı) doğrulanıyor. Testcontainers/Docker.DotNet DLL çakışması nedeniyle
    `Namines.Tests.RunTests` projesinde (ilk yazıldığı yerde TypeLoadException verdi —
    G12 notundaki çakışmanın tam kendisi).
  - `DockerTarFile` — `BranchTestRunnerService` ile paylaşılan tek kopya tar yardımcısı.
  - **Kapsam dışı, bilinçli:** 06'nın geri kalanı dış altyapı istiyor ve bu oturumda
    yapılmadı: Managed DB / Neon copy-on-write branch'leri (§3), MinIO/S3'e yedek (§9),
    Namines Bridge on-prem tünel agent'ı (§6), PII maskeleme (§4), plan bazlı kotalar
    (§10), Vault ile kimlik saklama (§5). Buradaki sağlayıcı **yerel geliştirme
    veritabanı** üretir — prod verisi için değildir.

- [x] **G25 — BYODB sertleştirme + branch DB tohumlama ([06](06-DATA-PLANE.md) §4-5)** ✅ TAMAMLANDI
  - `UserDbConnection` — kullanıcının kendi veritabanına açılan bağlantıların TEK
    kapısı. Introspection ve Gateway ayrı ayrı bağlantı açıyordu; TLS zorunluluğu
    birine eklenip diğerinde unutulabilirdi (OrgAccess'te bedeli ödenmiş hata).
  - **Salt-okunur oturum:** okuma yolları PostgreSQL/MySQL'de oturumu salt-okunura
    çekiyor — koruma bizim kodumuzun disiplinine değil, MOTORUN uyguladığı bir
    kurala dayanıyor, yani SQL üretimimizdeki bir hata bile veri yazamıyor.
    SQL Server/Oracle'da oturum seviyesinde karşılığı yok; orada uygulanmıyor ve
    "uygulandı" gibi de gösterilmiyor (`AppliesReadOnlySession`).
  - ⚠️ **Testin yakaladığı kusur:** TLS kuralı ilk yazımda "host public ise zorunlu"
    idi. Çözülemeyen bir ad (DNS'i henüz yayılmamış bir sunucu) "public değil"
    sayılıp TLS'siz bağlanıyordu — varsayılan güvensiz tarafa düşüyordu. Kural
    tersine çevrildi: **özel/loopback olduğu KESİN değilse TLS zorunlu**
    (`SsrfGuard.IsHostPrivate`).
  - `DbPrivilegeInspector` — "bu kullanıcı DROP TABLE yapabiliyor, daha dar bir rol
    öneriyoruz". Engellemiyor, gösteriyor: kullanıcının kendi veritabanı, ama
    insanlar alışkanlıkla süper kullanıcıyla bağlanıyor. Motor desteklenmiyorsa
    "yetki yok" DEMİYOR, bakmadığını söylüyor — "kontrol edilemedi" ile "risk yok"
    karışırsa kullanıcı sahte güvence kazanır. Öneri yalnızca yapılacak bir şey
    varken veriliyor; salt-okunur bağlantıya uyarı basmak uyarıyı gürültüye çevirir.
  - **Branch DB tohumlama:** şeması olan ama boş bir veritabanı pratikte işe
    yaramıyor. Üretim deterministik (yapay zekâ çağrısı yok) — bir geliştirme
    veritabanını doldurmak için API anahtarı gerektirmek ve her çalıştırmada farklı
    veri üretmek özelliği hem kırılgan hem tekrar edilemez yapardı. Sağlamadan ayrı,
    çünkü gerçek veriyle çalışılan bir branch'te tohumlama istenmez.
  - ⚠️ **Bulunan DI ömür hatası:** `BranchDatabaseProvisioner` singleton'dı ama
    scoped `ISmartSeedService` tüketiyordu — tutsak bağımlılık. **API Development'ta
    hiç açılmazdı.** Bir EF migration komutunun DI doğrulaması ortaya çıkardı.
    Çözüm bağımlılığı singleton'a zorlamak değil (HttpClient/IHttpContextAccessor
    zinciri istek başına durum taşıyor); tohumlama anında kısa ömürlü scope açılıyor.
    `IDdlGeneratorFactory` ise gerçekten durumsuz olduğu için singleton'a çekildi.
  - Doğrulama: `ByodbHardeningTests` 6/6 (salt-okunur oturumun yazmayı GERÇEKTEN
    reddettiği ve yazılabilir oturumun hâlâ yazabildiği, gerçek PostgreSQL'e karşı;
    süper kullanıcı ve salt-okunur rol raporları). `BranchDatabaseProvisionerTests`
    **17/17 gerçek Docker** (tohumlama sonrası satırların gerçekten var olduğu).

- [x] **G26 — Gateway API anahtarları, tablo izinleri ve OpenAPI ([08](08-GATEWAY-API.md) §1, §2, §4.3)** ✅ TAMAMLANDI
  - `GatewayApiKey` + `GatewayTablePermission` (migration `AddGatewayApiKeys`),
    `GatewayAccess` (üretim/doğrulama/izin — tek kopya, `OrgAccess` ile aynı gerekçe),
    `GatewayKeyController` (üret/listele/iptal + tablo izinleri),
    `X-Namines-Key` ile Gateway veri uçlarına erişim, `/tables`, `/openapi.json`.
  - **Ham anahtar SAKLANMIYOR** — yalnızca SHA-256 özeti. Anahtar bir kez gösteriliyor.
    Kontrol veritabanının bir yedeği sızsa bile müşterinin veritabanına erişim
    vermemeli; ham anahtarı saklamak tam olarak bunu verirdi. Karşılaştırma sabit
    zamanlı (normal string eşitliği ilk farklı baytta dönerek zamanlama sızdırır).
  - **08 §1'in kuralı kodda:** izin kaydı YOKSA erişim yok. Varsayılanı "her tablo
    okunabilir" yapmak, projeye sonradan eklenen bir tabloyu — `password_resets`
    gibi — kimse istemeden internete açardı. İki ayrı kapı var: anahtarın yazma
    yetkisi ve tablonun izni; ikisi de geçilmeli.
  - **İki kimlik yolu, farklı yetkiler:** oturum (Studio; kullanıcı bağlantı dizesini
    zaten kendisi giriyor, tablo izni uygulanmaz) ve API anahtarı (müşterinin
    uygulaması; tablo izinleri uygulanır). Anahtar yönetimi uçları YALNIZCA oturumla
    korunuyor — bir anahtarın kendi yetkisini genişletebilmesi tüm modeli anlamsız
    kılardı. Controller `[AllowAnonymous]` ama anonim erişim YOK: kontrol
    middleware'den controller'a taşındı, çünkü anahtar yolu JWT taşımıyor.
  - **OpenAPI 3.1** deterministik üretiliyor (dil modeline yazdırılmadı: belge
    istemci SDK'sı üretmek için kullanılıyor, "çoğunlukla doğru" bir çıktı sessizce
    yanlış tipli bir istemci demek). **Yalnızca izinli tablolar belgeleniyor** —
    aksi hâlde izin kuralı belge üzerinden delinir, şemanın tamamı okunabilir olurdu.
    Tamsayı/ondalık ayrılıyor (ikisini "number" saymak para alanlarını kayan noktaya
    düşürürdü) ve nullable kolonlar tip birleşimiyle işaretleniyor.
  - Doğrulama: `GatewayApiKeyTests` **17/17 gerçek PostgreSQL'e karşı** (bellek içi
    sağlayıcı unique index/FK uygulamaz, kurallar test edilmiş görünüp gerçekte
    doğrulanmamış olurdu). Tam paket **573 test yeşil**, RunTests 17/17.
  - **Kapsam dışı, bilinçli:** GraphQL, `/realtime`, `expand` ile ilişki gömme,
    `?or=(...)`, export/import, `/rpc`, `/query`, `/query/nl`, istemci SDK üretimi,
    metadata cache (§6). Anahtar/izin modeli bunların çoğunun ÖN KOŞULU olduğu için
    önce o yapıldı.

- [x] **G27 — Gateway sorgu dili + anahtar yönetimi arayüzü ([08](08-GATEWAY-API.md) §2.1)** ✅ TAMAMLANDI
  - `select` kolon projeksiyonu ve `or` grupları (§2.1). Projeksiyon yalnızca ağ
    trafiğini azaltmıyor: istemcinin istemediği bir kolonu döndürmek onu istemeden
    log'a/önbelleğe taşıyabilir.
  - **OR grupları parantezleniyor.** Parantezsiz yazılırsa AND'in önceliği yüzünden
    anlam SESSİZCE değişir ve filtre beklenenden fazla satır döndürür — gerçek
    Postgres'e karşı yazılan test tam bu senaryoyu kilitliyor (`note='a'` AND
    (paid OR shipped): parantezsiz hâlde shipped satırı note filtresini atlardı).
  - Keyfi derinlikte AND/OR ağacı bilinçli YAPILMADI: hem ayrıştırıcı hem "bu sorgu
    ne kadar pahalı" tahmini gerektirir; iki seviye pratikteki filtrelerin neredeyse
    tamamını karşılıyor.
  - `GatewayKeyPanel` (frontend/components/review) — anahtar üret/iptal + tablo
    izinleri. Üretilen ham anahtar, kullanıcı kapatana kadar duran bir blokta
    gösteriliyor (geçici bildirim değil): kaybolan anahtar geri getirilemez.
    Tablo listesi varsayılan olarak boş görünüyor, çünkü izin satırı olmayan tablo
    erişilemez demektir — kullanıcının "neden çalışmıyor" sorusuna cevap listenin
    kendisi olmalı.
  - ⚠️ **Bulunan hata (uygulamayı gerçekten çalıştırınca):** `ProvisionBranchDatabaseRequest`
    record'u `[ApiController]` attribute'unun hemen altına düşmüştü; ASP.NET onu
    controller sanıp "Action 'Equals' does not have an attribute route" ile
    **uygulamanın açılmasını tamamen engelliyordu.** 584 test yeşilken bile
    yakalanmamıştı, çünkü hiçbir test tam uygulamayı ayağa kaldırmıyor.
  - **Uçtan uca doğrulama (gerçek HTTP, gerçek kontrol DB'si):** kullanıcı kaydı →
    proje → anahtar üretimi (`nmn_…`, uyarı metniyle) → izin YOKKEN liste **403**
    ("not allowed to read 'users'") → tabloyu okumaya aç → salt-okunur anahtarla
    yazma denemesi **403** → `/tables` doğru satırı döndürdü → geçersiz anahtar
    **401** → iptal sonrası **401** → şema sürümü sonrası `/openapi.json` **200**:
    yalnızca `users` belgelendi (`password_resets` izinsiz olduğu için YOK),
    yalnızca okuma yolları çıktı, `id` integer, `note` `["string","null"]`.
  - ⚠️ **Doğrulanamayan:** `GatewayKeyPanel`'in canlı render'ı. JWT httpOnly
    cookie'de tutuluyor (XSS'e karşı doğru tasarım), yani tarayıcıda oturum açmak
    giriş formuna parola yazmayı gerektiriyordu. Tip kontrolü temiz ve component
    mevcut `TeamPanel`/`PanelKit` desenini izliyor, ama gözle görülmedi.
  - **Dokümandan bilinçli iki sapma:** (1) `Authorization: Bearer` yerine
    `X-Namines-Key` — aynı uçlarda JWT de var, ikisini tek başlıkta taşımak "hangi
    kimlik?" belirsizliği yaratıyordu. (2) argon2id yerine SHA-256 — argon2 DÜŞÜK
    entropili parolalar için; burada anahtar 256-bit rastgele, argon2'nin yavaşlığı
    hiçbir şey kazandırmaz, her isteğe gecikme ekler.
  - **Kapsam dışı:** `expand` (ilişki gömme) — çalışma zamanında ŞEMA bilgisi ister,
    ama Gateway durumsuz (istek başına bağlantı dizesi). Anahtar yolunda proje
    biliniyor, oturum yolunda bilinmiyor; yalnızca bir kimlik yolunda çalışan bir
    özellik, hiç olmamasından kötü. GraphQL, `/realtime`, export/import, `/rpc`,
    `/query`, SDK üretimi, metadata cache de yapılmadı.
  - **Neon (06 §3):** hesap gerektirdiği için yapılamadı. Geldiğinde YENİ BİR
    SOYUTLAMA GEREKMİYOR — `IBranchDatabaseProvisioner`'ın ikinci bir
    implementasyonu olarak takılır ve yapılandırmadan seçilir.

- [x] **G28 — API anahtarı kaynak kısıtları ve anahtar başına rate limit ([08](08-GATEWAY-API.md) §4.3, §5)** ✅ TAMAMLANDI
  - `allowedOrigins`, `allowedIps` (düz IP + CIDR) ve `rateLimitPerMinute`
    (migration `AddGatewayKeyRestrictions`), `GatewayKeyRestrictions` +
    `GatewayRateLimiter`, arayüzde açılır "Limits…" bölümü.
  - ⚠️ **Tasarımın kritik noktası — IP kısıtı ne zaman UYGULANMAZ:** `Program.cs`,
    `ForwardedHeaders:KnownNetworks` tanımlı DEĞİLSE `X-Forwarded-For`'ı
    doğrulamadan kabul ediyor (PaaS'te proxy IP'si dinamik olabildiği için bilinçli
    bir taviz, kod bunu zaten uyarıyla söylüyor). O hâlde istemci kendi adresini
    istediği gibi yazabilir ve IP beyaz listesi **hiçbir şey doğrulamaz**. Böyle bir
    ortamda listeyi "uyguluyormuş gibi" davranmak, kullanıcıya korunduğunu
    sandıracak sahte bir güvence verirdi. Karar: **liste doluysa ve adres güvenilir
    değilse istek REDDEDİLİR**, nedeni de söylenir. Kısıtı sessizce uygulanamaz
    hâle getirmek, onu hiç istememekten kötüdür.
  - Origin başlığı taşımayan istek de liste doluysa reddediliyor: "origin kısıtla"
    diyen biri, başlığı hiç göndermeyen istemciye kapıyı açık bırakmayı kastetmez.
  - Reddetme mesajı çağıranın IP'sini **yankılamıyor** — hangi adres olarak
    görüldüğünü söylemek, kuralı atlatmayı deneyen birine geri bildirim verir.
  - Ayrıştırılamayan bir kural asla eşleşmez: bozuk kuralı "her şeye uyar" saymak,
    tek bir yazım hatasıyla kısıtı tamamen kaldırırdı.
  - ⚠️ **Rate limit bellek içi, yani INSTANCE BAŞINA.** Doküman (§5) Redis token
    bucket istiyor; Redis bu kurulumda yok. İki instance'ta gerçek sınır iki katına
    çıkar. Sabit pencere seçildi — sürgülü pencere daha adil ama istek başına zaman
    damgası listesi tutmayı, yani bellekte sınırsız büyüyebilen bir yapıyı gerektirir;
    sınır koymak için bellek sızdırmak yanlış takas.
  - Doğrulama: `GatewayKeyRestrictionTests` 24/24 (CIDR bayt-sınırı dışı prefix'ler,
    IPv4-mapped IPv6, bozuk kural, pencere sıfırlanması, anahtarların pencere
    paylaşmaması). Tam paket **608 test yeşil**.
  - **Uçtan uca (gerçek HTTP):** origin'siz istek **403**, yanlış origin **403**,
    doğru origin + 2/dk limit → 1. ve 2. istek kısıtları geçti, 3. istek **429**;
    IP kısıtlı anahtar `KnownNetworks` tanımsızken **403** ve mesaj nedeni açıkça
    söyledi ("Refusing rather than pretending the restriction is enforced").

- [x] **G29 — Gateway sorgu dilinin kalanı: export ve expand ([08](08-GATEWAY-API.md) §2)** ✅ TAMAMLANDI
  - CSV/JSON dışa aktarım + `expand` (ilişki gömme). `CsvWriter` kendi yazıldı:
    kaçırılmayan bir virgül CSV'nin SÜTUN SAYISINI değiştirir ve dosya sessizce
    bozulur. Aynı sınıftan iki tuzak daha kapatıldı — sayılar kültürden bağımsız
    (Türkçe kültürde `12,5` yeni bir sütun açar), `byte[]` base64 (ToString()
    "System.Byte[]" verip veriyi yok ederdi). UTF-8 BOM: Excel BOM'suz dosyayı
    ANSI sanıp Türkçe karakterleri bozuyor.
  - Tavan aşılırsa sorgu **kırpılmaz, reddedilir** — kırpılmış dosya eksik
    olduğunu söylemez, kullanıcı onu tam sanıp üzerine rapor kurar.
  - `expand`'de ilişkiyi **çağıran bildiriyor**, Gateway şemadan çıkarmıyor:
    Gateway durumsuz, projenin şemasını bilmiyor. Şemayı sunucuda aramak yalnızca
    API-anahtarı yolunda mümkün olurdu; tek kimlik yolunda çalışan bir özellik hiç
    olmamasından kötüdür. İlişki başına TEK ek sorgu (§2.1'in "N+1 yok" vaadi).

- [x] **G30 — Eject hedeflerinin tamamı ([12](12-CODEGEN-EJECT.md))** ✅ TAMAMLANDI
  - **15 hedef:** `types.typescript` (P0), `types.zod`, `types.csharp`,
    `types.python` (Pydantic v2), `contract.graphql`, `contract.jsonschema`,
    `contract.protobuf`, `orm.drizzle`, `orm.typeorm`, `orm.sqlalchemy`,
    `orm.django`, `orm.sequelize`, `orm.gorm`, `mig.flyway`, `mig.liquibase`.
    `IEjectGenerator` + `EjectGeneratorRegistry`, `POST /api/compile/eject/{target}`
    (+ `/zip`), `/compile` sayfasında "Export to…" sekmesi.
  - Kayıt defteri **yansımayla taranmıyor**, elle yazılıyor: yansıma yarım kalmış
    bir üreticiyi sessizce yayına sokar. Bir hedefin listeye girmesi bilinçli olmalı.
  - `CanonicalType` ve `EjectNaming` tek kopya. 15 hedefin her biri kendi tip
    listesini yazsaydı, biri yeni bir tipi eklemeyi unuttuğunda o hedef sessizce
    "string" üretirdi — ve bu ancak çalışma zamanında görülürdü.
  - Her hedef **ifade edemediğini bildiriyor** (Prisma'da alınan karar): CHECK
    kısıtı, index, unique. Uyarılar ZIP'in içine de yazılıyor — dosyayı bir hafta
    sonra açan kişi neyin taşınmadığını hatırlamaz.
  - Hedefe özgü, sessizce yanlış çalışan noktalar kapatıldı: TypeScript'te BIGINT
    → `string` (`number` 2^53 üstünü yuvarlar), Go'da alanlar PascalCase (küçük
    harfle başlayan alan dışa açık olmaz ve hiç doldurulmaz) ve nullable kolonlar
    pointer (yoksa NULL ile boş metin ayırt edilemez), Django'da `db_table` ve
    DecimalField'ın zorunlu argümanları, Sequelize'de `freezeTableName`/
    `timestamps:false` (varsayılanlar var olmayan kolonlara sorgu atardı),
    TypeORM'de her ad açık, C#'ta `JsonPropertyName`, GraphQL'de custom scalar
    bildirimi, Liquibase'de CDATA sonlandırıcısının kaçışı.
  - Drizzle Oracle/SQL Server'ı **reddediyor** — sessizce `pg-core` yazmak
    derlenebilir ama tamamen yanlış bir dosya üretirdi.
  - Flyway/Liquibase DDL'i yeniden yazmıyor, mevcut ve gerçek motorlara karşı
    doğrulanmış üreticiyi kullanıyor; Liquibase'in soyut etiketlerine çevirmek o
    doğrulamayı kaybetmek olurdu.
  - Doğrulama: `EjectGeneratorTests` **69/69** — her hedef için ortak sözleşme
    (boş çıktı yok, tablo adı geçiyor, boş şemada patlamıyor) + hedefe özgü
    tuzaklar. Tam paket **725 test yeşil**.
  - ⚠️ **Sınır:** üretilen kodun DERLENDİĞİ doğrulanmadı — Go, Python, Java ve
    TypeScript derleyicileri gerekirdi. Prisma'da `prisma validate` ile yapılabilen
    şeyin karşılığı burada yok; testler metin sözleşmesini kilitliyor.

- [x] **G31 — Console eject: Next.js admin paneli ([07](07-CONSOLE-ADMIN-UI.md) §8)** ✅ TAMAMLANDI
  - `console.nextjs` eject hedefi: şemadan ÇALIŞAN bir admin paneli (package.json,
    tsconfig, layout, tablo listesi, dinamik `[table]` sayfası, tipli Gateway
    istemcisi).
  - **Panel Gateway'e API anahtarıyla konuşuyor, veritabanına doğrudan değil.**
    İki sebep: bağlantı dizesi tarayıcıya inmez, ve Gateway'in tablo izinleri ile
    PII maskelemesi panelde de geçerli olur — panel ayrı bir güvenlik yüzeyi açmaz.
    Anahtar `NEXT_PUBLIC_` öneki OLMADAN, yalnızca sunucuda okunuyor.
  - **Desen seçimi otomatik** (§3.2): birincil anahtarı olmayan tablo salt-okunur
    (anahtarsız satır güvenle hedeflenemez, Gateway zaten reddediyor), bileşik
    anahtarın tamamı yabancı anahtarsa junction, kendine referans veren FK varsa
    tree. "Sıfır konfigürasyonda anlamlı panel" vaadi buradan geliyor — kullanıcıya
    "bu tablo nasıl görünsün?" diye sormak panelin değerini yok ederdi.
  - Şema meta verisi **veri olarak** gömülüyor, tablo başına sayfa olarak değil:
    40 tablolu bir şemada 40 dosya üretmek, kullanıcının bakımını yapacağı kodu
    40 katına çıkarırdı.
  - ⚠️ **Dokümandan sapma, bilinçli:** §8 "Next.js 16 + shadcn/ui + TanStack Table"
    diyor; üretilen paket bunları kullanmıyor, düz React + inline stil. shadcn ayrı
    bir CLI adımı ve onlarca dosya ister. **Kutudan çıktığı gibi çalışan** bir panel,
    kurulum adımı gerektiren "daha güzel" bir panelden iyidir; çıktı gerçek kaynak
    kod olduğu için kullanıcı üstüne kurabilir.
  - ⚠️ Yol boyunca: JSX'in `style={{...}}` çift süslü parantezi C#'ın `$$` ham dize
    interpolasyon sınırlayıcısıyla çakışıp derleme hatası verdi; yer tutucu + Replace
    ile çözüldü.
  - **Doğrulama — 12'de yapamadığımın karşılığı burada var:** üretilen panel diske
    döküldü, `npm install` ve **`npm run build` temiz geçti** (4 rota üretildi),
    `tsc --noEmit` 0 hata. Yani "üretilen kod derleniyor" bu hedefte KANITLANDI.
    `EjectGeneratorTests` **78/78**, tam paket **734 test yeşil**.
  - **Kapsam dışı:** React+Vite, Blazor, Retool JSON hedefleri; form/düzenleme
    ekranları (şu an liste + sayfalama var, yazma yok); §4 Console RBAC, §5 audit
    log, §6 dashboard, §7 doğal dil sorgu, §9 özelleştirme overlay'i.

- [x] **G32 — TypeScript SDK üretimi ([12](12-CODEGEN-EJECT.md) §7, P0)** ✅ TAMAMLANDI
  - `sdk.typescript` hedefi: Gateway için tip güvenli istemci (`client.ts`,
    `types.ts`, `index.ts`, `README.md`). 12 §P0'ın kalan yarısıydı — tipler vardı
    ama müşteri hâlâ elle `fetch` yazıp gövdeyi doğru kurmak zorundaydı.
  - **Tablo başına metot üretiliyor**, tek bir `list(table)` değil: jenerik imzada
    tablo adını yanlış yazmak çalışma zamanına kalır, üretilen metotta yanlış ad
    DERLEME hatasıdır. SDK'nın tüm değeri hatayı erkene çekmek.
  - Tekil birincil anahtarı olmayan tabloya `get/update/delete` **üretilmiyor**:
    Gateway anahtarsız yazmayı zaten reddediyor, metot üretmek çağıranı çalışma
    zamanında patlayacak bir yola sokardı. Bu tablolar için yalnızca `list`/`export`
    var ve durum uyarıyla bildiriliyor.
  - Gateway'in ret mesajı `NaminesError` ile **olduğu gibi** aktarılıyor — hangi
    tablo reddedildi, hangi limit doldu, yazma neden geri alındı. Genel bir
    "request failed" bu bilgiyi çöpe atardı.
  - README anahtarın tarayıcıya konmamasını açıkça söylüyor: anahtar bir bearer
    kimlik bilgisi, tarayıcıya inerse her ziyaretçi anahtarın eriştiği tabloların
    tamamına erişir.
  - **Doğrulama:** üretilen SDK diske döküldü ve `strict` altında `tsc --noEmit`
    **0 hata** verdi; bileşik anahtarlı tablonun yalnızca `list`/`export` aldığı
    çıktıda görüldü. `EjectGeneratorTests` **86/86**, tam paket **742 test yeşil**.

---

## G-ekstra — Yol boyunca bulunanlar

- [x] `launchSettings.json` port çelişkisi — **zaten çözülmüştü** (`dfdfc49`, bu G14
      oturumundan önceki bir commit). Checklist'in kendisi güncel değildi, madde yanlış
      alarm; `applicationUrl` her iki profilde de `5000`, README/docker-compose'la tutarlı.
- [x] `DatabaseExecutorController.cs:33,50` — CS8625 nullable uyarısı düzeltildi.
      `ExecutorRequest.ConnectionString` `string` → `string?` (handler'lar kullanım
      sonrası bilerek `null`'a çekiyor, GC/güvenlik amaçlı — artık tip bunu doğru ifade
      ediyor). Build: 0 uyarı, 0 hata.
- [x] `DockerBackupService.cs` — `sqlcmd` çağrılarının 4'ünde de eksik olan `-b` bayrağı
      eklendi (G12'de `BranchTestRunnerService`'te bulunan aynı hata: `-b` olmadan
      `sqlcmd -i script.sql` T-SQL hatalarında sessizce exit 0 dönüyordu — Docker Sandbox
      özelliği hatalı DDL'i "başarılı" sayabiliyordu). Ayrıca aynı oturumda bulunan ikinci
      bir hata da düzeltildi: `mysqladmin ping`, MySQL 8'in iki-aşamalı başlangıcındaki
      geçici sunucuya karşı erken "hazır" diyordu — health check gerçek kimlik doğrulamalı
      bir `SELECT 1` sorgusuna çevrildi. Namines.Infrastructure build: 0 uyarı, 0 hata.

---

## Kod dışı işler (sen yapacaksın)

- [ ] `C:\Users\Enes Yel` dizinindeki yanlış git deposunu düzelt (remote'u `automated-recruitment-pipeline`)
- [ ] Ödeme altyapısı araştırması (Stripe TR sınırlı → Paddle / LemonSqueezy)
- [ ] `namines.com` alan adı + marka taraması

### Kodun beklediği kararlar/erişimler

Aşağıdakiler olmadan ilgili iş TAMAMLANAMAZ — kod tarafı hazır, eksik olan senin
vereceğin bilgi ya da hesap. Hiçbiri diğer işleri bloke etmiyor.

- [ ] **Neon hesabı + `NEON_API_KEY`** (06 §3). `neon.tech` → kayıt → proje → API key.
      Anahtarı sohbete yapıştırma, ortam değişkenine koy. Geldiğinde
      `IBranchDatabaseProvisioner`'ın ikinci implementasyonu olarak takılır.
- [ ] **npm + GitHub yayını.** MCP paketi, npm sarmalayıcısı ve release workflow
      hazır ama `npm publish` ve `git tag v0.1.0` atılmadı — hesap gerekiyor.
- [ ] **Gateway'in public alan adı** (`api.namines.com`?). OpenAPI'deki `servers`
      bloğu ve üretilen SDK'nın taban URL'i buna bağlı. Şimdilik göreli yol.
- [ ] **Plan başına rate limit sayıları** (08 §5: 600-10.000 rpm aralığı veriliyor).
- [ ] **Redis** kararı. Çok instance'lı rate limit (şu an bellek içi, instance
      başına) ve metadata cache (08 §6) buna bağlı.
- [ ] **Dokümandan iki sapmanın onayı:** (1) `Authorization: Bearer` yerine
      `X-Namines-Key` — aynı uçlarda JWT de var, tek başlıkta taşımak "hangi kimlik?"
      belirsizliği yaratıyordu. (2) argon2id yerine SHA-256 — argon2 düşük entropili
      PAROLALAR için; anahtar 256-bit rastgele, argon2'nin yavaşlığı hiçbir şey
      kazandırmaz, her isteğe gecikme ekler.
- [ ] **Stripe fiyat/plan eşlemesi** (22). Kod `SubscriptionStatus`'tan yalnızca
      Free/Pro çıkarabiliyor; Team/Enterprise ayrımı için plan alanı ve Stripe
      price id'leri gerekiyor.

### Kodun beklediği kararlar/erişimler

Aşağıdakiler olmadan ilgili iş TAMAMLANAMAZ — kod tarafı hazır, eksik olan senin
vereceğin bilgi ya da hesap. Hiçbiri diğer işleri bloke etmiyor.

- [ ] **Neon hesabı + `NEON_API_KEY`** (06 §3). `neon.tech` → kayıt → proje → API key.
      Anahtarı sohbete yapıştırma, ortam değişkenine koy. Geldiğinde
      `IBranchDatabaseProvisioner`'ın ikinci implementasyonu olarak takılır.
- [ ] **npm + GitHub yayını.** MCP paketi, npm sarmalayıcısı ve release workflow
      hazır ama `npm publish` ve `git tag v0.1.0` atılmadı — hesap gerekiyor.
- [ ] **Gateway'in public alan adı** (`api.namines.com`?). OpenAPI'deki `servers`
      bloğu ve üretilen SDK'nın taban URL'i buna bağlı. Şimdilik göreli yol.
- [ ] **Plan başına rate limit sayıları** (08 §5: 600-10.000 rpm aralığı veriliyor).
- [ ] **Redis** kararı. Çok instance'lı rate limit (şu an bellek içi, instance
      başına) ve metadata cache (08 §6) buna bağlı.
- [ ] **Dokümandan iki sapmanın onayı:** (1) `Authorization: Bearer` yerine
      `X-Namines-Key` — aynı uçlarda JWT de var, tek başlıkta taşımak "hangi kimlik?"
      belirsizliği yaratıyordu. (2) argon2id yerine SHA-256 — argon2 düşük entropili
      PAROLALAR için; anahtar 256-bit rastgele, argon2'nin yavaşlığı hiçbir şey
      kazandırmaz, her isteğe gecikme ekler.
- [ ] **Stripe fiyat/plan eşlemesi** (22). Kod `SubscriptionStatus`'tan yalnızca
      Free/Pro çıkarabiliyor; Team/Enterprise ayrımı için plan alanı ve Stripe
      price id'leri gerekiyor.
