# Namines — UI/UX Ürün Denetimi

> Yöntem: **Anla → Denetle → Akışları çıkar → Sistemi tanımla → Belgele → Uygula → Test et**
> Bütün sayılar ölçüldü: kod tabanında `grep`, çalışan uygulamada `getComputedStyle`
> ve DOM sorguları. Tahmin yok.

---

## 0. Görev tanımıyla ürün arasındaki uyuşmazlık (önce bu)

Görev tanımı bir **prompt mimarisi ürünü** anlatıyor: "prompt oluşturma, prompt
organizasyonu, fikirler arası bağlantılar, yeniden kullanılabilir prompt
sistemleri, node kategorileri (Prompt / Instruction / Context / Variable)".

**Namines bu değil.** Namines bir **veritabanı şema tasarım aracı**:

| Görev tanımı diyor ki | Namines'te gerçekte ne var |
|---|---|
| Node = prompt bloğu | Node = **veritabanı tablosu** |
| Edge = fikirler arası bağlantı | Edge = **yabancı anahtar (FK)** |
| Prompt kütüphanesi / organizasyonu | **Yok** — prompt tek seferlik bir girdi |
| Görsel prompt yapısı | **Yok** — tuval şemayı gösteriyor, prompt'u değil |
| Node kategorileri | Tek tip node var; ayrım tablonun kendi içeriğinde |

Prompt, üründe **tek bir yerde** var: doğal dil → şema üretimi (iniş sayfası ve
tuvaldeki AI kutusu). Bir çıktı üretip kayboluyor; saklanan, düzenlenen,
bağlanan bir nesne değil.

**Bu yüzden görevin kendi kuralı uygulandı:**
> *"Only use concepts that actually exist in the product. Do not invent
> unnecessary product layers."*

Denetim gerçek ürün üzerinden yapıldı. "Prompt Library", "Prompt Node",
"Variable Node" gibi katmanlar **uydurulmadı** — bunlar ürünün çözdüğü
problemle ilgisiz ve eklenmeleri Phase 3'ün açıkça yasakladığı şey olurdu.

---

## 1. Product Summary

Namines, veritabanı şemasını **tasarlayıp doğrulayan** bir araç.

Çözdüğü problem: bir şema çizmek kolay, ama o şemanın **gerçekten çalışacağını**
bilmek zor — hedef motorda derlenecek mi, veri kaybettirecek mi, bir migrasyon
neyi kıracak? Namines'in iddiası tek cümlede: **"AI üretir, kural motoru
kanıtlar."**

Üretim AI ile (doğal dil, görsel, API şeması, kod), **doğrulama deterministik**:
linter, altı motorda DDL üretimi, motorlar arası kayıp analizi, değişiklik etki
analizi. Bulgular bir modelden değil, her seferinde aynı cevabı veren koddan
geliyor.

---

## 2. Product Mental Model

Kullanıcının anlaması gereken zincir:

```
Prompt/Şablon  →  ŞEMA  →  Kural motoru  →  DDL / Kod / Migrasyon
   (girdi)      (nesne)     (kanıt)          (çıktı)
```

**Merkezî nesne ŞEMA.** Diğer her şey ya ona girdi ya ondan türev:

| Kavram | Rol | Nerede yaşıyor |
|---|---|---|
| **Şema** | Birincil nesne | Tuval + store |
| **Tablo (node)** | Şemanın parçası | Tuval |
| **İlişki (edge)** | Tablolar arası FK | Tuval |
| **Proje** | Kaydedilmiş şema + konumlar | Workspace kenar çubuğu |
| **Branch** | Şemanın sürümü | Bilgi paneli |
| **Şablon** | Hazır şema (20 adet) | Galeri / demo |
| **Prompt** | Şema üretmenin **bir yolu** | İniş + tuval AI kutusu |
| **Motor** | Hedef veritabanı | Seçici |

