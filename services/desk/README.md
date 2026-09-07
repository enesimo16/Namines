# Namines Desk

Veritabanınız için **barındırılan, deterministik CRUD arayüzü.**

Kullanıcı kendi Namines hesabıyla giriş yapar, bir proje seçer; Desk şemayı
okur, tabloları listeler, satırları gösterir ve ekleme/düzenleme/silme
yaptırır. **Veritabanı parolası hiçbir zaman tarayıcıya gelmez** — bağlantı
sunucuda şifreli durur, oturum sahibinin o projeye erişimi doğrulandıktan
sonra çözülür (`namines_desk/01-KIMLIK-VE-OTURUM.md`).

> v0.1'de giriş ham bir Gateway API anahtarıyla yapılıyordu. D1 ile bu kalktı:
> API anahtarları artık yalnızca **dış uygulamalar** için (bkz. "Kurulum
> akışı" altındaki not).
>
> **Durum: Desk v1 (D1-D7) + Desk v2 (E1-E4, E5.1-E5.2) tamamlandı.**
> Bitiş dokümanı: [`namines_desk/11-DESK-V2-TAMAMLANDI.md`](../../namines_desk/11-DESK-V2-TAMAMLANDI.md).

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

## Arayüz yapısı (v2.1 — pano kabuğu)

Giriş yapan kullanıcı doğrudan bir **yönetim panosuyla** karşılanır; ayrı bir
"önce proje seç" adımı yoktur, proje seçimi panonun kendi görünümüdür.

```
AppShell.tsx ............ kalıcı kabuk (sol gezinme + üst şerit)
  ├─ marka + arama (Ctrl/Cmd+K) + proje seçici
  ├─ gruplu menü: Genel · Veritabanı · İşlemler · Ayarlar
  │    └─ "Veri"nin altında yuvalanmış tablo listesi
  ├─ en altta: paneli daralt/genişlet (tercih localStorage'da)
  └─ üst şerit: ekmek kırıntısı + Ana uygulama + Destek + profil menüsü

page.tsx ................ oturum + yönlendirme (hangi görünüm, hangi tablo)
Projects.tsx ............ karşılama panosu (türetilmiş sayılar + proje kartları)
Desk.tsx ................ proje kapsamındaki İÇERİK (kabuk değil)
PageHead.tsx ............ her görünümün başlığı + sınırları
```

Görünüm ve seçili tablo **kabukta** (page.tsx) tutulur, veri ise `Desk.tsx`'te:
tablo listesi sol panelde yaşadığı için "hangi tablo seçili" bilgisini iki
yerde tutmak ikisinin ayrışmasına açık kapı bırakırdı.

**Tema:** açık/koyu, üst şeritteki anahtarla. Varsayılan açık (Desk veri yoğun
tabloların uzun süre okunduğu bir operasyon paneli), ama tercih kullanıcının:
seçim `localStorage`'da kalıcı, hiç seçim yoksa işletim sisteminin tercihi
okunuyor. Tema `<html data-theme>` ile uygulanıyor — bileşenler yalnızca token
okuyor, hiçbiri "hangi temadayım" diye sormuyor.

**Mobil:** 900px altında sol panel çekmeceye dönüşür (üst şeritte hamburger),
seçim yapılınca kendiliğinden kapanır.

Ayrıntı ve karar kayıtları: [`namines_desk/12-DESK-PANO-KABUGU.md`](../../namines_desk/12-DESK-PANO-KABUGU.md).

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

`NAMINES_FRONTEND` da ayrı bir değişken — bu **API değil, ana Namines
UYGULAMASI** (tarayıcıda açılan, varsayılan `http://localhost:3000`). Desk'in
"Namines'te henüz proje yok, ana uygulamada bir tane oluşturun" gibi
bağlantıları buraya gider; `NAMINES_API` ile karıştırılırsa kullanıcı JSON
döndüren API adresine düşer.

```bash
NAMINES_FRONTEND=https://namines.com npm run dev
```

### Backend tarafında gerekenler

```bash
Security__ConnectionEncryptionKey="<en az 32 karakter, yüksek entropi>"
# Yerel geliştirmede, veritabanı localhost'taysa:
Security__AllowPrivateDbHosts=true   # yalnızca Development'ta etkili
```

Anahtar tanımlı değilse backend bağlantı saklamayı **açıkça reddeder** —
sessizce şifresiz saklamaz.

> ⚠️ **Ortam adı Desk için kritik.** Backend `ASPNETCORE_ENVIRONMENT=Production`
> ise iki şey birden sessizce kapanır: (1) Desk'in origin'i CORS izin listesine
> otomatik EKLENMEZ — her istek tarayıcıda `Failed to fetch` olur; (2)
> `Security:AllowPrivateDbHosts` YOK SAYILIR — konteyner/localhost adresli bir
> veritabanına bağlanamazsınız, yani Desk'in çekirdek akışı hiç çalışmaz.
> Kök `docker-compose.yml` bu yüzden **Development** kullanıyor (yerel yığın
> zaten öyle: portlar localhost'a açık, DB parolası depoda). Gerçek dağıtımda
> Production + açık `Cors__AllowedOrigins__0=https://<desk-alan-adiniz>`.

---

## Kurulum akışı

1. Namines'te bir projeye canlı veritabanı bağlayın:
   `PUT /api/gateway/keys/project/{projectId}/connection`
2. Desk'te (`localhost:3200`) Namines hesabınızla giriş yapın.
3. Erişebildiğiniz projelerden birini seçin.

Tablo izni ayrıca seçilmez: oturum yolu, key yolunun aksine, **tablo
izinlerini uygulamaz** — projenize zaten erişiminiz varsa (Viewer ve üstü),
o projenin bağlı olduğu veritabanındaki her tabloyu Desk'te görürsünüz; yazma
ise Editor/Admin/Owner rolüyle sınırlı. Bu bilinçli: siz zaten bağlantı
dizesini kendiniz girebilen taraftasınız, izin katmanı yalnızca **dış
uygulamaları** (API anahtarları) sınırlamak için var.

> **API anahtarları kalktı mı?** Hayır — sahibi değişti. `POST
> /api/gateway/keys/{projectId}` hâlâ var ve dış uygulamalar için gerekli.
> Desk giriş için anahtar İSTEMİYOR (oturumla çalışıyor), ama anahtarları
> **yönetmek** için kendi ekranı var: sol panelde `Ayarlar → API anahtarları`
> (v2/E1.1, yalnızca proje sahibine).

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

## Kapsam dışı (bilinçli olarak, kalıcı)

v0.1'deki ilk liste artık büyük ölçüde bitti (FK açılır listesi, filtre/
sıralama, toplu silme, ham SQL konsolu, API anahtarı yönetimi, ekip
görüntüleme — hepsi v1/v2'de eklendi, aşağıya bkz.). Kalıcı olarak dışarıda
bırakılanlar için `namines_desk/10-DESK-V2-YOL-HARITASI.md`'nin "Kalıcı
olarak dışarıda" bölümüne bakın (şema düzenleme, konum kaydetme, proje
oluşturma/silme — hepsi ana uygulamanın işi kalıyor).

---

## Test

```bash
npm test              # tek seferlik, Vitest
npm run test:watch    # izleme modu
npm run test:coverage # kapsam raporu
```

Kapsam: `lib/schema.ts` (deterministik katman — kolon tipinden form
bileşenine çeviren TÜM kurallar), `lib/designSchema.ts` (`SchemaJson`/
`NodePositionsJson` ayrıştırma, camelCase/PascalCase toleransı),
`lib/apiKeys.ts` ve `lib/members.ts` (mock `fetch` ile — 204/hata gövdesi
ayrıştırma), `BULK_DELETE_THRESHOLD` sabiti. Bileşen (React) testleri henüz
yok — mevcut kapsam tamamen network/DOM'suz, saf mantık.

---

## Doğrulanmış olanlar

### Desk v1 (D1-D7) ve Desk v2 (E1-E4, E5.1-E5.2) — bitti

Ayrıntılı bitiş durumu, kanıt tablosu ve öğrenilen dersler (ör. "konteyner
`Up` ≠ güncel kod çalışıyor" — Docker imajını yeniden derlemeden backend
değişikliği yayılmaz): [`namines_desk/11-DESK-V2-TAMAMLANDI.md`](../../namines_desk/11-DESK-V2-TAMAMLANDI.md).

### v0.1 — API anahtarı yolu, gerçek PostgreSQL'e karşı tarayıcıdan uçtan uca (2026-09-01)

- Şema okundu, tablolar listelendi, kolon tiplerine göre form üretildi
- **Ekleme** → `psql` ile doğrudan doğrulandı, satır gerçekten yazıldı
- **Güncelleme** → `full_name` ve `is_active` gerçekten değişti
- **Silme** → satır gerçekten gitti
- İzin verilmeyen tablo **403**, geçersiz anahtar **401**, anahtarsız **401**
- Yazma izni olmayan tablo arayüzde **salt-okunur** gösterildi
