# Frontend lint borcu

**Ölçüm tarihi:** 2026-09-12 · `cd frontend && npx eslint .`

| | Başlangıç | Şimdi |
|---|---|---|
| **Hata** | 149 | **0** |
| Uyarı | — | 24 |
| `@typescript-eslint/no-explicit-any` | 26 | **0** |

`npm run lint` artık CI'da (`.github/workflows/ci.yml`, Frontend job). ESLint
uyarıda çıkış kodu 0 döndürdüğü için adım yalnızca **hata** seviyesinde kırıyor
— yani bu dosyadaki kalan maddeler CI'ı kırmıyor ama görünür kalıyor.

---

## Kalan uyarılar (24)

### 1. `react-hooks/set-state-in-effect` — 16 yer

**Kural neden `warn`'a indirildi (`frontend/eslint.config.mjs`):** kural
yalnızca söz dizimine bakmıyor, çağrı grafiğini de izliyor ve `await`'in
ötesini ayırt edemiyor. Şu kod işaretleniyor:

```ts
useEffect(() => { fetchTeam().finally(() => setIsLoading(false)); }, []);
```

Burada senkron hiçbir `setState` yok — `fetchTeam` ilk satırında `await`
ediyor. **Bu ölçülmüş bir sonuç:** `TeamModal` ve `CrossDatabasePanel` tam
olarak bu biçime dönüştürüldü, kural yine hata verdi. Yani "monte olurken veri
çek" desenini kuralı memnun edecek şekilde yazmanın bir yolu yok; React'in
kendi dokümanı ise bu deseni açıkça meşru sayıyor.

Kural tümden kapatılmadı: yeni ihlaller hâlâ görünüyor.

**Kuralın HAKLI olduğu ve düzeltilen yerler:**

| Dosya | Ne yapıldı | Kazanç |
|---|---|---|
| `ThemeToggleButton.tsx` | `useSyncExternalStore` | hidrasyon bekçisi 2 render → 1 |
| `ProductionScreen.tsx` | `useSyncExternalStore` | `prefers-reduced-motion` ilk karede DOĞRU (eskiden bir kare yumuşak kaydırma) |
| `Header.tsx` | efekt silindi | `startEditing` zaten atıyordu; efekt gereksizdi |
| `CommandPalette.tsx` | sıfırlama efektten olay işleyicisine | liste kısalınca geçersiz satırın işaretli göründüğü ara kare yok |
| `CanvasSearch.tsx`, `CommandPalette.tsx` | kapalıyken monte edilmiyor | durum montajla sıfırlanıyor |
| `TeamModal.tsx`, `CrossDatabasePanel.tsx` | kapalıyken monte edilmiyor | yeniden açılışta **önceki** ekibin/projenin verisi bir kare görünmüyordu artık |

**Kalan 16 yer** (hepsi "monte olurken/açılırken veri çek" ya da
zamanlayıcı/abonelik deseni):

`app/demo/page.tsx:132` · `app/review/page.tsx:54` ·
`TableEditorDrawer.tsx:45` · `AIPreferencesModal.tsx:369` ·
`CrossDatabasePanel.tsx:105` · `DockerSandboxPanel.tsx:157` ·
`EjectPanel.tsx:68` · `PrismaPreview.tsx:57` · `ReadmePreview.tsx:47` ·
`DatabasePromptVisualizer.tsx:27` · `PlanScreen.tsx:46` ·
`RotatingHeadline.tsx:28` · `TeamModal.tsx:80` · `MigrationWizard.tsx:74,85` ·
`TourOverlay.tsx:72`

### 2. `react-hooks/exhaustive-deps` — 4 yer

Hepsi satır içi `eslint-disable` ile ve gerekçesiyle işaretli: bağımlılık
listesine eklenmesi istenen değer her render'da yeniden üretilen bir fonksiyon
ve eklendiğinde sonsuz döngü oluşuyor.

### 3. `@next/next/no-img-element` — 3 yer

`app/new/page.tsx:357`, `app/share/[token]/page.tsx:175`,
`VisionUploadModal.tsx:330`. Üçü de **kullanıcının o anda seçtiği dosyanın**
`blob:`/`data:` önizlemesi. `next/image` bu kaynakları optimize edemez
(uzak desen tanımı gerekir, blob'da mümkün değil), o yüzden `<img>` doğru
araç. LCP etkisi yok: hiçbiri ilk boyamada görünmüyor.

---

## Bu turda ayrıca temizlenen şeyler

- **26 `any` → 0.** Şekiller uydurulmadı; `frontend/types/api.ts` tipleri
  yanıtları zaten tüketen bileşenlerden okunarak yazıldı (ilk üç tahminim
  yanlıştı ve `tsc` yakaladı).
- **`MergeConflictItem` ayrımlı birleşime dönüştü.** Önce `sourceValue`/
  `targetValue` `any` idi ve `ConflictResolverModal` her dalda farklı bir şekil
  varsayıyordu; varsayım yanlış olsa derleyici susar ve birleştirme **yanlış
  şema** üretirdi. Artık yanlış dal derlenmiyor.
- **25 ölü bağlama silindi.** İkisi ölüden fazlasıydı:
  `ConfirmDialog`'daki `accent` tasarım kurallarının yasakladığı `indigo`
  değerini taşıyordu; `check-e2e.mjs` "BEKLİYOR" sayısını hesaplayıp özete
  yazmıyordu (o durumdaki uçlar raporda sessizce kayboluyordu).
- **İki TDZ ihlali** (`CanvasExportToolbar`, `DockerSandboxPanel`): efektler
  kullandıkları `const` fonksiyondan önce duruyordu. Çalışma zamanında
  patlamıyordu (kapanış mount sonrası çağrılıyor) ama akış senkronlaşsa
  `ReferenceError` verirdi. Efektler bildirimden sonraya taşındı.
- **`check-templates.mjs`** artık API'ye ulaşamama **sebebini** yazdırıyor;
  eskiden bağlantı reddi ile 500 ayırt edilemiyordu.
