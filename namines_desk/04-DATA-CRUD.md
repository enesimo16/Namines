# 04 — Data (deterministik CRUD)

> **Bu ekran v0.1'de YAZILDI ve çalışıyor.** Bu doküman mevcut hâli kaydediyor
> ve v1'de eklenecekleri ayırıyor.

---

## 1. Çalışan hâl (v0.1)

`services/desk/` — tarayıcıdan uçtan uca, gerçek PostgreSQL'e karşı doğrulandı
(2026-09-01):

| Ne | Sonuç |
|---|---|
| Şema okuma → tablo listesi | ✔ |
| Satır listeleme + sayfalama | ✔ |
| **Ekleme** | ✔ `psql` ile doğrudan doğrulandı |
| **Güncelleme** | ✔ `full_name` + `is_active` gerçekten değişti |
| **Silme** | ✔ satır gerçekten gitti |
| İzin verilmeyen tablo | 403 |
| Yazma izni yok | arayüz salt-okunur, sebebi yazılı |

### Deterministik üretim (`lib/schema.ts`)

Saf fonksiyonlar, **AI yok**. Aynı veritabanı her zaman aynı arayüzü üretir:

| Kolon bilgisi | Bileşen |
|---|---|
| `bool` / `bit` | onay kutusu |
| `timestamp` / `datetime` | tarih-saat |
| `int` / `numeric` / `decimal` | sayı |
| `text` / `json` veya `length > 255` | çok satırlı |
| `isNullable = false` | zorunlu (`*`) |
| `isPK` + otomatik artan | ekleme formunda gizli |
| Bileşik PK | tablo **salt-okunur** |

**Bileşik PK neden salt-okunur:** Gateway'in `update`/`delete` uçları TEK bir
`pkColumn` alıyor. Düzenlenebilir göstermek, kaydetme anında sessizce **yanlış
satırı** güncellemek olurdu. Yanlış veri yazmaktansa düzenlemeyi kapatmak doğru
taviz.

---

## 2. v1'de eklenecekler

### 2.1 FK açılır listesi

Bugün FK alanı ham değer istiyor (`customer_id` için "3" yazmak gerekiyor),
hedef yalnızca etikette gösteriliyor.

v1: hedef tablodan kayıtlar çekilip **okunabilir** bir liste sunulur —
`displayColumn()` zaten yazılmış durumda (`name`/`title`/`email` gibi bir alan
arar, yoksa PK'ya düşer).

> ⚠️ **Ölçek sınırı:** 50.000 satırlı bir hedef tabloyu açılır listeye
> dökmek tarayıcıyı kilitler. Eşik: hedefte 200'den fazla satır varsa liste
> yerine **aramalı seçici** gerekir. v1'de basit liste + "çok fazla kayıt,
> ham değer girin" düşüşü; aramalı seçici v1.1.

### 2.2 Filtreleme ve sıralama

Gateway **zaten destekliyor** — `GatewayListRequest` şu alanları alıyor:
`Filters`, `OrGroups`, `OrderByColumn`, `SortDirection`, `Select`.

Yani bu tamamen bir arayüz işi, backend değişikliği gerektirmiyor.

- Kolon başlığına tıkla → sırala
- Kolon başına filtre (tipe göre: metin `contains`, sayı `>=`, tarih aralığı)

### 2.3 Dışa aktarma

`POST /api/gateway/export` var (`csv` formatı, `MaxRows` sınırlı). Bağlanacak.

---

## 3. Kapsam dışı (v1)

| Ne | Neden |
|---|---|
| Toplu düzenleme / silme | Geri alınamaz işlemin toplu hâli; önce tekil akış oturmalı |
| Ham SQL konsolu | `POST /api/gateway/query` var ama `CanExecuteSql` ayrı bir yetki — ayrı ekran, ayrı onay |
| İlişkili kayıtları satır içinde gösterme | `Expand` alanı Gateway'de var; canvas zaten ilişkiyi gösteriyor, öncelik düşük |

---

## 4. v1 kabul kriterleri

| # | Kriter | Kanıt |
|---|---|---|
| 1 | FK alanı okunabilir seçim sunar | `vehicles.customer_id` → "Enes Yel" seçilebilir |
| 2 | 200+ satırlı hedefte ham girişe düşer, sebebi yazılır | Demo DB'ye 300 satır ekle |
| 3 | Kolon sıralama gerçekten sunucuda yapılır | Ağ isteğinde `orderByColumn` görünür |
| 4 | Filtre sonucu `totalCount` ile tutarlı | Tarayıcı + `psql` sayımı |
| 5 | v0.1'in CRUD'u bozulmamış | Ekleme/güncelleme/silme tekrar `psql` ile doğrulanır |

Kriter 5 her v1 turunda tekrarlanır — yeni özellik eklerken çalışan yolu
kırmadığımızın tek kanıtı.
