# 06 — Modül denetimi: Hub, Desk, Vault, Ground, Flow gerçek modül mü?

> **Soru (ürün sahibi, 2026-10-30):** "Bizde modüller var. Onlar gerçekten
> bir modül mü, yoksa sahte klasörler mi açılmış? Her bir modül ayrı
> şemalarda mı?"
>
> **Yöntem:** Kod ölçüldü (`main` = `be9a234`): proje referansları, satır
> sayıları, kodun nerede yaşadığı, modül arayüzlerinin kullanımı, hangi
> sınıfın hangi tabloya dokunduğu ve EF modelinden üretilen DDL.

---

## 1. Kısa cevap

**Kısmen gerçek.** "Modüler monolit" iddiası (commit `a39e384`) **veritabanı
seviyesinde gerçekleşmiş, kod seviyesinde büyük ölçüde gerçekleşmemiş.**
- Dört modülün tabloları Postgres'te **gerçekten ayrı şemalarda**.
- Ama hepsi **tek bir DbContext** (`AuthDbContext`) üzerinden, aynı kullanıcıyla erişiliyor ve herkes herkesin tablosuna dokunabiliyor.
- **Desk ve Flow** projeleri pratikte **boş kabuk**: asıl kod başka yerde.
- **Vault ve Ground** gerçek kütüphaneler, ama iş mantıkları modülün dışında.
- **Hub** bir modül değil: projesi yok, şeması yok.

## 2. Modül modül

| Modül | Ayrı proje | Projedeki kod | Asıl iş mantığı nerede | Ayrı DB şeması | Modül arayüzü kullanılıyor mu | Hüküm |
|---|---|---|---|---|---|---|
| **Vault** | ✅ `Namines.Vault` | 16 dosya, **2144 satır**: yedek sağlayıcıları (Postgres/MySQL container dump), şifreleme (`BackupCipher`), dosya/S3 depolama | `Infrastructure/Services/VaultService` + 3 worker (~1800 satır), `VaultController` | ✅ `namines_vault` (3 tablo) | ❌ `IVaultModule` hiçbir yerde enjekte edilmiyor | **Yarı gerçek.** Teknik çekirdek modülde, iş mantığı dışarıda |
| **Ground** | ✅ `Namines.Ground` | 12 dosya, **1559 satır**: Neon/Supabase istemcileri ve sağlayıcıları, LocalPostgres | `Infrastructure/Services/GroundService` + 2 background service, `GroundController` | ✅ `ground` (1 tablo) | ❌ `IGroundModule` kullanılmıyor | **Yarı gerçek** (Vault ile aynı desen) |
| **Desk** | ✅ `Namines.Desk` | 6 dosya, **132 satır**: bir arayüz, bir token doğrulayıcı, model konfigürasyonu | `Infrastructure`: `GatewayService` (Desk SQL), `SqlWorkbenchService`, `DeskHandoff` | ✅ `desk` (3 tablo) | ❌ `IDeskModule` kullanılmıyor | **Kabuk.** Şeması gerçek, modül projesi göstermelik |
| **Flow** | ✅ `Namines.Flow` | 7 dosya, **165 satır**. `FlowRuleMatcher` yalnızca `Core.AutomationRuleMatcher.Match(...)`'i çağıran tek satırlık sarmalayıcı | `Core/Analysis/AutomationRuleMatcher`, `Infrastructure`: `AutomationExecutor` + worker + kuyruk + retention, `AutomationController` | ✅ `flow` (3 tablo) | ❌ `IFlowModule` kullanılmıyor (`IFlowRuleMatcher` yalnızca MCP ve CLI'da) | **Kabuk** |
| **Hub** | ❌ yok | — | `HubController` (~1000+ satır), `GithubBotService`, `GithubClient` | ❌ `public` şemasında: RepoIssues, RepoIssueComments, RepoStars + CloudProjects'e eklenmiş Hub sütunları | — | **Modül değil** |
| Gateway, Review (ChangeRequest), Studio, Launch, Admin | ❌ | — | `API` + `Infrastructure` | ❌ `public` | — | Monolit (böyle olması sorun değil, ama "modül" diye anılmamalı) |

## 3. Veritabanı seviyesi

**Gerçek olan:**
- Şema ayrımı EF modelinde açıkça yapılmış. `AuthDbContext.OnModelCreating` (satır 80-83) her modülün kendi `*ModelConfiguration.Configure(builder, schema)` metodunu çağırıyor; tablolar `flow`, `namines_vault`, `ground` ve `desk` şemalarına yazılıyor.
- Üretilen DDL bunu doğruluyor: 44 tablonun 10'u modül şemalarında.
- Bağımlılık yönü temiz: modül projeleri yalnızca `Namines.Core`'a bağlı, döngü yok. `Infrastructure` ve `API` modüllere bağlı.

**Gerçek olmayan (izolasyon):**
- **Tek DbContext, tek migration geçmişi, tek DB kullanıcısı.** `AuthDbContext` 37 `DbSet` taşıyor; modül tabloları da bunların içinde. Herhangi bir sınıf `_context.VaultBackups`'a erişebiliyor.
- **Modüller arası doğrudan tablo erişimi** ölçüldü:
  - `AdminController` 13 farklı tabloya doğrudan dokunuyor. Bunlar Flow, Vault, Ground, Desk ve `public` tabloları.
  - `AuthController`, Flow'un `AutomationRules` tablosuna dokunuyor.
  - `GatewayKeyController`, Ground'un `GroundDatabases` tablosuna dokunuyor.
  - `GatewayController` ve `SqlWorkbenchService`, Desk tablolarına dokunuyor.
  - Toplam 13 sınıf modül tablolarına doğrudan erişiyor.
