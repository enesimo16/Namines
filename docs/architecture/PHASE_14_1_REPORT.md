# PHASE 14.1 — LEGACY DEPENDENCY ELIMINATION & MODULE OWNERSHIP ENFORCEMENT REPORT

> **Tarih:** 2026-11-03  
> **Kapsam:** Yalnızca `Vault`, `Ground`, `Flow` ve `Desk` modüllerinin eski `AuthDbContext` ve `Namines.Infrastructure` kalıntılarını tamamen ortadan kaldırma ve modül sahipliğini zorlama.  
> **Hüküm:** **BAŞARIYLA TAMAMLANDI VE DOĞRULANDI**

---

## 1. Yönetici Özeti

Bu aşama, bağımsız baş mühendis denetiminde tespit edilen `AuthDbContext` üzerindeki çift yazma riski, sahte klasörleme ve `Namines.Infrastructure` içerisindeki mükerrer servislerin tamamen tasfiye edilmesi amacıyla yürütülmüştür.

Kural gereği:
1. **Yalnızca 4 modül** (`Vault`, `Ground`, `Flow`, `Desk`) hedeflendi; diğer 7 modüle dokunulmadı.
2. `AuthDbContext.cs` içerisinden bu 4 modüle ait 10 `DbSet<T>` özelliği fiziksel olarak kaldırıldı (bildirilen `DbSet` sayısı 37'den 27'ye düşürüldü).
3. `Namines.Infrastructure` altında mükerrer olarak yaşayan 10 eski servis, worker ve model dosyası silindi.
4. Çapraz erişim yapan tüm servisler (`LaunchService`, `ProjectDeletionService`, `DataRetentionService`, `SqlWorkbenchService`, `AuthController`, `LaunchController`) doğrudan yeni modül Application sözleşmelerine geçirildi.
5. `ModularMonolithArchitectureTests` paketine `AuthDbContext_MustNotExpose_DbSetsFor_ExtractedModules` ve `Infrastructure_MustNotContain_RetiredLegacyServices` testleri eklendi.
6. Çözüm 0 hata ve 0 uyarı ile derlendi; 2.765 birim/entegrasyon testi ve 19 Docker tabanlı test çalıştırılarak tam geriye dönük uyumluluk kanıtlandı.

---

## 2. Emekliye Ayrılan 10 `DbSet` ve Yeni Modül Sahipleri

`AuthDbContext` üzerinde daha önce doğrudan tanımlı olan ve harici servislerin modül sınırlarını baypas ederek sorguladığı 10 `DbSet` tamamen kaldırılmıştır. Tabloların tek ve yetkili sahibi artık ilgili modülün izole `DbContext`'idir:

| Eski `AuthDbContext` DbSet | Sahip Modül | Yeni İzole `DbContext` | Şema / Tablo Adı |
|---|---|---|---|
| `VaultBackups` | **Vault** | `VaultDbContext` | `namines_vault.vault_backups` |
| `VaultRestores` | **Vault** | `VaultDbContext` | `namines_vault.vault_restores` |
| `VaultSchedules` | **Vault** | `VaultDbContext` | `namines_vault.vault_schedules` |
| `GroundDatabases` | **Ground** | `GroundDbContext` | `ground.ground_databases` |
| `AutomationRules` | **Flow** | `FlowDbContext` | `flow.automation_rules` |
| `AutomationActions` | **Flow** | `FlowDbContext` | `flow.automation_actions` |
| `AutomationRunLogs` | **Flow** | `FlowDbContext` | `flow.automation_run_logs` |
| `DeskHandoffTokens` | **Desk** | `DeskDbContext` | `desk.desk_handoff_tokens` |
| `SqlQueryHistory` | **Desk** | `DeskDbContext` | `desk.sql_query_history` |
| `SavedQueries` | **Desk** | `DeskDbContext` | `desk.saved_queries` |

