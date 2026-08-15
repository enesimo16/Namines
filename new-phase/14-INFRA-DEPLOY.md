# 14 — Altyapı & Dağıtım

## 1. Ortamlar

| Ortam | Amaç | Alan adı | Altyapı |
|---|---|---|---|
| `local` | Geliştirme | `localhost` | Docker Compose |
| `preview` | Her PR için | `pr-{n}.preview.namines.dev` | K8s namespace, otomatik silme |
| `staging` | Prod öncesi | `staging.namines.dev` | K8s, prod'un küçük kopyası |
| `production` | Canlı | `namines.com` | K8s, çoklu AZ |

---

## 2. Yerel geliştirme

`docker-compose.yml` (kök):

```yaml
name: namines

services:
  postgres:
    image: postgres:17-alpine
    environment: { POSTGRES_PASSWORD: dev, POSTGRES_DB: namines_control }
    ports: ["5432:5432"]
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck: { test: ["CMD-SHELL","pg_isready -U postgres"], interval: 5s }

  redis:
    image: redis:7.4-alpine
    ports: ["6379:6379"]
    command: ["redis-server","--appendonly","yes"]

  nats:
    image: nats:2.11-alpine
    command: ["-js","-sd","/data"]
    ports: ["4222:4222","8222:8222"]
    volumes: [natsdata:/data]

  minio:
    image: minio/minio:latest
    command: server /data --console-address ":9001"
    environment: { MINIO_ROOT_USER: minio, MINIO_ROOT_PASSWORD: minio123 }
    ports: ["9000:9000","9001:9001"]
    volumes: [miniodata:/data]

  clickhouse:
    image: clickhouse/clickhouse-server:25-alpine
    ports: ["8123:8123"]
    volumes: [chdata:/var/lib/clickhouse]

  jaeger:
    image: jaegertracing/all-in-one:latest
    ports: ["16686:16686","4317:4317"]

  api:
    build: { context: ., dockerfile: src/Namines.Api/Dockerfile }
    ports: ["8080:8080"]
    env_file: [.env]
    depends_on: [postgres, redis, nats]

  gateway:
    build: { context: ., dockerfile: src/Namines.Gateway/Dockerfile }
    ports: ["8081:8081"]
    env_file: [.env]

  realtime:
    build: { context: ., dockerfile: src/Namines.Realtime/Dockerfile }
    ports: ["8082:8082"]
    env_file: [.env]

  worker:
    build: { context: ., dockerfile: src/Namines.Worker/Dockerfile }
    env_file: [.env]

  yjs:
    build: { context: ./services/yjs }
    ports: ["1234:1234"]

  web:
    build: { context: ./apps/web }
    ports: ["3000:3000"]

  console:
    build: { context: ./apps/console }
    ports: ["3001:3001"]

volumes: { pgdata: {}, natsdata: {}, miniodata: {}, chdata: {} }
```

Tek komut: `make dev` → `docker compose up -d && pnpm dev`

> **Faz 1'den kaldırılan:** `/var/run/docker.sock` mount'u. Yerel geliştirmede ephemeral sandbox `kind` (Kubernetes in Docker) ile veya doğrudan compose'daki postgres ile simüle edilir.

---

## 3. Production — Kubernetes

**Sağlayıcı önerisi (maliyet/karmaşıklık dengesi):**

| Seçenek | Aylık maliyet (başlangıç) | Karmaşıklık | Öneri |
|---|---|---|---|
| **Hetzner + k3s** | ~€60 | Orta | ✅ **Başlangıç için** — 3× CPX31, çok ucuz, EU |
| DigitalOcean DOKS | ~$120 | Düşük | Alternatif |
| AWS EKS | ~$300+ | Yüksek | Yıl 2, kurumsal gereksinim gelince |
| Railway / Render | ~$80 | Çok düşük | **İlk 6 ay için en pragmatik** — K8s'siz başla |

**Karar: İlk 6 ay Railway/Render + Vercel + Neon. Ölçek gerektirince Hetzner k3s'e taşı.** Tek geliştiricinin Kubernetes işletmesi ürün geliştirmeyi öldürür.

### Kubernetes manifest özeti (ölçeklendiğinde)

```
k8s/
├── base/
│   ├── namespace.yaml              # namines-prod, namines-sandbox
│   ├── api/{deployment,service,hpa,pdb}.yaml
│   ├── gateway/{deployment,service,hpa,pdb}.yaml
│   ├── realtime/{statefulset,service}.yaml
│   ├── worker/{deployment,hpa}.yaml
│   ├── bot/{deployment,service}.yaml
│   ├── redis/           # veya yönetilen
│   ├── nats/
│   ├── ingress.yaml                # Traefik
│   ├── networkpolicy.yaml          # default deny
│   └── externalsecrets.yaml
├── overlays/{staging,production}/
└── sandbox/
    ├── runtimeclass-gvisor.yaml
    ├── job-template.yaml
    ├── networkpolicy-deny-all.yaml
    └── resourcequota.yaml
```

### Ölçekleme kuralları

| Servis | Min | Max | Tetikleyici |
|---|---|---|---|
| `api` | 2 | 10 | CPU %70 |
| `gateway` | 2 | 20 | CPU %70 veya rps > 500/pod |
| `realtime` | 2 | 6 | bağlantı sayısı > 3000/pod |
| `worker` | 1 | 8 | NATS kuyruk derinliği > 50 |
| `bot` | 1 | 3 | CPU |