Prompt **birincil değil, bir giriş kapısı**. Ürünün merkezinde şema var.

---

## 3. Current Information Architecture

```
/                 İniş — prompt kutusu + şablon vitrini + "neden biz"
/demo             Girişsiz canlı demo (şablon + gerçek linter + gerçek DDL)
/canvas           ANA ÇALIŞMA ALANI — şema düzenleme
/compile          Çıktı: DDL, EF Core, Prisma, Mermaid, README, Docker…
/review           Değişiklik istekleri listesi
/review/[id]      Tek değişiklik incelemesi
/share/[token]    Salt-okunur paylaşım
/join/[token]     Ekip daveti
```

8 rota · 16 Zustand store · 67 bileşen.

**Yapısal değerlendirme:** hiyerarşi mantıklı ve rota sayısı ürünün kapsamına
göre az — bu iyi. Sorun rotalarda değil, **tek bir rotanın (`/canvas`) her şeyi
aynı anda göstermesinde**.

---

## 4. UX Audit

### 🔴 KRİTİK — K1: Yeni kullanıcının gördüğü ilk ekran bir multiplayer hata mesajıydı

**Problem.** `/canvas`'a ilk kez gelen kullanıcı şunu görüyordu:

> **Room is Empty**
> `Room ID: room-27c6f79b-e0c1-46e4-80c9-4c78e744ac04`
> *We connected to the room, but there are no active peers online to share the schema.*

**Kök neden (ölçüldü).** `useMultiplayer`, oda kimliği yoksa **rastgele bir tane
üretip URL'e `pushState` ile yazıyor**. `canvas/page.tsx` bunu 500ms'lik bir
yoklamayla okuyor ve şema henüz `null` olduğu için `MultiplayerLoadingScreen`
açılıyor. Yani odayı **uygulamanın kendisi yaratıyor**, sonra "oda boş" diyor.

**Kullanıcı etkisi.** Kullanıcı çok oyunculu bir şey istemedi, "oda" kavramını
bilmiyor, ekranda ham bir UUID var ve kendisine hiç davet etmediği "peer"lardan
bahsediliyor. Asıl karşılama ekranı (`EmptyCanvasState`) **hiç gösterilmiyordu**.

**Ek kusur.** Oda kimliği olmasa bile `!schema` dalı `return null` yapıyordu —
store `schema: null` ile başladığı için yeni kullanıcı **bomboş bir sayfa**
görürdü.

**Çözüm (uygulandı).** Kullanıcının **varış URL'inden** ayrım yapılıyor:
`useState` başlatıcısı ilk render'da, herhangi bir `pushState`'ten önce okunuyor.
`?roomId=` ile gelinmişse gerçek bir davet var → bekleme ekranı. Aksi hâlde boş
bir şema kurulup normal tuval + `EmptyCanvasState` akışı çalışıyor.

**Uygulama alanı.** `app/canvas/page.tsx`

---

### 🔴 KRİTİK — K2: İki onboarding sistemi aynı anda yarışıyordu

**Problem.** K1 düzeltilince ortaya çıktı: yeni kullanıcı `/canvas`'a geldiğinde
`EmptyCanvasState` (4 somut başlangıç yolu) ile 4 adımlık `TourOverlay` **aynı
anda** açılıyor; tur ekranı karartıp asıl karşılama ekranının üstünü örtüyordu.

**Kullanıcı etkisi.** Ona ne yapacağını anlatan şeyin önüne, ona ne yapacağını
anlatan başka bir şey çıkıyor. İkisi de yarım okunuyor.

**Çözüm (uygulandı).** Tur artık **yalnızca tuvalde tablo varken** başlıyor.
Boş tuvalde doğru onboarding `EmptyCanvasState` — dört somut yol veriyor ve
kullanıcı hemen çalışmaya başlıyor. Tur ise araç çubuğu özelliklerini işaret
ediyor; işaret edilecek bir şema olduğunda anlamlı.

