# ARCHITECTURE AUDIT — Mevcut Kod Tabanı ve Veritabanı Denetimi

> Tarih: 2026-11-02 · Baz alınan commit: `8cb5cbc` (`main`)

---

## 1. Yönetici Özeti

Namines arka ucu (`Namines.sln`) şu anda **11 `.csproj`** barındırmaktadır ancak gerçek bir **.NET Modular Monolith** ve **Katmanlı Mimari (Layered / Clean Architecture)** yapısında değildir:
1. **Veritabanı Erişimi Monolitiktir:** Dört modülün (`Vault`, `Ground`, `Desk`, `Flow`) tabloları PostgreSQL'de ayrı şemalara (`namines_vault`, `ground`, `desk`, `flow`) yazılmış olsa da, tamamı `Namines.Infrastructure` içindeki tekil [`AuthDbContext`](../../backend/Namines.Infrastructure/Data/AuthDbContext.cs) (37 `DbSet`) üzerinden yönetilmektedir.
2. **Modül Sınırları Delinmiştir:** 26 Controller ve 18 Infrastructure servisi doğrudan `AuthDbContext` enjekte etmektedir. Örneğin `AdminController` 13 farklı tabloya (`Vault`, `Ground`, `Flow`, `Desk`, `public`), `AuthController` `AutomationRules` tablosuna, `GatewayKeyController` `GroundDatabases` tablosuna doğrudan LINQ sorgusu atmaktadır.
3. **Modül İçi Katmanlı Mimari Yoktur:** Mevcut 4 modül projesinde `Domain`, `Application`, `Infrastructure`, `Presentation` ve `Contracts` katmanları ayrılmamıştır. Modül entity'leri bile modül içinde değil, `Namines.Core/Models/Auth/` altında yaşamaktadır.
4. **Ölü Soyutlamalar (Dead Abstractions):** `IVaultModule`, `IGroundModule`, `IDeskModule`, `IFlowModule` ve `IModuleEventBus` DI konteynerine kaydedilmiş ancak hiçbir gerçek iş akışı tarafından tüketilmemektedir.

---

## 2. Proje ve Bağımlılık Denetimi

| Proje | Mevcut Rolü | Gerçek Durumu | Mimari İhlaller |
|---|---|---|---|
| `Namines.Core` | Ortak modeller + arayüzler | 34+ DB Entity'si, tüm modüllerin modelleri, NSL, analizörler bir arada | Tüm modüllerin Domain modelleri burada toplanmış ("Ortak Çöp Kutusu" anti-pattern'i). |
| `Namines.Infrastructure` | Altyapı + tüm iş mantığı | `AuthDbContext`, `VaultService`, `GroundService`, `AutomationExecutor`, `SqlWorkbenchService`, 6 BackgroundService | Modüllerin içinde olması gereken Application ve Infrastructure katmanlarını yutmuş. |
| `Namines.API` | HTTP Host | 38 Controller, SignalR Hub, Seeder'lar | Controller'lar doğrudan `AuthDbContext` ve `Infrastructure` somut sınıflarına (`VaultService`, `GroundService`) bağımlı. |
| `Namines.Vault` | Yedekleme modülü | Yalnızca S3/File store, `BackupCipher` ve Docker dump sağlayıcıları | Entity'ler `Core`'da, `VaultService` ve 2 worker `Infrastructure`'da, `VaultController` `API`'de. `IVaultModule` ölü arayüz. |
| `Namines.Ground` | Yönetilen DB modülü | Yalnızca Neon/Supabase/LocalPostgres sağlayıcıları | `GroundDatabase` `Core`'da, `GroundService` ve `GroundPurgeBackgroundService` `Infrastructure`'da. `IGroundModule` ölü arayüz. |
| `Namines.Flow` | Otomasyon modülü | 7 dosya, 165 satır kabuk | `AutomationRuleMatcher` `Core`'da, `AutomationExecutor` + worker + retention `Infrastructure`'da. `IFlowModule` ölü arayüz. |
| `Namines.Desk` | Veri konsolu modülü | 6 dosya, 132 satır kabuk | `SqlWorkbenchService` ve `DeskHandoff` `Infrastructure`'da. `IDeskModule` ölü arayüz. |
| `Namines.Hub` | **YOK** | Dağınık (`HubController`, `GithubBotService`, `RepoIssue*`) | Ayrı projesi yok, ayrı DB şeması yok (`public` şemasında). |

---

## 3. Çekişmeli (Adversarial) Kod İncelemesi Bulguları

