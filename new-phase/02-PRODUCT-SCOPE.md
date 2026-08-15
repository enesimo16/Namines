# 02 — Ürün Kapsamı (Tam Envanter)

> ⚠️ **2026-08 güncellemesi:** Buradaki P0/P1 önceliklendirmesi, Console/Gateway'i
> Impact Analysis'ten önce sıralıyordu. [27-LIFECYCLE-PIVOT.md](27-LIFECYCLE-PIVOT.md)
> bunu düzeltti: Impact Analysis ([28](28-IMPACT-ANALYSIS-ENGINE.md)) + Database
> Change Review ([29](29-DATABASE-CHANGE-REVIEW.md)) + Server-Side Branching
> ([30](30-SERVER-SIDE-BRANCHING.md)) artık Console/Gateway'in geniş genişlemesinden
> ÖNCE geliyor. Aşağıdaki özellik envanteri hâlâ geçerli (hiçbiri iptal olmadı),
> sadece sıra değişti — güncel sıra için 27'yi oku.

Bu doküman **her özelliği** listeler: Faz 1'de var olanlar (`KORUNDU`), geliştirilenler (`TERFİ`), ve yeni eklenenler (`YENİ`).

Öncelik kodları: **P0** = v2.0 için zorunlu · **P1** = v2.1 · **P2** = v2.2+ · **P3** = ufuk

---

## DESIGN PLANE

### D1 — Namines Studio (Canvas)