**Uygulama alanı.** `components/tour/TourOverlay.tsx`

---

### 🟠 YÜKSEK — Y1: Boş tuvalde 28 buton görünüyor

**Problem.** Sıfır tablolu bir tuvalde ölçüldü: **28 görünür buton**. Bunların
arasında "Alternative (~1 round)", "Request Review", "Approve →" gibi hiçbiri
0 tabloyla anlamlı olmayan eylemler var.

**Kullanıcı etkisi.** Phase 2'nin sorduğu soru birebir doğuyor: *"Bu düğme ne
yapıyor? Şimdi ne yapmam gerekiyor?"* Yeni kullanıcı, hangi eylemin şu an
mümkün olduğunu ayırt edemiyor.

**Çözüm (uygulandı).** Aşamalı açığa çıkarma: `Approve`, `Request Review` ve
`Alternative` yalnızca tuvalde tablo varken görünüyor. Gizlemek, devre dışı
bırakmaya tercih edildi — devre dışı bir düğme hâlâ görsel gürültü üretiyor ve
sebebini yalnızca üzerine gelince söylüyor.

**Ölçüm:** boş tuvalde görünür buton **28 → 21**; şema yüklenince **28**'e
geri dönüyor.

**Uygulama alanı.** `components/canvas/panels/ToolbarPanel.tsx`

---

### 🟠 YÜKSEK — Y2: Semantik landmark yok, `<h1>` yok

**Problem (ölçüldü).** `/canvas` sayfasında `<main>` **0**, `<nav>` **0**,
`<h1>` **0**. Başlık hiyerarşisi `<h2>` ile başlıyor.

**Kullanıcı etkisi.** Ekran okuyucu kullanıcısı içeriğe atlayamıyor,
navigasyonu ayırt edemiyor ve sayfanın adını duyamıyor. `layout.tsx`'te
`id="main-content"` bir `<div>` üzerinde — "skip to content" bağlantısı var ama
gittiği yer bir landmark değil.

**Çözüm (uygulandı).** `<div id="main-content">` → `<main id="main-content">`;
header'ın sol şeridi `<nav aria-label="Global">` içine alındı. Bu şerit
**global** gezinme; araç çubuğu (tuval eylemleri) ve bilgi paneli (proje içi)
ayrı katmanlar olduğu için bilinçli olarak `nav` yapılmadı — Phase 8'in üç
katmanı karıştırmama kuralı.

**Ölçüm:** `<main>` 0 → **1**, `<nav>` 0 → **1**.

**Kalan:** sayfa başına tek `<h1>` (tuval sayfasında hâlâ 0).

**Uygulama alanı.** `app/layout.tsx`, `components/layout/Header.tsx`

---

### 🟠 YÜKSEK — Y3: Odak halkası kaldırılmış, yerine bir şey konmamış

**Problem (ölçüldü).** Kod tabanında **41 `outline-none`**, buna karşılık
yalnızca **10 `focus-visible`**.

**Kullanıcı etkisi.** Klavyeyle gezen kullanıcı çoğu kontrolde nerede olduğunu
göremiyor. FRONTEND.md §6 bunu "pazarlık edilemez" diyor; kural yazılmış ama
uygulanmamış.

**Kısmi çözüm (uygulandı).** `@utility focus-ring` eklendi ve `Button`
primitifinde varsayılan. Kalan 41 kullanımın migrasyonu sürüyor.

---

### 🟡 ORTA — O1: 143 toast çağrısı

**Problem (ölçüldü).** `showToast(` **143** yerde. Görev dokümanı: *"Avoid
excessive toast notifications."*

**Kullanıcı etkisi.** Her şey aynı kanaldan bildirilince önemli olan (veri kaybı
uyarısı) ile önemsiz olan (mod değişti) aynı ağırlıkta görünüyor; kullanıcı
toast'ları okumayı bırakıyor.