- **Modül arayüzleri ölü kod.** `IVaultModule`, `IGroundModule`, `IDeskModule` ve `IFlowModule` DI'a kaydediliyor ama hiçbir tüketici yok.
- **Modül şemaları arasında ve `public` ile FK yok.** Bu, DB denetimindeki DB-01/02/03 bulgularının (sahipsiz yedek, sağlayıcıda unutulan Ground DB'si) asıl nedeni.

## 4. Neden önemli?

- **Sahiplik belirsiz.** Vault'un kuralı ("yedek sessizce silinmez") Vault'ta değil; başka bir controller'ın yazdığı LINQ'te uygulanıyor ya da uygulanmıyor. Ekim denetimindeki birçok hata bu yüzden çıktı:
  - feature flag'ler hiçbir modülde uygulanmıyordu;
  - Ground claim durumu `GatewayKeyController`'daki listede eksikti;
  - proje silme Vault ve Ground'u unutuyordu.
- **"Modül" kelimesi ekipte yanlış bir güvenlik hissi yaratıyor.** Klasör var, sınır yok.
- **Strateji ile ilişkisi:** [strateji/10](../strateji/10-MOTOR-VE-MCP-MIMARISI.md)'daki "İşlem Kayıt Defteri" ve bağlayıcı çerçevesi, modüllerin dışarıya **tek bir giriş kapısı** sunmasına dayanıyor. Bugünkü yapıda o kapı yok.

## 5. Öneri: gerçek modüler monolit (aşamalı)

Hedef mikroservis **değil**. Aynı süreç ve aynı veritabanı kalıyor; değişen şey modüllerin sahiplik kurallarının **derleyici ve testlerle zorlanması**.

| Adım | İçerik | Efor |
|---|---|---|
| **M1 — Sınırı test et** | Mimari testleri (NetArchTest benzeri): "API controller'ları başka modülün tablosuna doğrudan erişemez", "Modül X yalnızca Core'a ve diğer modüllerin `Abstractions` ad alanına bağlanabilir". İlk başta bilinen ihlaller listeye alınır (allowlist), yenisi eklenemez | 2-3 gün |
| **M2 — İş mantığını eve taşı** | `VaultService` + worker'lar → `Namines.Vault`; `GroundService` + purge → `Namines.Ground`; `AutomationExecutor` + `AutomationRuleMatcher` → `Namines.Flow`; Desk SQL / workbench / handoff → `Namines.Desk`. Controller'lar ince kalır ve **yalnızca modül arayüzünü** (`IVaultModule.CreateBackupAsync` vb.) çağırır | 2-3 hafta |
| **M3 — Hub'ı modül yap** | `Namines.Hub` projesi; `RepoIssues`, `RepoIssueComments`, `RepoStars` → `hub` şeması (`ALTER TABLE ... SET SCHEMA`, veri kaybı yok) | 1 hafta |
| **M4 — Modüller arası iletişim** | Proje silme gibi akışlar modül arayüzleri veya mevcut (kullanılmayan) `IModuleEventBus` olaylarıyla yürür: `ProjectDeleted` → Vault yedekleri kaldırır, Ground sağlayıcı DB'sini siler, Flow kuralları temizler. `AdminController`'ın 13 tablo erişimi modüllerin "istatistik" arayüzlerine çevrilir | 1-2 hafta |
| **M5 — Modül başına DbContext (isteğe bağlı)** | `VaultDbContext`, `GroundDbContext`... aynı bağlantı, kendi şeması, kendi `__EFMigrationsHistory`. Modül tablosuna yalnızca modül erişebilir | 2 hafta |
| **M6 — DB rolleri (isteğe bağlı)** | Modül başına Postgres rolü, yalnızca kendi şemasına GRANT. İzolasyon DB tarafından da zorlanır | 3-4 gün |

**Ödünleşim (bilinçli karar gerekiyor):** Şu an devam eden DB düzeltmeleri, veri bütünlüğü için `vault_*` ve `ground_databases` tablolarından projeye **şemalar arası FK** ekliyor (DB-01/02).
- Bu, bugünkü tek DbContext yapısında doğru adım: sahipsiz kayıt ve faturalanan unutulmuş DB sorununu hemen kapatıyor.
- Ama M5'e geçildiğinde modüller arası FK'ler modülleri DB seviyesinde birbirine bağlar. O noktada iki seçenek var:
  - (a) FK'ler ham SQL migration'larıyla korunur ve modül DbContext'leri bunları bilmez;
  - (b) FK'ler kaldırılır, bütünlük M4'teki olaylarla ve periyodik "sahipsiz kayıt" taramasıyla sağlanır.
- **Öneri: (a).** Tek veritabanı ve tek ekip için bütünlük, saf modül bağımsızlığından daha değerli.

## 6. Kabul kriterleri ("modüller gerçek" demek için)

1. Mimari testleri CI'da çalışıyor; allowlist boş (M1 + M2 + M4 bitti).
2. Hiçbir controller başka modülün `DbSet`'ine dokunmuyor.
3. Her modül arayüzünün en az bir gerçek tüketicisi var; ölü arayüz kalmadı.
4. Proje silme, tek bir modül olayıyla Vault, Ground ve Flow'u temizliyor; entegrasyon testiyle kanıtlı.
5. Hub'ın tabloları `hub` şemasında.
