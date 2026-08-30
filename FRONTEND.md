# Namines — Frontend Tasarım Talimatları

> Bu dosya `frontend/` altında **görsel/UX** işi yaparken okunur. Backend/mimari
> işi için [AGENTS.md](AGENTS.md)'ye bak. İkisi birbirini geçersiz kılmaz —
> AGENTS.md genel proje kuralları, bu dosya sadece frontend/tasarım kuralları.

---

## 0. Zorunlu ilk adım: `ui-ux-pro-max` skill'i kullan

Bu proje `.claude/skills/ui-ux-pro-max/` altında kurulu bir tasarım zekası
aracına sahip (CSV veri tabanlı, 79 stil, 192 renk paleti, 119 UX kuralı,
22 teknoloji stack'i). **Görsel bir karar vermeden önce** ([SKILL.md](.claude/skills/ui-ux-pro-max/SKILL.md)'deki
tam protokole uy):

```bash
# Yeni bir sayfa/bileşen için tasarım sistemi sorgusu
python ".claude/skills/ui-ux-pro-max/scripts/search.py" "<ürün türü> <anahtar kelimeler>" --design-system -p "Namines"

# Nokta atışı bir konu için (dropdown, form, animasyon, vb.)
python ".claude/skills/ui-ux-pro-max/scripts/search.py" "<anahtar kelime>" --domain <ux|color|typography|landing|icons|gsap|react>
```

**Kural:** Sorgu sonucu **bu dosyadaki renk paletini geçersiz kılmaz** — palet
aşağıda sabit (§2). Skill'i tipografi eşleşmesi, UX kalıpları (dropdown/form/
navigasyon kuralları), erişilebilirlik kontrol listesi ve stack-özel
implementasyon detayları için kullan. Renk için skill'in önerdiği hex'leri
**doğrudan kopyalama** — aşağıdaki token'lara çevir (bkz. §4).

Sonuç boş dönerse veya konuyla alakasız görünürse, skill'in kendi kuralı
geçerli: uydurma, kullanıcıya "eşleşme yok, genel varsayılan kullanıldı" de.

---

## 1. Tasarım felsefesi

**Namines Studio + Console hissi**, jenerik "AI chatbot + dashboard" değil.
Kullanıcının kendi sözleriyle: *Database IDE + Backend Console + AI Copilot.*

- AI her zaman bir sohbet kutusunda olmak zorunda değil — komut, aksiyon,
  açıklama, review, diff, onay katmanlarında da yaşar (bkz. [new-phase/27 §13](new-phase/27-LIFECYCLE-PIVOT.md)).
- Mevcut arka plan kimliği (kayan yıldızlar + dalga animasyonu) **korunuyor**
  — kaldırma. Tonu daha siyahımsı/derin uzay hissine çekiyoruz (§2).
- Teknik, hassas, "hacker" havası — geliştirici aracı kimliği. Süslemeden
  kaçın, işlevsel minimalizm.

---

## 2. Renk paleti (SABİT — sorgulanmaz, ezberlenir)

**Kesin kural: `#FFFFFF`, `#FFF`, `#000000`, `#000` gibi saf değerler HİÇBİR
YERDE kullanılmaz** — component'te, globals.css'te, inline style'da, SVG'de.
Her zaman aşağıdaki off-black / off-white / minimal lacivert üçlüsünden.

```css
/* ── Zemin (off-black, mavimsi alt ton — uzay/yıldız temasına uyar) ──── */
--color-bg-base:        #05070C;   /* sayfa zemini, en derin */
--color-bg-elevated:    #0A0D14;   /* kart, panel, header zemini */
--color-bg-surface:     #10141D;   /* input, satır, ikinci seviye yüzey */
--color-bg-surface-2:   #161B26;   /* hover/active yüzey */

/* ── Kenarlık ─────────────────────────────────────────────────────────── */
--color-border:         #1E2430;   /* varsayılan ayırıcı */
--color-border-strong:  #2B3241;   /* vurgulu kenarlık, odak öncesi */

/* ── Metin (off-white — asla #fff) ───────────────────────────────────── */
--color-text-primary:   #E7E9EE;   /* başlık, birincil metin */
--color-text-secondary: #B4B9C6;   /* ikincil metin */
--color-text-muted:     #7A8194;   /* soluk metin — WCAG AA alt sınırı, altına inme */

/* ── Vurgu: MİNİMAL lacivert — TEK renkli aksan ailesi ──────────────────
   Bilinçli olarak desatüre. Parlak indigo/mor (#6366f1, #818cf8 gibi)
   KULLANILMAZ — bunlar "minimal lacivert" değil "canlı mor" sayılır. */
--color-accent:         #3C4A6B;   /* varsayılan vurgu (buton zemini, aktif oda) */
--color-accent-hover:   #4C5C82;   /* hover — biraz daha açık lacivert */
--color-accent-subtle:  #1A2033;   /* vurgu arka planı (rozet, seçili satır) */
--color-accent-text:    #AEB9D6;   /* vurgu üstü/yanı metin (linkler, aktif ikon) */

/* ── Odak halkası — beyaz DEĞİL, yumuşak lacivert ───────────────────── */
--color-focus-ring:     #5B6B93;

/* ── Semantik durumlar (TEK istisna — bkz. not) ─────────────────────── */
--color-danger:         #B8544B;   /* silme/hata — desatüre kırmızı-kiremit */
--color-danger-subtle:  #241512;
--color-success:        #4B8A6F;   /* başarı/sağlıklı/FinOps — desatüre yeşil */
--color-success-subtle: #10201A;
--color-warning:        #C2C7D1;   /* off-white — "uyarı rengi" yok, bkz. not */
--color-warning-subtle: #161B26;
```

