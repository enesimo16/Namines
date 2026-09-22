# 03 — Pazar ve Tasarım Analizi

> **Varsayım:** Bu doküman, [34-SENDEN-BEKLENENLER](../new-phase/34-SENDEN-BEKLENENLER.md)'deki
> her şeyin tamamlandığı senaryoyu değerlendiriyor — Stripe canlı, GitHub App
> kurulu, alan adı alınmış, npm'de yayında, kalıcı AI anahtarı var.
>
> **Ton:** Bu bir pazarlama dokümanı değil. Övmek işe yaramaz; ürünün nerede
> gerçekten savunulabilir olduğunu ve nerede kırılgan olduğunu ayırmak işe yarar.
>
> Tasarım bölümü **çalışan uygulamadan ölçülerek** yazıldı, koda bakarak değil.

---

# BÖLÜM 1 — PAZAR

## 1.1 Ürün gerçekte ne satıyor?

Namines'in üç ayrı şey olduğu iddia edilebilir ve bu bir sorun:

| Katman | Ne yapıyor | Pazardaki karşılığı |
|--------|-----------|---------------------|
| **A. Şema tasarımı** | Cümleden şema, canvas, 6 motora DDL | dbdiagram, DrawSQL, Azimutt, ChartDB |
| **B. Kod üretimi** | 18 eject hedefi, admin paneli, SDK | Prisma/Drizzle CLI, Supabase codegen |
| **C. Değişim yönetişimi** | Etki analizi, risk sınıfı, onay akışı, PR botu | **Bytebase, Atlas, Liquibase Pro** |

**Savunulabilir olan tek katman C.** A ve B emtia:
- A'yı bir geliştirici bugün Claude'a "bana e-ticaret şeması çiz" diyerek yapıyor
- B'yi Prisma'nın kendi CLI'ı bedava yapıyor

Doküman 27 bunu zaten tespit etmiş ve doğru tespit: *"AI kod/agent araçları şema
üretimini emtialaştırıyor, production değişikliği etrafındaki kanıt ve onay
sürecini emtialaştırmıyor."*

**Ama ürünün kendisi hâlâ A gibi konumlanıyor.** Ana sayfada kullanıcıyı
karşılayan şey bir prompt kutusu ve "saniyeler içinde veritabanı mimarisi
tasarla". Bu, en zayıf katmanı vitrine koymak demek.

> **En büyük stratejik risk bu.** Ürün C'de güçlü, A'da satılıyor. A'da satılan
> bir ürün, A'nın fiyatını alır — ve A'nın fiyatı sıfıra gidiyor.

## 1.2 Rekabet haritası

### Doğrudan rakip: Bytebase
En yakın rakip ve dürüst olmak gerekirse **daha olgun**. Açık kaynak, DB DevOps +
review workflow + çok motor. Namines'in C katmanındaki iddiasının çoğunu zaten
yapıyor.

**Namines'in Bytebase'e karşı gerçek farkları:**
1. **Şemayı sıfırdan üretebiliyor** — Bytebase var olan şemayı yönetir, tasarlamaz
2. **Deterministik kapı + AI birlikte** — Bytebase'de AI ikincil
3. **Netleştirme ajanı** — Bytebase'de karşılığı yok
4. **Çok daha ucuz** (aşağıda bunun neden bir sorun olduğuna bakacağız)

**Bytebase'in Namines'e karşı farkları:** olgunluk, açık kaynak topluluğu, kurumsal
referanslar, SSO/SAML, denetim standartları (SOC2), gerçek üretim yükü altında
kanıtlanmış olması.

### Atlas (ariga.io)
"Terraform for databases". Migration'ı kod olarak yönetiyor, planlama ve
doğrulama yapıyor. **Namines'in "kanıt" iddiasının en güçlü rakibi.**
Geliştirici-önce, CLI-önce. Namines'in avantajı: görsel + teknik olmayan birinin
onaylayabileceği bir inceleme ekranı. Atlas'ta o kitle yok.

### Supabase / Hasura / Directus
Farklı kategori (BaaS) ama **bütçe rakibi** — bir startup "veritabanı katmanı
için ne ödüyorum" diye baktığında aynı satırda görünüyorlar. Supabase $25/ay ile
hosting + auth + storage + realtime veriyor. Namines hosting vermiyor.

