# 13 — Erişilebilirlik denetimi

> ⚠️ Bu denetim **statik**tir: ekran okuyucu ile test edilmedi, otomatik
> a11y tarayıcısı (axe, Lighthouse) çalıştırılmadı, klavye ile gezilmedi.
> Aşağıdakiler kod taramasından çıkan **sayısal göstergeler** ve bunların
> işaret ettiği riskler.

---

## Ölçülen göstergeler

| Gösterge | Sayı | Yorum |
|---|---|---|
| `aria-*` / `role=` kullanımı | 151 | Var ve azımsanmayacak düzeyde |
| `<button>` öğesi | 305 | — |
| `<input>` öğesi | 51 | — |
| `htmlFor` (label bağlama) | **5** | ⚠️ Asıl bulgu |
| Radix UI bileşenleri | Dialog, Tooltip, ContextMenu | Erişilebilirliği hazır gelir ✅ |

---

## A11Y-001 — Form alanlarının çoğunda programatik etiket yok

### Finding (SAYIM DÜZELTİLDİ — 10.09.2026)

İlk sayım **eksikti**: "51 `<input>`, 5 `htmlFor`". Yalnızca `htmlFor`
sayılmıştı; `aria-label` ve **saran `<label>`** (örtük etiketleme) de tam
geçerli yollar ve depoda ikisi de kullanılıyordu.

Her etiketi tek tek açan bir ölçümle gerçek tablo:

```
TOPLAM form alanı: 118   (input + textarea + select, frontend + desk)
ETİKETLİ         :  41
    aria-label               33
    örtük <label>            19   ← ilk sayımda hiç görülmedi
    id<->htmlFor             21
    etiketsiz tür (hidden…)   6
ETİKETSİZ        :  77
```

Yani sorun gerçekti (77 alan), ama **51/5 tablosu iki yönden de yanlıştı**.

### Location
`frontend/` genelinde; en yoğun form alanları
`components/canvas/TableEditorDrawer.tsx` ve
`components/canvas/panels/AIPreferencesModal.tsx` içinde.

### Current State
Alanlar görsel olarak etiketli olabilir (yanında metin duruyor olabilir), ama
`<label htmlFor>` ya da `aria-label` ile **programatik** bağ yoksa ekran
okuyucu alana geldiğinde "edit text" der, ne olduğunu söylemez.

### Problem
WCAG 2.1 AA — **1.3.1 Info and Relationships** ve **4.1.2 Name, Role, Value**
ihlali. Ayrıca etiket tıklanabilir olmadığı için ince motor kontrolü sınırlı
kullanıcılar için hedef alanı küçülüyor (2.5.5 ile ilişkili).

### Risk
Ürün kamu sektörüne ya da erişilebilirlik yükümlülüğü olan bir kuruma
satılamaz. Avrupa'da EAA (European Accessibility Act) kapsamı genişliyor.

### Severity **HIGH** (uyum açısından) / MEDIUM (bugünkü kullanıcı için)
### Yapıldı (10.09.2026) — 117 / 118

```
ETİKETLİ         : 117
    aria-label               67
    örtük <label>            23
    id<->htmlFor             21
    etiketsiz tür             6
ETİKETSİZ        :   1
```

Kalan **1** tanesi `components/compile/PanelKit.tsx:45` ve **gerçek bir alan
değil** — bir açıklama yorumunun içinde geçen `` `<select>` `` metni.
Ölçüm betiğinin yanlış pozitifi; kod değişikliği gerekmiyor.

#### Bu işte yapılan bir hata ve neden geri alındı

İlk deneme etiketleri **placeholder metninden** üretti (38 alan). Sonuç
yarısında yanlıştı:

| Üretilen etiket | Alanın gerçek adı |
|---|---|
| `aria-label="John Doe"` | Full Name |
| `aria-label="600"` | Session timeout (saniye) |
| `aria-label="https://github.com/username"` | GitHub Profile URL |
| `aria-label="ör. customers"` | Tablo |
| `aria-label="public class AppDbContext : DbContext {"` | DbContext C# source code |

