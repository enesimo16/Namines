# 05 — Control Plane (Studio, Projeler, Organizasyonlar)

## 1. Sorumluluk sınırı

Control plane **veri taşımaz**, sadece *metadata* yönetir:
- Organizasyon / kullanıcı / üyelik / rol
- Proje / şema sürümü / branch / NSL dokümanı
- Veritabanı bağlantı kayıtları (şifreli)
- Faturalama / kota / kullanım
- Console ve Gateway'in okuduğu **metadata sözleşmesi**

Tenant satırlarına **asla** dokunmaz — o Gateway'in işi. Bu ayrım güvenlik ve ölçekleme için zorunlu.

---

## 2. Kaynak hiyerarşisi

```
Organization (org_01J...)
 ├─ Members (owner | admin | editor | viewer)
 ├─ Billing (Stripe customer, plan, kota)
 ├─ Secrets (BYOK anahtarları, harici DB connection string'leri)
 └─ Projects (prj_01J...)
     ├─ Schema Document (NSL, sürümlü)
     │   ├─ Branch: main
     │   ├─ Branch: feature/orders-v2
     │   └─ Versions (v1..vN, immutable, checksum'lı)
     ├─ Environments
     │   ├─ development  → Database (db_01J...)
     │   ├─ preview/*    → Branch DB (ephemeral)
     │   └─ production   → Database (db_01J...)
     ├─ Console Config (roller, görünümler, tema, alan adı)
     ├─ Gateway Config (API anahtarları, CORS, rate limit, RLS bağlamı)
     ├─ Integrations (GitHub repo, webhook, Slack)
     └─ Audit Log
```

ID formatı: **ULID + prefix** (`org_`, `prj_`, `db_`, `usr_`, `key_`, `job_`, `ver_`, `brn_`). Sıralanabilir, URL-güvenli, çakışmasız.

---

## 3. Namines Studio — bileşen mimarisi

```
app/(studio)/
  layout.tsx                  → shell, komut paleti, presence bar
  p/[projectId]/
    page.tsx                  → canvas (varsayılan)
    design/                   → NSL kod editörü (Monaco, .nsl syntax highlight)
    data/                     → tablo veri gezgini (Gateway üzerinden)
    migrations/               → migration geçmişi + risk raporu
    api/                      → API playground + OpenAPI görüntüleyici
    console/                  → Console yapılandırma
    settings/                 → proje ayarları, entegrasyonlar
```

### 3.1 Durum yönetimi

