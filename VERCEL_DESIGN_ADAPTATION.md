# Vercel-Inspired Design Adaptation — Namines

> Kaynak görev: `Vercel-Inspired UI-UX Analysis & Project Adaptation Task.md`
> Bu doküman yeniden tasarımın **tek doğruluk kaynağı**.
> Ölçümler `https://vercel.com` üzerinde canlı `getComputedStyle` ile alındı;
> proje ölçümleri kod tabanı üzerinde `grep` sayımlarıyla yapıldı.

---

## 1. Executive Summary

Namines zaten bir tasarım sistemine sahip: OKLCH tabanlı, tek merkezden
(`app/globals.css`) türetilen 32 renk token'ı ve bunu zorlayan bir denetim
betiği (`npm run check:design`). **Renk katmanı sağlam.** Sorun renkte değil,
**renk dışındaki her katmanda ölçek yokluğunda**.

Ölçüldü:

| Katman | Durum |
|---|---|
| Renk | ✅ 32 token, tek merkez, denetimli |
| Tipografi | ❌ 509 keyfi `text-[Npx]` kullanımı — ölçeğin dışında |
| Radius | ❌ 8 farklı değer eşzamanlı kullanımda |
| Buton | ❌ 286 buton, **sıfır** paylaşılan primitif |
| Container | ❌ 23 farklı `max-w-*`, `Container` bileşeni yok |
| Focus | ❌ 41 `outline-none`, yalnızca 10 `focus-visible` |
| Büyük ekran | ❌ `xl:` yalnızca 2 kez — 1920px+ için hiçbir uyarlama yok |

Yeni yön: **"Vercel'in ölçek disiplinini Namines'in kendi kimliğine getirmek."**
Renk kimliği (koyu, nötr gri, camgöbeği vurgu) **korunuyor**; değişen şey
ölçeklerin sayısı ve zorlanması.

---

## 2. Current Project UI Audit

### 2.1 Ölçülen sorunlar

**Tipografi — ölçek pratikte yok**

```
text-xs      332      text-[10px]   225
text-sm      121      text-[11px]   189
text-base     16      text-[9px]     73
text-2xl      10      text-[12px]    14
text-lg        8      text-[13px]     5
text-xl        3      text-[9.5px]    1
text-4xl       1      text-[8px]      1
text-3xl       1      text-[14px]     1
─────────────────    ──────────────────
ölçek: 492           keyfi: 509
```

Kullanımın **%51'i ölçeğin dışında**. Dahası `text-[8px]` ve `text-[9px]`
(74 kullanım) okunabilirlik alt sınırının altında — 11px genel kabul gören
taban.

**Radius — sekiz eşzamanlı değer**

`rounded-lg` 283 · `rounded-xl` 132 · `rounded-full` 73 · `rounded-md` 55 ·
`rounded` 54 · `rounded-2xl` 43 · `rounded-sm` 3 · `rounded-3xl` 3

Görev dokümanının "AI üretimi hatalar" listesinde birebir geçen madde:
*"Multiple unrelated border-radius values"*.

**Buton — 286 el yazması, sıfır primitif**

`components/ui/` dizini yok. Aynı birincil buton şu varyasyonlarla yazılmış:
`rounded-lg`/`rounded-xl`, `py-2`/`py-2.5`/`py-3`, `text-xs`/`text-sm`,
`font-semibold`/`font-bold`. Bir buton değiştirmek 286 dosyaya dokunmak demek.

**Layout — Container primitifi yok**

23 farklı `max-w-*` değeri. Sayfalar kendi genişliğini seçiyor: iniş sayfası
`max-w-4xl`, demo `max-w-6xl`, modal `max-w-2xl`. Aralarında ilişki yok.

**Erişilebilirlik — focus görünürlüğü kırık**

41 `outline-none` var, yalnızca 10 `focus-visible`. Yani odak halkası
çoğunlukla **kaldırılmış ama yerine bir şey konmamış** — klavye kullanıcısı
nerede olduğunu göremiyor. FRONTEND.md §6 bunu "pazarlık edilemez" diyor.

**Büyük ekran — hiç ele alınmamış**

`xl:` (1280px+) yalnızca **2 kez** kullanılmış. 1920px ve 2560px'te içerik
aynı `max-w-4xl` içinde kalıp ortada dar bir şerit hâline geliyor; kullanıcının
"monitörümde her şey çok büyük" geri bildiriminin yapısal karşılığı bu.

