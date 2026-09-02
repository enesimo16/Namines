# 08 — Tasarım Sistemi

> Vercel'in kontrol panelinden **düzen ve etkileşim** alınıyor. Renk paleti
> Namines'in kendi ölçülmüş token'ları.

---

## 1. Kabuk düzeni

```
┌──────────────────┬──────────────────────────────────────────────┐
│ [hesap ▾] Hobby  │  [proje ▾]         Ekran adı        [Agent]  │  56px üst şerit
├──────────────────┼──────────────────────────────────────────────┤
│ 🔍 Find      (F) │                                              │
│                  │                                              │
│ ▣ Projects       │              içerik                          │
│ ⬡ Deployments    │                                              │
│ ⋮⋮ Logs        › │                                              │
│ ⌁ Analytics      │                                              │
│ ⚙ Settings     › │                                              │
│                  │                                              │
│ ──────────       │                                              │
│ 👤 kullanıcı     │                                              │
└──────────────────┴──────────────────────────────────────────────┘
   240px sabit                    esnek
```

Vercel'den birebir alınan davranışlar:

- **Sol sütun kalıcı** — ekran değişse de yerinde kalır, seçili öğe vurgulu
- **Üstte iki kademeli bağlam** — hesap (sol üst) ve proje (üst orta)
- **Proje seçilmemişken** "All Projects", seçiliyken proje adı + geçiş oku
- Alt köşede kullanıcı, yanında bildirim rozeti
- Alt-ekranı olan öğelerde `›` (Logs, Settings) → ikinci seviye sol panel açar

### İkinci seviye (Vercel'in Observability/Firewall deseni)

Bir bölüme girildiğinde sol sütun **o bölümün** alt sayfalarına dönüşür, üstte
`‹ Geri` ile ana gezinmeye dönülür. Aynısı Desk'te Settings ve Logs için.

---

## 2. Renk

Ana uygulamanın ölçülmüş paleti kullanılıyor (`frontend/app/globals.css`'ten
**değer olarak** alındı, dosya import edilmedi — mikroservis sınırı):

```
--surface-900  #0b0b0b   zemin
--surface-800  #161616   panel / kart
--surface-700  #222222   kontrol
--surface-600  #2e2e2e   hover

--content-primary   #eceff1
--content-secondary #c8cbce
--content-muted     #acb2b7
--content-subtle    #999fa6

--accent        #006d6d  |  --accent-hover #198585
--accent-text   #76a9a9  |  --accent-subtle #001919

--line #2b2b2b  |  --line-strong #404040
--danger #af3c3a |  --success #267b4c
```

**Kural:** aksan rengi (teal) yalnızca *seçili durum* ve *birincil eylem* için.
Grafiklerde kategori ayırmak için renk kullanılmaz — yalnızca **başarı/hata**
semantik renk taşır ([`07`](07-ANALYTICS.md)).

### Neden Vercel'in mavisi alınmıyor

Vercel'in grafik mavisi onların marka rengi. Namines'in aksanı teal ve tüm
ürün boyunca tutarlı; ikinci bir marka rengi katmak iki ürün gibi gösterirdi.

---

## 3. Ölçek

Ana uygulamanın Vercel uyarlamasından (`VERCEL_DESIGN_ADAPTATION.md`) devralınan
kurallar:

| Token | Değer | Kullanım |
|---|---|---|
| `--radius-control` | 6px | buton, input, rozet |
| `--radius-card` | 10px | kart, panel |
| `--radius-modal` | 14px | modal, açılır menü |

**Kontrol yüksekliği tek: 36px.** Ana uygulamada bunun ihlali (24/32/36 karışık)
gerçek bir hizasızlık yaratmıştı ve düzeltildi; Desk aynı hatayı tekrarlamayacak.

Tipografi: 11px (etiket) · 12.5px (gövde) · 15px (başlık). 10px altı **yasak** —
ana uygulamanın denetim betiği de bunu zorluyor.

---

## 4. Bileşenler

v0.1'de yazılanlar (`services/desk/app/globals.css`): `.btn` (+ `-primary`,
`-danger`, `-sm`), `.grid-wrap` + tablo, `.field`, `.dialog`, `.notice`,
`.empty`, `.table-item`, `.pager`.

v1'de eklenecekler:

| Bileşen | Nerede |
|---|---|
| `.card` — proje kartı | [`02`](02-PROJECTS.md) |
| `.stat` — metrik kutusu | [`07`](07-ANALYTICS.md) |
| `.badge` — durum rozeti (nötr/başarı/hata/uyarı) | [`05`](05-DEPLOYMENTS.md), [`06`](06-LOGS.md) |
| `.filter-panel` — sol filtre sütunu | [`06`](06-LOGS.md) |
| `.timeline` — zaman çizelgesi çubuğu | [`06`](06-LOGS.md) |
| `.breadcrumb` — hesap › proje › ekran | kabuk |

---

## 5. Erişilebilirlik — baştan

Ana uygulamada erişilebilirlik sonradan denetlendi ve açık kaldı
(`UI_UX_PRODUCT_AUDIT.md` Y2/Y3). Desk'te baştan kural:

- Her ekranda **tek `<h1>`**
- `<nav>` / `<main>` / `<aside>` landmark'ları
- `outline-none` **yalnızca** yerine görünür bir `focus-visible` konursa
- İkon-only her düğmede `aria-label`
- Dokunma hedefi en az 44px (görsel boyut küçük olabilir)
- Renk tek başına anlam taşımaz — durum rozetinde metin de olur

---

## 6. Boş ve hata durumları

Her liste ekranının üç hâli tasarlanır: **dolu · boş · hatalı.**

- **Boş** neden boş olduğunu söyler ("Bu dönemde yazma işlemi yok. Okuma
  istekleri kaydedilmiyor.")
- **Hata** sunucunun ham mesajını taşır — "bir hata oluştu" yasak
- **Yükleniyor** iskelet (skeleton) kullanır, spinner değil

---

## 7. Hareket

`--ease-out: cubic-bezier(0.2, 0, 0, 1)` · 120ms (hover) / 180ms (panel).

`prefers-reduced-motion` mutlaka desteklenir. Grafiklerde giriş animasyonu yok —
veri okumayı geciktirir.
