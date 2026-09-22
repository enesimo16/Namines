# Namines — New Phase (Faz 2 Master Plan)

> **Durum:** Planlama dokümanı · **Tarih:** 2026-08-08 · **Sahip:** Enes Yel
> **Kaynak repo:** https://github.com/enesimo16/Namines
> **Bu klasör:** Namines'in "ERD aracı" konumundan **AI-native Data Platform** konumuna geçiş planı.

---

> ## 👉 İlk kez okuyorsan: **[BASLA-BURADAN.md](BASLA-BURADAN.md)**
> Bütün planın anlatı halinde özeti. ~20 dakika, tablo yok, baştan sona okunmak için yazıldı.
> Aşağıdaki dosyalar referanstır — ihtiyaç duydukça aç.
>
> ⚠️ **2026-08 stratejik dönüş:** [27-LIFECYCLE-PIVOT.md](27-LIFECYCLE-PIVOT.md) güncel
> yönü belirliyor — "AI ile database üret" değil, **"AI ile database/backend
> lifecycle'ını güvenle yönet."** 00/01/02/24 hâlâ geçerli ama önceliklendirmesi
> 27 tarafından düzeltildi. Kod yazmadan önce 27'yi oku.

---

## Tek cümlelik hedef (2026-08 itibarıyla keskinleşti)

> **Namines, bir fikri tarif ettiğin anda şemayı tasarlar, gerçek bir veritabanı ayağa kaldırır — ve en önemlisi, o şema değiştiğinde neyin kırılacağını KANITLAR, teknik olmayan birinin bile onaylayabileceği bir inceleme ekranında.**

Eski çerçeve (üretim-öncelikli) hâlâ geçerli ama artık ikincil — asıl fark
[27-LIFECYCLE-PIVOT.md §5](27-LIFECYCLE-PIVOT.md)'te: agent'lar (Claude Code, Codex)
kod/şema üretimini emtialaştırıyor, **production değişikliği etrafındaki kanıt ve
onay sürecini** emtialaştırmıyor.

Şema = tek doğruluk kaynağı (single source of truth). Geri kalan her şey (DDL, API, admin UI, dokümantasyon, migration, mock data, tipler) **türetilmiş artefakttır** ve otomatik güncellenir.

---

## Temel ilke: Hiçbir özellik silinmiyor

Faz 1'de yazılmış her özellik Faz 2'de bir katmana **terfi ediyor**:

| Faz 1 özelliği | Faz 2'deki yeri | Doküman |
|---|---|---|
| AI şema üretimi (metin/URL/görsel) | Copilot → Design Plane | [09-AI-LAYER.md](09-AI-LAYER.md) |
| Voice input (Whisper) | Copilot Voice Mode | [09-AI-LAYER.md](09-AI-LAYER.md) |
| React Flow canvas | Namines Studio | [05-CONTROL-PLANE.md](05-CONTROL-PLANE.md) |
| 6 motorlu DDL | NSL Compiler backend'leri | [12-CODEGEN-EJECT.md](12-CODEGEN-EJECT.md) |
| EF Core + Prisma export | Eject Targets (8 hedef) | [12-CODEGEN-EJECT.md](12-CODEGEN-EJECT.md) |
| Streamlit admin ZIP | **Console Eject** (React/Next/Blazor/Streamlit) | [07-CONSOLE-ADMIN-UI.md](07-CONSOLE-ADMIN-UI.md) |
| Docker sandbox | **Data Plane — Ephemeral Tier** (güvenli yeniden yazım) | [06-DATA-PLANE.md](06-DATA-PLANE.md) |
| SignalR multiplayer | CRDT + Redis backplane | [10-REALTIME-COLLAB.md](10-REALTIME-COLLAB.md) |
| Migration wizard | Migration Safety Engine | [11-MIGRATIONS-BRANCHING.md](11-MIGRATIONS-BRANCHING.md) |
| AI DBA advisor | Continuous Advisor (canlı DB metrikleriyle) | [09-AI-LAYER.md](09-AI-LAYER.md) |
| Smart Seed | Data Factory | [06-DATA-PLANE.md](06-DATA-PLANE.md) |
| DB introspection | Reverse Engineering + On-Prem Agent | [06-DATA-PLANE.md](06-DATA-PLANE.md) |
| SQL DDL import | NSL Importers (7 format) | [04-NSL-SCHEMA-IR.md](04-NSL-SCHEMA-IR.md) |
| CI schema-diff (`namines-diff.mjs`) | Namines Bot (GitHub App) | [11-MIGRATIONS-BRANCHING.md](11-MIGRATIONS-BRANCHING.md) |
| SQLite WASM konsolu | Studio Query Playground | [05-CONTROL-PLANE.md](05-CONTROL-PLANE.md) |
| Şablon galerisi | Blueprint Marketplace | [23-GTM.md](23-GTM.md) |
| PDF / Mermaid / README docs | Docs Engine | [12-CODEGEN-EJECT.md](12-CODEGEN-EJECT.md) |
| BYOK + AES-256-GCM | Secret Vault | [13-SECURITY.md](13-SECURITY.md) |
| Token havuzu / kota | Metering & Billing Engine | [22-BUSINESS-MODEL.md](22-BUSINESS-MODEL.md) |
| Stripe Pro tier | 4 katmanlı plan yapısı | [22-BUSINESS-MODEL.md](22-BUSINESS-MODEL.md) |
| Feedback widget | Product Signals | [21-OBSERVABILITY.md](21-OBSERVABILITY.md) |
| Share linki | Public Schema Pages (SEO + viral) | [23-GTM.md](23-GTM.md) |
| ⌘K / undo-redo / arama / kısayol | Studio UX Core | [05-CONTROL-PLANE.md](05-CONTROL-PLANE.md) |