### 2.2 Aktif regresyon (bu denetimde bulundu)

Bir önceki commit'te (`6bb058d`) kullanılmıyor sanılan 15 token silinmişti.
Silme öncesi arama **yalnızca `.tsx`/`.ts`** dosyalarında yapıldı,
`globals.css`'in kendisinde yapılmadı. Sonuç: **5 token, 9 kullanım kırık**:

| Token | Kullanım | Etkilenen |
|---|---|---|
| `--color-ink` | 4 | `.glass-input` metin rengi, kod blokları |
| `--color-bg-elevated` | 2 | Prism kod teması zemini |
| `--color-bg-surface` | 1 | Kod bloğu zemini |
| `--color-line` | 1 | Kod bloğu kenarlığı |
| `--shadow-glass` | 1 | `.glass-panel` gölgesi |

Tanımsız `var()` sessizce miras alınan değere düşüyor — hiçbir hata vermiyor.
Bu, denetim betiğinin kendi kör noktasının aynısı: **bir kuralı yalnızca bir
dosya türünde uygulamak, kuralın uygulandığı yanılsaması üretiyor.**

---

## 3. Vercel Design Analysis

Ölçümler canlı sayfada, 1600×900 ve 967px genişliklerde alındı.

### 3.1 Renk

```
--ds-gray-100   hsla(0, 0%, 95%, 1)
--ds-gray-400   hsla(0, 0%, 92%, 1)
--ds-gray-700   hsla(0, 0%, 56%, 1)
--ds-gray-1000  hsla(0, 0%,  9%, 1)
```

**Doygunluk her basamakta tam sıfır.** Vercel'in nötrleri "hafif mavimsi gri"
değil, **saf gri**. Renk yalnızca anlam taşıdığı yerde beliriyor (link, hata,
marka) — yüzeylerde asla.

463 CSS değişkeni var ama **rol katmanı dar**: `background-100/200`,
`gray-100…1000`, `gray-alpha-100…1000`. Bileşenler bu rollerden okuyor.

### 3.2 Tipografi

| Element | Boyut | Ağırlık | Satır Y. | Harf aralığı |
|---|---|---|---|---|
| H1 | 48px | **400** | 56px (1.17) | −2.88px (−0.06em) |
| H2 | 32px | **450** | 40px (1.25) | −1.6px (−0.05em) |
| H3 (UI) | 14px | **500** | 20px (1.43) | normal |
| Body | 16px | 400 | — | normal |

İki kural görülüyor:

1. **Boyut büyüdükçe ağırlık DÜŞÜYOR** (48px→400, 32px→450, 14px→500).
   Alışılmışın tersi. Büyük başlıkta kalınlık gürültü yapıyor; hiyerarşi
   boyutla kuruluyor, ağırlıkla değil.
2. **Negatif harf aralığı boyutla ölçekleniyor** (sabit px değil, ~−0.05em).
   Büyük metin optik olarak seyrek görünür; tracking bunu düzeltiyor.

### 3.3 Boşluk

Ölçülen dolgu ve boşluk değerleri: `4 · 8 · 12 · 16 · 20 · 24 · 40`

**4px tabanlı, keyfi değer yok.** 24 ile 40 arasında sıçrama var — ara
değerler (28, 32, 36) hiç kullanılmıyor. Az sayıda, uzak aralıklı basamak.

### 3.4 Layout ve Grid

- Dış container: **1400px** max
- İç okuma sütunu: **~960px**
- Header: **64px**, `position: sticky`, zemin **şeffaf**, `backdrop-filter: none`

Header'da blur yok — bu bilinçli: blur her scroll karesinde yeniden
hesaplanan pahalı bir filtre.

### 3.5 Kenarlık ve Radius

- Kenarlık: **tek değer** — `1px rgba(0, 0, 0, 0.08)`
- Radius: **üç değer** — `6px` (kontroller) · `8px` · `12px` (kartlar)
- `--geist-radius: 6px` tek token olarak tanımlı

### 3.6 Butonlar

Ölçülen: yükseklik **40px**, radius **6px**, yatay dolgu **12px**,
ağırlık 400. Tek bir yükseklik, tek bir radius — varyasyon yalnızca
zemin/kenarlıkta.

### 3.7 Kartlar

Zemin nötr, kenarlık tek hairline, radius 12px, gölge yok (yükseklik
kenarlıkla ifade ediliyor, gölgeyle değil).

