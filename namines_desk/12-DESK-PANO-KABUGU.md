# 12 — Desk v2.1: Pano Kabuğu (Dashboard Shell)

> **Durum:** yapıldı ve canlı doğrulandı (2026-09-07).
> İkinci tur (kullanıcı geri bildirimiyle çıkan hatalar ve mobil/tema
> eklentileri): **§5**.
> Önceki bitiş dokümanı: [`11-DESK-V2-TAMAMLANDI.md`](11-DESK-V2-TAMAMLANDI.md).

---

## 1. Neden değişti

Desk v2 bittiğinde **işlevsel olarak** tamamdı ama **kabuk olarak** değildi:
giriş yapan kullanıcıyı ekranın ortasında yüzen tek bir kart karşılıyordu
("proje seç"), tüm görünümler o karttan sonra gelen yatay bir düğme şeridine
sıkışmıştı ve sayfada kullanıcının nerede olduğunu söyleyen hiçbir kalıcı
işaret yoktu.

Kullanıcının kendi ifadesiyle: *"bu çok sığ ve ux konusunda çok kötü."*

Bir CRUD paneli, tasarım aracının aksine **uzun süre açık kalan bir operasyon
yüzeyi**. Bu tür yüzeylerde sektörün fiili standardı (Cloudflare, Vercel,
Supabase, Railway panoları) üç parçadan oluşur ve Desk artık aynı yapıda:

| Parça | İçeriği |
|---|---|
| **Sol gezinme (kalıcı)** | Marka + arama + proje seçici + gruplu menü + daralt/genişlet |
| **Üst şerit** | Ekmek kırıntısı (nerede olduğun) + hesap/destek eylemleri |
| **İçerik** | Seçili görünüm, kendi başlığı ve açıklamasıyla |

---

## 2. Ne yapıldı

### 2.1 Yeni kabuk — `app/AppShell.tsx`

- **Marka** sol üstte: logo işareti + "Namines Desk" + `beta` rozeti.
  (Cloudflare'da o köşede hesap e-postası duruyor; burada ürün adı duruyor —
  hesap bilgisi zaten sağ üstteki profil menüsünde.)
- **Arama** (`Ctrl/Cmd+K` ile odaklanır): hem gezinme başlıklarını hem de
  tablo adlarını süzer. Ayrı bir "komut paleti" katmanı **açılmadı** —
  aranacak şeylerin tamamı zaten sol panelde duruyor, ikinci bir katman
  aynı listeyi iki yerde tutmak olurdu.
- **Proje seçici**: hangi projedeysen orada yazıyor, tıklayınca panoya döner.
- **Gruplu menü**: `Genel` · `Veritabanı` · `İşlemler` · `Ayarlar`.
  Proje gerektiren maddeler proje seçilmeden **kilitli** görünür (asılı bir
  kilit ikonu + `disabled`), gizlenmez: ürünün neler yapabildiği, henüz
  kullanılamıyorken de görünür olmalı.
- **Tablo listesi**, `Veri` maddesinin **altında yuvalanmış** olarak duruyor.
  Üst seviyede olsaydı 20+ tablolu bir projede gezinmenin kendi bölümlerini
  görünmez kılardı.
- **Daralt/genişlet** en altta; tercih `localStorage`'da kalıcı.
  Daraltılmış hâlde yalnızca ikonlar kalır (248px → 60px).

### 2.2 Üst şerit

Ekmek kırıntısı (`Namines Desk / <proje> / <görünüm>`), "Ana uygulama"
bağlantısı, "Destek", ve **profil menüsü**: kullanıcı adı + e-posta + ana
uygulama + proje değiştir + destek + çıkış.

Kullanıcı adı/e-posta, JWT'nin gövdesinden **yalnızca gösterim için**
okunuyor (`lib/nav.ts` → `decodeSessionUser`). İmza burada doğrulanmıyor ve
doğrulanmamalı: yetki kararını her istekte sunucu veriyor, Desk'in kendi
yetki kararı yok.

