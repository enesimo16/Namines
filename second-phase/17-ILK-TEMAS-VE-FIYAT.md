# 17 — İlk Temas ve Fiyat

> **Sorulan iş:** canlı demo, paylaşılabilir sonuç sayfası, şablon galerisini
> görünür kılmak, "neden biz" ekranı, ve fiyatların güncellenmesi (Pro $15,
> Team $40, yıllık planlar + indirim).
>
> Beşi de aynı boşluğu kapatıyor: **ürünü ilk kez gören biri için Namines,
> "prompt yazınca şema çizen" araçlardan biriydi.** Ayırt eden şey — üretimi
> yapan AI'ın çıktısını deterministik bir kural motorunun ayrıca kanıtlaması —
> koddaydı ama hiçbir ekranda görünmüyordu. Görünmeyen fark, fark değildir.

---

## 1. Canlı demo — `/demo`

Girişsiz, AI'sız, **ama sahte değil.**

| | Karar | Gerekçe |
|---|---|---|
| Şema | Hazır şablon | AI çağrısı gerçek para harcıyor; kimliksiz bir uçtan sınırsız tetiklenebilirdi ve ücretsiz havuzu bir ziyaretçi akışı tek başına bitirebilirdi (bkz. [16](16-KOTA-VE-MALIYET.md)) |
| Bulgular | **Gerçek** — `POST /api/lint` | Kural motoru zaten kimliksiz ve deterministik. Ekran görüntüsü göstermek, ürünün en çok güvenilmesi gereken iddiasını ilk temasta doğrulanamaz kılardı |
| SQL | **Gerçek** — `POST /api/compile/sql` | Aynı gerekçe. 6 motorun hepsi demoda seçilebiliyor |
| Sunucu düşerse | **Hiçbir şey gösterilmiyor** | Uydurma bir "0 hata", demonun tek değerini (gerçek olması) yok ederdi |

Ziyaretçinin gördüğü çıktı, ödeyen bir müşterinin gördüğünün **birebir aynısı**.

`?template=<key>` ile açılıyor: iniş sayfasındaki galeriden tıklanan kart,
demoyu o şablonla açıyor.

### Yapılırken bulunan iki hata

1. **Izgara rengi hydration uyumsuzluğu.** Tuvalin nokta rengi CSS
   değişkeninden okunuyordu; sunucuda CSS yok, orada `transparent` dönüyor ve
   istemcideki gerçek renkle uyuşmuyordu. Tuval artık renk okunabilir hâle
   geldikten sonra çiziliyor.
2. **Şablon değişince şemanın yarısı ekran dışında kalıyordu.** Düğümler
   `useState` + efektle kuruluyordu; şablon değişince tuval bir render boyunca
   ESKİ düğümlerle yeniden kuruluyor ve `fitView` o eski şemaya göre
   çalışıyordu. Düğümler artık `useMemo` ile türetiliyor — demo salt okunur,
   düğümleri değiştiren bir etkileşim yok, dolayısıyla durum tutmanın gerekçesi
   de yoktu.

## 2. Paylaşılabilir sonuç sayfası — `/share/{token}`

Sayfa, OG görseli ve meta etiketleri zaten vardı ([23-GTM](../new-phase/23-GTM.md)
Döngü 1). **Eksik olan dönüşümdü:** ziyaretçi salt okunur bir tuvale düşüyor ve
oradan gidecek bir yeri olmuyordu.

Eklenenler:
- Marka artık **bağlantı** — viral döngünün son adımı tıklanamayan bir metinde kırılıyordu
- Şema özeti (tablo / kolon / ilişki sayısı) — paylaşan kişinin göstermek
  istediği şeyi ziyaretçinin sayması gerekmiyor artık
- **DBA rozeti sayfanın kendisinde** — README'ye rozet koymayı hiç düşünmemiş
  bir ziyaretçi de şemanın denetimden geçtiğini görüyor. Sunucudan gelen aynı
  SVG, sayfadaki ile README'dekinin aynı olmasını garanti ediyor
- **"Build your own" → `/demo`** — hesap duvarına değil, denenebilir bir yere

## 3. Şablon galerisi iniş sayfasında

Galeri zaten vardı ama yalnızca canvas içindeki bir modaldan açılıyordu — yani
onu görmek için önce şema üretmek, yani **önce hesap açmak** gerekiyordu. Ürüne
bakmaya gelen biri, ürünün ne ürettiğine dair tek somut örneği göremeden
ayrılıyordu.

