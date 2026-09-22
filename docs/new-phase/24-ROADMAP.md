# 24 — Yol Haritası (12 Ay)

> ⚠️ **2026-08-10 güncellemesi:** Faz 1 sonrası önceliklendirme [27-LIFECYCLE-PIVOT.md](27-LIFECYCLE-PIVOT.md)
> ile revize edildi — Impact Analysis + Database Change Review, Console/Gateway'in
> geniş genişlemesinden ÖNCE gelir. Faz 0 (G0-G6) aşağıdaki gibi geçerli; G7'den
> itibaren sıralama için önce 27'yi oku.

**Varsayım:** Tek geliştirici, haftada ~35 saat. Bu plan agresif ama gerçekçi — çünkü Faz 1'in %60'ı yeniden kullanılıyor.

**Prensip: Her faz sonunda satılabilir bir ürün olmalı.** Hiçbir zaman "12 ay sonra bitecek" durumuna girme.

---

## Faz 0 — Temel Düzeltmeler (Hafta 1-6)

> **Amaç:** Mevcut ürünü kırık olmaktan çıkar. Yeni özellik yok.

| # | İş | Etki | Zorluk |
|---|---|---|---|
| 0.1 | Monorepo yeniden yapılandırma ([17](17-DIRECTORY-STRUCTURE.md)) | 6 | 4 |
| 0.2 | `Namines.Nsl` çekirdeği: model + parser + writer | 10 | 6 |
| 0.3 | **Index + unique + check + composite key** desteği | 10 | 5 |
| 0.4 | **`ON DELETE` politikası** + `FkCascadeAnalyzer` | 10 | 3 |
| 0.5 | Tip eşleme matrisi (6 motor) | 9 | 4 |
| 0.6 | 6 DDL backend'ini NSL üzerine yeniden yaz | 9 | 6 |
| 0.7 | **Golden-file test altyapısı** (Verify) | 10 | 3 |
| 0.8 | **Testcontainers integration testleri** (6 motor) | 10 | 5 |
| 0.9 | Round-trip testi (NSL→DDL→DB→introspect→NSL) | 9 | 4 |
| 0.10 | Control DB: SQLite → PostgreSQL göçü | 8 | 4 |
| 0.11 | `Database.Migrate()` startup'tan çıkar, ayrı Job | 7 | 1 |
| 0.12 | Serilog dosya sink'ini kaldır → stdout + OTel | 6 | 1 |
| 0.13 | **`docker.sock` mount'ını kaldır** | 9 | 2 |
| 0.14 | SignalR: JWT auth + Redis backplane | 9 | 3 |
| 0.15 | Faz 1 şema göçü (`LegacyV1Migrator`) | 8 | 3 |
| 0.16 | CI: test + kapsam + SAST + sır tarama kapıları | 8 | 3 |
| 0.17 | `ForwardedHeaders` KnownNetworks düzeltmesi | 6 | 1 |

**Çıktı (v1.5):** Aynı özellikler, ama üretilen DDL gerçekten çalışıyor, index'ler var, testler var, ölçeklenebilir altyapı. **Bu tek başına yayınlanabilir ve "artık ciddi bir araç" mesajı verir.**

---

## Faz 1 — NSL & Studio (Hafta 7-14)

| # | İş | Etki | Zorluk |
|---|---|---|---|
| 1.1 | NSL: enum, view, generated column, RLS, comment, schema | 8 | 5 |
| 1.2 | NSL Validator (NSL001-025) + auto-fix | 9 | 5 |
| 1.3 | NSL Differ + MigrationPlanner + RiskClassifier | 10 | 6 |
| 1.4 | **DBML import/export** | 8 | 3 |
| 1.5 | `@namines/nsl` TS portu (tarayıcı önizlemesi) | 7 | 5 |
| 1.6 | Yjs CRDT entegrasyonu (canvas + sunucu) | 9 | 7 |
| 1.7 | Sunucu tarafı branch'ler + 3-way merge taşıma | 8 | 5 |
| 1.8 | Monaco `.nsl` kod editörü | 7 | 4 |
| 1.9 | Auto-layout (elkjs) + subject areas | 7 | 4 |
| 1.10 | Canvas sanallaştırma (1000 tablo) | 6 | 4 |
| 1.11 | Prompt'ları dosyalara taşı + versiyonla | 8 | 2 |
| 1.12 | **AI Eval Harness** + CI kapısı | 10 | 6 |
| 1.13 | Model kademelendirme + semantik cache geliştirme | 8 | 4 |
| 1.14 | OAuth (GitHub, Google) + organizasyonlar | 8 | 4 |

**Çıktı (v1.8):** Sınıfının en iyi şema tasarım aracı. dbdiagram'dan import edilebilir, ondan daha ifade gücü yüksek, ölçülen AI kalitesi var.

---

## Faz 2 — Data Plane (Hafta 15-22)