### 2.3 Karşılama panosu — `app/Projects.tsx`

Yüzen kartın yerini gerçek bir pano aldı: türetilmiş sayılar (proje, bağlı
veritabanı, tasarlanan tablo, motor sayısı — **hepsi `/api/auth/projects`
yanıtından hesaplanıyor**, ayrı bir uç ya da uydurma sayı yok), arama +
motor + bağlantı-durumu filtreleri, ve zengin proje kartları.

### 2.4 Görünüm başlıkları — `app/PageHead.tsx`

Her ekran artık kendi başlığını ve **sınırını** yazıyor; örneğin:
- *Kayıtlar:* "Okuma istekleri burada YOK — denetim kaydı yalnızca veriyi
  değiştiren işlemleri tutuyor."
- *Analitik:* "CPU/istek/transfer gibi barındırma metrikleri burada YOK:
  Namines kimsenin uygulamasını çalıştırmıyor."
- *Sürümler:* "Desk buradan DDL çalıştırmaz."

Bu sınırlar önceden yalnızca dokümanlarda yazılıydı; artık ekranın kendisinde.
Erken dönüş hâlleri de (yetki yok / boş / hata) başlığı taşıyor — başlıksız
bir ekran "yanlış yere mi geldim?" sorusu bırakıyordu.

### 2.5 Açık (beyaz) tema — `app/globals.css`

Desk koyudan **açık** temaya geçti; token **adları** değişmedi
(`--surface-900` en arka zemin, `--surface-800` yüzey …), yalnızca değerler
ve yön değişti. Böylece tüm mevcut bileşenler tek satır değişmeden yeni
paletle çalıştı.

**Neden ana uygulamadan farklı:** Namines (tasarım düzlemi) bir tuval —
koyu zemin orada içeriği öne çıkarıyor. Desk ise veri yoğun tabloların uzun
süre okunduğu bir operasyon paneli; bu tür yüzeylerde açık zemin fiili
standart. İki ürünün **bilinçli olarak** farklı görünmesi, hangi düzlemde
olduğunu da anlatıyor.

---

## 3. İç mimari değişikliği

`Desk.tsx` artık **kabuk değil, içerik**: sol panel, üst şerit ve görünüm
seçimi `AppShell` + `page.tsx`'e taşındı.

| Durum | Sahibi | Neden |
|---|---|---|
| Oturum, projeler | `page.tsx` | Kabuk da içerik de ikisine bakıyor |
| Görünüm (`view`) | `page.tsx` | Menü kabukta, içerik altta — tek kaynak şart |
| Seçili tablo | `page.tsx` | Tablo listesi kabukta yaşıyor |
| Şema/satırlar | `Desk.tsx` | Veri sahibi hâlâ içerik |

Tablo listesi Desk'ten kabuğa `onTablesLoaded` ile bildiriliyor — kabukta
ikinci bir şema isteği atmak, aynı veriyi iki kez çekmek olurdu.

> **Yolda yakalanan gerçek hata:** varsayılan tablo seçimi önce
> `Desk.reloadSchema` içinde yapılıyordu, ama o çağrı aynı zamanda Data
> görünümüne geçiriyordu — yani proje açılır açılmaz kullanıcı Şema yerine
> ham satırlara düşüyordu. Varsayılan seçim kabuğa taşındı ve **görünüm
> değiştirmeden** yapılıyor.

---

## 4. Yerel yığındaki iki gerçek arıza (düzeltildi)

Bu turda Desk'in **çekirdek akışı** (bir projeye canlı veritabanı bağlamak)
yerel ortamda hiç çalışmıyordu. İkisi de `docker-compose.yml`'deki ortam
adının `Production` olmasından geliyordu:

