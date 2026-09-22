# 27 — Lifecycle Pivot: AI-native Database & Backend Engineering Platform

> **Bu doküman 24-ROADMAP.md'yi geçersiz kılmaz, önceliklendirmesini düzeltir.** Faz 0
> (G0-G6, tamamlandı) hâlâ geçerli temel. Bu doküman, Faz 1'den itibaren neyin öne
> alınması gerektiğini belirliyor: **Impact Analysis + Database Change Review**,
> geniş "platform" genişlemesinden (Console, Gateway, otomasyon) önce gelir.
>
> Tetikleyici: kullanıcının 2026-08-10 tarihli stratejik brief'i. Tam soru-cevap
> converşasyonda; burada yalnızca kararlar ve gerekçeleri kalıcı hale getiriliyor.

---

## 1. Kuzey yıldızı çerçevesi (değişti)

**Eski çerçeve:** "AI ile database/backend üret" (generation-first)
**Yeni çerçeve:** "AI ile database/backend lifecycle'ını GÜVENLE yönet" (evolution + governance-first)

```
Requirement → AI Analysis → Schema → Database → API/Backend → Application
                                          ↓
                                       Change
                                          ↓
                              AI Impact Analysis  ← YENİ ODAK
                                          ↓
                                       Branch      ← sunucu-taraflı olmalı
                                          ↓
                                     Migration
                                          ↓
                                      Testing      ← Faz 0'da inşa edildi
                                          ↓
                                       Review       ← YENİ: Database PR
                                          ↓
                                      Approval
                                          ↓
                                    Production
```

**Neden bu sıra:** AI kod/agent araçları (Claude Code, Codex) şema/migration
*üretimini* zaten iyi yapıyor ve hızla emtialaşıyor. Onlar production değişikliği
etrafındaki **kanıt, yönetişim ve çok-kişili onay sürecini** çözmüyor — bu, ürün/
süreç problemi, prompt problemi değil. Namines'in savunulabilir tarafı burası.

---

## 2. Repo gerçeği — lifecycle'ın neresi dolu, neresi boş

| Adım | Durum | Kanıt |
|---|---|---|
| Requirement → AI Analysis → Schema | ✔ var | metin/görsel/URL'den şema üretimi |
| Schema → Database | ✔ **Faz 0'da sağlamlaştırıldı** | 6 motor, golden-file+Testcontainers, index/unique/check/cascade |
| Database → API/Backend | ✖ yok | export var (EF Core/Prisma), canlı Gateway yok |
| API → Application | ✖ yok | Console yok, sadece statik dev-package export |
| Change → Impact Analysis | ✖ yok | `FkCascadeAnalyzer` (G3) bu motorun İLK TUĞLASI ama genelleştirilmedi |
| Branch | ⚠ yarım | istemci-taraflı, cihaz-bazlı, gerçek version control değil |
| Migration | ⚠ yarım | `MigrationService` var, "diff+preview" seviyesinde, risk motoru yok |
| Testing | ✔ **Faz 0'da inşa edildi** | golden-file + gerçek DB doğrulama — ama geliştirici-CI amaçlı, ürün-içi "bu değişiklik güvenli mi" testi değil |
| Review / Approval | ✖ yok | PR-tarzı Database Change Review UI hiç yok |
| Production apply | ⚠ yarım | deploy dokümanı var, güvenli-apply + onay pipeline'ı yok |

**Sonuç:** Ürün "lifecycle platformu" iddiasının ~%20-25'inde. Ama eksik olan kısımların
ham malzemesi (FkCascadeAnalyzer, SchemaDiffRequest/Result, MigrationService,
AIDbaService, test altyapısı) zaten kodda duruyor — sıfırdan başlamıyoruz.

---

## 3. Açıkça reddedilenler (kullanıcının kendi fikirlerine itiraz dahil)

