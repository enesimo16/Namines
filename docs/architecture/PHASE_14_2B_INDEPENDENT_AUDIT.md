# NAMINES — PHASE 14.2B INDEPENDENT ACCEPTANCE AUDIT
## CloudProjects God Entity Decomposition — Bağımsız Kabul ve Güvenlik Denetimi

> **Tarih:** 2026-11-04  
> **Denetçi Rolü:** Principal Software Architect, Senior .NET Engineer, PostgreSQL Database Engineer & Security Auditor  
> **Denetim Kapsamı:** Phase 14.2B Uygulama Raporu ve Kod Tabanı Gerçekliği  
> **Nihai Karar:** ⚠️ **CONDITIONALLY ACCEPTED (ŞARTLI KABUL)**  
> *(Temel şema ve servis altyapısı başarıyla kurulmuş olmakla birlikte, 4 adet bloklayıcı entegrasyon ve veri senkronizasyonu açığı giderilmeden Phase 14.2C'ye GEÇİLEMEZ.)*  
> **Gerçek Tamamlanma Oranı:** **%75**

---

## 1. YÖNETİCİ ÖZETİ VE BAĞIMSIZ DENETİM KARARI

Phase 14.2B uygulama raporu (`PHASE_14_2B_IMPLEMENTATION_REPORT.md`), `CloudProjects` God Entity ve God Table yapısının tamamen ortadan kaldırıldığını ve tüm tüketicilerin yeni modül sözleşmelerine geçirildiğini iddia etmiştir. 

Mevcut kaynak kodlar, PostgreSQL migration betikleri, EF Core model yapılandırmaları ve birim/entegrasyon testleri üzerinde yapılan **satır satır bağımsız denetim**, bu iddiaların gerçeği tam yansıtmadığını ortaya koymuştur:

1. **God Table Fiziksel Olarak Durmaktadır:** `public.CloudProjects` tablosundan **hiçbir kolon fiziksel olarak DROP edilmemiştir (0 kolon silinmiştir)**. `BuildContractSql()` yalnızca bazı kolonları `NULL` yapmıştır; ancak `Visibility`, `Category`, `StarCount`, `ForkCount` ve `ForkedFromProjectId` alanları `NULL` dahi yapılmamıştır.
2. **Kritik Fonksiyonel Regresyon (Yeni Projelerin Paylaşılamaması):** `AuthController.cs` yeni proje oluştururken ve güncellerken yalnızca `_context.CloudProjects` tablosuna yazmaktadır; `studio.projects` tablosuna yazmamaktadır. Sonuç olarak, göçten sonra oluşturulan herhangi bir proje için `ShareController.CreateShareLink` çağrıldığında `StudioApplicationService` projeyi `studio.projects` içinde bulamamakta, `KeyNotFoundException` fırlatmakta ve API kullanıcıya **404 Not Found** dönmektedir!
3. **Vitrin ve Sosyal Sayaç Tutarsızlığı:** `HubController.ToggleStar` ve `AdjustStarCountAsync` metotları yalnızca `_context.CloudProjects.StarCount` kolonunu güncellemektedir; `hub.repositories.StarCount` kolonuna dokunmamaktadır. `HubController.Explore` ucu ise yalnızca `_context.CloudProjects` tablosunu sorgulamaktadır. Eğer `BuildContractSql()` (eski kolonları null yapan betik) çalıştırılırsa, `CloudProjects.Description` null olacağı için Explore akışı anında **0 sonuç döndürerek çökecektir.**
4. **Anahtar Rotasyonu (Key Rotation) İhlali:** `ConnectionSecretReencryptor.cs` yalnızca `CloudProjects.EncryptedConnectionString` alanını taramakta ve yeniden şifrelemektedir. `studio.project_connections` tablosu rotasyon kapsamı dışındadır.
5. **EF Core Model Snapshot Uyumsuzluğu:** `AuthDbContext` ve `AuthDbContextModelSnapshot.cs` içerisinde `CloudProject` hâlâ 14 kolon, tüm kısıtlar ve indeksleriyle yer almaktadır. EF Core seviyesinde herhangi bir migration üretilmemiştir.

Bu gerekçelerle, Phase 14.2B **tam kabul (ACCEPTED) alamamış**, **ŞARTLI KABUL (CONDITIONALLY ACCEPTED)** statüsüne alınmıştır.

---

## 2. BÖLÜM 1: CLOUDPROJECTS KALINTILARI DENETİMİ

### 2.1. Entity ve Model Snapshot Durumu
- **`CloudProject.cs`:** `backend/Namines.Core/Models/Auth/CloudProject.cs` incelendiğinde; `EncryptedConnectionString`, `ConnectionDbType`, `AllowDeskSql`, `ShareToken`, `ShareTokenHash`, `Slug`, `Description`, `Visibility`, `Category`, `Tags`, `StarCount`, `ForkCount`, `ForkedFromProjectId`, `LinkedGithubRepo` özelliklerinin **tamamı entity sınıfı üzerinde durmaktadır.**
- **`AuthDbContextModelSnapshot.cs`:** Satır 550–650 arasında `CloudProject` için yukarıda sayılan 14 alanın tamamı, check constraint'ler (`CK_CloudProjects_StarCount`, `CK_CloudProjects_Tags_Length` vb.) ve filtreli indeksler (`IX_CloudProjects_Explore_Public`) eksiksiz olarak snapshot içinde mevcuttur.
- **`AuthDbContext.cs`:** Satır 12'de `public DbSet<CloudProject> CloudProjects { get; set; } = null!;` durmaktadır. ModelBuilder içinde `Slug`, `Visibility` ve `StarCount` indeksleri ile tüm check constraint'ler tanımlanmaya devam etmektedir.

### 2.2. Fiziksel Kolon ve Kısıt Durumu (PostgreSQL)

| Kolon Adı | `CloudProjects`'teki Durumu | Hedef Tablo | Fiziksel Olarak DROP Edildi mi? | Güvenlik / Bütünlük Riski |
|---|---|---|---|---|
| `Id`, `Name`, `DbType`, `SchemaJson`, `NodePositionsJson`, `UserId`, `OrganizationId`, `AutoApproveSafeChanges` | Korunuyor | `studio.projects` | Hayır (Hedefte var) | Temel çekirdek model |
| `EncryptedConnectionString` | `UPDATE ... SET NULL` | `studio.project_connections` | **HAYIR (Kolon duruyor)** | Gateway & LaunchService eski kolondan okuyor |
| `ConnectionDbType` | `UPDATE ... SET NULL` | `studio.project_connections` | **HAYIR (Kolon duruyor)** | Gateway eski kolondan okuyor |
| `AllowDeskSql` | `UPDATE ... SET = false` | `studio.project_connections` | **HAYIR (Kolon duruyor)** | Gateway & AuthController eski kolondan okuyor |
| `ShareToken` | `UPDATE ... SET NULL` | Yok (`[NotMapped]`) | **HAYIR (Kolon duruyor)** | Düz metin veri temizlendi, kolon fiziksel duruyor |
| `ShareTokenHash` | `UPDATE ... SET NULL` | `studio.project_shares` | **HAYIR (Kolon duruyor)** | ShareController legacy fallback'inde aranıyor |
| `Slug` | `UPDATE ... SET NULL` | `hub.repositories` | **HAYIR (Kolon duruyor)** | HubController GetRepo/Explore eski kolondan okuyor |
| `Description` | `UPDATE ... SET NULL` | `hub.repositories` | **HAYIR (Kolon duruyor)** | HubController Explore eski kolondan okuyor |
| `Visibility` | **NULL BİLE YAPILMADI** | `hub.repositories` | **HAYIR (Kolon duruyor)** | HubController Explore filtreliyor |
| `Category` | **NULL BİLE YAPILMADI** | `hub.repositories` | **HAYIR (Kolon duruyor)** | HubController Explore filtreliyor |
| `Tags` (CSV) | `UPDATE ... SET NULL` | `hub.project_tags` | **HAYIR (Kolon duruyor)** | HubController Explore tag filtresinde aranıyor |
| `StarCount` | **NULL BİLE YAPILMADI** | `hub.repositories` | **HAYIR (Kolon duruyor)** | ToggleStar yalnızca bu kolonu güncelliyor! |
| `ForkCount` | **NULL BİLE YAPILMADI** | `hub.repositories` | **HAYIR (Kolon duruyor)** | Fork yalnızca bu kolonu artırıyor |
| `ForkedFromProjectId` | **NULL BİLE YAPILMADI** | `hub.repositories` | **HAYIR (Kolon duruyor)** | HubController ilişkide kullanıyor |
| `LinkedGithubRepo` | `UPDATE ... SET NULL` | `hub.repositories` | **HAYIR (Kolon duruyor)** | Eski kolon fiziksel duruyor |

**Sonuç:** `CloudProjects` tablosundan **tek bir kolon dahi DROP edilmemiştir.** `BuildContractSql()` yalnızca seçici bir `UPDATE ... SET = NULL` çalıştırmaktadır.

---

## 3. BÖLÜM 2: VERİ TAŞIMA VE MIGRATION GÜVENLİĞİ DENETİMİ

### 3.1. `Phase14_2B_DecompositionSql.cs` Analizi

1. **Idempotency (Yinelenebilirlik):**
   - `BuildExpandSql()`: `CREATE SCHEMA IF NOT EXISTS` ve `CREATE TABLE IF NOT EXISTS` kullandığı için tam idempotenttir.
   - `BuildBackfillSql()`: `studio.projects`, `studio.project_connections`, `studio.project_shares`, `hub.repositories` için `ON CONFLICT (...) DO UPDATE`, `hub.project_tags` için `ON CONFLICT (...) DO NOTHING` kullandığı için mükerrer satır üretmez. Tekrar çalıştırılabilir.
2. **Kriptografik Sır ve AAD Bütünlüğü:**
   - `studio.project_connections` aktarımında `EncryptedConnectionString` verisi doğrudan kopyalanmaktadır; şifreli metin veya AAD (`ProjectId`) bozulmamaktadır.
   - Ancak `ConnectionSecretReencryptor.cs` (Key Rotation) yalnızca `CloudProjects` tablosunu güncellemektedir. `studio.project_connections` tablosunu yeniden şifrelememektedir!
3. **Paylaşım Jetonları:**
   - `studio.project_shares` aktarımında düz metin token aktarılmamakta; yalnızca mevcut `ShareTokenHash` veya PostgreSQL `encode(sha256(convert_to(p."ShareToken", 'UTF8')), 'hex')` ile türetilen karma aktarılmaktadır.
   - Düz metin token'lar veritabanına sızmamaktadır.
4. **1NF Normalizasyonu (Etiketler):**
   - `unnest(string_to_array(p."Tags", ','))` ile CSV string'leri temizlenerek `hub.project_tags` tablosuna aktarılmaktadır.
5. **Verify Aşaması Eksiklikleri (`BuildVerifySql`):**
   - `studio.projects`, `studio.project_connections`, `studio.project_shares`, `hub.repositories` için satır sayısı denetimi yapılmaktadır.
   - **Kritik Eksik 1:** `hub.project_tags` tablosu için satır veya bütünlük denetimi **hiç yapılmamaktadır.**
   - **Kritik Eksik 2:** Doğrulama yalnızca `IF v_actual < v_expected` büyüklük kontrolü yapmaktadır. Orphan (yetim) kayıt denetimi veya alan bazında checksum/veri karşılaştırması bulunmamaktadır.
6. **Premature Contract Tehlikesi:**
   - `BuildContractSql()`, `CloudProjects` üzerindeki `EncryptedConnectionString`, `ShareTokenHash`, `Tags`, `Slug`, `Description` alanlarını `NULL` yapmaktadır.
   - **Tehlike:** Backend'deki `GatewayController`, `HubController.Explore`, `AuthController` bu alanları doğrudan okumaya devam ettiği için, bu SQL çalıştırıldığı anda canlı sistemde Gateway bağlantıları kopacak ve Explore akışı boş dönecektir!

---

## 4. BÖLÜM 3: MİMARİ SINIR VE BAĞIMLILIK DENETİMİ

Tüm backend kod tabanı taranmış ve modüler sınır ihlalleri tespit edilmiştir:

```
┌────────────────────────────────────────────────────────────────────────┐
│                   MİMARİ SINIR İHLAL HARİTASI                          │
├──────────────────────────┬─────────────────────────────────────────────┤
│ Dosya & Satır            │ Tespit Edilen İhlal Türü                    │
├──────────────────────────┼─────────────────────────────────────────────┤
│ AuthController.cs:613    │ p.EncryptedConnectionString != null         │
│ AuthController.cs:616    │ p.AllowDeskSql doğrudan okunuyor            │
│ GatewayController.cs:506 │ p.EncryptedConnectionString doğrudan Select │
│ GatewayController.cs:838 │ p.AllowDeskSql & ConnectionString doğrudan  │
│ GatewayController.cs:1764│ p.EncryptedConnectionString doğrudan Select │
│ GatewayKeyController:479 │ project.EncryptedConnectionString yazılıyor │
│ GatewayKeyController:572 │ project.AllowDeskSql doğrudan yazılıyor     │
│ HubController.cs:93      │ FindProjectByOwner -> _context.CloudProjects│
│ HubController.cs:133     │ Explore -> _context.CloudProjects sorgusu   │
│ HubController.cs:598     │ ToggleStar -> _context.CloudProjects        │
│ HubController.cs:670     │ AdjustStarCount -> _context.CloudProjects   │
│ HubController.cs:733     │ Fork -> _context.CloudProjects.AddAsync     │
│ LaunchService.cs:98, 112 │ project.EncryptedConnectionString doğrudan  │
│ Reencryptor.cs:23-40     │ CloudProjects üzerinden rotasyon            │
│ TenantAuthority:176, 273 │ _studioModule.DbContext'e doğrudan erişim   │
└──────────────────────────┴─────────────────────────────────────────────┘
```

### 4.1. `HubController.cs` İhlalleri
- `HubController`, `IHubApplicationService` enjeksiyonuna sahip olmasına rağmen, 1209 satırlık kod içerisinde yalnızca 2 noktada (`UpdateSettings` ve `Fork`) dual-write amacıyla `_hub`'ı çağırmaktadır.
- `Explore` uç noktası (satır 121–220) tamamen `_context.CloudProjects` üzerinden çalışmaktadır.
- `ToggleStar` ve `AdjustStarCountAsync` (satır 588–675) tamamen `_context.CloudProjects.StarCount` üzerinden çalışmaktadır.
- `GetRepo`, `DeleteRepo`, `GetIssues`, `CreateIssue` tamamen `_context.CloudProjects` tablosuna sorgu atmaktadır.

### 4.2. `AuthController.cs` İhlalleri
- `CreateProject`, `SyncProject`, `GetProjects` (satır 520–650) doğrudan `_context.CloudProjects` tablosuna yazmakta ve okumaktadır.
- `IStudioApplicationService` enjekte edilmemiştir; yeni projeler `studio.projects` tablosuna aktarılmamaktadır.

---

## 5. BÖLÜM 4: İŞLEVSEL REGRESYON DENETİMİ (12 TEMEL AKIŞ)

| No | İş Akışı | Önceki Davranış Korundu mu? | Yeni Modüler Yapıya Geçti mi? | Durum & Tespit |
|---|---|---|---|---|
| 1 | **Proje Oluşturma** | Evet (Çalışıyor) | ❌ **HAYIR** | Sadece `CloudProjects`'e yazıyor. `studio.projects`'e yazmıyor! |
| 2 | **Proje Güncelleme** | Evet (Çalışıyor) | ❌ **HAYIR** | Sadece `CloudProjects`'i güncelliyor. |
| 3 | **Şema & Canvas Kaydetme** | Evet (Çalışıyor) | ❌ **HAYIR** | `CloudProjects.SchemaJson` üzerinden. |
| 4 | **Connection Kaydetme / Çözme** | Kısmen | ⚠️ Kısmen | `TenantProjectAuthorityService` çift yazıyor/okuyor; ancak `GatewayController` doğrudan `CloudProjects` okuyor. |
| 5 | **Share Oluşturma / Görüntüleme** | ❌ **REGRESYON** | ⚠️ Kısmen | **KRİTİK HATA:** Yeni açılan projeler `studio.projects`'te olmadığı için `POST /api/share/{id}` **404 NOT FOUND** vermektedir! |
| 6 | **Repository Vitrin Ayarları** | Evet | ✅ Evet | `HubController.UpdateSettings` hem `CloudProjects` hem `hub.repositories` güncelliyor. |
| 7 | **Explore Feed ve Filtreleme** | Evet (Mevcutta) | ❌ **HAYIR** | Sadece `CloudProjects` sorguluyor. `ContractSql` çalıştırılırsa boş döner. |
| 8 | **Star ve Unstar** | Evet (Mevcutta) | ❌ **HAYIR** | Yalnızca `CloudProjects.StarCount` güncelleniyor; `hub.repositories.StarCount` sabit kalıyor! |
| 9 | **Fork / Klonlama** | Evet | ✅ Evet | `CloudProjects` + `hub.repositories` + `studio.projects` dual-write yapılıyor. |
| 10 | **Proje Silme (Deletion)** | Evet | ⚠️ Kısmen | `ProjectDeletionService` Studio ve Hub'dan siliyor; ancak ayrı transaction'larda çalıştığı için atomik değil. |
| 11 | **Launch / Provisioning** | Evet | ❌ **HAYIR** | `LaunchService` doğrudan `CloudProjects.EncryptedConnectionString` okuyor. |
| 12 | **Gateway Canlı Bağlantı** | Evet | ❌ **HAYIR** | `GatewayController` doğrudan `CloudProjects`'ten sorguluyor. |

---

## 6. BÖLÜM 5: TESTLERİN BAĞIMSIZ DOĞRULANMASI

### 6.1. Gerçek Test Çalıştırma Sonuçları
Tüm test paketleri makinede bağımsız olarak koşturulmuştur:

1. **`Namines.Tests.RunTests` (Gerçek Docker Testleri):**
   - Komut: `dotnet test backend/Namines.Tests.RunTests/Namines.Tests.RunTests.csproj`
   - Sonuç: **19/19 Başarılı** (0 Hata, 0 Atlanan, Süre: 1m 8s).
2. **`Phase14_2B_DecompositionTests` (PostgreSQL 17 Testcontainers):**
   - Komut: `dotnet test backend/Namines.Tests/Namines.Tests.csproj --filter "FullyQualifiedName~Phase14_2B"`
   - Sonuç: **4/4 Başarılı** (0 Hata, 0 Atlanan, Süre: 9s).
3. **`ModularMonolithArchitectureTests`:**
   - Sonuç: **13/13 Başarılı**.
4. **`ShareTokenHashTests` & `HubControllerTests`:**
   - Sonuç: **10/10 Başarılı**.

### 6.2. Testlerdeki Metodolojik Eksiklikler ve İllüzyonlar
Testlerin yeşil olması, mimarinin eksiksiz tamamlandığını **kanıtlamamaktadır**:
- **Test İllüzyonu 1 (`HubControllerTests`):** Testler `new HubController(db, userManager, ...)` çağrısında `_hub` parametresini `null` bırakmaktadır! Bu nedenle testler `IHubApplicationService`'i hiç çalıştırmamakta, yalnızca in-memory SQLite `CloudProjects` üzerinde dönmektedir.
- **Test İllüzyonu 2 (`ShareTokenHashTests`):** Testler `new ShareController(db, ...)` çağrısında `_studio` parametresini `null` bırakmakta, yalnızca legacy fallback kodunu test etmektedir.
- **Test İllüzyonu 3 (`Phase14_2B_DecompositionTests`):** PostgreSQL üzerinde koşan test, gerçek uygulama kodlarını (`AuthController`, `GatewayController`, `HubController`) değil; elle oluşturulan 3 satırlık izole bir mock tablo üzerinde SQL metinlerini çalıştırmaktadır. Gerçek uygulamanın contract sonrası davranışı test edilmemiştir.

---

## 7. BÖLÜM 6: BULGU TABLOSU (FINDINGS LOG)

| ID | Bulgu | Severity | Dosya / Kod Kanıtı | Gerekli Düzeltme | Bloklayıcı mı? |
|---|---|---|---|---|---|
| **SEC-14B-01** | Yeni oluşturulan projeler `studio.projects`'e yazılmıyor; Share bağlantısı 404 veriyor. | **CRITICAL (P0)** | `AuthController.cs:520` & `StudioApplicationService.cs:137` | `AuthController` içerisine `IStudioApplicationService` enjekte edilmeli, `CreateProject` ve `SyncProject`'te çift yazma (dual-write) sağlanmalı. | **EVET** |
| **SEC-14B-02** | `HubController.ToggleStar` ve sayaçlar `hub.repositories`'e yazmıyor; veriler desenkronize. | **HIGH (P1)** | `HubController.cs:659-675` | `ToggleStar` ve `AdjustStarCountAsync` işlemleri `IHubApplicationService` üzerinden `hub.repositories`'e senkronize edilmeli. | **EVET** |
| **SEC-14B-03** | Anahtar rotasyonu (`ConnectionSecretReencryptor`) `studio.project_connections`'ı kapsamıyor. | **HIGH (P1)** | `ConnectionSecretReencryptor.cs:23-40` | `ReencryptAsync` metodu `StudioDbContext.ProjectConnections` tablosunu da tarayıp yeniden şifrelemeli. | **EVET** |
| **SEC-14B-04** | `Phase14_2B_DecompositionSql.BuildContractSql` erken çalıştırılırsa Gateway ve Explore çöker. | **HIGH (P1)** | `Phase14_2B_DecompositionSql.cs:257-280` | `ContractSql` aşaması, tüm tüketiciler (`GatewayController`, `HubController.Explore`) tamamen yeni tablolara geçene (Phase 14.2E) kadar ertelenmeli veya kilitlenmeli. | **EVET** |
| **SEC-14B-05** | `BuildVerifySql` içerisinde `hub.project_tags` doğrulanmıyor; yetim kayıt kontrolü yok. | **MEDIUM (P2)**| `Phase14_2B_DecompositionSql.cs:202-250` | `BuildVerifySql` içine `hub.project_tags` satır sayısı kontrolü ve orphan ID eşleşme kontrolü eklenmeli. | **HAYIR** |
| **SEC-14B-06** | `ProjectDeletionService` ayrı transaction'larda çalışıyor (Dağıtık atomisite eksik). | **MEDIUM (P2)**| `ProjectDeletionService.cs:110-123` | `BuildingBlocks.Persistence.SharedTransactionManager` kullanılarak Studio, Hub ve AuthContext tek veritabanı transaction'ına bağlanmalı. | **HAYIR** |
| **SEC-14B-07** | Unit testler (`HubControllerTests`, `ShareTokenHashTests`) yeni modül servislerini mocklamıyor. | **MEDIUM (P2)**| `HubControllerTests.cs:60`, `ShareTokenHashTests.cs:72` | Test suitelerine `IHubApplicationService` ve `IStudioApplicationService` içeren kapsamlı senaryolar eklenmeli. | **HAYIR** |

---

## 8. PHASE 14.2B KABUL KRİTERLERİ DURUM MATRİSİ

| Kriter | Planlanan Hedef | Gerçekleşen Durum | Sonuç |
|---|---|---|---|
| **1. 3NF Tablo Tanımları** | `studio.projects`, `studio.project_connections`, `studio.project_shares`, `hub.repositories`, `hub.project_tags` | Tamamı oluşturuldu ve EF Core modelleri tanımlandı. | ✅ **BAŞARILI** |
| **2. Kriptografik Paylaşım Güvenliği** | SHA-256 karma tekil saklanmalı, raw token DB'ye yazılmamalı | `StudioApplicationService` ve `ProjectShare` üzerinde eksiksiz uygulandı. | ✅ **BAŞARILI** |
| **3. 1NF Etiket Normalizasyonu** | CSV etiketler ilişkisel `(ProjectId, Tag)` tablosuna ayrıştırılmalı | `hub.project_tags` tablosuna aktarıldı, B-tree indeks kuruldu. | ✅ **BAŞARILI** |
| **4. Dual-Write Bütünlüğü** | Yeni projeler ve güncellemeler normalize tablolara yansıtılmalı | `AuthController` ve `HubController.ToggleStar` yeni tablolara yazmıyor! | ❌ **BAŞARISIZ** |
| **5. Sıfır Regresyon Garantisi** | Dış API sözleşmeleri ve UI kesintisiz çalışmalı | Yeni açılan projelerin paylaşılamaması (404) hatası mevcut. | ❌ **BAŞARISIZ** |
| **6. Idempotent Migration SQL** | Expand -> Backfill -> Verify işlem içinde kayıpsız çalışmalı | PostgreSQL 17 testinde doğrulandı; ancak Verify'da tags eksik. | ⚠️ **KISMEN** |
| **7. God Entity Tasfiyesi** | Eski kolonlar ve bağımlılıklar temizlenmeli | Kolonlar fiziksel duruyor; tüketiciler doğrudan okumaya devam ediyor. | ⚠️ **KISMEN** |

---

## 9. NİHAİ KABUL KARARI VE BLOKLAYICI DÜZELTME PLANI

### Karar: ⚠️ CONDITIONALLY ACCEPTED (ŞARTLI KABUL)

Phase 14.2B, altyapı ve mimari tasarım seviyesinde hedeflerine büyük ölçüde ulaşmıştır. Ancak yukarıda listelenen **P0 ve P1 seviyesindeki 4 bloklayıcı açık kapatılmadan Phase 14.2C'ye geçilmesi sistem bütünlüğü açısından kabul edilemez.**

### Phase 14.2C Öncesi Zorunlu Düzeltme Planı (Remediation Plan):

1. **Adım 1 (P0 — Proje Oluşturma / Paylaşım Köprüsü):**
   - `AuthController.cs`'e `IStudioApplicationService` enjekte edilecek.
   - `CreateProject` ve `SyncProject` metotlarında proje `_context.CloudProjects`'e kaydedilirken eşzamanlı olarak `_studio.CreateOrUpdateProjectAsync` çağrılacak. Böylece yeni projelerin `studio.projects`'e yazılması ve `ShareController`'ın 404 vermesi engellenecek.
2. **Adım 2 (P1 — Sosyal Sayaç Senkronizasyonu):**
   - `HubController.ToggleStar` ve `AdjustStarCountAsync` metotlarına `_hub.ToggleStarAsync` çağrısı entegre edilecek.
3. **Adım 3 (P1 — Anahtar Rotasyonu Kapsamı):**
   - `ConnectionSecretReencryptor.cs` içerisine `StudioDbContext.ProjectConnections` tarama ve yeniden şifreleme mantığı eklenecek.
4. **Adım 4 (P1 — Doğrulama SQL Sertleştirmesi):**
   - `Phase14_2B_DecompositionSql.BuildVerifySql()` içerisine `hub.project_tags` satır sayısı denetimi eklenecek.
   - `BuildContractSql()`'in bağımlılıklar tamamen bitene kadar çalıştırılmaması gerektiği dokümante edilecek.

Bu 4 düzeltme tamamlanıp test edildikten sonra Phase 14.2B tam kabule dönecek ve Phase 14.2C'ye başlanabilecektir.
