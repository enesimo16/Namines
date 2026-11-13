# Faz 14.3 — Kabul edilen borçların kapanışı ve AI modülü

> Tarih: 2026-11-13 · Dal: `fix/debts-and-ai-module` → `main`
> Taban: `9a27094` (14.2D-2 3. tur ACCEPTED sonrası main)

14.2D-2 bağımsız denetiminin §11'de "sahibin bilmesi gerekenler" olarak listelediği kabul edilen borçlar ve
3. tur notları bu fazda ele alındı. Her madde için: ne vardı, ne yapıldı, hangi test kanıtlıyor.

## 1. Kapanan borçlar

| Denetim maddesi | Önce | Şimdi | Kanıt |
|---|---|---|---|
| **Borç 1** — varlık yerleşimi | Vault/Ground/Desk varlıkları ve `TeamInvite` `Namines.Core/Models/Auth` altında; modüllerin Domain katmanı yoktu | `VaultBackup/Restore/Schedule` → `Namines.Vault.Domain`, `GroundDatabase` → `Namines.Ground.Domain`, `DeskHandoffToken/SavedQuery/SqlQueryHistoryEntry` → `Namines.Desk.Domain`, `TeamInvite` → `Namines.Identity.Domain`. Migration snapshot'larındaki CLR adları güncellendi; tablo/kolon değişikliği yok | `Modules_keep_their_layers` artık yalnızca Flow'u muaf tutuyor (gerekçe aşağıda); her modül bağlamı için `HasPendingModelChanges` boş |
| **Borç 4** — AI kotası tahminle | Yalnızca `schema/generate` gerçek kullanımı bildiriyordu; diğer 9 AI ucu sabit tahminle ücretlendiriliyordu | Middleware, uç bildirmezse sağlayıcının istek kapsamlı ölçere (`IAiUsageTracker`) yazdığı GERÇEK `usage` toplamıyla uzlaştırıyor. Ölçüm yoksa tahmin kalır (ölçümsüzü "0" saymak bedava kullanım olurdu) | `AiQuotaReconciliationTests` (5) |
| **Borç 5** — denetim satırı silme | `purge_change_request_audit` kesimi çağırandan alıyor, yalnızca 90 gün tabanını zorluyordu; varsayılan saklama 730 gün olduğu hâlde uygulama rolü 90–730 gün arasını silebiliyordu | `Governance_02_AuditRetentionFloor`: taban sahibe ait `governance.audit_retention_floor_days()` işlevinde (730). Temizlik, çağıranın kesimini bu tabana kıstırır; uygulama politikadan fazlasını silemez. İşlev `GrantAppRole` kapsamına girmez, uygulama rolü değiştiremez | Gerçek PG: `Retention_purge_removes_only_expired_rows...` (700 günlük satır kalır), `A_hijacked_application_role_cannot_delete_audit_rows...` (tabanı değiştirme denemesi `42501`) |
| **R2-m4** — kimlik sınırı testi | `UserManager` taraması yalnızca imzalara bakıyordu; `RequestServices.GetService(typeof(UserManager<...>))` kaçıyordu | Denetleyici metot gövdelerinin IL'i de taranıyor (DbContext testindeki yöntem) | Mutasyon: QuotaController'a servis bulucu erişimi eklendi → test düştü (`QuotaController IL UserManager`1`), geri alındı |
| **R2-m6** — SQL sahiplik taraması | `Owners` listesinde Vault/Ground/Flow/Desk yoktu | Dört modül eklendi; tabloları başka modülün SQL metninde geçerse test düşer | `Sql_text_never_names_another_modules_table` (15 derleme) |
| **R3-n1** — abonelik durumları | Güncelleme yolu `incomplete_expired` ve `paused`'da kullanıcıyı ücretli bırakıyordu; checkout yolu farklı liste kullanıyordu | Tek kaynak: `SubscriptionAccess.Revokes` (canceled, unpaid, incomplete_expired, paused); iki yol da onu kullanıyor. `past_due` bilinçli olarak yumuşak dönem | `SubscriptionAccessTests` (9) |
| **R3-n2** — runbook | "JWT/yenileme jetonları" ifadesi (uygulamada yenileme jetonu yok) | `docs/deploy/MIGRATION-VE-GERI-ALMA.md` düzeltildi | okuma |
| **R2-m5** — Stripe zaman aşımı | — | `9a27094` ile zaten kapanmıştı (15 sn `CallTimeout`) | kod |

## 2. Bu fazda bulunan ve kapatılan sorunlar

- **Eksik Vault anahtarı girişi düşürüyordu.** `BackupCipher` singleton'ı anahtarı kurucuda doğruluyordu ve hesap silme yolu üzerinden
  AuthController'ın bağımlılık zincirindeydi: `Vault:BackupEncryptionKey` yoksa `/api/auth/login` ve `/register` 500 dönüyordu.
  Şimdi anahtar ilk kullanımda doğrulanıyor (Vault yine fail-closed) ve Development dışında **açılışta** zorunlu
  (`BackupCipher.ValidateConfiguration`). `--migrate` modu muaf: prod'daki `namines-migrate` konteynerine Vault anahtarı verilmiyor.
  Kanıt: `A_missing_key_refuses_on_first_use_not_on_construction`, anahtarsız örnekte giriş 200.
- **Modele hiç gitmeyen yanıtlar tam tahminle ücretleniyordu.** Canlı denemede boş şemalı `aidba` isteği LLM'e gitmeden
  "tablo yok" döndü, kullanıcıdan yine 3500 token düştü; anlamsal önbellek isabetleri (AIDba, SmartSeed) ve SmartSeed'in
  deterministik dalı da aynıydı. "Ölçüm yok = 0" çıkarımı güvenli DEĞİL (CoderAI çağrıyı istek kapsamının dışında yapıyor,
  bedava olurdu); bu yüzden açık sinyal eklendi: `IAiUsageTracker.RecordServedWithoutModel()`. Uç bunu söylerse ve ölçüm
  yoksa rezervasyonun tamamı iade edilir (Billing'in `ReconcileAsync`'i 0'ı bilinçli olarak "bilgi yok" saydığı için
  middleware doğrudan `RefundAsync` çağırır). Kanıt: `AiQuotaReconciliationTests` (5).
- **VoiceController Groq'a AI modülünü atlayarak gidiyordu.** Anahtar yoksa kurucusu her istekte `ArgumentNullException`
  atıyordu (500) ve paylaşılan HttpClient'ın varsayılan başlığını her istekte yeniden yazıyordu. Çağrı
  `Namines.AI`'daki `GroqSpeechTranscriber`'a taşındı (istek başına kimlik, anahtar yoksa 503). Kural testle zorlandı:
  `Ai_provider_hosts_live_only_in_the_AI_module` — LLM sağlayıcı adresleri AI modülü dışındaki hiçbir derlemenin IL'inde
  geçemez (mutasyon: QuotaController'a Groq adresi eklendi → test düştü, geri alındı).
- **Bilinen güvenlik açıklı paketler** (`dotnet list package --vulnerable`, main'de de vardı; derlemenin 2 uyarısı bunlardı):
  `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 8.0.11 → 8.0.34 (yüksek, GHSA-wr8v-9x5q-ggcm);
  OpenTelemetry ailesi 1.10 → 1.15.x (`OpenTelemetry.Api` orta, GHSA-8785-wc3w-h8q6, GHSA-g94r-2vxg-569j);
  Identity ve RunTests'te geçişli `System.Text.Json` 8.0.4 → 8.0.5 (yüksek, GHSA-8g4q-xg66-9fp4) ve Identity'de
  `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 → 2.1.12 (yüksek, GHSA-2m69-gcr7-jv3q). Infrastructure bunları zaten sabitliyordu ama
  `PrivateAssets=all` olduğu için Identity'ye yayılmıyordu. Şimdi tarama temiz, derleme **0 uyarı**; `/metrics` yeni
  OpenTelemetry ile çalışıyor.
- **Yazılamayan Vault yolu girişi düşürüyordu.** Prod imajının duman testinde login 403: `FileSystemBackupStore` kök dizini
  kurucuda oluşturuyordu ve konteynerde varsayılan `/app/vault-backups` ayrıcalıksız kullanıcıya yazılamaz. Dizin artık ilk
  yazımda oluşuyor; imajın varsayılanı `Vault__StoragePath=/app/data/vault-backups`. Kanıt:
  `FileSystemBackupStoreLazyRootTests`, imaj Production'da açılıp giriş 200.
- **API Dockerfile'ı Faz 14 modüllerini restore etmiyordu.** Restore katmanı yalnızca 7 csproj kopyalıyordu; BuildingBlocks ve 7 modül
  eksikti. Bütün proje grafiği eklendi ve imaj gerçekten build edildi.

## 3. AI modülü (`Namines.AI`)

AI kodu üç yere dağılmıştı: sağlayıcılar `Infrastructure/AI`'da, maliyet tablosu API middleware'inin if-else'inde, kayıtlar API'nin genel
servis listesinde. Artık tek modül:

| Katman | İçerik |
|---|---|
| `Domain` | `AiFeature` — bir AI özelliğinin kota kimliği (tahmin, mod alanı, deterministik/kendi-rezervasyon bayrakları) |
| `Application` | `AiFeatureCatalog` (hangi uç AI özelliği, ne kadar tutar — tek cevap), `AIFactory`, `AiUsageTracker`, `ISpeechTranscriber`, `JsonSanitizerPreprocessor` |
| `Infrastructure.Providers` | Groq, DeepSeek, Ollama sağlayıcıları, `GroqSpeechTranscriber`, model katalogları, `ChatCompletionProviderFactory` |
| `DependencyInjection` | `AddAiModule()` — bütün AI kayıtlarının tek noktası |

Sınırlar:
- Referanslar yalnızca Core ve BuildingBlocks; Infrastructure'a ve başka modüle referans **derleyici ve IL testleriyle** yasak
  (`Modules` listesinde, `Module_project_references_only_Core_and_BuildingBlocks`).
- Kota **defteri** Billing'de (`IAiQuota`), kullanıcı AI politikası Governance'ta (`IAiPolicies`); AI modülü ikisine yalnızca
  BuildingBlocks sözleşmeleriyle ulaşır, kendi tablosu yok (bu yüzden `Infrastructure.Persistence` yerine `Infrastructure.Providers`).
- `AIQuotaMiddleware` artık yalnızca HTTP adaptörü: rezerve et → çağır → iade/uzlaştır. Davranış eşliği `AiFeatureCatalogTests`
  ile her uç için (tahmin, mod alanı, bayraklar) sabitlendi.
- AI'yı **kullanan** özellikler (AIDba, SmartSeed, şema ajan hattı, Migration, CoderAI) tüketici olarak Infrastructure'da kalır.
  `AiToolRouter` Ops'un sözleşmesini uyguladığı için Infrastructure'da (modüller birbirine başvuramaz).

## 4. Bilinçli olarak yapılmayanlar

- **`ApplicationUser`/`Organization` Core'da kalıyor.** ASP.NET Identity tipleri ve Studio/Vault/Ground/Desk varlıklarının gezinti
  özellikleri bunlara bağlı; modüller Identity'ye başvuramadığı için bunlar paylaşılan çekirdek.
- **`AutomationRule` Core'da kalıyor.** Core'daki saf kural değerlendiricisi (`AutomationRuleMatcher`) ve modülsüz çalışan MCP aracı da
  kullanıyor. `Modules_keep_their_layers` yalnızca Flow'u bu gerekçeyle muaf tutuyor.
- **Git geçmişi (borç 6) yeniden yazılmadı.** `fea5610..e00b95f` aralığı derlenmiyor ama main'e itilmiş; geçmişi yeniden yazmak herkesin
  klonunu bozar. Bisect gerekirse bu aralık atlanır.
- **Gerçek Stripe, canlı AI sağlayıcısı, üretim DB, hacimli kilit penceresi (borç 8)** hâlâ sınanmadı: anahtar ve ortam gerektiriyor.
  Staging'de Stripe test modu + gerçek Groq anahtarıyla bir tur önerilir.

## 5. Doğrulama

| Koşu | Sonuç |
|---|---|
| `dotnet build Namines.sln` | 0 hata, 0 uyarı (taban: 2 uyarı = açıklı paketler) |
| `dotnet list package --vulnerable --include-transitive` | açık yok |
| `Namines.Tests` — Integration (gerçek PostgreSQL/Docker) | 330 / 330 |
| `Namines.Tests` — Services | 983 / 983 |
| `Namines.Tests` — geri kalan (Architecture, Security, Controllers, Analysis...) | 2089 / 2089 |
| **`Namines.Tests` toplam** | **3402 / 3402**, 0 atlanan (taban: 3345 geçen + 3 atlanan) |
| `Namines.Tests.RunTests` (gerçek Docker) | 19 / 19 |
| Prod Docker imajı | build edildi; Production modunda açılış, `/health` 200, giriş 200 |
| Mutasyonlar | IL kimlik taraması (R2-m4) ve LLM adres kuralı: ikisi de düştü, geri alındı |

Canlı (localhost, taze migration):
- Vault anahtarı **olmadan** Development'ta giriş 200 (önce 500); Production'da anahtarsız açılış reddedildi; `--migrate` anahtarsız geçti.
- `governance.__EFMigrationsHistory`'de `Governance_02` var, `audit_retention_floor_days()` = 730.
- Kayıt → proje → paylaşım → görüntüleme → iptal (404) → silme; AIDba, SmartSeed (deterministik), anonim AI isteği 401.
- Boş şemalı AIDba: kullanıcı sayacı 0 → 0 (önce 3500 düşüyordu). Groq anahtarı yokken ses: 503 `AI_NOT_CONFIGURED`
  (önce 500), kota iade edildi; yanlış içerik türü 400.
- `/metrics` 200 (OpenTelemetry 1.15).
- Paket yükseltmesi sonrası Integration dışı 3072/3072 ve paketlere dokunan Integration testleri (Redis, SignalR, gerçek PG) 37/37 yeniden koşuldu.