Placeholder bir **örnek değer**, alanın adı değil. Ekran okuyucunun "John Doe,
edit text" demesi hiç etiket olmamasından **daha kötü**: kullanıcıyı yanlış
yönlendirir. 38 ekleme geri alındı ve her alan görünen etiket metniyle tek tek
etiketlendi.

#### Kural olarak benimsenen
1. Etiket, **görünen metnin aynısı** (WCAG 2.5.3 Label in Name — sesli komut
   kullanıcısı ekranda gördüğünü söyler).
2. Saran `<label>` varsa **dokunulmaz**: `aria-label` görünen metni EZER ve
   2.5.3'ü ihlal ederdi.
3. Placeholder yalnızca zaten alanın adıysa kullanılır (ör. "Email address",
   "Password", "Branch name").

### Kalan iş
`<label htmlFor>` ile **tıklanabilir** etiket (WCAG 2.5.5, hedef alanını
büyütür) yapılmadı. `aria-label` 1.3.1 ve 4.1.2'yi karşılıyor;
tıklanabilirlik ayrı bir iyileştirme ve her alanın görünür bir `<label>`'ı yok.

### Effort M (yapıldı) · ### Priority P2

---

## A11Y-002 — Radix UI kullanımı: GÜÇLÜ YAN

`@radix-ui/react-dialog`, `react-tooltip`, `react-context-menu` kullanılıyor.
Bu bileşenler odak tuzağı (focus trap), `Esc` ile kapatma, `aria-modal`,
odak geri dönüşü gibi davranışları **hazır** getiriyor.

Yani modal erişilebilirliği — en sık batırılan alan — muhtemelen iyi durumda.
**Bulgu yok**, ama doğrulanmadı.

---

## A11Y-003 — Canvas'ın erişilebilirliği: yapısal sınır

Ürünün kalbi `@xyflow/react` ile sürükle-bırak bir şema tuvali. Bu tür
arayüzler **doğası gereği** klavye ve ekran okuyucu ile zor kullanılır.

### Değerlendirme
Bu bir "hata" değil, ürün kararı. Ama karşılığı olmalı: aynı işi yapan
**klavyeyle erişilebilir bir alternatif**. Depoda bunun adayı zaten var:
`.nsl` metin dili ve `/compile` rotası — şema metinle tanımlanabiliyor.

### Recommendation
`.nsl` yolunu "erişilebilir alternatif" olarak **belgele ve öne çıkar**.
Bu, sıfırdan bir çözüm geliştirmeden uyum argümanı kazandırır.
### Effort XS (belgeleme) · ### Priority P3

---

## Denetlenmeyen ama gerekli kontroller

Aşağıdakiler bu oturumda **yapılamadı** ve yapılmalı:

### ✅ Renk kontrastı ÖLÇÜLDÜ (10.09.2026) — B-42 kısmen

Ölçüm tarayıcıda, `oklch()` → lineer sRGB → WCAG bağıl parlaklık dönüşümüyle
yapıldı. **Yöntem doğrulandı:** siyah/beyaz için 21.00:1 döndürüyor.

> **İlk denemem yanlıştı ve 717 "hata" üretti.** Palet Tailwind 4 ile
> `oklch()` kullanıyor; benim ayrıştırıcım `rgb()` varsayıyordu ve
> `oklch(0.95 0.004 250)` değerlerini RGB sanıp neredeyse her öğe için
> 1.00:1 hesaplıyordu. Ekran-okuyucu-özel metinler de sayıma girmişti.
> Bu sayı **hiç rapor edilmedi** — dönüşüm düzeltilip yöntem bilinen
> değerlerle sınandıktan sonra ölçüm tekrarlandı.

#### İki SİSTEMİK hata bulundu ve düzeltildi