**Semantik renk istisnası (bilinçli, dokümante edilmiş sapma):** `danger`/
`success` ikilisi paletin "sadece off-black/off-white/lacivert" kuralının
dışında. Gerekçe: `ui-ux-pro-max`'ın kendi erişilebilirlik kuralı ("Relying
on color alone to convey meaning" + kontrast 4.5:1) hata/başarı durumlarının
ayırt edilebilir olmasını zorunlu kılıyor — hepsini laciverte boğmak, silme
onayı ile "kaydedildi" bildirimini görsel olarak ayırt edilemez yapardı. Bu
iki renk de **desatüre** tutuldu (parlak `#EF4444`/`#22C55E` değil) ki
paletin geri kalanıyla uyumlu kalsın. Component'te renk TEK BAŞINA anlam
taşımamalı — ikon/metin/ARIA ile desteklenmeli (skill'in kuralı).

**"Warning" bilerek üçüncü bir renk DEĞİL** (kullanıcı talimatı: "sarı bir şey
istemiyorum" — mustard/amber/gold ailesinin tamamı kaldırıldı: `#a6813f`,
`#c9b27f`, `#FFD700`, Tailwind'in `amber-*`/`yellow-*` sınıfları dahil).
Uyarı/dikkat durumları artık `--color-ink-secondary` (off-white) ile
gösteriliyor, ayrım renkle değil ikon (`AlertTriangle`) ve etiketle
sağlanıyor. Gerçekten **yıkıcı** bir onay isteniyorsa (şemayı geri dönüşsüz
üzerine yazma gibi) `--color-danger` kullanılır — "dikkat" ile "veri kaybı
riski" aynı şey değildir, ikincisi kırmızı hak eder.

---

## 3. Tipografi

`ui-ux-pro-max`'ın "Developer Mono" eşleşmesi (developer tool/IDE/kod editörü
için en yüksek uyum skoru):

```css
--font-mono: 'JetBrains Mono', 'Geist Mono', monospace;  /* başlıklar, kod, tablo/kolon adları, sayısal veri */
--font-sans: 'IBM Plex Sans', 'Inter', sans-serif;        /* gövde metni, UI etiketleri, butonlar */
```

```
https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500;600;700&display=swap
```

Mevcut `--font-geist-mono` kullanımı JetBrains Mono ile değiştirilir (aynı
kategori, ama JetBrains geliştirici araçlarında daha yüksek tanınırlığa
sahip — skill verisi bunu doğruluyor). Mono: başlıklar/tablo-kolon adları/
rakamlar. Sans: paragraf, buton metni, form etiketi.

---

## 4. Uygulama kuralı — token'lar `@theme` üzerinden, ham hex component'e YAZILMAZ

`app/globals.css`'teki `@theme inline` bloğu **tek doğruluk kaynağı**. Yeni bir
renk gerektiğinde:

1. Önce §2'deki token'lardan birine bak — büyük ihtimalle zaten var
2. Yoksa `@theme` bloğuna yeni bir `--color-*` token'ı ekle (mevcut `--color-surface-*`
   yorumundaki disiplinle aynı — bkz. dosyanın kendi notu: *"Yeni yüzeyler bu
   token'lardan alınmalı; ham hex yazma."*)
3. Component'te `bg-[#1a2333]` gibi keyfi Tailwind arbitrary-value hex **yazma** —
   `bg-surface-600` gibi token-tabanlı utility kullan

**Bu ihlaller TEMİZLENDİ (bkz. §8).** `frontend/app` + `frontend/components`
altında ham hex sayısı **0**. Yeni kod da ham hex yazmamalı — token kullan.

---

## 5. Kütüphane/araç zorunluluğu

Zaten `package.json`'da olanlar **korunur ve tek kaynak olarak kullanılır** —
yeni bir alternatif kütüphane ekleme (ör. yeni bir ikon seti, yeni bir CSS-in-JS
çözümü) açık gerekçe olmadan:

| Katman | Kullanılacak | Not |
|---|---|---|
| CSS framework | **Tailwind CSS v4** (`@theme inline`) | CSS-first config, `tailwind.config.js` yok |
| Bileşen primitifleri | **Radix UI** (`@radix-ui/react-*`) | Dialog, dropdown, context-menu, tooltip zaten kurulu |
| Varyant yönetimi | **class-variance-authority (cva)** deseni | shadcn/ui'nin kullandığı desen — Radix + cva + Tailwind |
| İkonlar | **lucide-react** | Emoji ikon olarak KULLANILMAZ (skill kuralı) |
| Canvas | **@xyflow/react** (React Flow) | Değişmiyor |
| Durum yönetimi | **Zustand** | Değişmiyor, yeni store gerekirse aynı desen |
| Animasyon | CSS keyframes (mevcut) veya **GSAP** (skill'in `--domain gsap` önerileri) | Framer Motion YOK, projede kurulu değil — ekleme |
| Form | **react-hook-form** zaten kurulu değilse native + Zustand | Karmaşık formda react-hook-form değerlendirilebilir |

Yeni bir npm paketi eklemeden önce: mevcut kurulu paketlerle çözülüp
çözülemeyeceğini kontrol et. Ekliyorsan gerekçesini commit mesajında yaz.

---

## 6. Erişilebilirlik — pazarlık edilemez (skill'in Öncelik 1-2 kuralı)

- Kontrast: metin/zemin en az 4.5:1 (küçük metin), 3:1 (büyük/kalın)
- Her interaktif eleman: min 44×44px dokunma alanı, görünür `:focus-visible` halkası
- İkon-tek buton: `aria-label` zorunlu
- `prefers-reduced-motion` saygı görür — yıldız/dalga animasyonları bu medya
  sorgusunda durdurulmalı/sadeleştirilmeli (şu an eksik, eklenmeli)
- Klavye tuzağı yok, tab sırası görsel sırayla eşleşir

---

## 7. İlk hedef: Anasayfa (bu oturumun kapsamı)

1. **Üst navbar** — logo, workspace butonu, feedback, sign up/login, ton
   hiyerarşisi (birincil eylem = accent, ikincil = muted metin)
2. **Prompt alanı** — hero'daki AI şema üretim kutusu, dropdown'lar (motor
   seçimi, model seçimi)
3. **Dropdown'lar** — Radix `Select`/`DropdownMenu` üzerinden, native `<select>`
   yerine (mevcut `select` CSS'i native — Radix'e taşınabilir, tutarlılık için)
4. Arka plan (yıldız + dalga) **kalıyor**, ton §2'deki `--color-bg-base`'e çekiliyor

Kapsam dışı (bu oturumda dokunulmuyor, ayrı iş): canvas sayfası, compile sayfası,
table editor drawer — palet migrasyonu aşamalı yapılacak, hepsini tek seferde
kırma riski almıyoruz.

**İkinci tur (tamamlandı):** Login/Sign Up modalı ([AuthModal.tsx](frontend/components/canvas/panels/AuthModal.tsx))
minimalist + mobil uyumlu (`max-h-[90vh] overflow-y-auto`) hale getirildi;
Google/GitHub butonları eklendi (**şu an sadece UI** — backend'de OAuth altyapısı
yok, tıklanınca "coming soon" toast'ı gösteriyor; gerçek OAuth ayrı bir iş,
client ID/secret ve backend endpoint gerektirir). Workspace paneli
([ProjectSidebar.tsx](frontend/components/layout/ProjectSidebar.tsx)) ve
Feedback widget'ı ([FeedbackWidget.tsx](frontend/components/feedback/FeedbackWidget.tsx))
aynı palete taşındı ve sadeleştirildi. Eski gradyanlı/yıldızlı SVG logo
kaldırıldı — yerine [Logo.tsx](frontend/components/layout/Logo.tsx) (tek renkli,
lucide `Database` ikonlu minimalist rozet) geldi. Header artık `sm`/`md`
breakpoint'lerinde daralıyor (workspace/login metinleri ikona düşüyor, proje adı
düzenleyici dar ekranda gizleniyor).

---

## 8. Token birleştirme + compile ekranı (tamamlandı)

§7'de "ayrı iş" diye ertelenen **compile (Approve sonrası) ekranı** ele alındı ve
paletin kod tabanındaki tutarsızlığı kökten temizlendi.

### 8.1 Neden tutarsızdı — ölçüm

`app/` + `components/` altında **42 dosyada 1753 ham hex** vardı. Asıl sorun
"disiplinsizlik" değil, **paletin eksik olması**ydı:

| Semptom | Kök neden |
|---|---|
| 4 farklı kırmızı (`#b8544b`, `#e08787`, `#c98d85`, `#c56a61`) | `--color-danger` (#b8544b) koyu zeminde küçük metin için ~3:1 → AA altı. Kod tabanı okunur bir kırmızıyı **kendiliğinden** üretti. |
| 3 farklı yeşil (`#4b8a6f`, `#7fb69a`, `#5da081`) | Aynı sebep, `--color-success` için. |
| `#7a8194` 186 kez ham | Token'ı yoktu — 4. metin adımı tanımlı değildi. |
| `#5b6378`, `#4a5163`, `#64748b` | AA altı griler (3.36:1 / **2.54:1** / 4.23:1) — "göz yoruyor"un ölçülebilir kısmı. |
| Indigo `#6366f1`/`#818cf8`/`#a5b4fc` | **Token katmanının kendisi** `--color-primary-glow: #4f46e5` tanımlıyordu; §2 bunu yasaklarken globals.css ihlal ediyordu, ihlal component'lere yayıldı. |
| Kod panellerinde `#999` | Prism `prism-tomorrow.css` kendi renk ailesini getiriyor (yorum ~3.5:1). |

### 8.2 Çözüm — rol ayrımı, ad hoc ton değil

Semantik renkler **iki adıma** ayrıldı; yeni ton eklemek yerine rol netleştirildi:

```css
--color-danger:      #b8544b;  /* zemin/kenarlık */
--color-danger-text: #e08787;  /* koyu zemin üstü metin/ikon — 7.66:1 */
--color-success:      #4b8a6f;
--color-success-text: #7fb69a; /* 8.69:1 */
--color-content-subtle: #7a8194;       /* 4. metin adımı — AA tabanı, 5.18:1 */
--color-content-primary-hover: #f5f6f8;
```

`--color-primary-glow*` **isimleri korundu** (referanslar kırılmasın) ama değerleri
aksan ailesine çekildi — indigo tamamen gitti. AA altı griler `content-subtle`'a
**yükseltildi**, altına inilmiyor.

### 8.3 Sonuç (ölçüldü, tarayıcıda)

- `app/` + `components/` ham hex: **1753 → 0**
- Metin token'larının hepsi WCAG AA üstü: 16.59 / 11.88 / 6.56 / 5.18 / 7.66 / 8.69 / 10.28 : 1
- Compile ekranında render edilen farklı metin rengi: **5**, AA altı: **0**
- Prism sözdizimi teması palete bağlandı (`pre[class*="language-"]` özgüllüğüyle,
  component import sırasından bağımsız kazanır)

### 8.4 Compile ekranı — ortak bölmeler

8 panelin (DDL, EF Core, Mermaid ER, Test Data, Data Dictionary, README, Docker
Sandbox, Developer Package) her biri kendi başlığını/kartlarını çiziyordu; kabuğun
üst şeridi zaten sekmeyi adlandırdığı için bu **bilgi tekrarıydı** ve içeriği
katlamanın altına itiyordu. `components/compile/PanelKit.tsx` tek kaynak oldu:
`Panel`, `PanelBar`, `Segmented`, `IconButton`, `ActionButton`, `PanelEmpty`,
`StatStrip`, `OptionCard`. Ölçüm: 1366×768'de **8 sekmenin hepsinde sayfa scroll'u 0px**,
kart tabanlı sekmelerde katlanma altında kalan aksiyon **yok**.

> ⚠️ **Tailwind v4 tuzağı:** `@theme`'e yeni token eklendiğinde Turbopack'in
> `.next` önbelleği eski utility setini servis etmeye devam ediyor — yeni
> `text-*` sınıfı **sessizce** üretilmiyor ve metin miras alınan renge düşüyor.
> Bu oturumda tam olarak bu yaşandı (190 kullanımlık `text-content-subtle`
> render edilmiyordu). Token ekledikten sonra `rm -rf .next` şart, ve sınıfın
> gerçekten üretildiğini tarayıcıda doğrula.

---

## 9. Palet denetimi ikinci tur — hazır Tailwind aileleri (tamamlandı)

§8.3 "`app/` + `components/` ham hex: 1753 → 0" diye kaydediyordu. Doğruydu ama
**eksikti**: `check-design-rules.mjs` yalnızca `#hex` arıyordu, dolayısıyla
ihlaller hex yazmayı bırakıp Tailwind'in kendi renk ailelerini yazmaya geçmişti.
Ölçüm yapıldığında **254 kullanım** vardı.

| Aile | Kullanım | Neden sorun |
|---|---|---|
| `zinc-*` | 194 | Paletin dışında bir nötr ölçek. `text-zinc-500` (#71717a) koyu zeminde **~4.0:1**, `text-zinc-600` (#52525b) **~2.6:1** — ikisi de WCAG AA altı |
| `rose/red-*` | 24 | `danger` / `danger-text` zaten var |
| `emerald-*` | 12 | `success` / `success-text` zaten var |
| `sky/indigo-*` | 16 | §2 indigo'yu **açıkça** yasaklıyor |
| `orange-*` | 8 | §2 amber/yellow/orange ailesinin tamamını kaldırmıştı; AWS kartında geri sızmıştı |

Ayrıca **saf değerler** kullanımdaydı: 14 × `text-white`, 31 × `bg-black/<alfa>`
(modal karartması). §2 "`#FFFFFF`/`#000000` HİÇBİR YERDE" diyor.

### Yapılanlar

1. **254 sınıf token'lara taşındı** — nötrler `surface-*`/`content-*`,
   semantikler `danger-*`/`success-*`, vurgu `accent-*`.
2. **Eksik iki token eklendi.** `text-warning-text` ve `border-surface-400`
   **kodda kullanılıyordu ama tanımlı değildi** — Tailwind v4 o utility'leri hiç
   üretmiyor ve stil sessizce düşüyordu. Sonuç: demo ekranında "uyarı" bulguları
   "not" bulgularıyla aynı tonda görünüyordu (§8.4'teki tuzağın aynısı).
3. **`--color-scrim` eklendi.** Karartma artık paletin en derin zemini
   (`#05070c`), saf siyah değil: nötr siyah, sayfanın mavimsi off-black zemininin
   yanında soğuk bir leke gibi duruyordu.
4. **Betik genişletildi** ve gerçekten yakaladığı **doğrulandı** (ihlal enjekte
   edilip çıkış kodunun 1 olduğu görüldü — geçtiğini varsaymak, kuralın
   uygulandığını varsaymanın ta kendisiydi):

```
✓ ham hex yok · palet dışı Tailwind ailesi yok · saf beyaz/siyah yok
```

### Bulunan ek hatalar

- `hover:` durumu temel durumla **aynı** olan 4 düğme (`bg-accent hover:bg-accent`)
  — üzerine gelince hiçbir şey olmuyordu.
- Paylaşım sayfasının markasında **emoji** (`⚡ Namines`) — §5 "emoji ikon olarak
  KULLANILMAZ" diyor, bu tek istisnaydı.

## 10. Mobil geçiş (tamamlandı)

375px'te ölçülen ve düzeltilenler:

| Ekran | Sorun | Çözüm |
|---|---|---|
| `/demo` | 20 şablon + 6 motor sarılınca **8 satır düğme**; içeriğe ulaşmadan ~700px kaydırma | Ölçek sekmesi (liste ≤12) + mobilde yatay kaydırma. Sayfa yüksekliği 2327 → 1681px |
| `/demo` | Bulgu paneli sabit 420px — tek bulguyla **450px boşluk** | Yükseklik `lg:`den itibaren sabit, mobilde içeriğe göre |
| `/demo` | "Generated … DDL" başlığı rozetle çakışıyordu | `flex-wrap` |
| `/demo` | Tuval 560px sabit | `340 → 440 → 560px` |
| İniş sayfası | 12 kart mobilde ~1300px'lik duvar; "neden biz" katlanmanın çok altında | 6 seçilmiş kart + "See all" |
| Şablon galerisi | 20 şablon düz liste; `max-h-[80vh]` mobilde taşıyor | Ölçek süzgeci + `90dvh` |
| Ayarlar modalı | Sekme şeridi kesiliyor, **Pricing sekmesi görünmüyordu** | `-mx-5 px-5` ile kenardan kenara kaydırma + `dvh` |
| `/share` | 7 öğe 52px'lik tek satırda üst üste biniyordu | Mobilde sarıyor, ikincil öğeler gizli |
| `/share` | `fitView` düğümler ölçülmeden — büyük şemada **boş tuval** | `useNodesInitialized` + `minZoom` |

Dokunma hedefleri (§6, 44×44) demo seçicilerinde, galeri düğmelerinde ve
modal kapatma düğmesinde `min-h-11` ile karşılandı.

> ⚠️ Mobilde **`h-screen` yerine `h-dvh`**: `vh`, mobil tarayıcılarda adres
> çubuğu gizlenene kadar gerçek yüksekliği vermiyor ve panelin altı ekranın
> dışında kalıyor.

### Kapsam dışı (bilinçli)

**Canvas düzenleyicisinin kendisi mobilde hâlâ dar.** Üst araç çubuğu
(`Alternative` / `Request Review` / `Approve`) 375px'te başlık şeridiyle
çakışıyor. Bu ayrı bir iş: canvas bir masaüstü düzenleyicisi ve mobil
yerleşimi, buradaki gibi birkaç sınıfla değil, araç çubuğunun yeniden
kurgulanmasıyla çözülür.

---

## 11. Tek renk merkezi (tamamlandı)

> **Sorulan iş:** "renkleri tek bir ortak merkeze topla, komple değiştir, çok
> karanlık, en azından tek yerden değiştirebileyim."

### 11.1 Neden tek merkez YOKTU — ölçüm

§8 ve §9 "ham hex 0" diye kaydediyordu. İkisi de doğruydu ve ikisi de eksikti:
denetim yalnızca **`.tsx`** dosyalarını tarıyordu.

| Katman | Ham renk | Ne boyuyordu |
|---|---|---|
| `app/globals.css` | **248** | Header, sidebar, canvas araç çubuğu, bağlam menüsü, proje kartları, Prism teması |
| React Flow'un kendi CSS'i | `#141414` | **Tuvalin tamamı** — uygulamanın en büyük yüzeyi |
| JSX string prop'ları | 3 | Minimap maskesi, tur overlay'i |

`globals.css` içinde FRONTEND.md §2'nin **açıkça yasakladığı** aileler duruyordu:
indigo `#4f46e5` / `#6366f1` (42 kullanım), amber `#fbbf24`, cyan `#06b6d4`,
Tailwind slate grileri. Yani ekranın büyük kısmı hiçbir zaman palete
bağlanmamıştı — "rengi tek yerden değiştiremiyorum" şikâyetinin ölçülebilir
karşılığı buydu.

### 11.2 Çözüm — on sayı, OKLCH, zemine göre türetme

`:root` içinde **on knob** var; başka hiçbir yerde ham renk yok:

```css
--ui-hue: 250;      --ui-chroma: 0.012;   --ui-bg-l: 34%;   --ui-bg-step: 3.6%;
--brand-hue: 262;   --brand-chroma: 0.075; --brand-l: 48%;
--danger-hue: 25;   --success-hue: 155;
```

**OKLCH, çünkü açıklık algısal:** L'yi %4 artırmak her tonda AYNI kadar
aydınlanma demek. HSL'de bu doğru değil ve ton değişince yüzey basamakları
birbirine giriyor.

**Metin adımları ZEMİNE GÖRE, mutlak değil.** Önce mutlaktı (%96, %86…) ve
knob çevrildiğinde kontrast sessizce çöküyordu: `--ui-bg-l` %26'da
`content-subtle` **3.37:1**'e düşüyordu (AA = 4.5:1). Yani knob'un kendisi
erişilebilirliği bozuyordu, ki bu onu işe yaramaz kılardı. Artık her metin
adımı zeminden sabit bir L farkında; sabit L farkı ≈ sabit kontrast.

**Tarayıcıda ölçüldü** (canvas ile gerçek piksel, WCAG formülü):

| `--ui-bg-l` | zemin rgb | primary | secondary | muted | subtle |
|---|---|---|---|---|---|
| %19 | (24,29,33) | 12.17 | 8.98 | 6.63 | **5.14** |
| %28 | (36,41,47) | 9.14 | 8.37 | 6.34 | **5.01** |
| **%34 (varsayılan)** | **(51,57,62)** | 7.0 | 7.0 | 6.0 | **4.8** |
| %36 | (56,62,67) | 6.49 | 6.49 | 5.78 | **4.65** |

Değerler en açık yüzeye (`surface-600`) karşı, yani **en kötü durum**.
%17-%36 aralığının tamamı AA üstünde; %36'nın üstünde `subtle` 4.5'in altına
düşüyor, o yüzden aralık orada bitiyor.

### 11.3 React Flow — üçüncü taraf stilini palete bağlama

`.react-flow.dark` kendi stylesheet'inde `background-color: #141414` taşıyor.
Knob çevrildiğinde paneller aydınlanıp **tuval karanlık kalıyordu**; ikisi
birbirinden kopuyordu ve hiçbir denetim bunu göremezdi (değer
`node_modules` içinde). Kütüphanenin resmî `--xy-*` tema değişkenleri
üzerinden 26 değer token'lara bağlandı — dist CSS'ini yamalamak yerine.

### 11.4 Denetim genişletildi

`check:design` artık `.css` dosyalarını da tarıyor (renk merkezinin `:root`
bloğu muaf), çok satırlı CSS yorumlarını doğru atlıyor, ve JSX string
prop'larındaki `rgb()/rgba()` kullanımını yakalıyor. **Yakaladığı doğrulandı:**
zinc sınıfı, `text-white`, ham hex ve `globals.css` gövdesine enjekte edilen
bir renk — dördü de çıkış kodu 1 verdi.

### 11.5 Nasıl denenir

`app/globals.css` içinde tek satır:

```css
--ui-bg-l: 30%;   /* daha koyu */   →   --ui-bg-l: 36%;   /* daha aydınlık */
```

Tuval, header, sidebar, modal, tablo düğümleri, minimap — hepsi birlikte
değişiyor (tarayıcıda %45'e çekilerek doğrulandı).

## 12. Uçtan uca özellik denetimi

`npm run check:e2e` — ayakta duran API'ye gerçek istek atıyor ve cevabın
**içeriğini** kontrol ediyor; 200 dönmesi yeterli sayılmıyor. Örneğin linter
testi PK'sız bir tablo gönderip uyarının gerçekten üretildiğini doğruluyor.

Betiği yazarken üç yanlış varsayım ortaya çıktı ve düzeltildi — sözleşmeyi
varsaymak yerine okumak gerekti: EF Core ucu **zip** döndürüyor (düz metin
değil), `codeschema/extract` `Files` alanını **sözlük** bekliyor (dizi değil),
`convert/analyze` `Source`/`Target` alan adlarını kullanıyor.

Sonuç: **30/32 çalışıyor, 0 kırık.** Kalan ikisi kodla ilgili değil —
Stripe fiyat kimlikleri girilmemiş, ve şema üretimi Groq ücretsiz katmanının
TPM duvarına çarpıyor (bkz. new-phase/34 §9.1).

## 13. Mobil — ikinci tur

§10'da "kapsam dışı" bırakılan **canvas düzenleyicisi** de mobile taşındı:

| Sorun | Çözüm |
|---|---|
| Araç çubuğu (`fixed top-2.5 right-6`, 10 düğme) 375px'te başlık şeridinin üstüne biniyordu | Mobilde başlığın ALTINDA, tam genişlikte, yatay kaydırılan şerit. Alta almak olmazdı — orada AI giriş kutusu var |
| Şema bilgi paneli tuvalin üçte birini kaplıyordu | Mobilde tek satır (ad + sayılar); branch denetimi ve tablo listesi masaüstüne özel |
| Panel araç çubuğunun altında kalıyordu | React Flow'un `.react-flow__panel { margin: 15px }` kuralı aynı özgüllükte ve sonra yüklendiği için kazanıyordu → `!mt-[120px]` |
| Minimap alttaki AI giriş kutusunu örtüyordu | 375px'te zaten okunmuyor → `md:` altında gizli |
| `scrollbar-none` sınıfı üç yerde kullanılıyordu ama **Tailwind çekirdeğinde yok** | `@utility scrollbar-none` eklendi |

## 14. Palet kısaltma, "yapay" görünüm, tablet ve büyük ekran (tamamlandı)

> **Sorulan iş:** "renk paletlerimizi daha da kısabiliriz ve renk temasını
> komple değiştirebiliriz şu an çok yapay duruyor, her cihaza (tablet/mobil)
> uyumlu mu bilmiyoruz, benim monitörümde her şey çok büyük gözüküyor."

### 14.1 Denetim betiğinin kendisi bir kontrol karakteri yüzünden ~2 hafta kör çalışıyordu

`check-design-rules.mjs`'e daha önce eklenen `RAW_FUNCTIONAL_COLOR` regex'i
(`rgba()`/`rgb()` yakalayan kural) hiçbir şeyi yakalamıyordu. Sebep: regex
kaynağının başında **görünmez bir U+0008 BACKSPACE karakteri** vardı —
muhtemelen önceki bir düzenlemede `\b` (regex kelime sınırı) bir Python
string'i içinde yazılmış ve Python onu backspace kaçış dizisi olarak
yorumlayıp dosyaya öyle yazmıştı. Terminalde, `Read` çıktısında, hatta
`.source` çıktısında GÖRÜNMÜYORDU — yalnızca kod noktası dökümüyle
(`codePointAt`) ortaya çıktı.

Temizlendiğinde denetim aynı anda **42 ihlal** buldu — hiçbiri daha önce
raporlanmamıştı:

| Bulunan | Nerede |
|---|---|
| `rgba(79,70,229,…)` — indigo-600, §2'nin **açıkça yasakladığı** aile | `ConfirmDialog.tsx` (onay butonu gölgesi) |
| `rgba(59,130,246,…)` — mavi-500 | `CanvasContextMenu.tsx` (bağlam menüsü gölgesi) |
| `rgba(239,68,68,…)`, `rgba(225,29,72,…)` — kırmızı/gül | `TableNode.tsx`, `ConfirmDialog.tsx` |
| `rgba(16,185,129,…)` — zümrüt | `TableNode.tsx` (başarı kenarlığı gölgesi) |
| 36 × `rgba(0,0,0,…)` | 30+ modal/panel gölgesi ve karartması |

Hepsi ilgili token'a (`--color-accent`, `--color-danger`, `--color-success`,
`--color-scrim`) `color-mix()` ile bağlandı. **Denetimin gerçekten
yakaladığı ayrıca doğrulandı**: yapay bir `rgba()` enjekte edilip çıkış
kodunun 1 döndüğü görüldü, sonra temizlendi.

**Ders:** "betik yeşil dönüyor" ile "kural uygulanıyor" aynı şey değil —
betiğin kendisinin de test edilmesi gerekiyordu, tam da §1'in söylediği gibi.

### 14.2 Palet kısaltıldı — 44 → 32 token

Ölçüldü: 44 `--color-*` token'ından **15'i hiçbir bileşende, hiçbir JS
köprüsünde kullanılmıyordu** (yalnızca kendi tanımlarında geçiyorlardı):
`--color-ink`/`-secondary`/`-muted` (3), `--color-ocean-dark`/`-mid`/`-light`
(3), `--color-primary-glow`/`-hover` (2), `--color-bg-elevated`/`-surface`/
`-surface-2` (3), düz `--color-line` (yalnızca `-strong`/`-solid`/
`-solid-strong` sürümleri kullanımdaydı) (1), `--shadow-neon`/`-hover`/
`--shadow-glass` (3). Hepsi kaldırıldı; `--color-bg-base` tek başına kaldı
çünkü PNG dışa aktarımın zemin rengi olarak gerçekten okunuyor
(`lib/designTokens.ts`).

### 14.3 "Yapay" görünümün ölçülebilir sebebi: aksan neredeyse zeminle aynı tondaydı

Vurgu rengi (`--brand-hue: 262`) nötr zeminin tonundan (`--ui-hue: 250`)
yalnızca **12° uzaktaydı** — göz, "burası zemin" ile "burası tıklanacak şey"i
neredeyse ayıramıyordu, arayüzün tamamı tek bir boğuk mavi-gri leke gibi
okunuyordu. Ayrıca 262° tam olarak indigo/mor ailesinin açısı — "her AI
ürünü aynı boğuk mora boyanmış" hissinin renk karşılığı buydu.

Vurgu **55° öteye**, camgöbeği/turkuaz ailesine (`--brand-hue: 195`)
taşındı ve doygunluğu bir tık artırıldı (`--brand-chroma: 0.075 → 0.09`).
Palet hâlâ desatüre ve "minimal lacivert" kimliğinde (parlak/neon değil),
ama artık aksan gerçekten AYRIŞIYOR — ve "veri/terminal aracı" hissini
jenerik "mor SaaS" paletinden daha net ayırıyor. `globals.css`'e beş alternatif
yön (mavi, yeşil-camgöbeği, bronz…) ve kaçınılacak açı aralıkları not
düşüldü — knob'u denemek isteyen tek satır değiştiriyor.

### 14.4 "Her şey büyük görünüyor" — TableNode 320px → 288px

Ölçüldü: 1920×1080'de %100 yakınlaştırmada bir tablo kartı ekranın
**%16,7'sini** kaplıyordu — yatayda altı taneden fazlası sığmıyordu. Bu,
"büyük monitörde her şey büyük" geri bildiriminin tek, ölçülebilir kaynağıydı
(kök font-size 16px, DPR 1, tarayıcı zoom'u %100 — büyütme yoktu; sorun
bileşenin kendi boyutuydu).

- Genişlik `w-80`(320px) → `w-72`(288px), köşe `rounded-2xl` → `rounded-xl`
- Başlık `py-3.5` → `py-2.5`, `text-base`(miras) → `text-sm`
- Satır `py-2.5` → `py-2`, kolon adı `text-sm`(14px) → `text-[13px]`, `truncate` eklendi (güvenlik: uzun ad artık taşmıyor, kesiliyor)
- `lib/schemaToFlow.ts`'deki yerleşim sabitleri SENKRON güncellendi (`NODE_HEADER` 64→48, `ROW_HEIGHT` 38→32, `GRID_SPACING_X` 400→340) — bileşenle sabitler birbirinden bağımsız kayarsa tablolar üst üste biner

Sonuç aynı ekranda ~%25 daha fazla tablo, aynı okunabilirlikte (tarayıcıda
yakından doğrulandı — bkz. ekran görüntüsü kaydı).

### 14.5 Tablet — gerçek bir kırık bulundu ve düzeltildi

768×1024 (tablet dikey) hiç test edilmemişti. Test edildiğinde: araç
çubuğu bu genişlikte **masaüstü moduna** geçiyordu (`md:` = 768px eşiği)
ama ~10 düğme + Approve CTA'sı bu genişliğe sığmıyordu — **Approve düğmesi
ekranın sağından tamamen taşıyordu**, birincil eylem tıklanamaz hâldeydi.

Kırılma noktası `md`(768px)'den `lg`(1024px)'e çekildi — hem araç çubuğunda
hem onunla senkron çalışması gereken şema bilgi panelinde (aksi hâlde
768-1023px aralığında ikisi üst üste binerdi, tam da §13'te mobilde
düzeltilen hatanın aynısı). 1024px'te (yeni eşik) tüm düğmelerin sığdığı
doğrulandı.

**Bulma şekli önemli:** yalnızca 375px (mobil) ve 1920px+ (masaüstü) test
edilmişti; 768-1023px aralığı hiç denenmemiş, "mobil kapsandı, masaüstü
kapsandı, ikisi arasında sorun olmaz" varsayılmıştı. Bu varsayım yanlıştı.

### 14.6 Sonuç

- Palet: **44 → 32 token**, hâlâ tek merkezden (§11), artık ölçülebilir
  şekilde de kısaltılmış
- Denetim: kendi kendini doğrulayan hâle getirildi (enjekte edilen ihlal
  gerçekten yakalanıyor)
- Büyüklük: TableNode %10 daha dar, satırlar %16 daha kısa — üç ölçekte de
  (375 / 768-1023 / 1920+) doğrulandı
- Tablet: gerçek bir "birincil eylem erişilemez" hatası bulundu ve düzeltildi