**Öneri.** Geri bildirim kanalı eyleme göre seçilmeli: kalıcı durum → durum
göstergesi (ör. "Kaydedildi" başlıkta), anlık onay → buton içi durum,
gerçekten kesintiye uğratması gereken → toast.

---

### 🟡 ORTA — O2: Yükleme deneyimi tamamen spinner

**Problem (ölçüldü).** **83 `Loader2`** kullanımı, **0 skeleton**.

**Kullanıcı etkisi.** Spinner "bir şey oluyor" der ama "ne geleceğini" söylemez;
liste ve panel yüklemelerinde algılanan süreyi uzatır ve içerik gelince yerleşim
kayar.

**Öneri.** Yapısı bilinen yüzeylerde (proje listesi, şablon ızgarası, bulgu
paneli) skeleton; süresi belirsiz tek işlemlerde spinner kalabilir.

---

### 🟡 ORTA — O3: Tipografi ölçeğinin yarısı ölçek dışı

**Problem (ölçüldü).** Ölçek tabanlı kullanım **492**, keyfi `text-[Npx]`
**509** — yani **%51 ölçek dışı**. Bunların 75'i 10px'in altındaydı.

**Kısmi çözüm (uygulandı).** 9 adımlık ölçek `@utility` olarak tanımlandı;
okunamayan 75 kullanım `text-micro` (11px) tabanına taşındı ve denetim tabanın
altını kalıcı olarak reddediyor. Kalan 414 `text-[10px]`/`text-[11px]`
migrasyonu bekliyor.

---

### 🟢 DÜŞÜK — D1: `components/ui` primitiflerini kullanan yok

**Problem (ölçüldü).** `Button` ve `Container` primitifleri var ama **0 dosya**
kullanıyor; 286 buton hâlâ el yazması.

**Not.** Bu bir kullanıcı hatası değil, bakım borcu — ama düzeltilmezse tutarlılık
kazanımları zamanla erir.

---

## 5. User Flows

### Flow A — Yeni kullanıcı (DÜZELTİLDİ)

```
ÖNCE:  /canvas → "Room is Empty" + ham UUID → çıkış
SONRA: /canvas → "CREATE YOUR DATABASE SCHEMA"
                 → AI ile üret | Görselden | Şablonlar | Sıfırdan
```

### Flow B — Prompt'tan şema

```
İniş prompt kutusu → Netleştirme soruları (AI'sız, kotasız)
→ Plan önizleme → Üretim (canlı adımlar) → Tuval
```
Netleştirme adımı bilinçli: tek cümleden şema üretmek, modelin boşlukları kendi
doldurması demekti ve kullanıcı hatayı ancak tuvalde görüyordu.

### Flow C — Şablondan şema

```
Galeri (ölçek süzgeci: mini / full / enterprise)
→ Replace veya Merge → Tuval
```
20 şablon, 384 tablo; hepsi ürünün kendi kural motorundan geçiyor.

### Flow D — Düzenleme

```
Tuval → tablo seç → çift tık / sağ tık → düzenle
→ Ctrl+Z geri al → otomatik kaydet
```

### Flow E — Büyük şema (40 tablo, 83 ilişki)

Mevcut destek: minimap, zoom denetimleri, `fitView` (minZoom korumalı),
Ctrl+F arama, komut paleti (⌘K), tablo listesi.
**Bu alan iyi durumda** — büyük şemalar gerçekten gezilebiliyor.

---

## 6. Navigation Architecture

Üç katman **ayrı tutulmalı** (görev Phase 8):

| Katman | Ne için | Nerede |
|---|---|---|
| **Global** | Alanlar arası (Workspace, Team, proje) | Üst başlık |
| **Bağlamsal** | Proje içi (branch, sürüm) | Bilgi paneli |
| **Araç** | Aktif tuval eylemleri | Araç çubuğu |

**Mevcut durum:** ayrım büyük ölçüde doğru. Zayıf nokta araç çubuğu — Y1'de
anlatıldığı gibi birincil, ikincil ve gelişmiş eylemler aynı şeritte.