| Katman | Araç | Ne tutar |
|---|---|---|
| Şema dokümanı | **Yjs `Y.Doc`** | CRDT — tablolar, kolonlar, pozisyonlar. Tek gerçek. |
| Sunucu verisi | **TanStack Query v5** | Proje listesi, migration geçmişi, kullanım |
| UI durumu | **Zustand** (Faz 1'den korunur) | Seçili tablo, açık paneller, modal'lar |
| Form | **React Hook Form + Zod** | Tablo/kolon editörü |
| Kalıcılık (offline) | **IndexedDB** (`y-indexeddb`) | Çevrimdışı düzenleme, sonradan senkron |

Faz 1'deki 14 Zustand store'u korunur, ama **şema state'i** Zustand'dan Yjs'e taşınır. Bu, undo/redo'nun sınırsız ve çok kullanıcılı olmasını sağlar (Faz 1'deki 50-snapshot yığını gider).

### 3.2 Canvas performansı

| Teknik | Etki |
|---|---|
| React Flow `onlyRenderVisibleElements` | 500+ tabloda zorunlu |
| Node memoization + `nodeTypes` stabil referans | Gereksiz render'ı %90 azaltır |
| Kolon listesi sanallaştırma (50+ kolonlu tabloda) | `@tanstack/react-virtual` |
| Edge yeniden hesaplamayı `requestIdleCallback`'e alma | Etkileşim akıcılığı |
| Auto-layout worker thread'de (`elkjs` Web Worker) | UI donmaz |
| Subject Area katlama | Görsel karmaşıklığı azaltır |

**Hedef:** 1000 tablo / 8000 kolon ile 60 FPS pan/zoom.

### 3.3 Otomatik yerleşim (auto-layout)

| Algoritma | Kütüphane | Ne zaman |
|---|---|---|
| Katmanlı (hiyerarşik) | `elkjs` (`layered`) | Varsayılan — FK yönüne göre |
| Güç yönlendirmeli | `d3-force` | Keşif modunda, 200+ tablo |
| Izgara | kendi kodumuz | "Düzenle" komutu |
| Subject-area gruplu | `elkjs` (`box` + alt layout) | Alan grupları tanımlıysa |

---

## 4. NSL Kod Editörü (yeni)

Studio'nun ikinci modu: şemayı metin olarak düzenle.

- **Monaco Editor** + özel `.nsl` dil tanımı (TextMate gramer + LSP-lite)
- Canvas ↔ Metin **iki yönlü canlı senkron** (Yjs sayesinde çakışmasız)
- Otomatik tamamlama: tablo adları, tipler, FK hedefleri
- Satır içi tanılama (NSL001-025 kuralları, kırmızı alt çizgi)
- Hover: tip eşleme önizlemesi ("`uuid` → SQL Server'da `UNIQUEIDENTIFIER`")
- CodeLens: "3 referans" (bu tabloya kaç FK var)
- Format on save (kanonik `.nsl` biçimi)

**Neden önemli:** Deneyimli geliştiriciler görsel editörden nefret eder. Metin modu, "bu araç bana ait değil" itirazını öldürür ve git-diff'lenebilirlik sağlar.

---

## 5. Query Playground

Faz 1'deki SQLite WASM konsolu korunur ve genişletilir:

| Mod | Ne yapar | Ne zaman |
|---|---|---|
| **Local (WASM)** | `sql.js` ile şemayı tarayıcıda çalıştır | DB provision edilmemişse, offline |
| **Live** | Gateway `/query` üzerinden gerçek DB'ye | DB varsa |
| **Explain** | `EXPLAIN ANALYZE` çıktısını görselleştir | Live modda |

Özellikler: sonuç ızgarası, CSV/JSON export, sorgu geçmişi, kayıtlı sorgular, AI ile "doğal dil → SQL", çoklu sekme.

**Güvenlik:** Live modda sorgular salt-okunur rol ile çalışır (varsayılan). Yazma için açık onay + audit log.

---

## 6. RBAC (organizasyon seviyesi)

| Rol | Proje oluştur | Şema düzenle | Migration uygula (prod) | Console verisi düzenle | Faturalama | Üye yönet |
|---|---|---|---|---|---|---|
| `owner` | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| `admin` | ✔ | ✔ | ✔ | ✔ | ✖ | ✔ |
| `editor` | ✖ | ✔ | ✖ (PR açabilir) | ✔ | ✖ | ✖ |
| `viewer` | ✖ | ✖ (yorum yapabilir) | ✖ | ✖ (salt-okunur) | ✖ | ✖ |
| `billing` | ✖ | ✖ | ✖ | ✖ | ✔ | ✖ |

Console içindeki **son kullanıcı rolleri** ayrıdır ([07-CONSOLE-ADMIN-UI.md](07-CONSOLE-ADMIN-UI.md)) — org rolleriyle karıştırılmamalı.

---

## 7. Kimlik doğrulama

| Yöntem | Durum | Not |
|---|---|---|
| E-posta + şifre | KORUNDU (ASP.NET Identity) | Argon2id'ye geçiş önerilir |
| E-posta doğrulama | YENİ | Zorunlu (spam hesap engelleme) |
| Magic link | YENİ | Sürtünmesiz giriş |
| GitHub OAuth | YENİ | **Geliştirici kitlesi için en önemlisi** |
| Google OAuth | YENİ | |
| 2FA (TOTP) | YENİ | Pro+ |
| Passkey / WebAuthn | P2 | |
| SAML / SCIM | P3 | Enterprise |

**Token stratejisi:**
- Access token: JWT, 15 dk, httpOnly + Secure + SameSite cookie (Faz 1 yaklaşımı korunur, süre kısaltılır)
- Refresh token: 30 gün, rotasyonlu, DB'de saklanır, iptal edilebilir
- Gateway API anahtarları: `nam_live_...` / `nam_test_...`, sadece hash saklanır, scope'lu
- Service token'ları: CI/CD ve Bridge için, kısa ömürlü

---

## 8. Metadata sözleşmesi (Console + Gateway'in okuduğu)

Control plane şu endpoint'i yayınlar; Console ve Gateway **sadece bunu** bilir:

```
GET /internal/v1/projects/{projectId}/metadata?env=production
→ {
    "projectId": "prj_...",
    "schemaVersion": 47,
    "checksum": "sha256:...",
    "engine": "postgres",
    "connectionRef": "sec_...",          // sır kasası referansı, düz metin değil
    "tables": [ { ...NSL tablo IR'ı + ui ipuçları... } ],
    "enums": [...],
    "roles": [ { "name": "support", "permissions": {...} } ],
    "rls": {...},
    "views": [...],
    "generatedAt": "2026-08-08T11:00:00Z",
    "ttlSeconds": 60
  }
```

Değişince NATS'ten `schema.version.changed` olayı yayınlanır → Gateway ve Console cache'lerini anında tazeler. TTL sadece emniyet ağıdır.

---

## 9. Proje ayarları yüzeyi

| Sekme | İçerik |
|---|---|
| General | Ad, slug, açıklama, ikon, silme |
| Environments | dev/staging/prod, her biri için DB bağlantısı |
| Database | Sağlayıcı, boyut, bölge, yedek programı, bağlantı bilgisi |
| Console | Alan adı, tema, roller, görünürlük |
| API | Anahtarlar, CORS, rate limit, OpenAPI indir |
| Collaboration | Üyeler, davetler, paylaşım linkleri |
| Integrations | GitHub repo, Slack, webhook'lar |
| AI | Sağlayıcı, model tercihi, BYOK anahtarı, gizlilik modu |
| Danger Zone | Transfer, arşivle, kalıcı sil (2 aşamalı onay) |