Kartlar **demoya** gidiyor, canvas'a değil: canvas'ta şablon yüklemek mevcut
çalışmanın üstüne yazıyor (Replace); tanımadığı bir ürüne ilk tıklamasında
ziyaretçiyi böyle bir karara zorlamak yanlış olurdu.

### 3.1 Şablonlar yeniden yazıldı — 5 küçükten 12 gerçeğe

**Sorun:** eldeki 5 şablonun en büyüğü 6 tabloydu. "Bizim işimize yaramaz" —
haklı: 4 tablolu bir şema, ürünün çözdüğü hiçbir problemi (kırılma analizi,
motorlar arası kayıp, ilişki karmaşası) göstermiyor. Şablon galerisi, ürünün
ne kadar ciddi bir şeyle başa çıktığını gösteremiyordu.

**Şimdi:** 20 şablon, **384 tablo**, üç ölçekte.

Ölçek bir etiket değil bir vaat — ve `check:templates` o vaadi denetliyor
(mini 4-9, standard 20-30, large 35-60 tablo). Tek bir alt sınır ("en az 20")
yetmiyordu: 6 tabloluk bir hızlı başlangıç KASITLI olarak küçük, ama
"kurumsal" diye etiketlenmiş 22 tabloluk bir şablon yanlış beklenti kurardı.

| Ölçek | Adet | Ne için |
|---|---|---|
| **Quick start** (mini, 6-7 tablo) | 6 | Auth & Roles, Tasks & Projects, Link Shortener, Newsletter, Feedback Board, Bookmarks & Notes — bir şeyi bugün kurup üstüne inşa etmek için. 6 tabloluk bir URL kısaltıcıya 25 tablo dayatmak, kullanıcıya silecek 19 tablo vermek olurdu. |
| **Full product** (standard, 21-25 tablo) | 12 | Aşağıdaki tablo |
| **Enterprise** (large, 39-40 tablo) | 2 | Multi-vendor Marketplace (39 tablo, 68 ilişki), Manufacturing ERP (40 tablo, **83 ilişki**) — 25 tabloda kural motorunun söyledikleri hâlâ gözle taranabilir; 40 tabloda taranamaz ve ürünün asıl işe yaradığı yer tam olarak orası. |

| Şablon | Tablo | İlişki |
|---|---|---|
| E-Commerce | 25 | 35 |
| Healthcare / EMR | 24 | 33 |
| SaaS Platform | 23 | 30 |
| Warehouse & Logistics | 23 | 33 |
| CRM & Sales | 22 | 41 |
| Banking & Ledger | 22 | 37 |
| HR & Payroll | 22 | 33 |
| Social Network | 22 | 39 |
| Publishing / CMS | 21 | 35 |
| Learning Platform | 21 | 31 |
| Hotel & Reservations | 21 | 28 |
| Helpdesk & SLA | 21 | 37 |

**Emoji kaldırıldı.** Tasarım sistemi zaten "ikonlar emoji değil, SVG" diyor
(FRONTEND.md); şablon kartları bu kuralın tek istisnasıydı ve kimse fark
etmemişti.

#### Kompakt tanım dili

Şablonlar önce her kolon ve her ilişki için elle kimlik yazılarak tutuluyordu.
Beş küçük şablonda katlanılabilirdi; 384 tabloda aynı kimliği üç ayrı yerde
doğru yazmak demek olurdu — ve **sessizce kırık ilişki** üretmenin en kolay
yolu budur: bağıntı ekranda hiç çizilmez, kimse fark etmez.

Yabancı anahtar artık kolonun kendi tanımında (`>tablo.kolon`) ve ilişkiler
oradan türetiliyor. Hedefi olmayan bir bağıntı **açılışta hata fırlatıyor**.

```
'organization_id UUID >organizations.id'
'manager_id INT >employees.id ?'      // ? = isteğe bağlı
```

#### Şablonlar ürünün KENDİ kural motorundan geçiyor

`npm run check:templates` her şablonu çalışan API'ye gönderiyor: gerçek linter
(`POST /api/lint`) ve **altı motorun hepsinde** gerçek DDL üreticisi. Sonuç:
**20/20 şablon, 0 hata, 0 uyarı, 6 motorda da derleniyor.**

Bu bir süs değil şart: "AI üretir, kural motoru kanıtlar" diyen bir ürünün
kendi örnek şemalarının o motordan geçememesi, iddiayı ilk temasta çürütürdü.
Pratikte üç kurala dönüştü — her tabloda tam bir birincil anahtar (ara
tablolarda vekil `id`), yabancı anahtar tipi hedefiyle aynı, ve iki tablo
birbirini işaret etmiyor (kendine dönen `parent_id`/`manager_id` güvenli).

