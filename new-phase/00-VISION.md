# 00 — Vizyon & Kategori

> ⚠️ **2026-08 güncellemesi:** Kuzey yıldızı cümlesi daha da keskinleşti — "şema
> değişir, geri kalan güncellenir" doğru ama eksik. Rekabetin (Supabase, Admin
> Pilot, Lovable) hızla kapattığı "generation-first" çerçeve yerine artık
> **"AI ile database/backend lifecycle'ını GÜVENLE yönetmek"** (evolution +
> governance-first) konumlandırması geçerli. Detay ve gerekçe:
> [27-LIFECYCLE-PIVOT.md](27-LIFECYCLE-PIVOT.md) ve güncel rakip verisi için
> [01-MARKET.md](01-MARKET.md)'in başındaki not.

## 1. Problem yeniden tanımı

Faz 1'in çözdüğü problem: *"Şema tasarlamaya nereden başlayacağımı bilmiyorum."*
Bu problemin ekonomik değeri düşük — çünkü herhangi bir LLM bunu bedavaya çözüyor.

Faz 2'nin çözeceği problem:

> **"Bir veri modelim var. Şimdi bunu gerçek bir veritabanına, çalışan bir API'ye, ekibimin kullanabileceği bir yönetim paneline dönüştürmem ve bunları model değiştikçe senkron tutmam gerekiyor. Bu iş 3 hafta sürüyor ve her şema değişikliğinde tekrar kırılıyor."**

Bu problemin ekonomik değeri yüksek, tekrarlayan ve bugün 4 ayrı araçla (dbdiagram + Supabase + Retool + Flyway) çözülüyor.

---

## 2. Kuzey yıldızı cümlesi

> **Şema değişir, geri kalan her şey kendini günceller.**

İngilizce ürün sloganı:
> **Namines — Design the schema. Get the whole backend.**

Türkçe:
> **Namines — Şemayı tasarla, arka ucun hazır.**

---

## 3. Kategori tanımı

Namines yeni bir kategori iddia eder: **Schema-Driven Backend Platform (SDBP)**

Mevcut kategoriler ve boşluk:

```
              TASARIM              VERİ                     UYGULAMA
              (modelleme)          (canlı DB)               (API + UI)
              ─────────────────────────────────────────────────────────
dbdiagram     ████████             ─                        ─
Azimutt       ██████               ── (sadece okuma)        ─
Supabase      ██ (tablo editörü)   ████████                 ██████ (API + basit tablo UI)
Directus      ██                   ██ (var olanı sarar)     ████████
Retool        ─                    ─                        ████████
Prisma        ████ (kod)           ──                       ──
Bytebase      ─                    ████ (yönetim)           ─
──────────────────────────────────────────────────────────────────────
NAMINES       ████████             ████████                 ████████
```

**İddia:** Üç sütunu da tek bir doğruluk kaynağından (NSL) türeten hiçbir ürün yok. Namines'in savunma hendeği (moat) tek tek özellikler değil, **üç plane arasındaki sürekli senkronizasyon**dur.

---

## 4. Ürün adları ve marka mimarisi

| Ad | Nedir | URL / adres | Faz 1 karşılığı |
|---|---|---|---|
| **Namines** | Şemsiye marka | `namines.com` | Namines |
| **Namines Studio** | Görsel tasarım workspace'i | `app.namines.com` | canvas sayfası |
| **Namines Copilot** | AI ajan katmanı | Studio içinde | AI servisleri |
| **NSL** (Namines Schema Language) | Şema dili + IR, `.nsl` uzantısı | — | `DatabaseSchema` modeli |
| **Namines Cloud** | Yönetilen veritabanı (Data Plane) | `db.namines.com` | Docker sandbox |
| **Namines Console** | Otomatik yönetim paneli | `console.namines.com/{proje}` | Streamlit ZIP |
| **Namines Gateway** | Otomatik REST/GraphQL API | `api.namines.com/v1/{proje}` | — (yeni) |
| **Namines CLI** | `npx namines` / `dotnet tool` | npm: `namines`, NuGet: `Namines.Cli` | `namines-diff.mjs` |
| **Namines Bot** | GitHub App, PR'da şema incelemesi | `github.com/apps/namines` | schema-diff workflow |
| **Namines Bridge** | On-prem introspection agent | self-hosted binary | DbIntrospectService |
| **Namines Hub** | Blueprint / şablon pazarı | `namines.com/hub` | şablon galerisi |
| **Namines Docs** | Dokümantasyon | `docs.namines.com` | README |
| **Namines Status** | Durum sayfası | `status.namines.com` | — |