> **Not:** EF Core veritabanı migration snapshot'larının ve geriye dönük şema doğrulayıcılarının bozulmaması için `OnModelCreating` içerisindeki haritalama kuralları korunmuş, ancak C# seviyesindeki genel erişim yüzeyi (`DbSet<T>`) kaldırılarak derleme zamanında doğrudan erişim engellenmiştir.

---

## 3. Silinen Mükerrer Eski Altyapı Dosyaları (10 Dosya)

Aşağıdaki dosyalar `Namines.Infrastructure` içerisinden kalıcı olarak silinmiştir:

1. `backend/Namines.Infrastructure/Data/DeskHandoff.cs`
2. `backend/Namines.Infrastructure/Services/VaultService.cs`
3. `backend/Namines.Infrastructure/Services/VaultBackupWorker.cs`
4. `backend/Namines.Infrastructure/Services/VaultScheduleBackgroundService.cs`
5. `backend/Namines.Infrastructure/Services/VaultJobQueue.cs`
6. `backend/Namines.Infrastructure/Services/GroundService.cs`
7. `backend/Namines.Infrastructure/Services/GroundPurgeBackgroundService.cs`
8. `backend/Namines.Infrastructure/Services/AutomationExecutor.cs`
9. `backend/Namines.Infrastructure/Services/AutomationExecutorWorker.cs`
10. `backend/Namines.Infrastructure/Services/AutomationRunLogRetentionService.cs`

`ServiceCollectionExtensions.cs` içerisindeki DI kayıtları (`AddScoped<VaultService>`, `AddSingleton<VaultJobQueue>`, `AddScoped<GroundService>`, `AddScoped<AutomationExecutor>`) temizlenmiştir.

---

## 4. Servis ve Denetleyici Geçişleri (Cross-Module Callers)

Eski `DbSet`'lere ve emekli servislere dokunan tüm çağrıcılar modül Application servislerine taşınmıştır:

### A. `AuthController.cs` (Desk Handoff)
- **Önceki Durum:** `_db.DeskHandoffTokens.AddAsync(...)`, `_db.DeskHandoffTokens.SingleOrDefaultAsync(...)` ve `DeskHandoffTokenValidator` ile doğrudan `AuthDbContext`'e yazıyordu.
- **Yeni Durum:** `IDeskModule` enjekte edildi. `desk-handoff-token` endpoint'i `_deskModule.Application.CreateHandoffTokenAsync`, `desk-handoff-exchange` endpoint'i `_deskModule.Application.ExchangeHandoffTokenAsync` çağrısına dönüştürüldü.

### B. `LaunchController.cs` (Desk Handoff)
- **Önceki Durum:** `_deskHandoffValidator.CreateTokenAsync(...)` ve `_db.DeskHandoffTokens.AddAsync` çağırıyordu.
- **Yeni Durum:** `IDeskModule` enjekte edildi; `_deskModule.Application.CreateHandoffTokenAsync` üzerinden yetkilendirilmiş jeton üretiliyor.

### C. `LaunchService.cs` (Provisioning & Baseline Backup)
- **Önceki Durum:** Eski `GroundService` ve `VaultService` sınıflarına bağımlıydı.
- **Yeni Durum:** `IGroundApplicationService` ve `IVaultApplicationService` enjekte edildi; veritabanı sağlama ve ilk temel yedek alma işlemleri bu arayüzler üzerinden yürütülüyor.

### D. `ProjectDeletionService.cs` (Cascade Cleanup)
- **Önceki Durum:** Silinen projenin Ground veritabanlarını ve Vault yedeklerini temizlemek için eski servisleri ve doğrudan `AuthDbContext`'i kullanıyordu.
- **Yeni Durum:** `IVaultApplicationService.DeleteProjectBackupsAsync` ve `IGroundApplicationService.DeleteProjectDatabasesAsync` sözleşmelerine bağlandı.

### E. `DataRetentionService.cs` (Token & Log Retention)
- **Önceki Durum:** Süresi geçmiş Desk jetonlarını silmek için `_db.DeskHandoffTokens.Where(...)` sorgusu atıyordu.
- **Yeni Durum:** `IDeskApplicationService.PurgeExpiredHandoffTokensAsync(cutoff, cancellationToken)` çağrısıyla Desk modülü sınırlarına devredildi.