### AI kod ajanları (Claude Code, Cursor)
**Hem rakip hem tamamlayıcı.** MCP sunucusu bu yüzden stratejik olarak doğru
hamle: rakip olmak yerine onların içine yerleşiyor. "Claude migration yazsın,
Namines kanıtlasın" konumu, "Claude yerine Namines kullan"dan çok daha
savunulabilir.

> **Öneri:** MCP/Skill dağıtımı bir yan özellik değil, **ana dağıtım kanalı**
> olarak ele alınmalı. En düşük maliyetli müşteri edinme yolu bu.

## 1.3 Fiyatlandırma — burada somut bir hata var

Mevcut yapı:

| Plan | Fiyat | Koltuk | Günlük token |
|------|-------|--------|--------------|
| Free | 0 | 1 | 20.000 |
| Pro | 7,5 $ | 1 | 200.000 |
| Team | 20 $ | 3 | 200.000 (paylaşılan) |

### 🔴 Hata 1: Team, küçük ekipler için mantıksız

| Ekip | Team | Alternatif | Kazanan |
|------|------|-----------|---------|
| 2 kişi | 20 $ · 200K token | 2× Pro = **15 $** · **400K token** | **Pro daha ucuz VE daha çok token** |
| 3 kişi | 20 $ · 200K token | 3× Pro = 22,5 $ · 600K token | Team 2,5 $ ucuz ama **3 kat az token** |

İki kişilik bir ekip Team almak için **daha fazla ödeyip daha az token** almak
zorunda. Tek kazandığı ortak workspace. Bu, çoğu ekibin "iki Pro alıp Slack'ten
konuşuruz" demesine yol açar.

**Sebep:** Token'ı Pro ile eşitleme kararı (senin talimatın, doğru bir üründen
mantığı vardı) ile fiyatın 2,67 katına çıkması aynı anda oldu. Mantık şuydu:
"Team daha çok token değil, birlikte çalışma satıyor." Doğru — ama o zaman
**fiyat da koltuk başına düşmeliydi, artmamalı.**

**Üç olası düzeltme:**
1. **Team'i koltuk başına fiyatla** — 3 koltuk × 7,5 $ = 22,5 $, her koltuk kendi
   200K token'ını getirir. Basit, adil, anlaşılır. *(Önerim bu.)*
2. **Team fiyatını düşür** — 12–15 $ arası, ortak workspace primi olarak.
3. **Team'e havuz ver** — 3 koltuk için 500K paylaşılan token.

### 🔴 Hata 2: Fiyat, ürünün iddiasıyla çelişiyor

7,5 $/ay bir **hobi aracı** fiyatı. Ama ürünün savunulabilir iddiası
"production veritabanı değişikliğini kanıtla ve onaylat" — bu bir **risk azaltma**
ürünü ve risk azaltma ürünleri pahalı satılır.

Karşılaştırma:
- Bytebase: ekip planları 100 $+/ay
- Liquibase Pro: kurumsal, binlerce dolar
- Retool: 10 $/kullanıcı/ay (ve o sadece bir panel aracı)

**7,5 $ diyen bir ürün, "veri kaybını önlüyorum" iddiasını taşıyamaz** — alıcı
"bu kadar ucuzsa ciddi olamaz" diye okur. Fiyat bir kalite sinyalidir.

**Öneri:** İki ayrı ürün çizgisi düşün:
- **Studio** (tasarım + üretim): 7,5 $ — bugünkü Pro. Bireysel geliştirici.
- **Guard** (etki analizi + review + bot + gateway): 39–79 $/ay ekip başına.
  Asıl para burada.

Bugünkü tek çizgi, en değerli özelliği en ucuz pakete koyuyor.

### 🟡 Not: Token birimi kullanıcıya anlamlı değil

"200.000 token" bir geliştiriciye hiçbir şey ifade etmiyor. "Günde ~40 şema
üretimi" ya da "~200 değişiklik incelemesi" çok daha anlaşılır. Kota ekranı
ham token yerine **iş birimi** göstermeli.

## 1.4 Konumlandırma önerisi

Mevcut vaat şu an fiilen: *"AI ile veritabanı tasarla."* → Emtia.

**Önerilen vaat:** *"Veritabanı değişikliğini production'a göndermeden önce
neyin kırılacağını kanıtla."*

