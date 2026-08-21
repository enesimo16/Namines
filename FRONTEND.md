# Namines — Frontend Tasarım Talimatları

> Bu dosya `frontend/` altında **görsel/UX** işi yaparken okunur. Backend/mimari
> işi için [CLAUDE.md](CLAUDE.md)'ye bak. İkisi birbirini geçersiz kılmaz —
> CLAUDE.md genel proje kuralları, bu dosya sadece frontend/tasarım kuralları.

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
