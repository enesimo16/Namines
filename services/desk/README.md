# Namines Desk

Veritabanınız için **barındırılan, deterministik CRUD arayüzü.**

Kullanıcı bir Gateway API anahtarı girer; Desk şemayı okur, tabloları listeler,
satırları gösterir ve ekleme/düzenleme/silme yaptırır. **Veritabanı parolası
hiçbir zaman tarayıcıya gelmez** — bağlantı sunucuda şifreli durur ve anahtardan
çözülür.

---

## Bu neden ayrı bir proje?

Namines'in kendisi bir **şema tasarım** aracı. Desk ise bir **veri yönetim**
aracı: farklı kullanıcı, farklı oturum modeli, farklı dağıtım. Aynı uygulamaya
sıkıştırmak ikisini de bulanıklaştırırdı.

**Mikroservis sınırı koda da uygulanıyor:**

- Kendi `package.json`'ı, kendi portu (**3200**), kendi `node_modules`'ü
- Ana `frontend/` uygulamasına ya da `Namines.Core`'a **hiçbir kod referansı yok**
- İletişim yalnızca **HTTP sözleşmesi** üzerinden (`/api/gateway/*`)
- `DeskTable` gibi tipler burada ayrıca tanımlı — **bilinçli kopya**, paylaşılan
  paket değil

Bu kural olmasaydı bu, klasörü ayrılmış tek bir monolit olurdu.

---

## Çalıştırma

```bash
cd services/desk
npm install
npm run dev          # http://localhost:3200
```

Ana Namines backend'inin ayakta olması gerekir (varsayılan `http://localhost:5000`).
Farklıysa:

```bash
NAMINES_API=https://api.namines.com npm run dev
```

### Backend tarafında gerekenler

```bash
Security__ConnectionEncryptionKey="<en az 32 karakter, yüksek entropi>"
# Yerel geliştirmede, veritabanı localhost'taysa:
Security__AllowPrivateDbHosts=true   # yalnızca Development'ta etkili
```

Anahtar tanımlı değilse backend bağlantı saklamayı **açıkça reddeder** —
sessizce şifresiz saklamaz.

---

## Kurulum akışı

1. Namines'te bir projeye canlı veritabanı bağlayın:
   `PUT /api/gateway/keys/project/{projectId}/connection`
2. Gateway API anahtarı üretin: `POST /api/gateway/keys/{projectId}`
3. Hangi tabloların açılacağını seçin (varsayılan: **hiçbiri**):
   `PUT /api/gateway/keys/{projectId}/tables`
4. Anahtarı Desk'e girin.

> Adım 3 atlanamaz: Gateway'in kuralı "hiçbir tablo varsayılan olarak açık
> değildir". Desk yalnızca açıkça izin verilen tabloları görür.

---

## Arayüz nasıl "deterministik"?

`lib/schema.ts` saf fonksiyonlardan oluşur ve **AI kullanmaz.** Bir alanın nasıl
görüneceği tahmin edilmez, kolon meta verisinden çıkarılır:

| Kolon bilgisi | Arayüz sonucu |
|---|---|
| `type` içinde `bool`/`bit` | onay kutusu |
| `type` içinde `timestamp`/`datetime` | tarih-saat girişi |
| `type` içinde `int`/`numeric`/`decimal` | sayı girişi |
| `type` içinde `text`/`json` ya da `length > 255` | çok satırlı |
| `references` dolu | hedef etikette gösterilir |
| `isNullable = false` | zorunlu alan (`*`) |
| `isPK` + otomatik artan | ekleme formunda **gizlenir** |
| Bileşik birincil anahtar | tablo **salt-okunur** (aşağıya bkz.) |

Aynı veritabanı her zaman aynı arayüzü üretir.

### Bileşik anahtar neden salt-okunur?

Gateway'in `update`/`delete` uçları **tek** bir `pkColumn`/`pkValue` alıyor.
Bileşik anahtarlı bir tabloyu düzenlenebilir göstermek, kaydetme anında sessizce
**yanlış satırı** güncellemek olurdu. Yanlış veriyi sessizce yazmaktansa
düzenlemeyi kapatmak doğru taviz.

---

## Kapsam dışı (ilk sürüm)

Bunlar eksik değil, **bilinçli olarak sonraya** bırakıldı:

- **FK açılır listesi.** Hedef tablo biliniyor ve etikette gösteriliyor, ama
  değerler için hedeften veri çekilmiyor — ham değer giriliyor.
- **Filtreleme / sıralama / arama.** Gateway `filters` ve `orderByColumn`
  destekliyor; arayüzü henüz yok.
- **Toplu işlem, dışa aktarma.** `export`/`import` uçları hazır, bağlanmadı.
- **`/kullanıcı/proje` rotası.** Şu an tek sayfa + anahtar girişi. Rota, kullanıcı
  adının URL'de benzersizliği garanti edildikten sonra eklenecek.

---

## Doğrulanmış olanlar

Gerçek PostgreSQL'e karşı, tarayıcıdan uçtan uca (2026-09-01):

- Şema okundu, tablolar listelendi, kolon tiplerine göre form üretildi
- **Ekleme** → `psql` ile doğrudan doğrulandı, satır gerçekten yazıldı
- **Güncelleme** → `full_name` ve `is_active` gerçekten değişti
- **Silme** → satır gerçekten gitti
- İzin verilmeyen tablo **403**, geçersiz anahtar **401**, anahtarsız **401**
- Yazma izni olmayan tablo arayüzde **salt-okunur** gösterildi
