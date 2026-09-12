# 22 — Ürün backlog'u

> ## ✅ DURUM (2026-09-10): **45 kapandı, 19 açık**
>
> P0'ların 6/6'sı kapalı.
>
> **Geri çekilen yanlış pozitifler (2):**
> - **B-24** — `pageSize` tavanı zaten vardı (`Math.Clamp(1, 200)`).
> - **B-54** — Desk gezinmesi zaten "Yedekler" / "Barındırma" diyor.
>
> **10.09.2026'da kapananlar:**
> B-39, B-41, B-46, B-52, B-53, B-59, B-61, B-64 (altyapı/güvenlik/erişilebilirlik)
> ve B-21, B-22 (sorgu geçmişi + kaydedilmiş sorgular, canlı doğrulandı).
>
> **Açık kalan P1'ler:** B-12 (MSSQL/Oracle FK canlı doğrulama — Docker/disk
> engeli), B-15 (frontend store testleri), B-20 (demo→hesap), B-26 (Ground
> strateji kararı), B-57 (eslint).
>
> Yeni regresyon testleri: `SecurityHardeningTests.cs` (23),
> `SecurityStampValidationTests.cs` (7), `DockerServiceConstructionTests.cs` (5),
> `SqlWorkbenchServiceTests.cs` (18).


Tüm bulgular tek listede. **P0 = üretime çıkmadan önce**, P1 = yüksek,
P2 = orta, P3 = düşük.

Efor: XS (<1s) · S (<1g) · M (1-3g) · L (1hafta+) · XL (1ay+)

---

## P0 — STOP SHIP

| ID | Kategori | İş | Kaynak | Efor |
|---|---|---|---|---|
| ~~B-01~~ | Security | ✅ JWT fallback `!IsDevelopment()` + fallback eşitlik kontrolü | SEC-005 | XS |
| ~~B-02~~ | Security | ✅ Mermaid `securityLevel: 'strict'` | SEC-004 | XS |
| ~~B-03~~ | Security | ✅ Ekran + state + handler + localStorage verisi kaldırıldı | SEC-003 | S |
| ~~B-04~~ | Security | ✅ `SqlExecutionAudit` + migration; proje bağı **isteğe bağlı** bırakıldı (ürün kararı) | SEC-001 | M |
| ~~B-05~~ | Security | ✅ `CsrfProtectionMiddleware` + frontend başlığı + 8 test | AUTHZ-004 | S |
| ~~B-06~~ | DevOps | ✅ `docker-compose.prod.yml` — migrate profili, kaynak limitleri, health check, sırlar zorunlu | DEVOPS-001 | M |

**P0 toplamı: ~1 hafta.**

---

## P1 — Yüksek