| Kod | Özellik | Durum | Öncelik |
|---|---|---|---|
| D1.01 | React Flow tabanlı interaktif canvas | KORUNDU | P0 |
| D1.02 | Tablo/kolon/ilişki sürükle-bırak düzenleme | KORUNDU | P0 |
| D1.03 | Undo/Redo (50 snapshot) | TERFİ → CRDT tabanlı sınırsız geçmiş | P0 |
| D1.04 | ⌘K komut paleti | KORUNDU + genişletildi (Copilot komutları) | P0 |
| D1.05 | Ctrl+F canvas arama | KORUNDU | P0 |
| D1.06 | Klavye kısayolları modalı | KORUNDU | P1 |
| D1.07 | Minimap + tablo renklendirme | KORUNDU | P1 |
| D1.08 | Tablo çoğaltma, zoom-to-table | KORUNDU | P1 |
| D1.09 | Bağlam menüsü (sağ tık) | KORUNDU | P1 |
| D1.10 | Boş canvas onboarding durumu | KORUNDU | P1 |
| D1.11 | Otomatik yerleşim (auto-layout: dagre/elk) | YENİ | P0 |
| D1.12 | **Alan grupları (Subject Areas)** — 200+ tablolu şemalarda katlanabilir bölgeler | YENİ | P1 |
| D1.13 | **Notlar / sticky note / annotation** | YENİ | P1 |
| D1.14 | **Sanal render (virtualization)** — 1000+ tablo performansı | YENİ | P1 |
| D1.15 | **Şema karşılaştırma görünümü** (iki sürümü yan yana) | YENİ | P1 |
| D1.16 | Query Playground (SQLite WASM konsolu) | TERFİ → canlı DB'ye de bağlanır | P0 |
| D1.17 | **ER notasyonu seçimi** (Crow's foot / IE / UML / Chen) | YENİ | P2 |
| D1.18 | **Erişilebilirlik**: klavyeyle tam gezinme, ARIA, ekran okuyucu | TERFİ | P1 |
| D1.19 | Tema (koyu/açık/sistem) + yoğunluk ayarı | YENİ | P2 |
| D1.20 | Çevrimdışı mod (IndexedDB + sonradan senkron) | YENİ | P2 |

### D2 — NSL (Namines Schema Language)

Detay: [04-NSL-SCHEMA-IR.md](04-NSL-SCHEMA-IR.md)

| Kod | Özellik | Durum | Öncelik |
|---|---|---|---|
| D2.01 | Tablo / kolon / PK / FK / nullable / default | KORUNDU | P0 |
| D2.02 | **Index** (b-tree, unique, partial, expression, include, fulltext, GIN/GIST) | YENİ 🔴 | **P0** |
| D2.03 | **UNIQUE constraint** (tekil + bileşik) | YENİ 🔴 | **P0** |
| D2.04 | **CHECK constraint** | YENİ | P0 |
| D2.05 | **Bileşik PK ve bileşik FK** | YENİ | P0 |
| D2.06 | **ON DELETE / ON UPDATE davranışı** (NO ACTION varsayılan) | YENİ 🔴 | **P0** |
| D2.07 | **Enum tipleri** | YENİ | P0 |
| D2.08 | **Tablo/kolon açıklamaları (comment/description)** | YENİ | P0 |
| D2.09 | **Schema / namespace desteği** (`public`, `sales`, `dbo`) | YENİ | P0 |
| D2.10 | **Computed / generated kolonlar** | YENİ | P1 |
| D2.11 | **View / materialized view** | YENİ | P1 |
| D2.12 | **Sequence / identity ayarları** | YENİ | P1 |
| D2.13 | **Collation, charset** | YENİ | P1 |
| D2.14 | **Trigger / stored procedure (opak blok)** | YENİ | P2 |
| D2.15 | **Partitioning** (range/list/hash) | YENİ | P2 |
| D2.16 | **RLS politikaları** (satır seviyesi güvenlik) | YENİ | P1 |
| D2.17 | **Domain tipleri / custom types** | YENİ | P2 |
| D2.18 | **Tag / metadata** (PII, sensitive, deprecated) | YENİ | P1 |
| D2.19 | **UI ipuçları** (Console'un kullanacağı: label kolonu, widget tipi, sıralama) | YENİ | P0 |
| D2.20 | Metin formatı `.nsl` + JSON IR + JSON Schema | YENİ | P0 |

### D3 — Copilot (AI)

Detay: [09-AI-LAYER.md](09-AI-LAYER.md)

| Kod | Özellik | Durum | Öncelik |
|---|---|---|---|
| D3.01 | Metinden şema üretimi | KORUNDU | P0 |
| D3.02 | URL'den şema üretimi (SSRF korumalı) | KORUNDU | P1 |
| D3.03 | Görselden şema üretimi (vision) | KORUNDU | P1 |
| D3.04 | Sesli giriş (Whisper) | KORUNDU | P2 |
| D3.05 | Şema revizyonu (seçili tablolar) | KORUNDU | P0 |
| D3.06 | AI DBA advisor + sağlık skoru | TERFİ → canlı metriklerle sürekli | P0 |
| D3.07 | Smart Seed (alan-farkında mock data) | TERFİ → Data Factory | P0 |
| D3.08 | DbContext tersine mühendislik | KORUNDU | P1 |
| D3.09 | AI şema açıklaması | KORUNDU | P1 |
| D3.10 | **Agent modu** — çok adımlı görev yürütme (planla→uygula→doğrula) | YENİ | P0 |
| D3.11 | **Doğal dilde sorgu → SQL** (Console içinde) | YENİ | P1 |
| D3.12 | **Migration risk analizi** (AI + deterministik kural) | YENİ | P0 |
| D3.13 | **Eval harness** — her prompt için ölçülen kalite | YENİ 🔴 | **P0** |
| D3.14 | **Semantik önbellek** | KORUNDU (SemanticCacheService) + geliştirildi | P1 |
| D3.15 | BYOK (kendi anahtarın) | KORUNDU | P1 |
| D3.16 | Çoklu sağlayıcı (Groq/Gemini/Ollama/OpenAI/Anthropic) | TERFİ | P1 |
| D3.17 | **Şema→ürün gereksinimi tersine çevirme** (DB'den PRD üret) | YENİ | P3 |

---

## DATA PLANE

Detay: [06-DATA-PLANE.md](06-DATA-PLANE.md)

| Kod | Özellik | Durum | Öncelik |
|---|---|---|---|
| DP.01 | **Yönetilen DB provisioning** (PostgreSQL) | YENİ 🔴 | **P0** |
| DP.02 | Yönetilen DB — MySQL / SQL Server | YENİ | P1 |
| DP.03 | **Ephemeral sandbox DB** (eski Docker sandbox'ın güvenli hali) | TERFİ 🔴 | **P0** |
| DP.04 | **Branch DB** (şema branch'i = ayrı DB/schema, kopya-yaz) | YENİ | P0 |
| DP.05 | **Kendi DB'ni bağla (BYODB)** — connection string ile | TERFİ (DbConnectionPanel) | P0 |
| DP.06 | Canlı introspection (INFORMATION_SCHEMA) | KORUNDU | P0 |
| DP.07 | **Namines Bridge** — on-prem agent (outbound-only tünel) | YENİ | P2 |
| DP.08 | Migration uygulama (apply/rollback) | TERFİ | P0 |
| DP.09 | **Yedekleme + PITR** | TERFİ (backup vardı) | P1 |
| DP.10 | Data Factory (Smart Seed'in ölçekli hali, referans bütünlüklü) | TERFİ | P0 |
| DP.11 | **Veri import** (CSV/JSON/Parquet/Excel → tablo) | YENİ | P1 |
| DP.12 | **Veri export** (CSV/JSON/SQL dump/Parquet) | TERFİ | P1 |
| DP.13 | **Bağlantı havuzu (PgBouncer)** | YENİ | P1 |
| DP.14 | **Sorgu performans içgörüleri** (pg_stat_statements) | YENİ | P2 |
| DP.15 | **Otomatik index önerisi** (gerçek sorgu loglarından) | YENİ | P2 |
| DP.16 | **PII maskeleme** (branch/preview DB'lerde) | YENİ | P2 |
| DP.17 | Bölge seçimi (EU/US/TR) | YENİ | P2 |

---

## APP PLANE

### A1 — Namines Console (Otomatik Admin Panel) 🔴 **ANA YENİ ÜRÜN**

Detay: [07-CONSOLE-ADMIN-UI.md](07-CONSOLE-ADMIN-UI.md)

| Kod | Özellik | Durum | Öncelik |
|---|---|---|---|
| A1.01 | **Şemadan otomatik CRUD arayüzü** (liste/detay/form) | YENİ 🔴 | **P0** |
| A1.02 | **İlişki farkında navigasyon** (FK'ya tıkla → ilgili kayıtlar) | YENİ | P0 |
| A1.03 | **Akıllı widget eşleme** (tip→bileşen: tarih picker, enum select, JSON editor, dosya yükleme) | YENİ | P0 |
| A1.04 | Filtreleme / sıralama / arama / sayfalama | YENİ | P0 |
| A1.05 | Toplu işlemler (bulk edit/delete/export) | YENİ | P1 |
| A1.06 | **RBAC** — rol bazlı tablo/kolon/satır izinleri | YENİ | P0 |
| A1.07 | **Denetim kaydı (audit log)** — kim neyi ne zaman değiştirdi | YENİ | P0 |
| A1.08 | Özel görünümler (kayıtlı filtre + kolon düzeni) | YENİ | P1 |
| A1.09 | Gömülü dashboard (sayaç, grafik, son kayıtlar) | YENİ | P1 |
| A1.10 | **Doğal dille sorgu** ("geçen ay iptal edilen siparişler") | YENİ | P1 |
| A1.11 | Doğrulama kuralları (NSL constraint'lerinden türetilmiş) | YENİ | P0 |
| A1.12 | Dosya/görsel alanları (S3/MinIO entegrasyonu) | YENİ | P1 |
| A1.13 | **Workflow / eylem butonları** (webhook veya SQL tetikle) | YENİ | P2 |
| A1.14 | Beyaz etiket (logo, renk, özel alan adı) | YENİ | P2 |
| A1.15 | Çoklu dil (TR/EN + i18n altyapısı) | TERFİ | P1 |
| A1.16 | Mobil duyarlı Console | YENİ | P1 |
| A1.17 | **Console Eject** → Next.js / React / Blazor / **Streamlit** kaynak kodu | TERFİ 🔴 | P1 |

### A2 — Namines Gateway (Otomatik API)

Detay: [08-GATEWAY-API.md](08-GATEWAY-API.md)

| Kod | Özellik | Durum | Öncelik |
|---|---|---|---|
| A2.01 | **Otomatik REST API** (her tablo için CRUD) | YENİ 🔴 | **P0** |
| A2.02 | **OpenAPI 3.1 spesifikasyonu** otomatik üretimi | YENİ | P0 |
| A2.03 | **GraphQL endpoint** (şemadan türetilmiş) | YENİ | P1 |
| A2.04 | Filtreleme/sıralama/sayfalama sorgu dili | YENİ | P0 |
| A2.05 | **Row-Level Security zorlaması** | YENİ | P0 |
| A2.06 | API anahtarı yönetimi + scope | YENİ | P0 |
| A2.07 | Rate limiting (kiracı bazlı) | TERFİ | P0 |
| A2.08 | **Realtime abonelik** (WebSocket, CDC tabanlı) | YENİ | P2 |
| A2.09 | Webhook (satır değişikliğinde) | YENİ | P2 |
| A2.10 | **Tip üretimi**: TypeScript / C# / Python istemci SDK'sı | YENİ | P1 |
| A2.11 | Edge caching | YENİ | P3 |

---

## PLATFORM

### P1 — İşbirliği & Sürümleme

| Kod | Özellik | Durum | Öncelik |
|---|---|---|---|
| PL.01 | Gerçek zamanlı imleçler + presence | KORUNDU | P0 |
| PL.02 | **CRDT tabanlı çakışmasız düzenleme (Yjs)** | TERFİ 🔴 | **P0** |
| PL.03 | **Redis backplane** (çok instance) | YENİ 🔴 | **P0** |
| PL.04 | Paylaşılabilir salt-okunur link | KORUNDU | P0 |
| PL.05 | **Sunucu tarafı branch'ler** (yerel değil) | TERFİ 🔴 | **P0** |
| PL.06 | 3-yollu birleştirme + çakışma çözücü | KORUNDU + sunucu tarafına taşındı | P0 |
| PL.07 | **Yorum / tartışma** (tablo veya kolon üzerinde) | YENİ | P1 |
| PL.08 | Sürüm geçmişi + zaman tüneli | YENİ | P1 |
| PL.09 | **Namines Bot** — GitHub App, PR'da şema incelemesi | TERFİ | P1 |
| PL.10 | Git senkronizasyonu (`.nsl` dosyası repoda) | YENİ | P1 |

### P2 — Kimlik, Faturalama, Yönetim

| Kod | Özellik | Durum | Öncelik |
|---|---|---|---|
| PL.11 | E-posta + şifre auth (Identity) | KORUNDU | P0 |
| PL.12 | **OAuth** (GitHub, Google) | YENİ | P0 |
| PL.13 | **Organizasyon / takım / davet** | YENİ | P0 |
| PL.14 | Organizasyon RBAC (owner/admin/editor/viewer) | YENİ | P0 |
| PL.15 | **SSO / SAML / SCIM** | YENİ | P3 |
| PL.16 | Stripe abonelik (4 katman) | TERFİ | P0 |
| PL.17 | **Kullanım bazlı metering** (DB saati, API çağrısı, AI token) | YENİ | P0 |
| PL.18 | Sır kasası (BYOK, connection string, AES-256-GCM + KMS) | TERFİ | P0 |
| PL.19 | Geri bildirim widget'ı | KORUNDU | P2 |
| PL.20 | Durum sayfası + olay iletişimi | YENİ | P2 |

### P3 — Codegen & Eject

Detay: [12-CODEGEN-EJECT.md](12-CODEGEN-EJECT.md)

| Kod | Hedef | Durum | Öncelik |
|---|---|---|---|
| CG.01 | DDL: PostgreSQL | KORUNDU | P0 |
| CG.02 | DDL: SQL Server | KORUNDU | P0 |
| CG.03 | DDL: MySQL | KORUNDU | P0 |
| CG.04 | DDL: MariaDB | KORUNDU | P1 |
| CG.05 | DDL: SQLite | KORUNDU | P1 |
| CG.06 | DDL: Oracle | KORUNDU | P1 |
| CG.07 | DDL: **CockroachDB / Yugabyte** | YENİ | P3 |
| CG.08 | EF Core modelleri + DbContext | KORUNDU | P0 |
| CG.09 | EF Core migration | KORUNDU | P0 |
| CG.10 | Prisma şeması | KORUNDU | P0 |
| CG.11 | **Drizzle ORM şeması** | YENİ | P1 |
| CG.12 | **SQLAlchemy / Django models** | YENİ | P2 |
| CG.13 | **TypeORM / Sequelize** | YENİ | P2 |
| CG.14 | **GORM (Go)** | YENİ | P3 |
| CG.15 | TypeScript tipleri / Zod şeması | YENİ | P1 |
| CG.16 | **Liquibase / Flyway / Atlas HCL** dışa aktarım | YENİ | P2 |
| CG.17 | DBML import/export | YENİ 🔴 | **P0** (GTM için kritik) |
| CG.18 | Data Dictionary PDF | KORUNDU | P1 |
| CG.19 | README.md + Mermaid (ER/class/flow) | KORUNDU | P1 |
| CG.20 | **Docusaurus/MkDocs site export** | YENİ | P3 |
| CG.21 | Streamlit admin app ZIP | KORUNDU | P1 |
| CG.22 | **Next.js admin app export** | YENİ | P1 |
| CG.23 | **Blazor admin app export** | YENİ | P2 |
| CG.24 | Seed/mock data (SQL/CSV/JSON) | KORUNDU | P0 |

---

## Özet sayılar

| | Adet |
|---|---|
| Korunan Faz 1 özelliği | 38 |
| Terfi ettirilen | 21 |
| Yeni eklenen | 82 |
| **Toplam kapsam** | **141 özellik** |
| P0 (v2.0 zorunlu) | 52 |
| P1 | 48 |
| P2 | 28 |
| P3 | 13 |

**Kritik yol (🔴 işaretliler, 12 madde):** Bunlar olmadan ürün anlamsız — NSL'in index/unique/cascade desteği, provisioning, Console CRUD, Gateway REST, CRDT+backplane, sunucu branch'leri, eval harness, DBML.
