# 04 — Frontend denetimi (Next.js / React)

> ## ✅ DURUM (2026-09-09)
> | Bulgu | Durum |
> |---|---|
> | FE-001 `frontend` testi yok | 🟡 Açık — P1, L |
> | FE-002 `AIPreferencesModal` 1.521 satır | 🟡 Kısmî — sahte jeton bloğu ve ölü kod çıktı, hâlâ büyük |
> | FE-003 `dangerouslySetInnerHTML` | ✅ Mermaid `strict` (SEC-004) |
> | FE-004 `localStorage` yayılımı | ✅ Sahte jeton verisi temizleniyor; tercihler kaldı (kabul edilebilir) |
> | FE-005 Merkezî API istemcisi baypası | 🟡 Açık — P2 |
> | FE-006 `console.log` | ✅ `removeConsole` (error/warn korunuyor) |
> | FE-007 Bundle ölçülmedi | 🟡 Açık — önce ölçülmeli |
> | **FE-008 Lint CI'da koşmuyor** | 🆕 **Yeni bulgu** — aşağıda |

**Ölçüm:** `frontend/` 146 dosya, 31.658 satır. `services/desk/` 44 dosya, 6.274 satır.
Next.js 16.2.6, React 19.2.4, App Router, Tailwind 4.

---

## İki ayrı frontend var — ve bu bilinçli

| | `frontend/` | `services/desk/` |
|---|---|---|
| Rol | Tasarım tuvali, AI, derleme, paylaşım | Barındırılan deterministik CRUD arayüzü |
| Rotalar | 12 | Tek sayfa + bileşen modülleri |
| Test | **Yok** | **99 vitest testi** ✅ |
| Typecheck | `next build` | Ayrı `tsc --noEmit` script'i ✅ |
| Backend'e bağı | axios + fetch | Yalnızca HTTP (mikroservis sınırı) |

`services/desk`, `frontend`'den **daha disiplinli**: kendi typecheck'i, kendi
test paketi, açık mikroservis sınırı (package.json açıklamasında yazılı).
Bu fark tesadüfi değil, Desk sonradan ve daha olgun bir yaklaşımla yazılmış.

---

## FE-001 — `frontend/` için otomatik test YOK

### Finding
31.658 satırlık ana uygulamada birim/bileşen testi bulunamadı. Yalnızca üç
"check" script'i var: `check:design`, `check:templates`, `check:e2e` — bunlar
lint benzeri statik kontroller, test değil.

### Location
`frontend/package.json` — `scripts` bloğunda `test` yok.

### Problem
Karşılaştırma aynı depodan: `services/desk` 6.274 satır için 99 test yazmış.
`frontend` beş katı büyüklükte ve sıfır teste sahip. Şema düzenleme, geri alma
(`useProjectHistoryStore`, 711 satır), sürüm çakışması gibi mantığı yoğun
alanlar tamamen elle doğrulanıyor.

### Risk
Regresyonlar yalnızca kullanıcı tarafından fark ediliyor. `AGENTS.md`'nin kendi
dersi burada geçerli: *"testler geçiyor hiçbir şey kanıtlamıyor"* — burada
geçecek test bile yok.

### Severity **HIGH** · ### Effort L · ### Priority P1

### Recommendation
Tamamını test etmeye çalışma; **mantık yoğun store'lardan başla**:
`useSchemaStore` (804 satır), `useProjectHistoryStore` (711 satır),
`lib/templates.ts` (1.942 satır). Bunlar saf TypeScript — React render'ı
gerekmeden vitest ile test edilebilir. Desk'te zaten kurulu düzen kopyalanabilir.

---

## FE-002 — `AIPreferencesModal.tsx` 1.521 satır

### Location
`frontend/components/canvas/panels/AIPreferencesModal.tsx`

### Problem
Tek bir modal bileşeni; içinde AI ayarları, istatistikler, profil kaydetme,
sahte jeton yönetimi (bkz. SEC-003) ve tema tercihleri var. Beş ayrı sorumluluk.

Ayrıca **11 ayrı `localStorage` anahtarı** doğrudan bu dosyadan okunup
yazılıyor (`namines-ai-max-tokens`, `namines-ai-sql-pretty`, `namines-stats-*` …).
Anahtar isimleri string olarak dağınık — yazım hatası derleme zamanında
yakalanmaz.

### Severity **MEDIUM** · ### Effort M · ### Priority P2

### Recommendation
1. Sekme başına bileşene böl (Ayarlar / İstatistik / Profil).
2. `localStorage` erişimini tek bir tipli modüle topla
   (`lib/preferences.ts`, anahtarlar `const` olarak).

---

## FE-003 — `dangerouslySetInnerHTML` — iki kullanım, biri riskli