| # | İş | Etki | Zorluk |
|---|---|---|---|
| 2.1 | `IDatabaseProvider` soyutlaması | 8 | 3 |
| 2.2 | **Neon provider** (managed PostgreSQL) | 10 | 5 |
| 2.3 | **Ephemeral sandbox** (K8s Job + gVisor) | 9 | 7 |
| 2.4 | Sıcak havuz optimizasyonu | 7 | 4 |
| 2.5 | Sır kasası (Vault) entegrasyonu | 8 | 4 |
| 2.6 | Migration Executor (advisory lock, checkpoint, timeout) | 10 | 6 |
| 2.7 | Güvenli desenler (CONCURRENTLY, NOT VALID, lock_timeout) | 10 | 5 |
| 2.8 | Rollback script üretimi + otomatik yedek | 9 | 4 |
| 2.9 | Drift tespiti | 7 | 3 |
| 2.10 | Introspection derinleştirme (6 motor, tüm nesneler) | 8 | 6 |
| 2.11 | Data Factory (ölçekli seed, referans bütünlüklü) | 8 | 5 |
| 2.12 | PgBouncer + bağlantı yönetimi | 7 | 3 |
| 2.13 | Yedekleme / restore API'si | 7 | 3 |
| 2.14 | NATS iş kuyruğu + Worker servisi | 8 | 4 |

**Çıktı (v2.0-beta):** Şemadan gerçek, çalışan bir veritabanı. **İlk büyük farklılaşma.**

---

## Faz 3 — App Plane (Hafta 23-34) ★ EN KRİTİK FAZ

| # | İş | Etki | Zorluk |
|---|---|---|---|
| 3.1 | Metadata sözleşmesi + cache + event invalidation | 9 | 4 |
| 3.2 | **Gateway: REST CRUD** (tüm operasyonlar) | 10 | 7 |
| 3.3 | Gateway sorgu dili (filtre/sıralama/expand/sayfalama) | 10 | 6 |
| 3.4 | API anahtarı + scope + rate limit | 9 | 4 |
| 3.5 | RLS + rol filtresi + kolon maskeleme | 10 | 6 |
| 3.6 | OpenAPI 3.1 otomatik üretimi | 8 | 3 |
| 3.7 | **Console: Renderer motoru** (widget eşleme) | 10 | 8 |
| 3.8 | Console: liste/detay/form/filtre | 10 | 7 |
| 3.9 | Console: ilişki navigasyonu + FK combobox | 9 | 5 |
| 3.10 | Console: sayfa desenleri (master-detail, tree, kanban) | 8 | 6 |
| 3.11 | Console: RBAC + rol editörü | 9 | 5 |
| 3.12 | **Audit log** (Console + control plane) | 9 | 4 |
| 3.13 | Console: dashboard motoru | 7 | 5 |
| 3.14 | Console: özelleştirme overlay'i | 8 | 4 |
| 3.15 | TypeScript SDK + tip üretimi | 8 | 4 |
| 3.16 | Console özel alan adı + beyaz etiket | 6 | 4 |
| 3.17 | GraphQL endpoint (HotChocolate) | 7 | 6 |

**Çıktı (v2.0):** **Ürünün tamamı.** Fikirden çalışan backend + admin panele. Bu, satılabilir ilk gerçek sürüm.

---

## Faz 4 — Ekip & Ekosistem (Hafta 35-44)

| # | İş | Etki | Zorluk |
|---|---|---|---|
| 4.1 | **Branch DB** (Neon branching) | 9 | 5 |
| 4.2 | **Namines Bot** (GitHub App) | 9 | 6 |
| 4.3 | `.nsl` ↔ repo iki yönlü senkron | 8 | 5 |
| 4.4 | **Namines CLI** (npm + dotnet tool) | 8 | 5 |
| 4.5 | Yorumlar + mention + bildirim | 6 | 4 |
| 4.6 | Sürüm geçmişi + zaman tüneli | 7 | 4 |
| 4.7 | **Console Eject** (Next.js + Streamlit + Blazor) | 8 | 6 |
| 4.8 | Ejected Developer Package (docker compose ile çalışan) | 8 | 4 |
| 4.9 | Drizzle / SQLAlchemy / Django backend'leri | 6 | 5 |
| 4.10 | Blueprint Hub + topluluk katkısı | 7 | 4 |
| 4.11 | **Public şema sayfaları + OG image + fork** (viral) | 9 | 3 |
| 4.12 | `create-namines-app` | 7 | 3 |
| 4.13 | VS Code eklentisi (`.nsl` syntax + önizleme) | 6 | 4 |
| 4.14 | Stripe: 4 katman + metering + aşırı kullanım | 9 | 5 |

**Çıktı (v2.1):** Ekip ürünü + büyüme döngüleri çalışıyor.

---

## Faz 5 — Kurumsal & Ölçek (Hafta 45-52)