### 3.8 Motion

Hover geçişleri kısa ve yalnızca `opacity`/`transform`/`background` üzerinde.
Sürekli çalışan arka plan animasyonu yok.

---

## 4. What We Will NOT Copy

| Vercel'de var | Neden almıyoruz |
|---|---|
| **Açık (beyaz) tema varsayılanı** | Namines bir veritabanı IDE'si; uzun oturumlarda koyu tema tercih ediliyor ve mevcut kimlik (kayan yıldız + dalga arka planı) koyu zemine bağlı |
| **Geist font ailesi** | Lisans/ağırlık maliyeti; IBM Plex Sans + JetBrains Mono zaten kurulu ve FRONTEND.md §3'te gerekçelendirilmiş |
| **48px hero başlığı** | Namines'in iniş sayfası bir pazarlama sayfası değil, doğrudan bir araç girişi (prompt kutusu). Dev başlık, asıl eylemi aşağı iter |
| **Mega menü navigasyonu** | Namines'in bilgi mimarisi düz: Workspace / Team / proje. Mega menü uydurma karmaşıklık olur |
| **Marka gradyanları / ışıma** | FRONTEND.md §2 gradyan ve glow'u açıkça yasaklıyor; görev dokümanı da "excessive glowing" diyor |
| **Tam beyaz `#fff` yüzeyler** | §2 saf değerleri yasaklıyor |

---

## 5. Our Adapted Design Direction

Vercel'den alınan **ilke**, kopyalanan değer değil:

| Vercel ilkesi | Namines uyarlaması |
|---|---|
| Nötr gri, doygunluk sıfır | `--ui-chroma: 0` — zaten bu tura geçildi |
| Az sayıda, uzak aralıklı basamak | Radius 8→3, tipografi ölçeği 8 adım |
| Boyut büyüdükçe ağırlık düşer | Display/H1'de `font-weight: 400`, negatif tracking |
| Tek kenarlık değeri | `--border-hairline` tek token |
| Yükseklikle değil kenarlıkla ayrım | Gölge yalnızca gerçekten yüzen katmanda (modal) |
| Sabit container ölçeği | `Container` primitifi: `--w-prose` / `--w-app` / `--w-wide` |
| Kontrollerde tek yükseklik | Buton yükseklik ölçeği: 32 / 36 / 44 |

---

## 6. Color Palette

Renk katmanı **değişmiyor** — §11'de kurulan OKLCH merkezi zaten Vercel'in
"tek kaynak + rol katmanı" ilkesini karşılıyor. Yalnızca kırık 5 token
onarılıyor ve nötr gri (`--ui-chroma: 0`) korunuyor.

```css
--ui-hue: 250;      --ui-chroma: 0;     --ui-bg-l: 34%;   --ui-bg-step: 3.6%;
--brand-hue: 195;   --brand-chroma: 0.09;   --brand-l: 48%;
--danger-hue: 25;   --success-hue: 155;
```

Yüzeyler `surface-900…600`, metin `content-primary…subtle`, kenarlık
`surface-500/400`, vurgu `accent`/`accent-hover`/`accent-text`/`accent-subtle`.

---

## 7. Typography System

Yeni ölçek — **her adım bir token, keyfi `text-[Npx]` yasak**:

| Token | Boyut | Ağırlık | Satır Y. | Tracking | Kullanım |
|---|---|---|---|---|---|
| `text-display` | 40px | 400 | 1.1 | −0.03em | Yalnızca iniş hero |
| `text-h1` | 30px | 400 | 1.2 | −0.025em | Sayfa başlığı |
| `text-h2` | 24px | 450 | 1.25 | −0.02em | Bölüm başlığı |
| `text-h3` | 18px | 500 | 1.35 | −0.01em | Alt bölüm |
| `text-body-lg` | 16px | 400 | 1.6 | normal | Giriş paragrafı |
| `text-body` | 14px | 400 | 1.55 | normal | Gövde, varsayılan |
| `text-label` | 13px | 500 | 1.4 | normal | Form etiketi, buton |
| `text-caption` | 12px | 400 | 1.4 | normal | Yardımcı metin |
| `text-micro` | 11px | 500 | 1.3 | 0.02em | Rozet, tablo meta — **taban** |

**Kural:** 11px altına inilmez. Mevcut `text-[9px]`/`text-[8px]` (74 kullanım)
`text-micro`'ya yükseltilir.