| ID | Kategori | İş | Kaynak | Efor |
|---|---|---|---|---|
| ~~B-07~~ | Security | ✅ `DbConnectionFailure` ortak sınıflandırıcısı | SEC-002, TD-006 | S |
| ~~B-08~~ | Security | ✅ Kilitleme (5/15dk) + `AccessFailedAsync` + login rate limit | AUTH-002 | S |
| ~~B-09~~ | Security | ✅ `sstamp` claim + `SecurityStampValidation` + `POST /api/auth/revoke-all-sessions` | AUTH-001, F-04 | M |
| ~~B-10~~ | Security | ✅ `OrgRoleExtensions.IsAtLeast` + testler | AUTHZ-001 | S |
| ~~B-11~~ | Security | ✅ 4 alan: bağlantı dizesi + 3 hash | ARCH-004 | S |
| B-12 | Bug | MSSQL + Oracle FK introspection'ını **canlı doğrula** | I-06 | M |
| ~~B-13~~ | Stability | ✅ 120 sn zaman aşımı + iptal | REL-001 | S |
| ~~B-14~~ | Stability | ✅ `PartialApplyPossible` + UI uyarısı | DB-007 | S |
| ~~B-15~~ | Testing | ~~`frontend` store'ları için test~~ ✅ vitest altyapısı kuruldu (hiç yoktu) + 28 test; test yazarken GERÇEK bir hata bulundu ve düzeltildi | FE-001, TD-001 | L |
| ~~B-16~~ | Testing | ✅ 5'ten fazla atlama build'i kırıyor | TEST-001 | S |
| ~~B-17~~ | DevOps | ✅ `dotnet list --vulnerable` + `npm audit` | DEVOPS-004 | S |
| ~~B-18~~ | DevOps | ✅ `deploy/URETIM-CALISTIRMA.md` §3 — proxy gereklilikleri ve cross-site kararı |
| ~~B-19~~ | UX | ✅ `EmptyState` bileşeni + 5 ince ekran (3'ü zaten iyiydi) | UX-001, F-05 | S |
| ~~B-20~~ | UX | ~~Demo → hesap geçişinde işi koru~~ ✅ CANLI ÖLÇÜLDÜ: iş KORUNUYOR (misafir 25 tablo → giriş → bulutta 25 tablo). Bulgu yanlış pozitifti; `Header` etkisi önce yüklüyor sonra indiriyor | UX-002, F-06 | M |
| ~~B-21~~ | Feature | ~~Sorgu geçmişi~~ ✅ CANLI DOĞRULANDI | F-01 | S |
| ~~B-22~~ | Feature | ~~Kaydedilmiş sorgular~~ ✅ CANLI DOĞRULANDI | F-02 | S |
| ~~B-23~~ | Architecture | ✅ **Karar değişti:** refactor yerine konvansiyon testi — yetkisiz uç eklenince build kırılıyor. Filtre işi BACK-004 (Gateway bölme) ile birlikte yapılacak | ARCH-002, TD-002 | S |
| ~~B-24~~ | Performance | ❌ **Geri çekildi** — tavan zaten vardı (`Math.Clamp(1,200)`) | PERF-007 | XS |
| ~~B-25~~ | Backend | ✅ `[EnableRateLimiting("sensitive")]` | BACK-002 | XS |
| ~~B-26~~ | Product | ~~Ground'un konumunu yeniden belirle (strateji kararı)~~ ✅ `docs/GROUND-KONUMLANDIRMA.md` (öneri, onay bekliyor): Ground = deneme zemini, üretim kullanıcının platformunda | RW-02 | — |

---

## P2 — Orta

| ID | Kategori | İş | Kaynak | Efor |
|---|---|---|---|---|
| ~~B-27~~ | Security | ✅ `Security:DbEgress:AllowedHosts` + executor ortak politikaya bağlandı; **canlı doğrulandı** | SEC-006 | M |
| ~~B-28~~ | Security | ✅ 12 karakter yapıldı; **sızdırılmış-parola listesi (HIBP) hâlâ açık** | SEC-007 | S |
| ~~B-29~~ | Security | ✅ Üç motorda da sınırlayıcı kaçırılıyor + testler | 06 §son | S |
| ~~B-30~~ | Security | ✅ TOTP + kurtarma kodları, **12 adım canlı doğrulandı**. ⚠️ Arayüz henüz yok (B-62) | F-09 | M |
| ~~B-31~~ | Database | ✅ Prod compose'da `migrate` profili + bekleyen migration'da fail-fast | DB-003, TD-007 | M |
| ~~B-32~~ | Stability | ~~Optimistic concurrency (`RowVersion`)~~ ✅ `xmin` + 409 + istemci uyarısı; CANLI DOĞRULANDI (200 / 409 / geriye uyumlu). Teşhis düzeltildi: üzerine yazma yoktu, **sessiz düşürme** vardı | REL-003a, TD-009 | M |
| ~~B-33~~ | Stability | ✅ 3 deneme / 5 sn — **yalnızca** control DB | REL-004 | XS |
| ~~B-34~~ | Stability | ✅ Prod compose'da `deploy.resources.limits` | REL-005 | S |
| ~~B-35~~ | Performance | ~~Vault yedeğini arka plan işine taşı (202 Accepted)~~ ✅ kuyruk + `BackgroundService` (kendi DI kapsamı) + açılışta uzlaştırma; arayüz iş BİTİNCE "alındı" diyor | PERF-003 | M |
| ~~B-36~~ | Performance | ~~`mermaid` + `sql.js` dinamik import (önce ölç)~~ ✅ ÖLÇÜLDÜ: `/compile` 1830→1268 KB. CANLI DOĞRULANDI | PERF-004 | S |
| ~~B-37~~ | Architecture | ~~`ScaffolderService`'i böl~~ ✅ 1.761 → 120 satır, 5 üreteç; önce snapshot testi yazıldı, çıktı birebir aynı | ARCH-003, TD-003 | M |
| B-38 | Architecture | `AIPreferencesModal`'ı böl + tipli tercih modülü — 🟡 **bilinçli olarak ertelendi:** dosya 1651 satır, 30+ birbirine bağlı `useState` (6 sekme: profil/hesap/AI/fiyat/yardım/analitik). Körlemesine bölmek — bu oturumun geri kalanında derinlemesine doğrulanamayacak kadar riskli; regresyon ihtimali kazancından yüksek. Güvenli yol: önce her sekmenin state bağımlılık haritası çıkarılıp, sonra TEK sekme taşınıp canlı doğrulanmalı — bu ayrı bir oturum gerektiriyor | FE-002, TD-004 | M |
| ~~B-39~~ | Frontend | ~~`DbPushModal`'ı merkezi API istemcisine taşı~~ ✅ `executorService` | FE-005 | S |
| ~~B-40~~ | Testing | ~~E2E: tek uçtan uca akış (Playwright)~~ ✅ **Karar değişti:** Playwright yerine mevcut `check-e2e.mjs` harness'ına zincir bloğu eklendi (kayıt→proje→DDL→409 çakışma kanıtı). Tarayıcı ikilisi indirmek disk kısıtını ihlal ederdi; DOM etkileşimi bu oturumda Browser paneliyle elle doğrulandı | TEST-005 | M |
| ~~B-41~~ | A11y | ~~Form alanlarına etiket (51 input)~~ ✅ 117/118 (kalan 1 = yorum içi, yanlış pozitif) | A11Y-001 | M |
| ~~B-42~~ | A11y | ~~Kontrast + klavye + ekran okuyucu denetimi~~ ✅ kontrast: açık VE koyu temada 4 sistemik hata (şerit gradyanı iki temada da kırıktı) düzeltildi, 0 hata (10 sayfa × 2 tema); klavye: 29 erişilemez öğe + `useFocusTrap` odak geri yükleme hatası (TÜM modalları etkiliyordu) düzeltildi, uçtan uca CANLI doğrulandı | 13 | M |
| ~~B-43~~ | Feature | ~~Kısmi (tek tablo) geri yükleme~~ ✅ PostgreSQL: `pg_restore -t` (dump zaten `-Fc` özel biçim, önceki analiz sanıldığından kolay çıktı). MySQL/MariaDB: dump düz SQL, seçici uygulama mümkün değil — sessizce tam geri yükleme yapmak yerine `NotSupportedException` ile AÇIKÇA reddediyor. Docker gerektirmeyen komut-üretim testleri (2) yazıldı; canlı `pg_restore` çalıştırması disk kısıtı yüzünden yapılamadı | F-07 | M |
| ~~B-44~~ | Feature | ~~CSV/JSON dışa aktarma + `CanExport` izni~~ ✅ CSV/JSON zaten vardı; eksik olan `CanExport` izniydi — iki kapı (anahtar + tablo), CANLI DOĞRULANDI | F-08 | M |
| ~~B-45~~ | Feature | ~~Supabase sağlayıcısı~~ ✅ `SupabaseProvider` (Neon deseni); CANLI DOĞRULANMADI — `IsLiveVerified: false`, 12 test | F-10 | M |
| ~~B-46~~ | Build | ~~Docker.DotNet sürüm çatışmasını çöz~~ ✅ etkisiz kılındı (Lazy) + teşhis düzeltildi | TD-005, BACK-006 | M |
| ~~B-47~~ | DevOps | ~~İleriye uyumlu migration kuralı + geri alma prosedürü~~ ✅ kural + prosedür + **testle zorlanıyor** (`MigrationCompatibilityTests`) | DEVOPS-005 | M |
| ~~B-48~~ | Docs | ✅ `deploy/URETIM-CALISTIRMA.md` | 25 | M |

---

## P3 — Düşük

| ID | Kategori | İş | Kaynak | Efor |
|---|---|---|---|---|
| ~~B-49~~ | Security | ✅ Yanıltıcı kod ve yorum kaldırıldı | SEC-008 | XS |
| ~~B-50~~ | Frontend | ✅ `removeConsole` ({ exclude: error, warn }) | FE-006 | XS |
| ~~B-51~~ | Performance | ~~`templates.ts` dinamik import~~ ✅ ÖLÇÜLDÜ: `/canvas` 1653→1551 KB. CANLI DOĞRULANDI | PERF-005 | S |
| ~~B-52~~ | Database | ~~Kararsız sayfalamayı API sözleşmesinde işaretle~~ ✅ `GatewayListResult.StablePagination` | DB-005 | S |
| ~~B-53~~ | A11y | ~~`.nsl` yolunu erişilebilir alternatif olarak belgele~~ ✅ `docs/ERISILEBILIRLIK.md` | A11Y-003 | XS |
| ~~B-54~~ | UX | ❌ **Geri çekildi** — Desk gezinmesi zaten "Yedekler"/"Barındırma" diyor | 12 | XS |
| ~~B-55~~ | Product | ~~Mobil hedefini açıkça belirle ve yaz~~ ✅ ÖLÇÜLDÜ + `docs/MOBIL-HEDEFI.md` (öneri, onay bekliyor) | Senaryo 7 | XS |
| B-56 | i18n | Çok dil altyapısı — **ve iki uygulamanın dilini birleştir** (`frontend` İngilizce, `desk` Türkçe) — 🟡 **bilinçli olarak ertelendi:** kullanıcıya görünen yüzlerce string'i elle taşımak, bu oturumda derinlemesine test edilemeyecek bir kapsam (her ekranın hem yeni dizeleri hem de kırılmamış yerleşimi canlı doğrulanmalı). Önerilen yol: i18n altyapısını (kütüphane + dil seçici + sözlük iskeleti) kurup metinleri ekran ekran, her taşımadan sonra canlı doğrulayarak aşamalı taşımak | 24, UX-004 | M |

---

## Denetim sırasında ORTAYA ÇIKAN yeni maddeler

| ID | Kategori | İş | Kaynak | Efor | Öncelik |
|---|---|---|---|---|---|
| B-57 | Frontend | 128 eslint hatasını temizle, sonra CI'a `npm run lint` ekle | FE-008 | L | P1 |
| ~~B-58~~ | Security | ✅ HIBP k-anonimlik kontrolü; **canlı doğrulandı** (`Password123456` → 42.513 sızıntı → red) | SEC-007 | S | P2 |
| ~~B-59~~ | Testing | ~~`SecurityStampValidation` için entegrasyon testi~~ ✅ | B-09 | S | P2 |
| ~~B-60~~ | Docs | ✅ `.gitignore` yeni belgeleri yutuyordu — `docs/`, `deploy/` açıldı | DOC-005 | XS | P1 |
| ~~B-61~~ | Docs | ~~`*.md` yok sayma politikasını tersine çevir~~ ✅ varsayılan artık takip et | DOC-005 | S | P2 |
| ~~B-62~~ | UX | ✅ MFA bölümü (yerel QR + elle anahtar + `otpauth://` + kurtarma kodları). QR tamamen tarayıcıda üretiliyor, sır 3. tarafa gitmiyor (B-64) | AUTH-003 | M | P1 |
| ~~B-64~~ | UX | ~~MFA için **yerel** QR üretimi (kütüphane kararı) + authenticated dalın elle teyidi~~ ✅ | AUTH-003 | S | P2 |
| ~~B-63~~ | Security | ✅ `[Authorize]` + `withCredentials`; **canlı doğrulandı** (cookiesiz 401, cookie ile 404) | 29 | XS | P2 |

---

## Kategori dağılımı

| Kategori | P0 | P1 | P2 | P3 | Toplam |
|---|---|---|---|---|---|
| Security | 5 | 5 | 4 | 1 | **15** |
| Testing | — | 2 | 1 | — | 3 |
| DevOps | 1 | 2 | 2 | — | 5 |
| UX / A11y | — | 2 | 2 | 2 | 6 |
| Stability | — | 2 | 4 | — | 6 |
| Feature | — | 2 | 3 | — | 5 |
| Architecture | — | 1 | 3 | — | 4 |
| Performance | — | 1 | 2 | 1 | 4 |
| Bug | — | 1 | — | — | 1 |
| Docs / Product | — | 1 | 1 | 3 | 5 |
| **Toplam** | **6** | **19** | **22** | **7** | **54** |

Güvenliğin baskın olması beklenen bir sonuç: bu bir veritabanı erişim ürünü ve
denetim özellikle o gözle yapıldı. **Bulunanların hiçbiri "yeniden yaz"
gerektirmiyor** — hepsi noktasal düzeltme.

---

## B-42 — kontrast bölümü KAPANDI (12.09.2026)

**Ölçüm aracı:** `frontend/scripts/contrast-audit.js` (tarayıcı konsolu;
`selfTest` = 21 öz sınaması var). CSS'i statik okumak bu projede yeterli değil
— renkler `oklch()` + `calc()` + tema override'ı ile üretiliyor.

**Sonuç:** açık ve koyu temada 10 sayfa, **0 hata**.

### Bulunan ve düzeltilen sistemik hatalar

| # | Hata | Ölçüm (önce → sonra) |
|---|---|---|
| 1 | Tanıtım şeridi: metin `--surface-900` kullanıyordu, ama şerit gradyanı temayla TERS yönde değişiyor | koyu 1.30 / 3.14 / 7.49 → **14.86 / 11.91 / 9.53**; açık 1.13 / 1.25 / 1.18 → **16.51 / 14.83 / 15.80** |
| 2 | `ZeroDriftSection`: 10 satır içi renk koyu zemin için sabitlenmişti | 1.59–1.84 → **5.52–5.83** (`--phrase-l`) |
| 3 | `accent`/`danger` zeminli 4 düğmede koyu mürekkep (biri `text-white`, tasarım kuralları bunu yasaklıyor) | 3.12 / 2.74 → **5.96 / 6.78** (`--accent-on`) |
| 4 | Açık tema `--content-subtle` ve `--content-muted` yalnızca EN AÇIK yüzeye göre seçilmişti | subtle 3.63 → **4.62** (en koyu açık yüzeyde); muted 3.89 → **5.48** |

**Şeridin iki temada da kırık olması, önceki "koyu tema 811/811 geçiyor"
ölçümümün gradyan zeminleri GÖRMEDİĞİ anlamına geliyor.** Denetim aracı
artık gradyan zeminli öğeleri `unmeasurable` olarak işaretliyor ve onlar
elle, gradyanın tüm duraklarına karşı ölçülüyor. Aynı kör noktadan kaynaklı
ikinci bir ders: bir token, kullanıldığı TÜM yüzeylere karşı ölçülmeli —
`--content-subtle` ilk denemede %50'ye çekilmişti ve `surface-700` üstünde
4.35 ile `/review` sayfasında yakalandı.

**B-42'de kalan:** klavye gezinmesi ve ekran okuyucu denetimi.

## B-42 — klavye + ekran okuyucu bölümü de KAPANDI (12.09.2026)

**Ölçüm aracı:** `frontend/scripts/a11y-audit.js` (tarayıcı konsolu).

### Ölçüm yöntemi iki kez DÜZELTİLDİ (ilk sonuçlar sahteydi)

| İlk ölçüm | Gerçek | Neden yanlıştı |
|---|---|---|
| "75 odaklanabilir öğenin 73'ünde odak halkası yok" | **0** | `el.focus()` PROGRAMATİK odaktır ve Chrome bunun için `:focus-visible` tetiklemez. Gerçek Tab tuşuyla doğrulandı: `outline: 2px solid`, halka `globals.css`'teki global kuraldan geliyor. |
| "74 tıklanabilir öğe klavyeyle erişilemez" | **29** | `cursor: pointer` CSS'te MİRAS ALINIYOR; tıklanabilir bir div'in tüm torunları da "pointer" görünüyordu. Araç artık React'in kendi `onClick` prop'unu okuyor (`__reactProps$…`). |
| "13 onay kutusu 14x14, çok küçük" | **0** | Her biri 256x24 bir `<label>` içinde; etiket metnine tıklamak da çalışıyor, yani gerçek hedef 256x24. Araç artık etiketi ölçüyor. |

### Düzeltilen GERÇEK hatalar

1. **29 tıklanabilir öğe klavyeyle erişilemiyordu** — tanıtım sayfasındaki 8
   ekosistem kartı, demo sayfasındaki 20 şablon kartı, başlıktaki kullanıcı
   menüsü. `lib/a11y.ts` → `activateOnKey` + `role="button"` + `tabIndex={0}`.
   `<button>` kullanılamadı: kartlar içinde `<h3>` var, HTML bunu yasaklıyor.
2. **`useFocusTrap`'te odak geri yükleme hatası — TÜM modalları etkiliyordu.**
   `previousActiveElement`, odak modala taşındıktan SONRA okunuyordu; yani
   "önce odakta olan öge" olarak modalın İÇİNDEKİ ilk öge kaydediliyordu.
   Modal kapanınca odak `<body>`'ye düşüyordu (WCAG 2.4.3).
3. **Demo şablon modalı bir ARIA diyalogu değildi**: `role`/`aria-modal`/
   `aria-label` yok, odak modalın ARKASINDA kalıyor, ve kapatma düğmesi
   `title="Close (Esc)"` yazdığı hâlde Escape'i dinleyen kod YOKTU — arayüz
   tutmadığı bir söz veriyordu.
4. `ContextualHelpTooltip` düğmesi 22x22 idi, genişletilmiş tıklama alanı
   yoktu (`tap-44` eklendi).
5. Başlık atlamaları: `/` h1→h3, `/canvas` h2→h4 ve `/canvas`'ta hiç `<h1>`
   yoktu (ekran okuyucu için `sr-only` h1 eklendi).

### Uçtan uca klavye döngüsü CANLI doğrulandı

Karta odaklan → **Enter** → diyalog açıldı → odak diyaloğun İÇİNE geçti →
**Escape** → diyalog kapandı → **odak karta geri döndü**.

**Kalan (kabul edilmiş):** modal arka planları (`div.fixed inset-0`) ve
React Flow tuvali odaklanabilir değil. Arka planlar için Escape çalışıyor;
React Flow üçüncü taraf ve kendi klavye desteğini getiriyor.