Neden bu daha iyi:
- Bir **acıya** bağlı (veri kaybı, gece 3'te geri alma), bir **isteğe** değil
- Ölçülebilir bir değeri var (önlenen kesinti)
- AI ajanlarının çözmediği yer — yani emtialaşmıyor
- Teknik olmayan onaylayıcıyı sürece sokuyor (CTO, ürün müdürü) → **bütçe sahibi**

Ana sayfa buna göre değişmeli: prompt kutusu **kalmalı** ama ikinci sıraya. İlk
ekranda görülmesi gereken şey bir **impact report** olmalı — "bu değişiklik 3
tabloyu, 12.400 satırı etkiliyor, geri alınamaz" gibi.

## 1.5 Hedef kitle — kim gerçekten öder?

| Segment | Ödeme isteği | Neden |
|---------|-------------|-------|
| Solo geliştirici / öğrenci | 🔴 Düşük | Bedava alternatif çok; ücretsiz katman yeterli |
| Küçük ekip (2–5), production'da | 🟢 **Yüksek** | Veri kaybı gerçek bir korku, DBA yok |
| Ajans / yazılım evi | 🟢 **Yüksek** | Çok müşteri, çok şema, tekrar eden iş |
| Kurumsal | 🟡 Orta | İster ama SSO/SOC2/on-prem şartı arar — henüz yok |

**Odak: küçük ekip + ajans.** DBA'sı olmayan ama production'ı olan ekipler.
Namines onların olmayan DBA'sı.

## 1.6 Riskler

| Risk | Şiddet | Not |
|------|--------|-----|
| **AI sağlayıcı bağımlılığı** | 🔴 Yüksek | Groq tek nokta. Model kaldırıldığında ürün durdu — bu zaten bir kez yaşandı. NaiCatalog soyutlaması doğru hamle ama ikinci bir sağlayıcı hiç denenmedi |
| **AI maliyeti marjı yiyor** | 🔴 Yüksek | 7,5 $'lık planda 200K token/gün = ayda 6M token. Sağlayıcı fiyatı artarsa marj negatife döner |
| **Bytebase/Atlas hızlı hareket eder** | 🟡 Orta | İkisi de AI ekliyor. Zaman avantajı sınırlı |
| **Tek geliştirici riski** | 🟡 Orta | Bakım, destek, güvenlik yaması hepsi tek kişide |
| **Kurumsal gereksinimler** | 🟡 Orta | SSO, SOC2, on-prem yok — en çok ödeyecek segment kapalı |
| **"AI yanlış şema üretti" itibar riski** | 🟢 Düşük | Deterministik kapı bunu büyük ölçüde kapatıyor — ürünün en güçlü tarafı |

## 1.7 İlk 90 gün için öneri

1. **Konumlandırmayı değiştir** (kod değil, metin işi — en ucuz, en yüksek etki)
2. **Fiyatlandırmayı düzelt** — Team koltuk başına
3. **MCP'yi npm'de yayınla** ve dağıtım kanalı olarak öne çıkar
4. **5 gerçek kullanıcı bul**, ücret alma, sadece izle — hangi katmanı
   kullandıklarını gör. A mı C mi kullanıyorlar? Bu, tüm stratejiyi belirler
5. **Tek bir vaka çalışması yaz**: "X ekibi şu değişikliği göndermeden önce
   Namines yakaladı." Bir tane gerçek hikâye, on sayfa özellik listesinden değerli

---

# BÖLÜM 2 — TASARIM

> Aşağıdakiler çalışan uygulamadan (localhost:3003, production build) DOM ve
> hesaplanmış stiller ölçülerek bulundu.

## 2.1 Doğru olanlar ✅

| Kontrol | Sonuç |
|---------|-------|
| Renk token'ları | `--color-accent: #3c4a6b`, `--color-focus-ring: #5b6b93`, `--color-danger: #b8544b`, `--color-success: #4b8a6f` — FRONTEND.md §2 ile birebir |
| Tipografi | Gövde `IBM Plex Sans`, başlık `JetBrains Mono` — §3 ile birebir |
| Metin kontrastı | Ölçülen tüm metinler WCAG AA'yı geçiyor |
| Yatay taşma (375px) | Yok. Dekoratif dalgalar 4000px ama `overflow-x` ile kontrol altında |
| Yasak renkler | Parlak indigo/mor yok, sarı/amber ailesi yok |

Tasarım sistemi **gerçekten uygulanmış** — çoğu projede bu doküman yazılır ve
uygulanmaz; burada uygulanmış.

## 2.2 🔴 Dokunma hedefleri — 18 elemanın 15'i kuralı ihlal ediyor

FRONTEND.md §6: *"Her interaktif eleman: min 44×44px dokunma alanı"* — ve bu
bölüm **"pazarlık edilemez"** başlığı altında.

Ölçülen:

| Eleman | Boyut | Gereken |
|--------|-------|---------|
| **Delete project** | 22×22 | 44×44 |
| **Close workspace** | 24×24 | 44×44 |
| **Log Out** | 26×26 | 44×44 |
| Yeni Proje | 100×28 | yükseklik 44 |
| Workspace / Team | ~119×32 | yükseklik 44 |

**En kötüsü "Delete project" (22×22).** Yıkıcı bir eylem, gereken alanın dörtte
birinde. Mobilde yanlışlıkla basılma olasılığı yüksek ve sonucu proje silmek.

> **Düzeltme ucuz:** görsel boyutu değiştirmeden `padding` veya bir
> `::after` ile tıklama alanını genişletmek yeterli. Görünüm aynı kalır,
> hedef büyür.

## 2.3 🟡 Ham hex sızıntısı — 29 adet

FRONTEND.md §4 açıkça diyor: *"Bu ihlaller TEMİZLENDİ... ham hex sayısı **0**.
Yeni kod da ham hex yazmamalı."*

Bugünkü sayı **29**:

| Dosya | Adet |
|-------|------|
| `TableEditorDrawer.tsx` | 9 |
| `ToastContainer.tsx` | 6 |
| `app/share/[token]/page.tsx` | 4 |
| `app/canvas/page.tsx` | 4 |
| diğer 4 dosya | 6 |

**Hepsi eşit derecede kötü değil:**
- `TABLE_COLORS` (TableEditorDrawer) — kullanıcıya sunulan renk paleti, yani
  **veri**. Token olması şart değil, savunulabilir.
- `ToastContainer` — `#4b8a6f`, `#b8544b`, `#e7e9ee` değerleri **zaten var olan
  token'ların birebir kopyası**. Bu gerçek bir sapma: token değişirse toast
  değişmez, sessizce ayrışır.

> **Asıl bulgu dokümanın kendisi:** FRONTEND.md "0" diyor, gerçek 29. Doküman
> gerçeği yansıtmıyor. Ya kod düzeltilmeli ya doküman — ama ikisi ayrı kalırsa
> doküman güvenilirliğini kaybeder ve kimse ona bakmaz.

## 2.4 🟡 `prefers-reduced-motion` — iki doküman çelişiyor

**FRONTEND.md §6:** *"`prefers-reduced-motion` saygı görür — yıldız/dalga
animasyonları bu medya sorgusunda durdurulmalı/sadeleştirilmeli (şu an eksik,
eklenmeli)"* — "pazarlık edilemez" başlığı altında.

**globals.css:301-309:** dalga/yıldız arka planının bundan **bilerek muaf**
tutulduğunu, kullanıcı talimatı olduğunu ve genel kuralın kaldırıldığını yazıyor.

Yani kodda `prefers-reduced-motion` **uygulanmıyor** ve gerekçesi yazılı. Ama
FRONTEND.md hâlâ "eklenmeli" diyor.

**Bu bir kod hatası değil, bir doküman çelişkisi.** İkisinden biri güncellenmeli.

**Değerlendirmem:** Koddaki gerekçe makul (arka planı tamamen dondurmak "bozuk"
görünüyor) ama **orta yol denenmemiş**: animasyonu durdurmak yerine yavaşlatmak
ya da genliğini azaltmak, hem kimliği korur hem vestibüler rahatsızlığı azaltır.
Vestibüler bozukluğu olan kullanıcılar için sürekli hareket eden bir arka plan
gerçek bir sorun — "kısa/sönük" olması bunu çözmüyor, çünkü arka plan sürekli.

## 2.5 🟡 Masaüstü-önce ürün, mobil vaat yok

Canvas React Flow üzerine kurulu — sürükle-bırak, çoklu seçim, bağlantı çizme.
Bunlar dokunmatikte pratikte çalışmaz.

Sayfa mobilde **taşmıyor** (teknik olarak doğru) ama **canvas mobilde
kullanılabilir değil.** Bu bir hata değil, bir konumlandırma sorusu:

- Mobilde ne vaat ediliyor? Hiçbir şey mi, salt-okunur mu?
- Şu an kullanıcı mobilde giriyor, düzgün bir sayfa görüyor, canvas'a giriyor
  ve kullanamıyor. **En kötü senaryo bu** — hiç girememekten daha kötü.

**Öneri:** Mobilde canvas yerine bilinçli bir salt-okunur görünüm
(`SchemaTextualSummary` zaten var) + "düzenlemek için masaüstü" mesajı. Dürüst
olmak, sessizce çalışmamaktan iyidir.

## 2.6 Ürün tasarımı — arayüzden bağımsız

### 🔴 Ana sayfa yanlış hikâyeyi anlatıyor
§1.4'te anlatıldı. İlk ekran "AI şema üretir" diyor; ürünün savunulabilir tarafı
"değişikliği kanıtlar". Vitrin ile depo farklı.

### 🟡 Netleştirme adımı çok iyi ama gizli
5 soruluk netleştirme akışı ürünün **en özgün parçalarından biri** ve rakiplerde
yok. Ama ancak prompt yazıp "Generate"e bastıktan sonra görünüyor. Ana sayfada
bunun bir vaat olarak görünmesi gerekir — "Sana ne istediğini soracağız,
tahmin etmeyeceğiz."

### 🟡 NAI model seçimi kullanıcıya bir iş yüklüyor
Üç model iyi bir sadeleştirme (sekizden çok daha iyi). Ama şu soru hâlâ
kullanıcıda: "flash mı normal mi?" Çoğu kullanıcı bilmez ve bilmek zorunda
değil.

**Öneri:** Varsayılanı otomatik seç (iş türü + karmaşıklığa göre — arketip
tespiti zaten var) ve model seçimini "gelişmiş" altına gizle. Kullanıcı
isterse ezer.

### 🟢 Kota şeffaflığı doğru yapılmış
Kendi hakkın + paylaşılan havuz birlikte gösteriliyor. Çoğu ürün ikincisini
gizler ve kullanıcı "neden kısıtlandım" diye anlamaz.

---

# BÖLÜM 3 — ÖZET

## En yüksek etkili 5 iş

| # | İş | Tür | Etki |
|---|-----|-----|------|
| 1 | **Konumlandırmayı C katmanına çevir** | metin | 🔴 Stratejik — ürünün fiyatını belirler |
| 2 | **Team fiyatlandırmasını düzelt** (koltuk başına) | karar | 🔴 Bugün mantıksız, satış engelliyor |
| 3 | **Dokunma hedeflerini 44px'e çıkar** | kod (küçük) | 🔴 Kendi "pazarlık edilemez" kuralı ihlal |
| 4 | **5 gerçek kullanıcı bul, izle** | araştırma | 🔴 A mı C mi kullanıyorlar — her şey buna bağlı |
| 5 | **MCP'yi npm'e yayınla** | hesap | 🟡 En ucuz dağıtım kanalı |

## Dürüst genel değerlendirme

**Güçlü:** Mühendislik kalitesi bu ölçekteki projelerin çok üstünde. Deterministik
kapı, 6 motorda gerçek DDL doğrulaması, golden-file disiplini, kararların
gerekçeleriyle yazılması — bunlar ciddi ekiplerde bile sık görülmez. Tasarım
sistemi yazılmış **ve uygulanmış**.

**Kırılgan:** Ürünün ne sattığı net değil ve en zayıf katman vitrinde. Fiyat,
iddiayla çelişiyor. Team planı bugün matematiksel olarak mantıksız. Tek AI
sağlayıcısına bağımlılık, bir kez yaşanmış bir arızanın tekrarını bekliyor.

**Tek cümle:** Teknik risk düşük, **pazar riski yüksek** — ve pazar riski kod
yazarak azalmıyor, konuşarak azalıyor. Sıradaki en değerli iş bir özellik değil,
beş kullanıcı görüşmesi.