| Konum | Değerlendirme |
|---|---|
| `app/layout.tsx:40` | Muhtemelen tema/JSON-LD script'i — statik, **risk düşük** |
| `components/compile/MermaidPreview.tsx:53` | **Riskli** — bkz. [SEC-004](06-security-audit.md#sec-004--mermaid-securitylevel-loose--dangerouslysetinnerhtml) |

---

## FE-004 — `localStorage` yayılımı

**80 kullanım** var. JWT **yok** (doğru), ama şunlar var:
- Kullanıcı tercihleri (kabul edilebilir)
- İstatistik sayaçları (kabul edilebilir)
- Sahte API jetonları (SEC-003 — kaldırılmalı)

`services/desk/lib/auth.ts` `sessionStorage` kullanıyor ve **neden
`localStorage` olmadığını yorumda açıklıyor**. Aynı titizlik `frontend`'de yok.

### Severity **LOW** · ### Priority P3

---

## FE-005 — Merkezî API istemcisi var, ama tek değil

`frontend/services/api.ts` (657 satır) merkezî bir istemci. Ancak
`components/compile/DbPushModal.tsx` doğrudan `fetch(...)` çağırıyor
(satır 48, 79).

### Problem
Merkezî istemciyi baypas eden çağrılar, oradaki ortak davranışı da kaçırır:
hata biçimlendirme, 401 yönlendirmesi, timeout, iptal. Bu iki çağrı tam da
**en tehlikeli** uca (executor) gidiyor.

### Severity **MEDIUM** · ### Effort S · ### Priority P2

### Recommendation
`DbPushModal`'ı `services/api.ts` üzerinden geçir. Kural olarak: ham `fetch`
yalnızca `services/` altında.

---

## FE-006 — React desenleri

Örnekleme yapıldı (`canvas/page.tsx`, `TableEditorDrawer`, `ToolbarPanel`,
`useSchemaStore`):

| Kontrol | Sonuç |
|---|---|
| State yönetimi | Zustand, store'lar sorumluluğa göre ayrılmış ✅ |
| Prop drilling | Store sayesinde sınırlı ✅ |
| `useEffect` bağımlılık hataları | Örneklemede görülmedi ✅ |
| Bellek sızıntısı (temizlenmeyen abonelik) | Örneklemede görülmedi ✅ |
| `console.log` | **7 adet** — üretim derlemesinde temizlenmeli |
| Gereksiz re-render | Ölçülmedi ⚠️ — profil çıkarılmadı |

`console.log` sayısı (7) ihmal edilebilir; yine de `next.config` içinde
`compiler.removeConsole` ile üretimde silinmesi P3 bir iş.

---

## FE-007 — Bundle ve performans: ÖLÇÜLMEDİ

Bu denetimde `next build` çalıştırılıp bundle analizi yapılmadı. Ancak
bağımlılık listesinden **yapısal** riskler görünüyor:

| Paket | Not |
|---|---|
| `mermaid` ^11.15 | Büyük; yalnızca `/compile` rotasında gerekli → **dinamik import edilmeli** |
| `sql.js` ^1.14 | WASM, ~1 MB+ → aynı şekilde |
| `@xyflow/react` ^12.10 | Canvas'ta gerekli, kabul |
| `prismjs` | Küçük |

### Recommendation
`mermaid` ve `sql.js` için `next/dynamic` + `ssr: false`. Bu ikisi muhtemelen
ilk yüklemenin en büyük payı ve **ana sayfada hiç gerekmiyorlar**.

### Severity **MEDIUM** (doğrulanmadı) · ### Effort S · ### Priority P2

> ⚠️ **Doğrulanmadı:** bundle boyutu ölçülmedi. Yukarıdaki öneri paket
> boyutlarına dair genel bilgiye dayanıyor; uygulamadan önce
> `@next/bundle-analyzer` ile ölçülmeli.


---

## FE-008 — `npm run lint` var ama hiç koşmuyor (YENİ BULGU)

> Bu bulgu ilk denetimde **kaçırıldı**. Düzeltmeleri uygularken `npx eslint .`
> çalıştırıldığında ortaya çıktı — denetimin kendi sınırının kanıtı: statik
> tarama, çalıştırılmayan bir aracın çıktısını göremez.

### Finding
`frontend/package.json` bir `lint` script'i tanımlıyor
(`"lint": "eslint"`), ama `.github/workflows/ci.yml` onu **hiç çağırmıyor**.
Sonuç, ölçüldü:

```
✖ 207 problems (129 errors, 78 warnings)
```

### Location
`frontend/package.json` (script var) · `.github/workflows/ci.yml` (çağrı yok)

### Problem
CI `tsc --noEmit` ve `next build` koşuyor — ikisi de tip hatalarını yakalar
ama **lint kurallarını yakalamaz**. Bu yüzden 129 hata sessizce birikti.

Bunların çoğu `@typescript-eslint/no-explicit-any`. Ama aralarında **gerçek
React hataları** da var:

| Kural | Ne demek |
|---|---|
| `react-hooks/set-state-in-effect` | Efekt içinde senkron `setState` → basamaklı render |
| `Cannot access variable before it is declared` | Sıralama değişirse bozulacak kod |
| `react-hooks/immutability` | Değiştirilemez değere yazma |

Yani bu, "stil borcu" değil; içinde davranışsal riskler var.

### Risk
Lint hatası birikmiş bir kod tabanında yeni bir gerçek hata **görünmez** olur:
130. hata, 129'un arasında fark edilmez.

### Severity **MEDIUM**

### Recommendation — sırayla
1. **CI'a şimdi eklemeyin.** Bugün eklenirse build anında kırmızıya döner ve
   ekip onu devre dışı bırakmayı öğrenir; bu, hiç eklememekten kötüdür.
2. Önce **davranışsal** kuralları düzeltin (`react-hooks/*` ailesi — tahminen
   10-15 yer). Bunlar gerçek hata.
3. `no-explicit-any`'leri ayrı bir geçişte temizleyin ya da bilinçli olarak
   uyarıya düşürün.
4. **Sonra** CI'a `npm run lint` ekleyin ve kırmızıyı orada tutun.

### Bu denetimde ne yapıldı
Dokunulan dosyalardaki lint hataları düzeltildi:
`AIPreferencesModal` **10 → 1** (kalan: modal açılırken formu `localStorage`'dan
dolduran 15+ `setState`; düzeltmesi 20 state'lik bir refactor gerektiriyor ve
davranış riski, stil kazancından büyük — bilinçli olarak bırakıldı).

Geri kalan 128 hata **açık** ve backlog'a alındı (B-57).

### Effort L · ### Priority P1
