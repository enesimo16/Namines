# 07 — Namines Console (Otomatik Yönetim Paneli)

> **Bu, Faz 2'nin en önemli ürünüdür.** Kullanıcının sorusunun cevabı: *"database oluşturup arayüz sunmak mantıklı mı?"* → Evet, ve doğru uygulaması budur.

---

## 1. Temel karar: Runtime-rendered, codegen değil

| | Codegen (Faz 1: Streamlit ZIP) | Runtime render (Faz 2) |
|---|---|---|
| Şema değişince | Kullanıcı yeniden indirir, yeniden deploy eder | **Anında güncellenir** |
| Barındırma | Kullanıcının derdi | Bizde (retention!) |
| Özelleştirme | Kod değiştirir → bir daha üretemez | Yapılandırma katmanı, üretim kaybolmaz |
| Ürün değeri | Tek seferlik | **Sürekli** |
| Lock-in itirazı | Yok | **Eject ile çözülür** |

**Ama ikisi de var:** Console runtime'da çalışır; kullanıcı istediği an **Eject** ile kaynak kodu alır (Next.js / React / Blazor / **Streamlit** — Faz 1 özelliği burada yaşar).

---

## 2. Mimari

```
console.namines.com/p/{projectSlug}
   │
   ├─ Next.js 16 App Router (tek uygulama, tüm projelere hizmet eder)
   │   │
   │   ├─ [1] Metadata'yı çek: GET /internal/v1/projects/{id}/metadata
   │   │      → tablolar, kolonlar, ilişkiler, ui ipuçları, roller
   │   │
   │   ├─ [2] Rota ağacını türet:
   │   │      /p/{slug}                    → dashboard
   │   │      /p/{slug}/{table}            → liste
   │   │      /p/{slug}/{table}/new        → oluştur
   │   │      /p/{slug}/{table}/{pk}       → detay + düzenle
   │   │      /p/{slug}/{table}/{pk}/{rel} → ilişkili kayıtlar
   │   │      /p/{slug}/_query             → SQL/NL sorgu
   │   │      /p/{slug}/_settings          → yapılandırma
   │   │
   │   ├─ [3] Her sayfayı Renderer ile çiz (aşağıda)
   │   │
   │   └─ [4] Veriyi Gateway'den al: api.namines.com/v1/{slug}/...
   │
   └─ WebSocket: schema.version.changed → metadata tazele, sayfayı yenile
```

**Kritik nokta:** Console'un kendisi tek bir Next.js deploy'udur. 10.000 proje için 10.000 uygulama yok — tek uygulama, metadata'ya göre kendini şekillendirir.

---

## 3. Renderer motoru

### 3.1 Kolon → Widget eşleme tablosu

