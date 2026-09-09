# 10 — DevOps ve dağıtım denetimi

> ## ✅ DURUM (2026-09-09)
> | Bulgu | Durum |
> |---|---|
> | DEVOPS-001 Üretim dağıtım tanımı yok | ✅ `docker-compose.prod.yml` yazıldı |
> | DEVOPS-002 TLS/proxy belirsiz | 🟡 Kısmî — prod compose'da varsayım ve `X-Forwarded-Proto` uyarısı yazılı; gerçek mimari hâlâ belgelenmeli |
> | DEVOPS-003 CI kalitesi | Bulgu yoktu |
> | DEVOPS-004 CI'da eksik adımlar | ✅ Bağımlılık taraması (NuGet + npm) + atlanan test eşiği |
> | DEVOPS-005 Sıfır kesinti / geri alma | 🟡 Açık — migration ayrıldı, ileriye uyumluluk kuralı hâlâ yazılmadı |

## Mevcut durum

| Öğe | Durum | Konum |
|---|---|---|
| CI (derleme + test) | ✅ Var, gerçek testler koşuyor | `.github/workflows/ci.yml` |
| Release workflow | ✅ Var | `.github/workflows/release.yml` |
| Şema diff PR yorumcusu | ✅ Var | `.github/workflows/namines-schema-diff.yml` |
| Yerel geliştirme yığını | ✅ Var, dürüstçe etiketlenmiş | `docker-compose.yml` |
| **Üretim dağıtım tanımı** | ❌ **YOK** | — |
| Reverse proxy / TLS sonlandırma | ❌ Yok | — |
| Health check uçları | ✅ `/health`, `/health/ready`, `/health/live` | `Program.cs:607-621` |
| Rollback prosedürü | ❌ Belgelenmemiş | — |

---

## DEVOPS-001 — Üretim dağıtım tanımı yok

### Finding
Depoda çalıştırılabilir tek dağıtım tanımı `docker-compose.yml` ve o
**açıkça yerel geliştirme** için. Üretim için yalnızca iki örnek env dosyası
var (`deploy/backend.env.example`, `deploy/frontend.env.example`).

### Current State
`docker-compose.yml` kendi kendini dürüstçe etiketliyor (satır 31-39):

> Bu compose dosyası YEREL GELİŞTİRME yığını: portlar localhost'a açık, DB
> parolası depoda, migration açılışta koşuyor.

Bu **iyi bir yorum** — ama üretim karşılığı hiç yazılmamış.

### Problem
"Web üzerinde yayınlanmış" bir ürünün dağıtımı depoda tanımlı değilse, o dağıtım
tek bir kişinin belleğinde yaşıyor. Sunucu kaybedildiğinde yeniden kurulamaz;
yeni bir ortam (staging) açılamaz; dağıtımın hangi ayarlarla yapıldığı
denetlenemez.

### Risk
- Felaket kurtarma imkânsız (bkz. `18` — DR raporu).
- SEC-005'in senaryosu tam olarak buradan doğuyor: `ASPNETCORE_ENVIRONMENT`
  değerini kimin, nerede, nasıl ayarladığı yazılı değil.

### Severity **HIGH**
### Recommendation
`docker-compose.prod.yml` (ya da tercih edilen platformun tanımı) ekle:
`ASPNETCORE_ENVIRONMENT=Production`, sırlar env'den, `restart: unless-stopped`,
kaynak limitleri, migration **ayrı adım** (bkz. DB-003).
### Effort M · ### Priority P0

### ✅ Yapıldı — `docker-compose.prod.yml`
Geliştirme yığınından farkları, her biri bir denetim bulgusuna karşılık geliyor:

| Üretim yığınında | Neden |
|---|---|
| `ASPNETCORE_ENVIRONMENT=Production` | SEC-005'in fail-closed JWT kapısını devreye sokar |
| DB parolası `${POSTGRES_PASSWORD:?...}` | Tanımsızsa compose **durur** — sessiz varsayılan yok |
| Control DB portu host'a **açılmıyor** (`expose`) | Geliştirmede 15432 host'a bağlı |
| API ve frontend `127.0.0.1`'e bağlı | TLS ve rate limit reverse proxy'de sonlanır |
| Migration **ayrı `migrate` profili** (`command: ["--migrate"]`) | DB-003: deploy anında sessiz şema değişikliği ve çoklu instance yarışı |
| `Database__MigrateOnStartup=false` | Aynı |
| `restart: unless-stopped` | Süreç çöktüğünde geri gelsin |
| `deploy.resources.limits.memory` | REL-005: bir Vault yedeği host'u OOM'a sürüklemesin |
| `healthcheck` → `/health/ready` | Trafik yalnızca control DB erişilebilirken alınsın |

**Ek kod değişikliği:** `Database:MigrateOnStartup=false` iken uygulama artık
`GetPendingMigrations()` ile şemayı **doğruluyor** ve bekleyen migration varsa
Development dışında **açılmayı reddediyor**. Önceden yalnızca bir bilgi satırı
yazıp devam ediyordu — bu, deploy'da migration adımı atlandığında hatanın
saatler sonra rastgele bir kullanıcıda ortaya çıkması demekti.

---

## DEVOPS-002 — TLS sonlandırma tanımlı değil