Kuralları betikte yeniden yazmak, iki ayrı doğruluk kaynağı üretmek olurdu;
asıl soru "linter ne diyor" ve bunun tek cevabı linter'ın kendisi.

**Bilgi seviyesindeki notlar kasıtlı olarak engellemiyor.** Linter tablo
adlarının PascalCase olmasını tercih ediyor; şablonlar snake_case, çünkü
hedeflenen motorlarda yerleşik gelenek bu. Şablonları linter'ın bir tercihine
uydurmak, demoyu güzelleştirmek uğruna gerçekçiliği bozmak olurdu.

#### Büyük şemayı göstermek için düzeltilen dört şey

1. **Tuval yerleşimi 3 sütuna sabitliydi.** 25 tablo, 9 satırlık ince bir
   şeride dönüşüyordu. Sütun sayısı artık tablo sayısının kareköküne yakın
   (25 → 5×5) ve **satır yüksekliği o satırdaki en uzun tabloya göre**: sabit
   300px, 12 kolonlu bir tablonun altındakinin üzerine biniyordu. Bu düzeltme
   `schemaToFlow`'da, yani AI'ın ürettiği büyük şemalar da faydalanıyor.
2. **`fitView` düğümler ölçülmeden çalışıyordu.** 5-6 tabloda yetişiyordu;
   25 tabloda görünüm dönüşümü birim kalıyor ve **tuval bomboş görünüyordu** —
   üstelik 25 düğüm de DOM'daydı. Artık `useNodesInitialized` bekleniyor.
