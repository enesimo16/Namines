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
| B-15 | Testing | `frontend` store'ları için test | FE-001, TD-001 | L |
| ~~B-16~~ | Testing | ✅ 5'ten fazla atlama build'i kırıyor | TEST-001 | S |
| ~~B-17~~ | DevOps | ✅ `dotnet list --vulnerable` + `npm audit` | DEVOPS-004 | S |
| ~~B-18~~ | DevOps | ✅ `deploy/URETIM-CALISTIRMA.md` §3 — proxy gereklilikleri ve cross-site kararı |
| ~~B-19~~ | UX | ✅ `EmptyState` bileşeni + 5 ince ekran (3'ü zaten iyiydi) | UX-001, F-05 | S |
| B-20 | UX | Demo → hesap geçişinde işi koru | UX-002, F-06 | M |
| ~~B-21~~ | Feature | ~~Sorgu geçmişi~~ ✅ CANLI DOĞRULANDI | F-01 | S |
| ~~B-22~~ | Feature | ~~Kaydedilmiş sorgular~~ ✅ CANLI DOĞRULANDI | F-02 | S |
| ~~B-23~~ | Architecture | ✅ **Karar değişti:** refactor yerine konvansiyon testi — yetkisiz uç eklenince build kırılıyor. Filtre işi BACK-004 (Gateway bölme) ile birlikte yapılacak | ARCH-002, TD-002 | S |
| ~~B-24~~ | Performance | ❌ **Geri çekildi** — tavan zaten vardı (`Math.Clamp(1,200)`) | PERF-007 | XS |
| ~~B-25~~ | Backend | ✅ `[EnableRateLimiting("sensitive")]` | BACK-002 | XS |
| B-26 | Product | Ground'un konumunu yeniden belirle (strateji kararı) | RW-02 | — |

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
| B-38 | Architecture | `AIPreferencesModal`'ı böl + tipli tercih modülü | FE-002, TD-004 | M |
| ~~B-39~~ | Frontend | ~~`DbPushModal`'ı merkezi API istemcisine taşı~~ ✅ `executorService` | FE-005 | S |
| B-40 | Testing | E2E: tek uçtan uca akış (Playwright) | TEST-005 | M |
| ~~B-41~~ | A11y | ~~Form alanlarına etiket (51 input)~~ ✅ 117/118 (kalan 1 = yorum içi, yanlış pozitif) | A11Y-001 | M |
| B-42 | A11y | Kontrast + klavye + ekran okuyucu denetimi — 🟡 **kontrast ÖLÇÜLDÜ ve 2 sistemik hata düzeltildi** (koyu tema 4 sayfada 811/811 geçiyor); klavye, ekran okuyucu ve açılış sayfasının AÇIK teması (78 hata) açık | 13 | M |
| B-43 | Feature | Kısmi (tek tablo) geri yükleme | F-07 | M |
| ~~B-44~~ | Feature | ~~CSV/JSON dışa aktarma + `CanExport` izni~~ ✅ CSV/JSON zaten vardı; eksik olan `CanExport` izniydi — iki kapı (anahtar + tablo), CANLI DOĞRULANDI | F-08 | M |
| B-45 | Feature | Supabase sağlayıcısı | F-10 | M |
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
| B-56 | i18n | Çok dil altyapısı — **ve iki uygulamanın dilini birleştir** (`frontend` İngilizce, `desk` Türkçe) | 24, UX-004 | M |

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