---

## 7. Canvas UX Analysis

**İyi olan:** sonsuz tuval, nokta ızgarası, minimap, zoom denetimleri, çoklu
seçim, bağlam menüsü, klavye kısayolları, `useNodesInitialized` ile doğru
`fitView`, mobilde yatay kaydırılan araç çubuğu.

**Bu turda düzelen:** React Flow'un kendi `#141414` zemini palete bağlandı
(26 `--xy-*` değişkeni); tuval artık uygulamayla aynı yüzey ailesinde.

**Kalan:** Y1 (boş tuvalde eylem gürültüsü).

---

## 8. Node System

Tek node tipi var: **tablo**. Görev dokümanının önerdiği kategoriler
(Prompt / Instruction / Variable) **uygulanmadı** — üründe karşılıkları yok.

Node anatomisi:

```
Başlık   → tablo adı · kolon sayısı · DBA rozeti · düzenle
Gövde    → kolon satırları (PK/FK ikonu · ad · tip · NULL)
Bağlantı → her satırın iki yanında handle
```

Ölçüler bu turlarda sıkılaştırıldı: genişlik **320 → 288px**, satır
**38 → 32px**, radius artık `--radius-card` (10px). Ayrım renkle değil
**ikon + kenarlıkla** yapılıyor (PK anahtar ikonu, FK zincir ikonu) — görev
dokümanının "avoid assigning different colors to everything" kuralıyla uyumlu.

---

## 9. Design Direction

**Kimlik:** "veritabanı mühendisliği için sakin, hassas bir alet."

Koyu, tam nötr gri yüzeyler; tek bir desatüre camgöbeği vurgu; gradyan yok,
ışıma yok, cam efekti yalnızca iki yüzeyde. Yıldız/dalga arka planı marka
kimliği olarak korunuyor ama yalnızca iniş sayfasında.

Kaçınılanlar (görev Phase 26 ile birebir): jenerik AI moru, neon, aşırı
glassmorphism, dev yuvarlak kartlar, her şeyin bir kartın içinde olması.

---

## 10. Color System

Tek merkez: `app/globals.css` `:root`. **On knob** her rengi belirliyor;
başka hiçbir yerde ham renk yok (denetimle zorlanıyor).

```css
--ui-hue: 250;  --ui-chroma: 0;      --ui-bg-l: 34%;  --ui-bg-step: 3.6%;
--brand-hue: 195;  --brand-chroma: 0.09;  --brand-l: 48%;
--danger-hue: 25;  --success-hue: 155;
```

Semantik roller: `surface-900…600` (zemin) · `content-primary…subtle` (metin) ·
`accent` / `accent-hover` / `accent-text` / `accent-subtle` (vurgu) ·
`danger` / `success` / `warning` (durum) · `scrim` (karartma) ·
`border-hairline` / `border-strong` (kenarlık).

Metin adımları **zemine göre** türetiliyor; `--ui-bg-l` %17-%36 aralığının
tamamında her token WCAG AA üstünde (tarayıcıda ölçüldü, en düşük 4.65:1).

**Tema kararı: yalnızca koyu.** Namines uzun oturumlu bir IDE; tuval
uygulamanın en büyük yüzeyi ve koyu zemin şema okumayı kolaylaştırıyor.
`--ui-bg-l` knob'u isteyene açık yönü zaten mümkün kılıyor.

---

## 11. Typography

| Token | Boyut | Ağırlık | Satır Y. | Tracking |
|---|---|---|---|---|
| `text-display` | 40px | 400 | 1.1 | −0.03em |
| `text-h1` | 30px | 400 | 1.2 | −0.025em |
| `text-h2` | 24px | 450 | 1.25 | −0.02em |
| `text-h3` | 18px | 500 | 1.35 | −0.01em |
| `text-body-lg` | 16px | 400 | 1.6 | — |
| `text-body` | 14px | 400 | 1.55 | — |
| `text-label` | 13px | 500 | 1.4 | — |
| `text-caption` | 12px | 400 | 1.4 | — |
| `text-micro` | 11px | 500 | 1.3 | +0.02em |