---

## Doküman haritası

### Strateji
| # | Dosya | İçerik |
|---|---|---|
| 00 | [00-VISION.md](00-VISION.md) | Vizyon, kategori tanımı, ürün adları, kuzey yıldızı metrikleri |
| 01 | [01-MARKET.md](01-MARKET.md) | Rakip matrisi, pazar boşluğu, konumlandırma, fiyat kıyası |
| 02 | [02-PRODUCT-SCOPE.md](02-PRODUCT-SCOPE.md) | Tam özellik envanteri (mevcut + yeni), 3 plane modeli |

### Mimari
| # | Dosya | İçerik |
|---|---|---|
| 03 | [03-ARCHITECTURE.md](03-ARCHITECTURE.md) | Sistem mimarisi, servisler, portlar, veri akışları |
| 04 | [04-NSL-SCHEMA-IR.md](04-NSL-SCHEMA-IR.md) | **NSL** — yeni şema dili ve IR, tam gramer + JSON şeması |
| 05 | [05-CONTROL-PLANE.md](05-CONTROL-PLANE.md) | Studio, proje yönetimi, workspace, RBAC |
| 06 | [06-DATA-PLANE.md](06-DATA-PLANE.md) | DB provisioning, tenancy, branch DB, backup, on-prem agent |
| 07 | [07-CONSOLE-ADMIN-UI.md](07-CONSOLE-ADMIN-UI.md) | **Otomatik admin panel motoru** — en kritik yeni ürün |
| 08 | [08-GATEWAY-API.md](08-GATEWAY-API.md) | Otomatik REST/GraphQL/RPC + Realtime + RLS |
| 09 | [09-AI-LAYER.md](09-AI-LAYER.md) | Copilot ajanları, model matrisi, prompt mimarisi, eval |
| 10 | [10-REALTIME-COLLAB.md](10-REALTIME-COLLAB.md) | CRDT, presence, backplane, conflict resolution |
| 11 | [11-MIGRATIONS-BRANCHING.md](11-MIGRATIONS-BRANCHING.md) | Migration güvenliği, branch, PR botu, rollback |
| 12 | [12-CODEGEN-EJECT.md](12-CODEGEN-EJECT.md) | Codegen hedefleri, eject, dokümantasyon motoru |

### Uygulama
| # | Dosya | İçerik |
|---|---|---|
| 13 | [13-SECURITY.md](13-SECURITY.md) | Tehdit modeli, izolasyon, sır yönetimi, uyumluluk |
| 14 | [14-INFRA-DEPLOY.md](14-INFRA-DEPLOY.md) | Kubernetes, ortamlar, CI/CD, maliyet |
| 15 | [15-PACKAGES.md](15-PACKAGES.md) | **Tüm paketler** — NuGet, npm, Docker image, CLI, sürümler |
| 16 | [16-API-SURFACE.md](16-API-SURFACE.md) | **Tüm endpoint/route/hub/port** listesi |
| 17 | [17-DIRECTORY-STRUCTURE.md](17-DIRECTORY-STRUCTURE.md) | Hedef monorepo ağacı, dosya dosya |
| 18 | [18-CONTROL-PLANE-DDL.md](18-CONTROL-PLANE-DDL.md) | Namines'in kendi veritabanı şeması (tam DDL) |
| 19 | [19-ENV-VARS.md](19-ENV-VARS.md) | Tüm ortam değişkenleri + `.env.example` |
| 20 | [20-TESTING-EVALS.md](20-TESTING-EVALS.md) | Test stratejisi, golden files, AI eval harness |
| 21 | [21-OBSERVABILITY.md](21-OBSERVABILITY.md) | Log, metrik, trace, ürün analitiği |