3. **Tam sığdırma okunaksızdı.** 25 tabloyu yan panele sığdırmak %20
   yakınlaştırma demekti (tablolar 60px'lik lekeler). Tuval tam genişliğe
   alındı ve `minZoom: 0.45` kondu: şemanın tamamını göstermeyi bırakıp
   kullanıcıyı kaydırmaya bırakmak daha dürüst.
4. **Tekerlek sayfayı değil tuvali yakınlaştırıyordu.** Tam genişlikte 560px'lik
   bir tuvalde, sayfayı kaydırmak isteyen ziyaretçi içeride sıkışıyordu.

#### Bulgular gruplandı

25 tablolu bir şemada "Table 'x' should ideally be PascalCase" yirmi beş kez
yan yana yazılıyordu — kural motoru tek bir şey söyleyen bir araç gibi
görünüyordu. Aynı kural artık tek satırda, `×25` rozetiyle ve örnek tablo
adlarıyla. Sayı korunuyor, tekrar korunmuyor.

## 4. "Neden biz" bölümü

İniş sayfasında dört madde — hepsi koddaki **gerçek** bir yeteneğe karşılık
geliyor ve hepsi demoda denenebiliyor:

1. AI önerir, kural motoru karar verir
2. Altı motor, gerçek DDL — ve motorlar arası taşımada ne kaybedileceği önceden
3. Namines kullanıcının veritabanına **asla yazmıyor**
4. Kırıcı değişiklikler merge'den önce görünüyor

Var olmayan bir özelliği buraya yazmak, ürünü ilk temasta yalancı çıkarırdı;
bölümün amacı tam tersi.

---

## 5. Fiyat: $15 / $40 + yıllık

| | Eski | Yeni |
|---|---|---|
| Pro (aylık) | $7,50 | **$15** |
| Pro (yıllık) | — | **$150** (aylık $12,50) |
| Team (aylık) | $20 | **$40** |
| Team (yıllık) | — | **$400** (aylık $33,33) |
| Yıllık indirim | — | **%17** (2 ay bedava) |

### Fiyat artık sunucuda

Fiyat, bir React bileşeninin içinde düz metindi (`"$7.5/mo"`). Stripe'taki fiyat
değiştiğinde ekran eski tutarı göstermeye devam ederdi ve **kullanıcı farkı
ancak kartından çekilen tutarda görürdü.**

Tek kaynak: `Namines.Core/Analysis/PricingCatalog.cs`, ekrana
`GET /api/subscription/plans` ile (kimliksiz — ürünün ne kadar tuttuğunu
öğrenmek için önce hesap açmak gerekmemeli).

**Fiyatın kendisi kodda, KİMLİĞİ yapılandırmada.** Stripe fiyat kimliği ortama
göre değişiyor (test/canlı) ve bir sırdan çok bir adres; koda gömmek test
anahtarıyla canlıya çıkmak demekti.

### Yıllık indirim neden %17

"2 ay bedava" (10 ay öde, 12 ay kullan). Gerekçe pazarlama değil aritmetik:
Stripe'ın işlem başına **sabit $0,30**'u aylık planda her ay tekrar kesiliyor,
yıllıkta bir kez. İndirimin bir kısmını bu tasarruf zaten karşılıyor
(bkz. [16](16-KOTA-VE-MALIYET.md)).

İndirim rozeti **hesaplanıyor, yazılmıyor**: fiyatlardan biri değişip etiket
elle güncellenmezse, ekranda gerçek olmayan bir indirim durur — bu, fiyat
etiketiyle kesilen tutarın uyuşmaması kadar ciddi bir hata.

### Yeni marj

Aylık Pro $15, Stripe kesintisi (yurt içi + Billing) −$0,84 → net **$14,16**.
%100 kullanımda API maliyeti −$1,49 → **net kâr $12,67, marj %89**.
Sabit $0,30'un payı %4'ten **%2'ye** indi.

Yıllık Pro $150 tek işlem: sabit ücret 12 kez değil 1 kez kesiliyor —
$3,60 yerine $0,30.

### Yapılırken bulunan üç hata

1. **`ResolvePlanCode` yalnızca aylık Team fiyatına bakıyordu.** Yıllık Team
   satın alan bir müşteri Pro'ya düşerdi — yani en çok ödeyen müşteri en az
   hakkı alırdı. Artık Team'in bütün fiyat kimlikleri karşılaştırılıyor.
2. **Free kartı, oturumu açan kişinin kotasını gösteriyordu.** Tavanı sunucudan
   okumak doğruydu ama `dailyLimit` **kullanıcının** tavanı, planın değil: Pro
   bir kullanıcıya Free kartında 200K, Dev hesabına "2147484K" yazılıyordu.
   Gerçek sayı artık yalnızca kullanıcı zaten Free'deyken gösteriliyor.
3. **"yearly billing coming soon" aylık sekmede de çıkıyordu.** Satın
   alınamama sebebi döneme bağlı — aylık kimliği kurulu ama yıllığı kurulmamış
   olabilir.

### Ne yapılmadı

- **Fiyat kimlikleri girilmedi** — Stripe hesabı hâlâ bekliyor
  ([34-SENDEN-BEKLENENLER](../new-phase/34-SENDEN-BEKLENENLER.md) §2). Kimlik
  boşken düğme **pasif** gösteriliyor ve sebebi yazıyor; 500 veren bir düğmeye
  tıklatmaktan iyi.
- **Mevcut abonelerin fiyatı değiştirilmedi.** Şu an ödeyen kimse yok, ama kural
  yine de doğru olan: Stripe'ta fiyat değişikliği mevcut aboneliklere kendiliğinden
  yansımaz ve yansıtılması bir ürün kararıdır.

---

## ⚠️ Dikkat

- **Demo AI'a hiçbir şey göndermiyor.** Bu bir kısıt değil tasarım: kimliksiz
  bir uçtan AI tetiklenebilseydi, ücretsiz havuz ziyaretçi trafiğiyle bitebilirdi.
- **Fiyatı iki yerde değiştirmeyin.** Ekran `PricingCatalog`'u okuyor; bileşene
  tutar yazmak eski hatayı geri getirir.
- **Yıllık fiyatlar Stripe'ta AYRI birer fiyat olarak kurulmalı.** Aynı fiyat
  kimliğini iki döneme vermek, "yıllık" düğmesinin aylık abonelik açması demek —
  ve bu ekrandan anlaşılmaz. `PricingCatalogTests` bu durumu yakalıyor.

## 🔴 Yapılmayacak

- **Demoda AI çağırmak.** Ne "sadece bir kere", ne "hız sınırıyla".
- **Fiyatı istemciden almak.** Checkout planı ve dönemi sunucuda çözüyor;
  bilinmeyen ad sessizce Pro/aylık'a düşmüyor.
- **Paylaşım sayfasında ziyaretçinin şemasını düzenletmek.** Sayfa salt okunur;
  "kendine kopyala" demonun ve editörün işi.
- **Şablonları linter'ı susturmak için PascalCase'e çevirmek.** Örnek şemalar
  gerçek şemalara benzemeli; ekranı temiz göstermek için gerçekçiliği bozmak,
  tam da ürünün karşı çıktığı şey.
- **20 tablonun altında şablon eklemek.** `check:templates` bunu hata sayıyor:
  küçük bir örnek, ürünün çözdüğü problemi gösteremiyor.
