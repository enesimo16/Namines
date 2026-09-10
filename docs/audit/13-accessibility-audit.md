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

| Kontrol | Nasıl | Süre |
|---|---|---|
| Renk kontrastı (WCAG AA 4.5:1) | Lighthouse / axe DevTools | 1 saat |
| Klavye ile tam gezinme | Elle: sadece Tab/Enter/Esc | 2 saat |
| Odak görünürlüğü (`:focus-visible`) | Elle + CSS taraması | 1 saat |
| Ekran okuyucu ile ana akış | NVDA / VoiceOver | Yarım gün |
| Başlık hiyerarşisi (h1→h2→h3) | axe | 1 saat |

**Not:** `FRONTEND.md` sabit bir renk paleti tanımlıyor. Kontrast oranları o
paletin kendisinde bir kez ölçülürse, tüm uygulama için ölçülmüş olur — bu
denetimin en verimli ilk adımı.