### 3.1. Derleme ve Bağımlılık Bulguları
- **ARCH-01 (`NU1504` Yinelenen Paket Referansı):** `Namines.Infrastructure.csproj` satır 41'de `Microsoft.Extensions.Caching.Memory 8.0.1` tanımlı; aynı paket `backend/Directory.Build.props` satır 10'da da tanımlı olduğu için her derlemede `NU1504` uyarısı üretiyor.
- **ARCH-02 (`CS8604` Olası Null Başvuru Uyarıları):** `GatewayController.cs` (satır 261, 1511, 1701), `TeamController.cs` (satır 105) ve `ChangeRequestController.cs` (satır 580) içinde doğrulanmamış nullable kullanıcı kimliği aktarımları derleme uyarısı üretiyor.

### 3.2. Önbellek, Kiracı İzolasyonu ve Hata Sızıntısı Bulguları
- **SEC-01 (`SemanticCacheService` Eksik Şema Özeti ve Kiracı Bağlamı Eksikliği):**
  - [`SemanticCacheService.cs`](../../backend/Namines.Infrastructure/Services/SemanticCacheService.cs) yalnızca tablo adı, kolon adı, kolon tipi, `IsPK` ve `IsFK` alanlarını özetliyor (`ComputeSchemaHash`).
  - Tablo indekslerini (`Indexes`), `UniqueConstraints`, `CheckConstraints`, `DefaultValue`, `IsNullable`, `IsIdentity` ve `EnumValues` alanlarını **hiç hesaba katmıyor**. Kullanıcı AI DBA'nın önerdiği bir indeksi şemaya ekleyip tekrar analiz istediğinde, önbellek **aynı hash**'i üretip eski "indeks eksik" raporunu döndürüyor!
  - Ayrıca önbellek anahtarında kiracı/kullanıcı bağlamı (`tenantScope`) bulunmuyor ve `IMemoryCache` singleton iken `SemanticCacheService` gereksiz yere `Scoped` olarak kaydedilmiş.
- **SEC-02 (`AIDbaController` ve `SmartSeedController` Ham İstisna Sızıntısı):**
  - [`AIDbaController.cs:83`](../../backend/Namines.API/Controllers/AIDbaController.cs#L83) ve [`SmartSeedController.cs:131`](../../backend/Namines.API/Controllers/SmartSeedController.cs#L131) yakalanan beklenmeyen istisnaların ham `ex.Message` metnini doğrudan HTTP 500 yanıt gövdesinde istemciye döndürüyor.

### 3.3. Veritabanı ve Çoklu Kiracı (Multi-Tenancy) Bulguları
- **DB-NORM-01 (Geçişli `OrganizationId` Kopyaları — 3NF İhlali ve Bayat Veri Riski):**
  - `VaultBackup`, `VaultRestore`, `VaultSchedule` ve `GroundDatabase` tablolarında hem `ProjectId` hem `OrganizationId` tutuluyor. Oysa `OrganizationId` projenin özelliğidir (`ProjectId -> OrganizationId`). Proje sahipliği/organizasyonu değiştiğinde alt tablolardaki `OrganizationId` güncellenmiyor.
- **DB-NORM-02 (`CloudProjects` Tanrı Tablosu — God Table):**
  - Çekirdek proje kimliği, Hub yayın meta verileri (`Slug`, `Visibility`, `Category`, `Tags`, `StarCount`, `ForkCount`, `LinkedGithubRepo`), canlı veritabanı bağlantı sırrı (`EncryptedConnectionString`, `ConnectionDbType`, `AllowDeskSql`), paylaşım jetonu (`ShareToken`, `ShareTokenHash`) ve yönetişim ayarı (`AutoApproveSafeChanges`) tek tabloda birleşmiş durumda.
- **DB-NORM-03 (1NF İhlali — Virgülle Ayrılmış Çok Değerli Sütunlar):**
  - `CloudProjects.Tags`, `RepoIssues.Labels`, `GatewayApiKeys.AllowedOrigins`, `GatewayApiKeys.AllowedIps` ve `GatewayTablePermissions.MaskedColumns` virgülle ayrılmış metin olarak saklanıyor.
- **DB-NORM-04 (`ChangeRequestAuditLogs` Aşırı Yüklenmesi):**
  - Hem `ChangeRequest` onay/red geçmişi hem de `ProjectDeleted` ve `AccountDeleted` yaşam döngüsü olayları aynı tabloya (`ChangeRequestId` sütununa `ProjectId` yazılarak) kaydediliyor.