| Arıza | Sebep | Düzeltme |
|---|---|---|
| `Security:ConnectionEncryptionKey tanımlı değil` → 500 | Anahtar hiç verilmemişti; servis bağlantı saklamayı **açıkça reddediyor** (düz metin saklamıyor — doğru davranış) | Yerel geliştirme anahtarı compose'a eklendi |
| Konteyner adresli DB'ye bağlanılamıyor | `DbHostAccessPolicy` özel/ayrılmış adreslere yalnızca **Development**'ta izin veriyor | `ASPNETCORE_ENVIRONMENT=Development` + `Security__AllowPrivateDbHosts=true` |

Ortam adı zaten yanlıştı: bu compose dosyası portları localhost'a açan,
DB parolasını depoda taşıyan, açılışta migration koşan bir **yerel
geliştirme** yığını. (Aynı yanlış ad bir önceki turda CORS arızasına da
sebep olmuştu.)

**Canlı kanıt (2026-09-07):** `desk_demo` veritabanı (customers/orders/
order_items, gerçek FK'lar ve satırlar) oluşturuldu, projeye bağlandı
(`connected: true`), ve kabuğun içinde uçtan uca doğrulandı:

| Ekran | Sonuç |
|---|---|
| Karşılama panosu | 2 proje, türetilmiş sayılar, filtreler |
| Şema (Canvas) | 3 tablo, FK çizgileri, drift rozetleri, otomatik yerleşim |
| Veri | `customers` — 3 kayıt · 5 kolon, filtre/sıralama/sayfalama |
| SQL konsolu | Açıldı, `SELECT … FROM customers` **3 gerçek satır** döndürdü |
| Sürümler / Kayıtlar / Analitik / Ekip / API anahtarları | Hatasız, başlıklı |
| Sol panel daraltma | 248px → 60px (ölçüldü), tercih kalıcı |
| Profil menüsü | Ad + e-posta + 4 eylem, dışarı tıklayınca kapanıyor |

---

## 5. İkinci tur — kullanıcı geri bildirimiyle düzeltilenler (2026-09-07)

Kabuk ayakta durduktan sonra gerçek kullanımda çıkan sorunlar. Hepsi
düzeltildi ve canlı doğrulandı.

### 5.1 🔴 Sonsuz istek döngüsü (429) — en kritik hata

**Belirti:** "Veri kısmı düzgün çalışmıyor", "istekler 429 dönüyor".

**Kök neden:** `page.tsx`, Desk'e oturumu satır içi veriyordu:
`session={{ token, projectId }}`. Bu, **her render'da yeni bir nesne**
demek. `Desk.tsx`'in üç efekti de (`reloadSchema`, sürüm yoklaması,
`loadRows`) `[session]`'a bağlı olduğu için her render yeniden istek
atıyor, gelen yanıt state'i güncelleyip yeni bir render tetikliyordu:
kapalı bir döngü.

Kabuk öncesinde `page.tsx` nadiren render oluyordu ve hata gizli kalmıştı;
görünüm/tablo durumu kabuğa taşınınca her gezinme döngüyü besledi.

| Ölçüm | Önce | Sonra |
|---|---|---|
| 8 saniyede API isteği | **978** | **0** |

Düzeltme: oturum nesnesi `useMemo` ile `[token, projectId]`'ye bağlandı —
ve **tüm erken `return`'lerden önce** (hook'lar koşullu çağrılamaz).

> **Ders:** bir nesne/dizi prop'u alt bileşende `useEffect` bağımlılığı
> oluyorsa, üst bileşende MUTLAKA memoize edilmeli. Bu, "çalışıyor gibi
> görünen" ama sunucuyu döven bir hata sınıfı.

### 5.2 Sol paneldeki proje seçici artık gerçek bir menü

Önceki hâlde düğme doğrudan panoya atıyordu, ama yanındaki çift ok ikonu
bir liste vaat ediyordu — "çalışmıyor" izlenimi buradan geliyordu. Artık
projeler menüde listeleniyor (bağlantı durumu noktasıyla), panoya
uğramadan geçiş yapılıyor; bağlantısı olmayan proje seçilirse Import
akışının olduğu panoya yönlendiriliyor.