| NSL tipi / etiket | Liste görünümü | Form widget'ı | Filtre |
|---|---|---|---|
| `bool` | ✓/✗ rozet | Switch | Üçlü (evet/hayır/tümü) |
| `int*`, `decimal` | sağa yaslı sayı | NumberInput (min/max CHECK'ten) | Aralık |
| `money`, `decimal` + `@ui(money)` | para formatı + kur | CurrencyInput | Aralık |
| `varchar(n)` n≤80 | metin | TextInput (maxLength=n) | contains / eşittir |
| `varchar(n)` n>80, `text` | kısaltılmış + tooltip | Textarea / RichText (`@ui(rich)`) | full-text |
| `text` + `@ui(markdown)` | önizleme | Markdown editör | — |
| `date` | yerelleştirilmiş tarih | DatePicker | Aralık + hazır ("son 7 gün") |
| `timestamptz` | göreli ("3 saat önce") | DateTimePicker + TZ | Aralık |
| `uuid` | monospace kısa | ReadOnly (PK ise) / TextInput | eşittir |
| `enum<T>` | renkli rozet | Select (değerler enum'dan) | çoklu seçim |
| `json`, `jsonb` | `{...}` özet | Monaco JSON editör + şema doğrulama | JSON path |
| `bytes`, `@ui(file)` | dosya adı + boyut | Dosya yükleme (S3 presigned) | var/yok |
| `@ui(image)` | küçük resim | Görsel yükleme + kırpma | var/yok |
| `@tag(pii)` | maskeli (`a***@x.com`) | izin varsa açık | ✖ (aranamaz) |
| `@tag(sensitive)` | `••••••` | write-only | ✖ |
| FK kolonu | **hedef tablonun label'ı**, tıklanabilir | Arama yapan Combobox (uzak arama) | Referans seçici |
| `array<T>` | çip listesi | Çoklu ekleme | contains |
| `vector(n)` | boyut bilgisi | gizli | ✖ |
| computed/generated | değer, gri | salt-okunur | ✔ |
| `@ui(color)` | renk noktası | Renk seçici | — |
| `geometry` | mini harita | Harita seçici (P2) | bbox |

**FK'nın label'ı nereden gelir:** `@ui(label: display_name)` NSL'de tanımlıysa oradan; yoksa sezgisel — ilk `varchar` kolonu, adı `name`/`title`/`email` içeren kolon, yoksa PK.

### 3.2 Tablo → Sayfa deseni seçimi

| Desen | Ne zaman seçilir | Örnek |
|---|---|---|
| **Standart CRUD** | Varsayılan | `products` |
| **Ana-detay (master-detail)** | Tablonun 1-n çocuğu var ve çocuk sadece ona ait | `orders` + `order_items` |
| **Salt-okunur log** | `@tag(append_only)` veya sadece INSERT izni | `audit_log` |
| **Tekil kayıt (singleton)** | Tablo tek satır tutuyor (`@ui(singleton)`) | `settings` |
| **Ağaç görünümü** | Kendine referans veren FK var | `categories` (`parent_id`) |
| **Takvim görünümü** | Ana tarih kolonu + `@ui(calendar)` | `appointments` |
| **Kanban** | `enum` durum kolonu + `@ui(kanban: status)` | `tasks` |
| **Ara tablo (junction)** | Sadece 2 FK'dan oluşan bileşik PK | `user_roles` → ayrı sayfa yok, ilişki editörü olur |

Bu desen seçimi **otomatik** yapılır, kullanıcı override edebilir. "Sıfır konfigürasyonda anlamlı bir panel" vaadi buradan gelir.

---

## 4. Console RBAC (son kullanıcı rolleri)

Org rollerinden **ayrı**. Müşterinin kendi ekibi için.

```jsonc
{
  "roles": [
    {
      "name": "support",
      "description": "Müşteri destek ekibi",
      "tables": {
        "users":  { "read": true, "create": false, "update": ["display_name","country_code"], "delete": false,
                    "columnMask": ["password_hash"],
                    "rowFilter": "deleted_at is null" },
        "orders": { "read": true, "create": false, "update": ["status"], "delete": false,
                    "rowFilter": "placed_at > now() - interval '90 days'" },
        "audit_log": { "read": false }
      },
      "actions": ["export_csv"],
      "dashboards": ["support_overview"]
    }
  ]
}
```

- **Kolon maskeleme:** hassas kolonlar hiç gönderilmez (client-side gizleme değil — Gateway'de kesilir)
- **Satır filtresi:** SQL WHERE olarak Gateway'de zorlanır, RLS ile birleşir
- **Alan bazlı yazma izni:** `update` bir kolon listesi olabilir
- Roller Studio'da görsel editörle veya NSL'de tanımlanır

---

## 5. Denetim kaydı (audit log)

Her yazma işlemi kaydedilir:

```jsonc
{
  "id": "aud_01J...",
  "at": "2026-08-08T12:04:11Z",
  "actor": { "type": "console_user", "id": "cu_...", "email": "a@x.com", "role": "support" },
  "action": "update",
  "table": "orders",
  "rowKey": { "id": 44821 },
  "before": { "status": "pending" },
  "after":  { "status": "paid" },
  "ip": "88.x.x.x",
  "userAgent": "...",
  "requestId": "req_..."
}
```
- Kalıcılık: tenant DB'de `_namines_audit` tablosu **veya** ClickHouse (plan bazında)
- Değiştirilemez (append-only), export edilebilir (CSV/JSON)
- Console'da filtrelenebilir zaman tüneli görünümü
- **Kurumsal satışta en çok sorulan özellik budur.**

---

## 6. Dashboard motoru

Her proje için otomatik başlangıç dashboard'u:
- Her tablo için satır sayısı kartı
- Son 30 gün büyüme grafiği (ana tarih kolonu varsa)
- Son eklenen 10 kayıt
- Enum kolonları için dağılım pasta grafiği

Kullanıcı ekleyebilir: SQL tabanlı kart, sayaç, çizgi/bar/pasta grafik, tablo. Grafikler [dataviz kılavuzuna](https://) uygun tek bir tasarım sisteminde.

> Kapsam sınırı: Console bir BI aracı **değildir**. Grafikler operasyonel bağlam içindir; Metabase'e rakip olmaya çalışmıyoruz (bkz. [00-VISION.md §7](00-VISION.md)).

---

## 7. Doğal dilde sorgu

```
Kullanıcı: "geçen ay iptal edilen ve tutarı 1000 TL üstü siparişler"
   → Copilot: NSL metadata + örnek satırlar (PII'siz) ile prompt
   → Üretilen SQL gösterilir (kullanıcı görür, onaylar)
   → Salt-okunur rolle çalıştırılır, LIMIT 1000 zorlanır
   → Sonuç ızgarası + "bunu kayıtlı görünüm yap" butonu
```
Güvenlik: üretilen SQL parse edilir; `DROP/TRUNCATE/ALTER/GRANT` içeriyorsa reddedilir. Yazma sorguları ayrı, açık onaylı akış.

---

## 8. Console Eject (Faz 1 Streamlit özelliğinin yaşadığı yer)

```
POST /v1/projects/{id}/console/eject?target=nextjs
```

| Hedef | Çıktı | Durum |
|---|---|---|
| **Next.js 16 + shadcn/ui** | Tam kaynak, TanStack Table, Zod formlar, Gateway SDK'sı | YENİ, P1 |
| **React + Vite** | SPA sürümü | YENİ, P2 |
| **Blazor Server/WASM** | .NET kitlesi için | YENİ, P2 |
| **Streamlit** | Faz 1'deki paket, güncellenmiş | **KORUNDU**, P1 |
| **Retool JSON** | Retool'a import edilebilir uygulama tanımı | YENİ, P3 |

Eject edilen paket: `README.md`, `.env.example`, `Dockerfile`, `docker-compose.yml`, CI workflow'u ve **"Namines'e geri dönmek istersen: `npx namines sync`"** notu.

**Pazarlama mesajı:** *"No lock-in. Eject to real code any time."* Bu, kurumsal alımlarda en büyük itirazı ortadan kaldırır ve rakiplerin (Retool, Xano) yapamadığı şeydir.

---

## 9. Özelleştirme katmanı

Kullanıcı üretilen paneli değiştirebilir ama **üretim kabiliyetini kaybetmez** — değişiklikler ayrı bir overlay dokümanında tutulur:

```jsonc
{
  "tables": {
    "orders": {
      "displayName": "Siparişler",
      "icon": "shopping-cart",
      "hidden": false,
      "listColumns": ["id", "user_id", "status", "total", "placed_at"],
      "defaultSort": "placed_at desc",
      "pageSize": 50,
      "pattern": "master-detail",
      "columns": {
        "total": { "widget": "currency", "currencyFrom": "currency", "label": "Tutar" },
        "internal_note": { "hidden": true }
      },
      "actions": [
        { "id": "refund", "label": "İade Et", "type": "webhook",
          "url": "https://api.musteri.com/refund", "confirm": true, "roles": ["admin"] }
      ]
    }
  },
  "theme": { "primary": "#3b82f6", "logo": "s3://...", "mode": "system" }
}
```
Şema değişince overlay korunur; silinen kolonlara ait ayarlar temizlenir ve kullanıcıya bildirilir.

---

## 10. Barındırma ve alan adı

| Plan | Console adresi |
|---|---|
| Free | `console.namines.com/p/{slug}` (Namines markalı) |
| Pro | `{slug}.namines.app` |
| Team | `admin.musterifirma.com` (özel alan adı, otomatik TLS) |
| Enterprise | Self-hosted Console container'ı |

---

## 11. Neden bu, ürünün kaderini değiştirir

| | Faz 1 (ERD aracı) | Faz 2 (Console'lu) |
|---|---|---|
| Kullanım sıklığı | Proje başına 1-2 kez | **Günlük** (ekip veriyi buradan yönetiyor) |
| Kim kullanıyor | Sadece geliştirici | Geliştirici + operasyon + destek + yönetici |
| Koltuk sayısı | 1 | **5-50** |
| Terk etme maliyeti | Sıfır | Yüksek (veri + iş akışı burada) |
| Doğal fiyat | $9/ay | $19-39/kişi-ay |
| Genişleme geliri | Yok | **Koltuk bazlı, otomatik** |

Bu tablo, projeye devam etme gerekçesinin tamamıdır.