### Finding
Yığında nginx / Caddy / Traefik yok; 443 portu geçmiyor; sertifika yönetimi
(Let's Encrypt vb.) tanımlı değil.

### Problem
Bu bir PaaS (Vercel/Railway) tarafından çözülmüş olabilir — `AuthController`'ın
`Auth:CrossSiteCookie` yorumu tam da Vercel + Railway senaryosunu anlatıyor.
Ama bu **varsayım**; depoda yazılı değil.

Önemi: `Secure = crossSite || Request.IsHttps`. TLS'in nerede sonlandığı ve
`X-Forwarded-Proto` başlığının güvenilip güvenilmediği (`UseForwardedHeaders`
yapılandırması **görülmedi**) doğrudan auth cookie'sinin güvenliğini belirliyor.

### Severity **MEDIUM** (dağıtım gerçeğine bağlı)
### Recommendation
Dağıtım mimarisini bir sayfaya yaz: TLS nerede sonlanıyor, hangi proxy başlıkları
güveniliyor, `ForwardedHeadersOptions` gerekiyor mu. Sonra `Request.IsHttps`'in
proxy arkasında doğru çalıştığını **doğrula**.
### Effort S · ### Priority P1

---

## DEVOPS-003 — CI kalitesi: GÜÇLÜ YAN

`ci.yml` dikkatle yazılmış ve **gerekçeleri yorumda**:

- Her push ve PR'da koşuyor.
- `concurrency` ile eski koşular iptal ediliyor.
- `permissions: contents: read` — en az yetki ✅
- Docker'ın varlığı **açıkça kontrol ediliyor** (`docker info`) — testlerin
  sessizce atlanmasına karşı.
- Testler `parallelizeAssembly=false` ile sıralı koşuyor; gerekçesi yazılı
  (paralel çalıştırmak Docker'ı boğuyor).
- `timeout-minutes: 30`.

Workflow'un başındaki yorum, bu CI'ın **neden eklendiğini** de kaydediyor:
daha önce hiçbir pipeline build/test koşmuyordu. Bu tür kayıtlar ender ve değerli.

**Bulgu yok.**

---

## DEVOPS-004 — CI'da eksik adımlar

| Eksik | Durum |
|---|---|
| Frontend testi | 🟡 Açık — test yok (FE-001) |
| `tsc --noEmit` (frontend) | ✅ **Zaten vardı** — ayrı adım olarak koşuyor (yanlış pozitif) |
| Bağımlılık güvenlik taraması | ✅ `dotnet list --vulnerable --include-transitive` + `npm audit --audit-level=high` |
| Atlanan test eşiği | ✅ `.trx`'ten sayılıyor; 5'i aşarsa build kırılıyor |
| Container imaj taraması (Trivy vb.) | 🟡 Açık — P2 |

**Bağımlılık taraması uyarı değil HATA olarak eklendi:** `Critical` ya da `High`
seviyede bir paket bulunursa build kırılıyor. Görmezden gelinen bir uyarı,
olmayan bir kontroldür.

**Atlanan test eşiği neden 0 değil:** ortama bağlı birkaç meşru atlama olabilir
(bu makinede MSSQL bellek yetmezliği gibi). Ama onlarca atlama, testlerin
koşmadığı anlamına gelir — `DURUM.md`'de kayıtlı "127 test sessizce atlandı,
suite yine yeşil yandı" olayı tam olarak budur.

### Effort (hepsi birlikte) S · ### Priority P1

---

## DEVOPS-005 — Sıfır kesintili dağıtım ve geri alma

Health check uçları **var** (`/health/ready`, `/health/live`) — yani altyapı
hazır. Ama:

- Rolling update / mavi-yeşil tanımı yok.
- Geri alma prosedürü yazılı değil.
- Migration açılışta koştuğu için **geri alma migration'ı geri almaz** —
  eski sürüm yeni şemaya karşı çalışmak zorunda kalır.

### Severity **MEDIUM**
### Recommendation
Migration'ları **ileriye uyumlu** yaz (kolon ekle, hemen silme; iki aşamalı
yeniden adlandırma) ve bunu bir kural olarak belgele. Bu, geri almayı
kod seviyesinde mümkün kılan tek yaklaşım.
### Effort M · ### Priority P2

---

## Yapılandırma yönetimi

| Kontrol | Sonuç |
|---|---|
| Sabit kodlanmış URL | Bulunamadı ✅ |
| Sabit kodlanmış kimlik bilgisi | Yalnızca compose'da, **açıkça dev olarak etiketli** ✅ |
| Depoda üretim sırrı | Yok ✅ (git geçmişi dahil tarandı) |
| `.env` gitignore | ✅ `.env`, `.env.*`, `!.env.example` |
| Sır tek kaynak | ✅ Kök `.env`, `__` convention |
| Ortam ayrımı | 🟡 Development / Production var; **Staging yok** ve SEC-005 buna bağlı |

`.env.example` olağandışı derecede iyi belgelenmiş: her anahtarın **neden**
gerektiği, boş bırakılırsa ne olacağı ve hangi anahtarın hangisinden farklı
olması gerektiği yazılı. Bu, denetimin gördüğü en iyi örneklerden biri.