İki kural: **boyut büyüdükçe ağırlık düşer** (büyük metin zaten dikkat çekiyor;
kalınlık gürültü), ve **11px altına inilmez** (denetimle zorlanıyor).

Fontlar: IBM Plex Sans (arayüz) · JetBrains Mono (başlık, tablo/kolon adı, kod).

---

## 12. Spacing

4px tabanlı: `4 · 8 · 12 · 16 · 24 · 32 · 48 · 64 · 96`
(Tailwind: `1 · 2 · 3 · 4 · 6 · 8 · 12 · 16 · 24`)

Bölüm ritmi: mobil `py-12`, masaüstü `py-24`. Ara basamaklar (`p-5`, `p-3.5`)
mevcut kodda yaygın; yeni kodda kullanılmıyor.

---

## 13. Borders & Radius

**Radius — üç değer** (565 keyfi kullanım taşındı, ölçek dışı 0):

| Token | Değer | Kullanım | Adet |
|---|---|---|---|
| `--radius-control` | 6px | buton, input, rozet | 392 |
| `--radius-card` | 10px | kart, tablo düğümü, panel | 132 |
| `--radius-modal` | 14px | modal, drawer | 46 |
| `rounded-full` | — | **tek istisna:** avatar, nokta | 73 |

**Kenarlık — tek değer:** `--color-border-hairline` (%12 off-white).
Vurgu gerektiğinde `--color-border-strong` (%20).

---

## 14. Component System

| Bileşen | Durum | Gerekçe (ölçüm) |
|---|---|---|
| `Button` | Var, **benimsenmedi** | 286 el yazması buton |
| `Container` | Var, **benimsenmedi** | 23 farklı `max-w-*` |
| `Card` | Önerildi | Kart deseni 40+ yerde |
| `Input` | Önerildi | `glass-input` + 20+ el yazması |
| `Badge` | Önerildi | Rozet deseni 30+ yerde |

**Eklenmeyecek:** `Tabs`, `Accordion`, `Tooltip` — projede tekrar etmiyorlar;
eklemek görev dokümanının yasakladığı gereksiz soyutlama olur.

---

## 15. Interaction Patterns

| Etkileşim | Davranış |
|---|---|
| Tabloya çift tık | İşlem popover'ı (düzenle / DBA / kopyala) |
| Sağ tık | Bağlam menüsü |
| Handle sürükle | FK ilişkisi kur |
| ⌘K | Komut paleti |
| Ctrl+F | Tuval araması |
| Ctrl+Z / Ctrl+Shift+Z | Geri / ileri al |
| `?` | Kısayol yardımı |
| Ok tuşları | Seçili tabloyu 10px kaydır |

Kısayollar tarayıcı geleneğiyle çakışmıyor. Yıkıcı eylemler (tablo silme)
onay diyaloğundan geçiyor ve geri alınabiliyor.

---

## 16. Motion System

| Token | Süre | Kullanım |
|---|---|---|
| `--dur-fast` | 120ms | hover, focus |
| `--dur-base` | 180ms | açılır menü, geçiş |
| `--dur-slow` | 280ms | modal giriş/çıkış |
| `--ease-out` | `cubic-bezier(0.2, 0, 0, 1)` | varsayılan |

Yalnızca `opacity`, `transform`, `background-color`, `border-color`
animasyonlanıyor. `prefers-reduced-motion` UI geçişlerini kapatıyor;
**istisna** yıldız/dalga arka planı (marka kimliği, durdurulduğunda arka plan
hata gibi görünüyordu).

---

## 17. Responsive Strategy