**1) Açık temada semantik metin token'ları BEYAZ oluyordu** (ciddi)

Açık tema `--ui-bg-l`'yi %98.5 yapıyor, ama semantik metin token'larını
override etmiyordu. O token'lar koyu tema için yazılmış formülü kullanıyor:

```
--success-text: oklch(calc(var(--ui-bg-l) + 55%) …)
   %98.5 + %55 = %153.5  →  kırpılır  →  oklch(100%) = BEYAZ
```

Yani açık temada beyaza yakın yüzey üstünde **beyaz metin**. Ölçülen:

| Token | Önce | Sonra | En koyu açık yüzeyde (`--surface-600`) |
|---|---|---|---|
| `--success-text` | **1.07:1** | **7.36:1** | 4.55:1 ✅ |
| `--danger-text` | **1.01:1** | **7.38:1** | 4.56:1 ✅ |

**135 çağrı noktası** etkileniyordu (`text-success-text`, `text-danger-text`).
Düzeltme: açık temaya mutlak değerli override eklendi (`globals.css`).
"+%55" formülü zemin koyu olduğunda doğru; açık temada metnin
**koyulaşması** gerekiyor.

**2) Zemin token'ları METİN olarak kullanılıyordu**

| Sorun | Ölçülen | Doğru token | Ölçülen |
|---|---|---|---|
| `text-success` (L=%52, bir ZEMİN tonu) | 3.47:1 | `text-success-text` | 7.02:1 |
| `bg-accent` + `text-surface-900` | 3.15:1 | `bg-accent` + `text-accent-on` | 6.25:1 |

İkincisi **yalnızca koyu temada** hatalıydı: `--surface-900` temayla dönüyor
(koyu temada neredeyse siyah), aksan dönmüyor. Açık temada aynı sınıf
6.40:1 veriyordu — bu yüzden hata tek temada görünüyor ve kolayca gözden
kaçıyordu. Çözüm: temayla DÖNMEYEN yeni bir `--accent-on` token'ı
(beyaza yakın; aksan her iki temada da orta-koyu olduğu için ikisinde de
geçiyor).

38 + 14 = **52 çağrı noktası** düzeltildi.

#### Ölçüm sonucu

| Sayfa | Tema | Ölçülen öğe | Başarısız |
|---|---|---|---|
| `/canvas` | koyu | 426 | **0** ✅ |
| `/security` | koyu | 82 | **0** ✅ |
| `/new` | koyu | 7 | **0** ✅ |
| `/` (açılış) | koyu | 296 | **0** ✅ (düzeltmeden önce 10) |
| `/` (açılış) | **açık** | 300 | **78** ❌ |

#### Açık kalan: açılış sayfası AÇIK TEMA (78 hata)

Baskın sebep `--namines-gradient-headline`: %78-84 açıklıkta oklch tonları,
koyu zemin için tasarlanmış. Açık temada başlıklar 1.59-1.69:1'e düşüyor.
Bu bir token yanlışı değil, **açılış sayfasının açık temada hiç
tasarlanmamış** olması — ayrı bir tasarım işi, tek satırlık düzeltmesi yok.

### Hâlâ DENETLENMEYEN kontroller

| Kontrol | Nasıl | Süre |
|---|---|---|

| Klavye ile tam gezinme | Elle: sadece Tab/Enter/Esc | 2 saat |
| Odak görünürlüğü (`:focus-visible`) | Elle + CSS taraması | 1 saat |
| Ekran okuyucu ile ana akış | NVDA / VoiceOver | Yarım gün |
| Başlık hiyerarşisi (h1→h2→h3) | axe | 1 saat |

**Not:** `FRONTEND.md` sabit bir renk paleti tanımlıyor. Kontrast oranları o
paletin kendisinde bir kez ölçülürse, tüm uygulama için ölçülmüş olur — bu
denetimin en verimli ilk adımı.
