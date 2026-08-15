# 26 — Terimler Sözlüğü

## Namines terimleri

| Terim | Tanım |
|---|---|
| **NSL** | *Namines Schema Language* — şema tanım dili ve ara temsil (IR). `.nsl` metin formatı + kanonik JSON. Sistemdeki tek doğruluk kaynağı. |
| **IR** | *Intermediate Representation* — NSL'in makine tarafından işlenen kanonik JSON hali. |
| **Studio** | Görsel tasarım workspace'i (canvas + kod editörü). `app.namines.com` |
| **Console** | Şemadan runtime'da üretilen yönetim paneli. `console.namines.com` |
| **Gateway** | Tenant veritabanı üzerinde otomatik REST/GraphQL API. `gw.namines.com` |
| **Copilot** | AI ajan katmanı (12 dar sorumluluklu ajan). |
| **Bridge** | Müşteri ağında çalışan, outbound-only tünel agent'ı (on-prem DB erişimi). |
| **Bot** | GitHub App — PR'da şema incelemesi ve risk raporu. |
| **Hub** | Blueprint (şablon) pazarı. |
| **Blueprint** | Hazır, kurulabilir şema şablonu. Faz 1'deki "template"in halefi. |
| **Design Plane** | Modelleme katmanı (Studio, Copilot, codegen). |
| **Data Plane** | Gerçek veritabanlarının yaşadığı katman (provisioning, migration, seed). |
| **App Plane** | Kullanıcıya dokunan katman (Console, Gateway, RBAC). |
| **Control Plane** | Metadata yöneten API — tenant verisine hiç dokunmaz. |
| **Eject** | Üretilen sistemi kaynak kod olarak dışa aktarma (lock-in karşıtı özellik). |
| **Overlay** | Console özelleştirmelerinin tutulduğu, şemadan ayrı yapılandırma katmanı. |
| **Subject Area** | Canvas'ta tabloların gruplandığı, katlanabilir mantıksal bölge. |
| **Golden file** | Beklenen codegen çıktısının kayıtlı snapshot'ı; regresyon testi için. |
| **Round-trip testi** | NSL → DDL → gerçek DB → introspect → NSL; başı ve sonu eşleşmeli. |
| **Eval harness** | AI çıktı kalitesini ölçen otomatik test sistemi. |
| **Data Factory** | Referans bütünlüklü, ölçekli sahte veri üretici (Smart Seed'in halefi). |
| **Drift** | Canlı DB'nin NSL'den sapması (biri elle DDL çalıştırmış). |
| **Metadata sözleşmesi** | Control plane'in yayınladığı, Console ve Gateway'in tükettiği tanım. |

## Veritabanı terimleri

| Terim | Tanım |
|---|---|
| **DDL** | *Data Definition Language* — CREATE/ALTER/DROP ifadeleri. |
| **DML** | *Data Manipulation Language* — SELECT/INSERT/UPDATE/DELETE. |
| **DCL** | *Data Control Language* — GRANT/REVOKE. |
| **PK / FK** | Primary Key / Foreign Key. |
| **RLS** | *Row-Level Security* — satır seviyesinde erişim kontrolü (PostgreSQL, SQL Server). |
| **3NF** | Üçüncü normal form — temel normalizasyon hedefi. |
| **Cascade** | FK silme/güncelleme davranışı; `CASCADE` ilişkili satırları da siler. **Faz 1'de varsayılan olarak açıktı — kritik hata.** |
| **Çoklu cascade yolu** | Aynı tabloya iki farklı cascade yolu; SQL Server bunu reddeder. |
| **Partial index** | Koşullu index (`WHERE ...`). |
| **Covering index** | `INCLUDE` ile ek kolon taşıyan index (index-only scan sağlar). |
| **CONCURRENTLY** | PostgreSQL'de tabloyu kilitlemeden index oluşturma. |
| **NOT VALID** | PostgreSQL'de constraint'i tarama yapmadan ekleme; sonra `VALIDATE`. |
| **Expand-contract** | Kırıcı şema değişikliğini kesintisiz yapma deseni (ekle → çift yaz → doldur → bırak). |
| **PITR** | *Point-In-Time Recovery* — herhangi bir ana geri dönme. |
| **Introspection** | Canlı veritabanının yapısını okuma (INFORMATION_SCHEMA / sistem katalogları). |
| **Bloat** | PostgreSQL'de ölü satırların şişirdiği tablo/index alanı. |
| **Advisory lock** | Uygulama seviyesi kilit (eşzamanlı migration'ı engeller). |
| **CDC** | *Change Data Capture* — veri değişikliklerini akış olarak yakalama. |

## Mimari terimler

| Terim | Tanım |
|---|---|
| **CRDT** | *Conflict-free Replicated Data Type* — çakışmasız eşzamanlı düzenleme yapısı (Yjs). |
| **Yjs** | JavaScript CRDT kütüphanesi; Namines'in işbirliği çekirdeği. |
| **Backplane** | Çok instance'lı SignalR'da mesajları dağıtan bileşen (Redis). **Faz 1'de yoktu.** |
| **Multi-tenancy** | Tek sistemde birden çok müşteriyi izole biçimde barındırma. |
| **DB-per-tenant** | Her projeye ayrı veritabanı (Namines'in seçtiği izolasyon modeli). |
| **Copy-on-write branch** | Veritabanı dalı; veri kopyalanmadan paylaşılır, yazınca ayrışır (Neon). |
| **gVisor** | Google'ın kullanıcı-alanı çekirdeği; container izolasyonunu güçlendirir. |
| **Firecracker** | AWS'nin microVM teknolojisi. |
| **SSRF** | *Server-Side Request Forgery* — sunucuyu iç ağa istek atmaya zorlama. |
| **DNS rebinding** | DNS yanıtını değiştirerek SSRF filtresini atlatma. |
| **Prompt injection** | Kullanıcı içeriğine gömülü talimatlarla LLM'i kandırma. |
| **BYOK** | *Bring Your Own Key* — kullanıcının kendi AI API anahtarı. |
| **BYODB** | *Bring Your Own Database* — kullanıcının kendi veritabanı. |
| **Zarf şifreleme** | Veriyi DEK ile, DEK'i KMS anahtarıyla şifreleme. |
| **ULID** | Sıralanabilir, URL-güvenli benzersiz kimlik. |
| **OTLP** | OpenTelemetry Protocol. |
| **RED metrikleri** | Rate, Errors, Duration. |
| **SLO / SLI** | Service Level Objective / Indicator. |
| **Hata bütçesi** | SLO'nun izin verdiği başarısızlık payı. |
| **Kuyruk-tabanlı örnekleme** | Trace'i tamamlandıktan sonra (hata/yavaşlık varsa) saklama. |
| **BSL** | *Business Source License* — belirli süre sonra açık kaynağa dönen lisans. |
| **MoR** | *Merchant of Record* — vergi/faturalama sorumluluğunu üstlenen ödeme sağlayıcı (Paddle, LemonSqueezy). |

## Ürün / iş terimleri

| Terim | Tanım |
|---|---|
| **Aha momenti** | Kullanıcının ürünün değerini ilk kez hissettiği an. Namines'te: Console'da ilk kayıt eklenmesi. |
| **Activation** | Kayıttan sonra aha momentine ulaşma oranı. |
| **North Star metriği** | Ürünün başarısını en iyi temsil eden tek metrik. Namines'te: haftalık aktif provisioned DB. |
| **Karşı-metrik** | North Star'ı kovalarken bozulmaması gereken metrik (p95 gecikme). |
| **NRR** | *Net Revenue Retention* — mevcut müşterilerden gelir büyümesi. |
| **LTV / CAC** | Yaşam boyu değer / müşteri edinme maliyeti. |
| **Churn** | Müşteri kaybı oranı. |
| **Genişleme geliri** | Mevcut müşterinin koltuk/kullanım artışından gelen gelir. |
| **PLG** | *Product-Led Growth* — ürünün kendisinin satış kanalı olması. |
| **Viral katsayı** | Her kullanıcının getirdiği yeni kullanıcı sayısı. |
| **Build in public** | Metrikleri ve süreci açıkça paylaşarak topluluk kurma. |
| **Dogfooding** | Kendi ürününü kendi geliştirmende kullanma. |
| **TAM** | *Total Addressable Market*. |

## Faz 1 → Faz 2 terim değişiklikleri

| Faz 1 | Faz 2 | Neden |
|---|---|---|
| `DatabaseSchema` | NSL Document | İfade gücü genişledi |
| `StableUuid` | `uuid` (her nesnede) | Kavram tüm nesnelere yayıldı |
| Şablon (template) | Blueprint | Pazar/ekosistem çağrışımı |
| Docker sandbox | Ephemeral Database | Uygulama detayı isimden çıktı |
| Dev package | Developer Package / Eject | Amaç netleşti |
| Smart Seed | Data Factory | Ölçek arttı |
| AI DBA | Copilot Critic / Advisor | Ajan mimarisine uyum |
| Token havuzu | AI kredisi | Kullanıcıya anlaşılır birim |
| Room (SignalR) | Collaboration Session | Auth'lu, kalıcı |
| Cloud Project | Project (+ Organization) | Çok kiracılı yapı |