**Alan adı önerisi sırası:** `namines.com` → `namines.dev` → `namines.io` → `getnamines.com`
**Sosyal:** `@naminesdev` (X, GitHub org, LinkedIn), `namines` (npm scope: `@namines/*`)

---

## 5. Üç Plane modeli

```
┌─────────────────────────────────────────────────────────────────────┐
│  DESIGN PLANE — "modeli düşün"                                      │
│  Studio · Copilot · Canvas · Templates · Docs · DBA Advisor         │
│  Çıktı: NSL dokümanı (tek doğruluk kaynağı)                         │
└──────────────────────────────┬──────────────────────────────────────┘
                               │  NSL derlenir
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│  DATA PLANE — "veriyi çalıştır"                                     │
│  Provisioning · Branch DB · Migration · Seed · Backup · Introspect  │
│  Çıktı: canlı, sürümlenmiş veritabanı                               │
└──────────────────────────────┬──────────────────────────────────────┘
                               │  metadata yayılır
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│  APP PLANE — "kullanılabilir yap"                                   │
│  Console (admin UI) · Gateway (API) · Realtime · RBAC · Workflows   │
│  Çıktı: son kullanıcının dokunabildiği ürün                         │
└─────────────────────────────────────────────────────────────────────┘
        ↑                                                    ↓
        └──────────  EJECT: her katmandan kod olarak çık  ────┘
```

**Neden bu sıralama önemli:** Kullanıcı Design Plane'den girer (düşük taahhüt), Data Plane'de bağlanır (verisi orada), App Plane'de kilitlenir (ekibi kullanıyor). Bu bir **taahhüt merdiveni**dir ve Faz 1'de sadece ilk basamak vardı.

---

## 6. Kuzey yıldızı metrikleri

| Seviye | Metrik | Hedef (12. ay) |
|---|---|---|
| **North Star** | Haftalık aktif **provisioned database** sayısı | 2.000 |
| Activation | Kayıttan sonra 24 saat içinde DB provision eden kullanıcı % | ≥ 35% |
| Aha moment | "Şema → canlı Console'da ilk kayıt eklendi" süresi | < 5 dakika |
| Retention | 4. hafta retention (DB provision edenler) | ≥ 55% |
| Doğruluk | Golden-file DDL testlerinin geçme oranı | 100% |
| AI kalitesi | Copilot eval suite skoru | ≥ 0.85 |
| Gelir | MRR | $8.000 |
| Verimlilik | AI maliyeti / aktif kullanıcı / ay | < $0.60 |

**Karşı-metrik (bunu bozmamak şartıyla):** p95 Gateway yanıt süresi < 150 ms.

---

## 7. Neyi yapmıyoruz (kapsam dışı)

Kapsamı korumak için açıkça reddedilenler:

- ❌ Genel amaçlı BI / dashboard aracı olmak (Metabase değiliz)
- ❌ Full no-code app builder olmak (Bubble değiliz) — Console **veri odaklı**dır, keyfi UI değil
- ❌ Kendi veritabanı motorumuzu yazmak
- ❌ NoSQL / graph / vector DB desteği (v1'de; v2 değerlendirilir)
- ❌ Mobil native uygulama
- ❌ Kendi LLM'imizi eğitmek

---

## 8. Üç yıllık ufuk

| Yıl | Konum |
|---|---|
| **Yıl 1** | Şemadan canlı backend. Bireysel geliştirici + küçük ekip. Self-serve. |
| **Yıl 2** | Ekip ürünü: branch/PR/review, RBAC, audit, on-prem Bridge, SOC 2. Enterprise satış başlangıcı. |
| **Yıl 3** | Veri platformu: policy-as-code, data contracts, lineage, çoklu-DB federasyon, marketplace ekosistemi. |