PodDisruptionBudget: her serviste `minAvailable: 1`. Rolling update, `maxUnavailable: 0`.

---

## 4. Frontend dağıtımı

| Uygulama | Platform | Not |
|---|---|---|
| `apps/web` (marketing + Studio) | Vercel | ISR, edge, otomatik önizleme |
| `apps/console` | Vercel | Wildcard alan adı (`*.namines.app`) |
| `apps/docs` | Vercel / Cloudflare Pages | Nextra veya Docusaurus |

**Kritik uyarı (Faz 1'den ders):** `NEXT_PUBLIC_API_URL` **build zamanında** gömülür. Ortam başına ayrı build gerekir. Faz 1 README'sinde bu doğru not edilmişti — korunur ve CI'da doğrulanır.

---

## 5. CI/CD

`.github/workflows/`:

| Workflow | Tetikleyici | Adımlar |
|---|---|---|
| `ci.yml` | PR | restore → build → **unit test** → **golden-file test** → **Testcontainers integration** → lint → SAST → sır tarama |
| `eval.yml` | PR (AI dosyaları değişince) | AI eval suite → skor regresyonu varsa bloke |
| `preview.yml` | PR açılınca | Preview namespace + preview DB + Vercel önizleme |
| `release.yml` | `main`'e merge | Görüntü build + imzala + push → staging deploy → smoke test → **manuel onay** → production |
| `nightly.yml` | Gece | Tam test matrisi (6 motor × 3 sürüm), yedek restore tatbikatı, bağımlılık taraması |
| `schema-review.yml` | PR (`.nsl` değişince) | Namines Bot (kendi ürünümüzü kendimizde kullanıyoruz) |

**Kalite kapıları (merge için zorunlu):**
- Tüm testler yeşil
- Kod kapsamı ≥ %70 (Nsl/Compiler için ≥ %90)
- Golden-file testleri %100
- Yüksek/kritik güvenlik açığı yok
- AI eval skoru düşmemiş

---

## 6. Veritabanı migration (control plane)

```
EF Core migration'ları CI'da doğrulanır, deploy'da ayrı bir Job olarak çalışır.
Uygulama başlangıcında Database.Migrate() ÇAĞRILMAZ.
```

> **Faz 1'den değişiklik:** `Program.cs`'te startup'ta `Database.Migrate()` vardı — çok instance'ta yarış koşulu üretir. Ayrı bir `migrate` Job'a taşınır, deployment ondan sonra başlar (init container veya Helm hook).

---

## 7. Maliyet modeli (aylık, ölçeğe göre)

### Başlangıç (0-100 kullanıcı)

| Kalem | Sağlayıcı | Maliyet |
|---|---|---|
| Backend hosting | Railway | $20 |
| Frontend | Vercel Hobby/Pro | $0-20 |
| Control DB | Neon | $0-19 |
| Redis | Upstash | $0-10 |
| Object storage | Cloudflare R2 | $0-5 |
| AI | Groq free + Anthropic | $30 |
| Alan adı + e-posta | Cloudflare + Resend | $5 |
| Gözlemlenebilirlik | Grafana Cloud free | $0 |
| **Toplam** | | **~$90/ay** |

### Büyüme (1.000 kullanıcı, 300 provisioned DB)

| Kalem | Maliyet |
|---|---|
| K8s (Hetzner 3 node) | €60 |
| Tenant DB'ler (Neon, scale-to-zero) | $250 |
| Control DB + replica | $50 |
| Redis + NATS | $40 |
| Storage + CDN | $30 |
| AI | $400 |
| ClickHouse | $30 |
| Gözlemlenebilirlik | $50 |
| Stripe komisyonu | ~%3 gelir |
| **Toplam** | **~$920/ay** |

Bu ölçekte hedef MRR $8.000 → **brüt marj ~%88**. Sağlıklı.

**En büyük maliyet riski:** AI. Bu yüzden [09 §8](09-AI-LAYER.md)'deki maliyet kontrolleri opsiyonel değil, zorunlu.

---

## 8. Yedekleme ve felaket kurtarma

| Varlık | Yedek | Sıklık | Saklama | Test |
|---|---|---|---|---|
| Control DB | PITR + snapshot | sürekli / günlük | 30 gün | Aylık restore |
| Tenant DB | Sağlayıcı PITR | sürekli | plana göre | Çeyreklik |
| NSL sürümleri | S3 versiyonlu | her sürüm | süresiz | — |
| Object storage | Cross-region replication | sürekli | 90 gün | Çeyreklik |
| Sırlar (Vault) | Şifreli snapshot | günlük | 90 gün | Çeyreklik |

**RTO 4 saat / RPO 15 dakika.** Runbook: `docs/runbooks/disaster-recovery.md`.

---

## 9. Release stratejisi

- **Trunk-based**: `main` her zaman deploy edilebilir
- **Feature flag**: her riskli özellik bayrak arkasında (`Namines.FeatureFlags`, OpenFeature standardı)
- **Canary**: production'a %5 trafik ile başla, 15 dk metrik izle, otomatik geri alma
- **Sürümleme**: SemVer, `v2.0.0`, aylık minor / haftalık patch
- **Değişiklik günlüğü**: otomatik (conventional commits) + insan tarafından yazılmış özet