| Fikir | Karar | Gerekçe |
|---|---|---|
| Jenerik web/mobil/PWA app üretici | ❌ | Lovable/Bolt/v0 ile doğrudan, kazanılamaz rekabet |
| Jenerik otomasyon platformu ("Namines Flow" marka hedefi) | ❌ | n8n 2.0 (70+ AI node, LangChain) + Zapier + Make çok ileride; dar entegrasyon (DB event→webhook) OK, platform iddiası değil |
| AI Dataset Factory ("Namines Data") | ❌ (uzun vadede bile şüpheli) | Bambaşka alıcı (ML mühendisi), marka odağını sulandırır |
| Database Doctor'ın "~%X performans iyileştirmesi" iddiası (şimdilik) | ⚠ ertelendi | Gerçek sorgu telemetrisi (pg_stat_statements) gerektirir, BYODB müşterisinde ayrı bir izin/güven eşiği — yapısal analiz bunu dürüstçe iddia edemez |
| Kendi PostgreSQL/K8s cluster'ı işletmek | ❌ | Tek kişilik ekip için ölümcül, satın al (Neon vb.) |
| Mobil app = masaüstünün küçültülmüş hali | ❌ | Mobil = Monitor + Approve + Operate, ayrı bir yüzey |

---

## 4. Kabul edilen, önceliklendirilen yeni yön

**Ana farklılaştırıcı: Database Change Review ("Database PR")**
Schema Diff + Impact Analysis + Risk Skoru + Test Sonucu + İnsan Onayı — tek ekranda,
GitHub PR'ın veritabanı karşılığı. Bunu 6-motor doğrulanmış DDL doğruluğuyla
birleştirmek kimsenin yapmadığı bir şey.

**Revize edilmiş ilk 10 görev (G8'den devam):**

1. `SchemaImpactAnalyzer` — `FkCascadeAnalyzer`'ı genelleştir (Namines.Core/Analysis)
2. Migration risk sınıflandırması (safe/risky/destructive/breaking) → `MigrationService`
3. **Control DB: SQLite → PostgreSQL (G7)** — artık server-side branch'in ön koşulu
4. Sunucu-taraflı branch modeli (control DB'de gerçek tablo)
5. Database Change Review UI (diff + impact + risk + test sonucu)
6. "Run Tests" aksiyonu — Testcontainers altyapısını runtime'a taşı (ephemeral sandbox)
7. Etkilenen API/UI statik tahmini (tam Gateway beklemeden)
8. Minimal Gateway — şemadan otomatik salt-okunur REST (liste+detay)
9. AI Impact Explainer ajanı — yapısal analizi insan diline çevirir
10. Destructive işlem onay mekanizması — control DB'de audit + approval tablosu

---

## 5. Claude Code / Codex / Lovable farkı (kalıcı konumlandırma notu)

Agent'lar kod/şema/migration **üretimini** emtialaştırıyor — iyi prompt'la iyi sonuç
alınabiliyor. Emtialaştırmadıkları şey: production veri değişikliği etrafındaki
**kanıt, yönetişim, çok-kişili onay süreci.**

| | Agent (Claude Code/Codex) | Namines |
|---|---|---|
| Doğruluk kanıtı | Yok, agent "doğru olduğunu düşünür" | Golden-file + gerçek DB testi |
| Teknik olmayan onay | Hayır (terminal çıktısı) | Risk skorlu diff, tek tık |
| Proje hafızası | Yok, her oturum sıfırdan | DB-destekli kalıcı yapı |
| Yıkıcı işlem koruması | Agent'ın kararına bağlı | Yapısal olarak onay zorunlu |
| Çok kişili süreç | Yok | Yazar/reviewer/onaylayan ayrı roller |

**Konumlandırma cümlesi:** "AI şemayı yazsın" değil, **"AI'ın önerdiği değişiklik
kanıtlanabilir şekilde güvenli ve teknik olmayan biri tarafından onaylanabilir."**

---

## 6. Hedef segment (değişmedi, pekişti)

Kurumsal .NET/çoklu-motor ekipler + yazılım ajansları + Türkiye pazarı + migration
korkusu olan herkes. İndie hacker/startup'a "hızlı MVP" olarak satma — Lovable'la
doğrudan çarpışır, kaybedilir. B2B/ajans/kurumsal yön savunulabilir.