| # | İş | Etki | Zorluk |
|---|---|---|---|
| 5.1 | **Namines Bridge** (on-prem agent) | 9 | 7 |
| 5.2 | PII maskeleme (branch DB'lerde) | 8 | 5 |
| 5.3 | SSO/SAML/SCIM | 8 | 6 |
| 5.4 | TR bölge desteği | 7 | 5 |
| 5.5 | Continuous DBA Advisor (canlı metriklerle) | 8 | 5 |
| 5.6 | Index Advisor (gerçek sorgu loglarından) | 8 | 5 |
| 5.7 | Doğal dil sorgu (Console içinde) | 7 | 4 |
| 5.8 | AI Agent modu (çok adımlı orchestrator) | 8 | 6 |
| 5.9 | Realtime abonelikler (CDC → WebSocket) | 6 | 6 |
| 5.10 | Self-hosted dağıtım (Helm chart) | 7 | 5 |
| 5.11 | MySQL/SQL Server managed provider'ları | 7 | 5 |
| 5.12 | SOC 2 hazırlık çalışmaları | 7 | 5 |

**Çıktı (v2.2):** Kurumsal satışa hazır.

---

## Kilometre taşları ve karar noktaları

| Kilometre | Ne zaman | Başarı kriteri | **Başarısızsa** |
|---|---|---|---|
| **M1 — Temel sağlam** | Hafta 6 | 6 motorda %100 golden test, 0 kritik güvenlik açığı | Devam etme, düzelt |
| **M2 — Tasarım lideri** | Hafta 14 | DBML import çalışıyor, eval ≥ 0.85, 50 beta kullanıcı | Ürün-pazar sinyali zayıf → pivot değerlendir |
| **M3 — İlk DB canlı** | Hafta 22 | 100 provisioned DB, provisioning başarı %95 | Provider stratejisini gözden geçir |
| **M4 — Console canlı** | Hafta 34 | 30 ödeyen müşteri, activation %25 | **En kritik karar noktası** — Console kullanılmıyorsa tez yanlış |
| **M5 — Büyüme** | Hafta 44 | $3.000 MRR, viral katsayı > 0.3 | GTM'i yeniden kur |
| **M6 — Kurumsal** | Hafta 52 | 2 enterprise pilot, $8.000 MRR | Enterprise'ı ertele, self-serve'e odaklan |

**M4 en önemlisidir.** Bu planın tüm tezi "Console retention yaratır" varsayımına dayanıyor. Hafta 34'te Console kullanım verisi bu tezi doğrulamıyorsa (DAU/proje < 2, ekip kullanıcısı davet oranı < %20), stratejiyi değiştir: migration güvenliği + kurumsal legacy DB yönetimine yönel.

---

## Öncelikli 20 iş (etki/zorluk oranına göre sıralı)

Zaman kısıtlıysa bu sırayla git:

| Sıra | İş | Etki | Zorluk | Oran |
|---|---|---|---|---|
| 1 | `ON DELETE` politikası + cascade analizi | 10 | 3 | 3.33 |
| 2 | Golden-file test altyapısı | 10 | 3 | 3.33 |
| 3 | `docker.sock` kaldır | 9 | 2 | 4.50 |
| 4 | SignalR auth + backplane | 9 | 3 | 3.00 |
| 5 | Public şema sayfaları (viral) | 9 | 3 | 3.00 |
| 6 | Prompt'ları dosyalara taşı | 8 | 2 | 4.00 |
| 7 | Serilog stdout + startup migrate kaldır | 7 | 1 | 7.00 |
| 8 | Index + unique + check desteği | 10 | 5 | 2.00 |
| 9 | Testcontainers integration | 10 | 5 | 2.00 |
| 10 | DBML import/export | 8 | 3 | 2.67 |
| 11 | Neon provider (managed DB) | 10 | 5 | 2.00 |
| 12 | Migration güvenli desenleri | 10 | 5 | 2.00 |
| 13 | NSL Differ + risk sınıflandırma | 10 | 6 | 1.67 |
| 14 | AI Eval Harness | 10 | 6 | 1.67 |
| 15 | Gateway REST CRUD | 10 | 7 | 1.43 |
| 16 | Console Renderer motoru | 10 | 8 | 1.25 |
| 17 | RLS + kolon maskeleme | 10 | 6 | 1.67 |
| 18 | Ephemeral sandbox (güvenli) | 9 | 7 | 1.29 |
| 19 | Namines Bot | 9 | 6 | 1.50 |
| 20 | Yjs CRDT | 9 | 7 | 1.29 |

---

## Zaman bütçesi uyarısı

Bu plan ~1.800 saat iş içeriyor. Haftada 35 saatte **52 hafta**. Gerçekte:

- Beklenmeyen işler: +%30
- Destek/topluluk/GTM: haftada 8 saat
- Öğrenme eğrisi (K8s, gVisor, CRDT): +%10

**Gerçekçi süre: 16-18 ay** tek kişiyle. Seçenekler:
1. Kapsamı Faz 3'e kadar kısıtla (v2.0'da dur, satmaya başla) → **9 ay** ✅ önerilen
2. Faz 4'ten sonra bir geliştirici işe al (MRR $3K'yı geçince)
3. Faz 5'i tamamen ertele

**Öneri: Hafta 34'te (v2.0) dur, 3 ay sadece sat ve öğren, sonra Faz 4'e devam et.**