| Aralık | Davranış |
|---|---|
| < 640 | Tek sütun; seçiciler yatay şerit; araç çubuğu başlık altında; minimap gizli; bilgi paneli tek satır |
| 640–1023 | İki sütun ızgara; araç çubuğu **hâlâ şerit** (tablet düzeltmesi) |
| 1024–1279 | Masaüstü yerleşimi; `--w-app` (1200px) |
| ≥ 1280 | İçerik `--w-app`'e genişler; tipografi bir adım büyür |

`h-screen` yerine `h-dvh`; dokunma hedefleri `min-h-11` (44px).

---

## 18. Accessibility

**Karşılanan:** kontrast (ölçüldü, tüm metin token'ları AA üstü) · dokunma
hedefi 44×44 (`tap-44`) · `prefers-reduced-motion` · ikon-tek butonlarda
`aria-label` (2 eksik) · onay diyalogları · klavye kısayolları.

**Açık:** Y2 (landmark + `<h1>` yok) · Y3 (41 `outline-none` vs 10
`focus-visible`).

---

## 19. Performance

**İyi:** `useSchemaStore`'da dar selector'lar (selector'suz abonelik sürükleme
sırasında her tabloyu yeniden render ediyordu) · node pozisyonu aboneliksiz
okunuyor (O(n²) → O(n)) · diff modunda `animate-pulse` kaldırıldı (20 tabloda
20 sonsuz animasyon).

**İzlenecek:** 58 `backdrop-blur` kullanımı — blur her karede yeniden
hesaplanıyor. Vercel'in header'ında ölçülen: `backdrop-filter: none`.
Header'a blur **eklenmedi**; mevcut kullanımlar statik katmanlarda.

---

## 20. File-Level Implementation Plan

| Dosya | Değişiklik | Sebep |
|---|---|---|
| `app/canvas/page.tsx` | Varış URL'inden oda ayrımı; boş şema kurulumu; yükleme durumu | **K1** — ilk ekran multiplayer hatasıydı |
| `components/tour/TourOverlay.tsx` | Tur yalnızca tablo varken başlıyor | **K2** — çift onboarding |
| `app/globals.css` | Radius/tipografi/motion/layout token'ları; kırık token onarımı | Ölçek yokluğu |
| `scripts/check-design-rules.mjs` | Tanımsız token · okunamayan font · ölçek dışı radius kuralları | Kural uygulanmıyordu |
| `components/ui/Button.tsx` | **Yeni** | 286 el yazması buton |
| `components/ui/Container.tsx` | **Yeni** | 23 farklı genişlik |
| `components/canvas/**` (29 dosya) | 280 radius → token | Tutarsız radius |
| `components/canvas/nodes/TableNode.tsx` | 288px, 32px satır, `--radius-card` | Büyük şemada okunabilirlik |
| `app/layout.tsx` | `<main>` landmark | **Y2** (bekliyor) |
| `components/canvas/panels/ToolbarPanel.tsx` | Aşamalı açığa çıkarma | **Y1** (bekliyor) |

---

## 21. UX Decisions & Rationale

**Karar: "Room is Empty" ekranı yalnızca gerçek davete gösteriliyor.**
*Ne değişti:* ayrım `urlRoomId`'den **varış URL'ine** taşındı.
*Neden:* oda kimliğini uygulamanın kendisi üretiyordu; "URL'de roomId var mı"
sorusu "kullanıcı paylaşılan odaya mı katıldı" sorusunun cevabı değildi.
*Hangi problemi çözüyor:* K1 — ürünün ilk ekranı bir iç mekanizma hatasıydı.

**Karar: Tur, boş tuvalde başlamıyor.**
*Neden:* `EmptyCanvasState` zaten dört somut başlangıç yolu veriyor; tur ise
araç çubuğunu işaret ediyor ve işaret edilecek şey yokken anlamsız.
*Hangi problemi çözüyor:* K2 — iki onboarding birbirinin üstünü örtüyordu.

**Karar: Prompt katmanı ÜRETİLMEDİ.**
*Ne değişti:* hiçbir şey — "Prompt Library", "Prompt Node", "Variable Node"
eklenmedi.
*Neden:* Namines'te prompt saklanan bir nesne değil, şema üretmenin bir yolu.
Bu katmanları eklemek, kullanıcının zihin modeline ürünün çözmediği bir problemi
sokmak olurdu.
*Hangi kural:* Phase 3 — *"Only use concepts that actually exist in the product."*

**Karar: Açık tema yapılmadı.**
*Neden:* tuval uygulamanın en büyük yüzeyi ve koyu zemin uzun oturumda şema
okumayı kolaylaştırıyor. `--ui-bg-l` knob'u yönü zaten açık bırakıyor.

**Karar: Node kategorileri (renk kodlaması) eklenmedi.**
*Neden:* tek node tipi var (tablo). Ayrım tablonun içinde: PK/FK ikonları,
NULL rozetleri, DBA uyarı sayacı. Görev dokümanı: *"Avoid assigning different
colors to everything."*

---

## 22. Bu turda uygulananlar ve doğrulama

| # | Bulgu | Durum | Ölçüm |
|---|---|---|---|
| **K1** | İlk ekran multiplayer hatasıydı | ✅ Düzeltildi | "Room is Empty" → **yok**; karşılama ekranı → **var** |
| **K2** | İki onboarding yarışıyordu | ✅ Düzeltildi | Boş tuvalde tur → **kapalı**; şema gelince → **açılıyor** |
| **Y1** | Boş tuvalde 28 buton | ✅ Düzeltildi | **28 → 21**; şema gelince 28'e dönüyor |
| **Y2** | Landmark ve `<h1>` yok | 🟡 Kısmen | `<main>` 0→1, `<nav>` 0→1; `<h1>` bekliyor |
| **Y3** | Odak halkası kaldırılmış | 🟡 Kısmen | `focus-ring` utility + `Button` varsayılanı; 41 kullanım bekliyor |
| **O1** | 143 toast | ⏳ Belgelendi | Kanal seçimi önerisi §4/O1 |
| **O2** | 0 skeleton, 83 spinner | ⏳ Belgelendi | Öneri §4/O2 |
| **O3** | Tipografi %51 ölçek dışı | 🟡 Kısmen | 75 okunamayan → `text-micro`; 414 bekliyor |
| **D1** | `ui/` primitifleri benimsenmedi | ⏳ Belgelendi | 0 dosya kullanıyor |

**Regresyon kontrolü** (hepsi çalışan uygulamada):

| Kontrol | Sonuç |
|---|---|
| `npx tsc --noEmit` | ✅ temiz |
| `npm run check:design` | ✅ temiz (5 kural) |
| `npm run check:templates` | ✅ 20/20 şablon, 384 tablo, 6 motor |
| `npm run check:e2e` | ✅ **30/32 çalışıyor, 0 kırık** |

Kalan 2 e2e kalemi kodla ilgili değil: Stripe fiyat kimlikleri girilmemiş ve
şema üretimi Groq ücretsiz katmanının TPM duvarına çarpıyor.

### Bilinçli olarak yapılmayanlar

- **Prompt katmanı** (Library / Prompt Node / Variable Node) — §0'daki gerekçe.
- **Node renk kategorileri** — tek node tipi var; ayrım ikonla yapılıyor.
- **Açık tema** — §10'daki gerekçe.
- **Header'a backdrop blur** — her scroll karesinde yeniden hesaplanıyor;
  Vercel'in header'ında ölçülen de `backdrop-filter: none`.
- **Tipografi migrasyonunun tamamı** — 414 kullanım tek commit'te taşınırsa
  diff gözden geçirilemez ve görsel regresyon denetlenemez hâle gelir.
  Denetim şu an tabanın altına inilmesini engelliyor; ölçek kademeli tamamlanacak.