### 5.3 Açık/koyu tema anahtarı (üst şeritte)

`ThemeToggle.tsx` — tema `<html data-theme>` ile uygulanıyor, hiçbir bileşen
"hangi temadayım" diye sormuyor (yalnızca token okuyor). Sıra: kullanıcının
kayıtlı tercihi → işletim sisteminin tercihi → açık. Koyu palet, Desk'in
ilk sürümündeki paletin ta kendisi.

### 5.4 Mobil / dar ekran

900px altında sol panel **çekmeceye** dönüşüyor (üst şeritte hamburger,
arkasında karartma, seçim yapınca kendiliğinden kapanıyor); 560px altında
üst şeritteki metin bağlantıları gizleniyor (profil menüsünde zaten var).
Logs/Deployments'ın yan panelleri alta iniyor. Ölçüldü: 375px'te yatay
taşma yok.

### 5.5 Veri ekranı

- **Filtreler katlanır oldu:** her kolon için bir kutu çiziliyordu; 15
  kolonlu bir tabloda bu, verinin kendisini ekrandan iten bir giriş
  duvarıydı. Artık üst şeritte "Filtreler (n)" düğmesi arkasında.
- **"İşlem" sütunu sağa, seçim kutusu sola sabitlendi** (sticky): geniş
  tabloda yatay kaydırınca Düzenle/Sil görünüm dışına çıkıyordu.
- Uzun hücreler kırpılıyor (satırı sonsuza uzatmasınlar).

### 5.6 Analitik grafiği

Grafik, tek veri noktası olan bir projede **ekranı kaplayan tek bir dolu
dikdörtgene** dönüşüyordu (çubuk genişliği kova sayısına bölünüyordu) ve
altı işlem türü birbirine yakın **altı gri tonuydu** — yığında hangi
dilimin ne olduğu ayırt edilemiyordu.

Düzeltme: çubuk genişliği 44px ile sınırlandı ve seri ortalandı; ızgara
çizgileri + Y ekseni etiketleri (yuvarlatılmış tavanla) + ilk/son kova
etiketi + fare üstünde toplam eklendi; renkler anlamlı ve ayırt edilebilir
oldu (**ekleme yeşil, güncelleme mavi, silme kırmızı**).

### 5.7 Bağlam yenilemede korunuyor

Seçili proje ve görünüm `sessionStorage`'da (JWT ile **aynı ömür**).
Yolda bir hata daha çıktı ve düzeltildi: yazma efekti ilk render'da da
çalışıp, geri yükleme efektinin okuduğu kaydı başlangıç değeriyle
**eziyordu** — belirti, yenilemeden sonra görünümün hep "Projeler"e
düşmesiydi. Bir `ref` bayrağıyla ilk yazma atlanıyor.

### 5.8 Adlandırma

Üst şeritteki "Ana uygulama" → **"Namines"** (profil menüsünde
"Namines'i aç"). Ürünün adı, jenerik bir tanımdan daha net.

---

## 6. Bilinçli olarak yapılmayanlar

- **Komut paleti (⌘K modalı):** arama sol panelde; ikinci bir katman aynı
  listeyi iki yerde tutmak olurdu. Gezinme büyürse yeniden değerlendirilir.
- **Bildirim/aktivite çanı:** Desk'in ürettiği bir bildirim akışı yok;
  boş bir çan ikonu, olmayan bir özelliği varmış gibi gösterirdi.
- **Sunucuda saklanan tema/panel tercihi:** tercihler tarayıcıda
  (`localStorage`). Sunucuya yazmak, her tercih değişiminde bir istek ve
  yeni bir uç demek — kazancı yok.
- **Grafik kütüphanesi:** grafik hâlâ bağımlılıksız inline SVG
  (`07-ANALYTICS.md` §5'in kararı korundu).