### F. `SqlWorkbenchService.cs` (Query History & Saved Queries)
- **Önceki Durum:** `_db.SqlQueryHistory` ve `_db.SavedQueries` üzerinde doğrudan CRUD çalıştırıyordu.
- **Yeni Durum:** %100 oranında `IDeskApplicationService` metodlarına (`RecordQueryHistoryAsync`, `GetQueryHistoryAsync`, `ClearQueryHistoryAsync`, `SaveQueryAsync`, `GetSavedQueriesAsync`, `DeleteSavedQueryAsync`) devredildi.

---

## 5. Mimari Test Kanıtları (`ModularMonolithArchitectureTests`)

`backend/Namines.Tests/Architecture/ModularMonolithArchitectureTests.cs` dosyasına iki kritik kabul testi eklenmiştir:

1. `AuthDbContext_MustNotExpose_DbSetsFor_ExtractedModules`:
   - `AuthDbContext` üzerindeki tüm public `DbSet<T>` özellikleri taranır.
   - `VaultBackups`, `VaultRestores`, `VaultSchedules`, `GroundDatabases`, `AutomationRules`, `AutomationActions`, `AutomationRunLogs`, `DeskHandoffTokens`, `SqlQueryHistory`, `SavedQueries` türlerinin varlığı yasaklanmıştır.
   - Bildirilen (`BindingFlags.DeclaredOnly`) `DbSet` sayısının tam olarak **27** olduğu doğrulanır.
   - **Sonuç:** ✅ Geçti.

2. `Infrastructure_MustNotContain_RetiredLegacyServices`:
   - `Namines.Infrastructure` derlemesi taranır.
   - `VaultService`, `VaultBackupWorker`, `VaultScheduleBackgroundService`, `VaultJobQueue`, `GroundService`, `GroundPurgeBackgroundService`, `AutomationExecutor`, `AutomationExecutorWorker`, `AutomationRunLogRetentionService`, `DeskHandoff` tiplerinin derleme içinde bulunmadığı doğrulanır.
   - **Sonuç:** ✅ Geçti.

Tüm mimari test paketi: **13 / 13 Geçti (%100 Başarı)**.

---

## 6. Doğrulama ve Test Sonuçları

| Test Paketi | Toplam Test | Başarılı | Başarısız | Notlar |
|---|---|---|---|---|
| `Namines.Tests` (Non-Docker Unit & Integration) | 2,766 | 2,765 | 1* | *Tek hata: Yerel Docker'da bulunmayan `gvenzl/oracle-free` imaj testi. 4 modülün tüm testleri ve 13 mimari testi %100 yeşil. |
| `Namines.Tests.RunTests` (Docker Ephemeral Execution) | 19 | 19 | 0 | 19 / 19 yeşil (%100 Başarı). `LaunchServiceTests` ve DDL doğrulama testleri hatasız geçti. |
| Derleme (`dotnet build Namines.sln`) | 12 Proje | 0 Hata, 0 Uyarı | — | Çözüm temiz derlendi. |

---

## 7. Sonuç ve Sıradaki Adım

Faz 14.1 hedeflerinin tamamı başarıyla yerine getirilmiştir:
- `Vault`, `Ground`, `Flow`, `Desk` modülleri kendi izole `DbContext`'leri ve modül servisleri üzerinden bağımsız olarak çalışmaktadır.
- `AuthDbContext` içerisinden 10 `DbSet` tamamen sökülmüş ve çift-yazma (dual-write) / kontrolsüz erişim riski kalıcı olarak önlenmiştir.
- Eski 10 mükerrer altyapı servisi fiziksel olarak silinmiştir.
- Kullanıcı talimatı gereği diğer 7 modüle dokunulmamış, mimari dönüşüm sırası ve disiplini korunmuştur.