**Kural:** boyut büyüdükçe ağırlık düşer (Vercel §3.2 ilkesi).

---

## 8. Spacing System

4px tabanlı, Vercel'in ölçülen basamakları:

```
4 · 8 · 12 · 16 · 24 · 32 · 48 · 64 · 96
```

Tailwind karşılığı: `1 · 2 · 3 · 4 · 6 · 8 · 12 · 16 · 24`.

`p-5` (20px), `p-3.5` (14px), `p-2.5` (10px) gibi ara basamaklar mevcut kodda
yaygın; **yeni kodda kullanılmaz**, migrasyon sırasında en yakın basamağa
yuvarlanır. Bölüm ritmi: mobil `py-12`, masaüstü `py-24`.

---

## 9. Layout & Grid

| Token | Değer | Kullanım |
|---|---|---|
| `--w-prose` | 720px | Okuma metni, form odaklı sayfa |
| `--w-app` | 1200px | Varsayılan uygulama içeriği |
| `--w-wide` | 1440px | Tablo/canvas gibi geniş içerik |

Breakpoint'ler (Tailwind varsayılanı korunuyor):
`sm 640` · `md 768` · `lg 1024` · `xl 1280` · `2xl 1536`

**Yeni kural:** `xl:` ve `2xl:` artık kullanılıyor — büyük monitörde içerik
`--w-app`'e kadar genişliyor, dar bir şeritte kalmıyor.

---

## 10. Radius & Borders

Radius **üç değere** iniyor (Vercel: 6/8/12):

| Token | Değer | Kullanım |
|---|---|---|
| `rounded-control` | 6px | Buton, input, select, rozet |
| `rounded-card` | 10px | Kart, panel, tablo düğümü |
| `rounded-modal` | 14px | Modal, drawer, açılır menü |
| `rounded-full` | — | Yalnızca avatar ve nokta göstergesi |

`rounded`, `rounded-sm`, `rounded-md`, `rounded-2xl`, `rounded-3xl` **terk
ediliyor**.

Kenarlık **tek değer**: `--border-hairline` = `color-mix(in srgb,
var(--content-primary) 12%, transparent)`. Vurgu gerektiğinde
`--border-strong` (%20).

---

## 11. Component System

Yeni `components/ui/` altında, yalnızca **gerçekten tekrar eden** primitifler:

| Bileşen | Gerekçe (ölçüm) |
|---|---|
| `Button` | 286 el yazması buton |
| `Container` | 23 farklı `max-w-*` |
| `Card` | Kart deseni 40+ yerde tekrar ediyor |
| `Input` | `glass-input` + 20+ el yazması alan |
| `Badge` | Rozet deseni 30+ yerde |

Varyantlar — `Button`: `primary` · `secondary` · `ghost` · `danger` · `icon`.
Boyutlar: `sm` (32px) · `md` (36px) · `lg` (44px).

**Yapılmayacak:** `Tabs`, `Accordion`, `Tooltip` gibi projede tekrar etmeyen
soyutlamalar. Görev dokümanı: *"Do not create abstractions that are not
actually needed."*

---

## 12. Motion System

| Token | Süre | Kullanım |
|---|---|---|
| `--dur-fast` | 120ms | Hover, focus |
| `--dur-base` | 180ms | Açılır menü, geçiş |
| `--dur-slow` | 280ms | Modal giriş/çıkış |
| `--ease-out` | `cubic-bezier(0.2, 0, 0, 1)` | Varsayılan |

Mevcut kodda 6 farklı süre var (`duration-75/150/200/250/300/500`) — üçe
iniyor. Yalnızca `opacity`, `transform`, `background-color`, `border-color`
animasyonlanır.