### İş
| # | Dosya | İçerik |
|---|---|---|
| 22 | [22-BUSINESS-MODEL.md](22-BUSINESS-MODEL.md) | Fiyatlandırma, katmanlar, birim ekonomisi, metering |
| 23 | [23-GTM.md](23-GTM.md) | Pazara çıkış, büyüme döngüleri, içerik, topluluk |
| 24 | [24-ROADMAP.md](24-ROADMAP.md) | 12 aylık faz planı, sprint kırılımı, DoD (⚠ 27 ile önceliklendirmesi düzeltildi) |
| 25 | [25-RISKS.md](25-RISKS.md) | Risk kaydı, azaltma planları |
| 26 | [26-GLOSSARY.md](26-GLOSSARY.md) | Terimler sözlüğü |

### Lifecycle Pivot (2026-08 — güncel yön)
| # | Dosya | İçerik |
|---|---|---|
| 27 | [27-LIFECYCLE-PIVOT.md](27-LIFECYCLE-PIVOT.md) | **Başla buradan** — güncel strateji, repo gerçeği, Claude Code/Lovable farkı |
| 28 | [28-IMPACT-ANALYSIS-ENGINE.md](28-IMPACT-ANALYSIS-ENGINE.md) | "Bu değişiklik neyi etkiler" motoru — yeni ana farklılaştırıcı |
| 29 | [29-DATABASE-CHANGE-REVIEW.md](29-DATABASE-CHANGE-REVIEW.md) | GitHub PR'ın veritabanı karşılığı — onay ekranı |
| 30 | [30-SERVER-SIDE-BRANCHING.md](30-SERVER-SIDE-BRANCHING.md) | Cihaz-bazlı branch'ten sunucu-otoriteli modele geçiş |
| 31 | [31-NEW-BUSINESS-LINES.md](31-NEW-BUSINESS-LINES.md) | 5 yeni iş fikri, ticari değerlendirme + öncelik sırası |
| 32 | [32-DEFERRED-NOT-REJECTED.md](32-DEFERRED-NOT-REJECTED.md) | "Kesinlikle yapmayacaklar" — neden şimdi değil, ne zaman evet |
| 33 | [33-MCP-AND-SKILL.md](33-MCP-AND-SKILL.md) | MCP sunucusu + Claude Skill — geliştirme döngüsüne girmek (R4'ün uygulaması) |
| 34 | [34-SENDEN-BEKLENENLER.md](34-SENDEN-BEKLENENLER.md) | Kodun hazır olduğu ama bir hesap/karar beklediği işler — basit dille |
| 35 | [35-KALAN-BUYUK-ISLER.md](35-KALAN-BUYUK-ISLER.md) | Sıradaki büyük başlıklar + önerilen sıra ("eksik" değil, "sonraki") |
| 36 | [36-KOTA-VE-AJAN.md](36-KOTA-VE-AJAN.md) | Plan bazlı kotalandırma + şema üretiminin ajan hattı (üret → denetle → düzelt) |

---

## Nasıl okumalı

- **Sadece 20 dakikan varsa:** `27` → `00` → `01`
- **Kod yazmaya başlayacaksan:** `27` → `28` → `29` → `30` → `17` → `19`
- **Yatırımcıya/jüriye anlatacaksan:** `27` → `00` → `01` → `22` → `24`
- **"Şunu neden yapmıyoruz" sorusuna cevap arıyorsan:** `32`

---

## Uyarılar (dokümanların dışında, ama önemli)

1. `C:\Users\Enes Yel` dizininin kendisi bir git deposu ve remote'u `automated-recruitment-pipeline`. Yani ev dizinin yanlış bir repoya bağlı. `git add -A` çalıştırırsan tüm ev klasörünü commit'lemeye çalışır. Bunu düzeltmen gerekiyor.
2. Bu plan **tek geliştirici için 12 aylık** bir kapsamdır. [24-ROADMAP.md](24-ROADMAP.md) bunu satılabilir ara sürümlere böler — hepsini bitirmeden de para kazanabilirsin.
3. Planın en riskli varsayımı: kullanıcının verisini senin platformunda tutmaya razı olması. Bunun panzehiri "eject" özelliğidir ([12](12-CODEGEN-EJECT.md)) ve stratejik olarak zorunludur.