`prefers-reduced-motion`: UI geçişleri kapanır. **İstisna:** yıldız/dalga arka
planı muaf kalır — bu marka kimliği (FRONTEND.md'de gerekçeli) ve durdurulduğunda
arka plan hata gibi görünüyordu.

---

## 13. Responsive Strategy

| Aralık | Davranış |
|---|---|
| < 640 (mobil) | Tek sütun, yatay kaydırılan şerit seçiciler, canvas araç çubuğu başlık altında |
| 640–1023 (tablet) | İki sütun kart ızgarası, araç çubuğu **hâlâ şerit** (bu turda düzeltildi) |
| 1024–1279 (laptop) | Masaüstü yerleşimi, `--w-app` |
| ≥ 1280 (masaüstü+) | `xl:` ile içerik `--w-app`'e genişler, tipografi bir adım büyür |

---

## 14. Accessibility Rules

- Kontrast: küçük metin ≥ 4.5:1 (ölçülüp doğrulandı, bkz. FRONTEND.md §11.2)
- **Her `outline-none` bir `focus-visible` halkasıyla eşleşmek zorunda** —
  şu an 41'e karşı 10; bu tur kapatılıyor
- Dokunma hedefi ≥ 44×44 (`tap-44` utility'si mevcut)
- Semantik HTML, doğru başlık hiyerarşisi, ikon-tek butonlarda `aria-label`
- `prefers-reduced-motion` desteği (§12 istisnasıyla)

---

## 15. Implementation Plan

- [x] Kırık token onarımı (aktif regresyon)
- [x] Motion token'ları
- [x] Radius ölçeği (8 → 3)
- [x] Tipografi ölçeği token'ları
- [x] Layout genişlik token'ları
- [x] `Container` primitifi
- [x] `Button` primitifi
- [x] Denetim: keyfi `text-[Npx]` yasağı
- [x] Denetim: terk edilen radius yasağı
- [x] Okunamayan `text-[8/9px]` → `text-micro`
- [x] Focus görünürlüğü
- [x] Büyük ekran (`xl:`) uyarlaması
- [x] Final QA

---

## 16. File-Level Change Map

| Dosya | Planlanan değişiklik |
|---|---|
| `app/globals.css` | Kırık 5 token onarımı; radius/tipografi/motion/layout token'ları; `@utility` tanımları |
| `scripts/check-design-rules.mjs` | Keyfi font boyutu + terk edilen radius kuralları; token tanım/kullanım tutarlılığı |
| `components/ui/Button.tsx` | **Yeni** — 5 varyant, 3 boyut |
| `components/ui/Container.tsx` | **Yeni** — 3 genişlik |
| `app/page.tsx` | Hero tipografisi, `Container`, `xl:` |
| `app/demo/page.tsx` | `Container`, tipografi token'ları |
| `components/landing/TemplateStrip.tsx` | Kart radius + tipografi |
| `components/landing/WhyNamines.tsx` | Bölüm ritmi |
| `components/canvas/nodes/TableNode.tsx` | Radius token'ı |

---

## 16.1 Uygulama sırasında bulunan iki kırık

Bu tur, **uygulamanın kendisi iki gerçek hatayı ortaya çıkardı** — ikisi de
"sessizce bozuk" sınıfında, yani hiçbir hata vermeden yanlış çalışan türden.

**1. Beş tanımsız token (aktif regresyon).**
§2.2'de anlatıldı. Onarıldı ve bir daha olmaması için denetime **tanımsız
token kuralı** eklendi: `var(--x)` okunuyor ama `--x:` tanımlı değilse hata.
Kural yazılırken üç tanımsız token daha çıktı — biri gerçek kırıktı
(`select:hover` zemini hiç boyanmıyordu), ikisi `next/font`'un runtime'da
enjekte ettiği font değişkenleriydi ve muaf tutuldu.

**2. Yorum satırı CSS derlemesini kırdı.**
Denetim mesajına örnek bir Tailwind sınıfı yazdım. Tailwind v4 kaynak
dosyaları **düz metin olarak** tarıyor ve denetim betiği de taranan dosyalar
arasında — boru işareti içeren arbitrary-value örneğini gerçek bir sınıf sanıp
`border-radius: var(--a|b|c)` üretti ve tüm CSS derlemesi çöktü
(`Unexpected token Delim('|')`, sayfa HTTP 500).

Ders: **"yorum yazmak kodu çalıştırmaz" varsayımı Tailwind v4'te geçerli
değil.** Örnek sınıf adları yorumda bile yazılmaz.

---

## 17. Decisions & Rationale

**Karar: Nötr gri (`--ui-chroma: 0`) korunuyor.**
Vercel ilkesi: nötrlerde doygunluk sıfır (§3.1 ölçümü). Kullanıcı bu turda
zaten nötre geçilmesini istedi ve ölçüm bunu doğruluyor — "belirsiz mavimsi"
bir gri, "bilinçli nötr" bir griden daha az kararlı okunuyor.

**Karar: Başlıklarda ağırlık düşürülüyor (400/450), tracking negatif.**
Vercel ilkesi §3.2. Bu, "oversized typography everywhere" hatasına düşmeden
hiyerarşi kurmanın yolu — büyük metin zaten dikkat çekiyor, bir de kalın
olması gürültü.

**Karar: Radius 8 → 3 değer.**
Vercel'de ölçülen: 6/8/12. Görev dokümanının yasak listesinde "multiple
unrelated border-radius values" birebir var. Üç değer, "kontrol / kart /
modal" ayrımını taşımaya yetiyor.

**Karar: `Button` primitifi ekleniyor, `Tabs`/`Tooltip` eklenmiyor.**
286 buton ölçüldü — soyutlama gerçek tekrardan doğuyor. Tabs/Tooltip projede
tekrar etmiyor; onları eklemek görev dokümanının açıkça yasakladığı
"gereksiz soyutlama".

**Karar: Header'a backdrop blur EKLENMİYOR.**
Vercel'in header'ı ölçüldüğünde `backdrop-filter: none` çıktı. Blur her scroll
karesinde yeniden hesaplanıyor; görev dokümanı §10 "expensive blur filters"
diyor. Mevcut `backdrop-blur` kullanımları modal gibi statik katmanlarda kalıyor.

**Karar: Açık tema yapılmıyor.**
Vercel varsayılanı beyaz, ama Namines bir veritabanı IDE'si ve mevcut marka
kimliği (yıldız/dalga arka planı) koyu zemine bağlı. `--ui-bg-l` knob'u
isteyene açık temayı zaten mümkün kılıyor; varsayılanı değiştirmek kimliği
kırardı.

**Karar: radius migrasyonu TAMAMLANDI — 565 kullanım, 8 değer → 3 token.**

İlk denemede yalnızca uç değerler (`rounded-sm`, `rounded-3xl`) taşınmış,
`rounded-lg`/`rounded-xl` "kademeli migrasyon" gerekçesiyle ertelenmişti.
**Bu yanlış karardı ve kullanıcı haklı olarak fark etti:** ekranda hiçbir şey
değişmemişti.

Sebep ölçüldü: `Button` ve `Container` primitiflerini **0 dosya** kullanıyordu,
yeni tipografi ölçeği yalnızca iniş hero'sunda vardı. Yani tasarım sistemi
kurulmuş ama uygulama ona **taşınmamıştı** — hiçbir şey tüketmeyen bir sistem
"uygulanmış" sayılmaz.

Migrasyon tamamlandı:

| | Önce | Sonra |
|---|---|---|
| Farklı radius değeri | 8 | **3** (+ `rounded-full` istisnası) |
| `radius-control` (6px) | — | 392 |
| `radius-card` (10px) | — | 132 |
| `radius-modal` (14px) | — | 46 |
| Ölçek dışı kalan | 565 | **0** |

Denetim artık ölçek dışı her radius'u reddediyor; `rounded-full` (avatar,
nokta göstergesi) ve token biçimleri geçiyor — beş vakada test edilerek
doğrulandı.

Canlı ölçüm (canvas): tablo düğümü **10px**, bilgi paneli **14px**, araç
çubuğu butonları **6px**.

**Karar: `text-[10px]`/`text-[11px]` (414 kullanım) da bu turda taşınmadı.**
Aynı gerekçe. Yasaklanan, okunabilirlik tabanının ALTI (10px'ten küçük) —
orada 75 kullanım vardı ve hepsi `text-micro`'ya taşındı. Denetim artık
tabanın altına inilmesini kalıcı olarak engelliyor.

---

## 18. Doğrulama sonuçları

Tümü çalışan uygulamada ölçüldü:

| Kontrol | Sonuç |
|---|---|
| `npx tsc --noEmit` | ✅ temiz |
| `npm run check:design` | ✅ temiz (5 kural: ham hex · palet dışı aile · saf b/s · tanımsız token · yasak bağımlılık) |
| `npm run check:templates` | ✅ 20/20 şablon, 384 tablo, 6 motor |
| `npm run check:e2e` | ✅ **30/32 çalışıyor, 0 kırık** (2'si kod dışı: Stripe kimlikleri, Groq TPM) |
| Okunamayan font (<10px) | ✅ 75 → 0 |
| Ölçek dışı radius | ✅ 565 → 0 (8 değer → 3 token) |
| Tanımsız token | ✅ 8 → 0 |
| 375px yatay taşma | ✅ yok |
| 1920px içerik genişliği | ✅ 896px → 1200px |
| H1 ölçek uyumu | ✅ 40px / 400 / −1.2px (Vercel ilkesi) |
