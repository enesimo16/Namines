# 03 — Sistem Mimarisi

## 1. Üst seviye görünüm

```
                          ┌──────────────────────────────┐
   Tarayıcı ──────────────│  CDN / Vercel Edge           │
                          │  namines.com (marketing)     │
                          │  app.namines.com (Studio)    │
                          │  console.namines.com (admin) │
                          └───────────┬──────────────────┘
                                      │ HTTPS
                          ┌───────────▼──────────────────┐
                          │   Edge / Ingress (Traefik)   │
                          │   TLS, WAF, rate limit       │
                          └───┬───────────┬──────────┬───┘
                              │           │          │
        ┌─────────────────────▼──┐  ┌─────▼──────┐ ┌─▼─────────────────┐
        │ CONTROL PLANE API      │  │ GATEWAY    │ │ REALTIME          │
        │ Namines.Api (.NET 9)   │  │ (.NET 9)   │ │ (SignalR + Yjs)   │
        │ :8080                  │  │ :8081      │ │ :8082             │
        └──┬────────┬────────┬───┘  └─────┬──────┘ └──────┬────────────┘
           │        │        │            │               │
           │        │        │            │               │
    ┌──────▼──┐ ┌───▼────┐ ┌─▼──────────┐ │        ┌──────▼──────┐
    │Postgres │ │ Redis  │ │ NATS       │ │        │ Redis       │
    │ control │ │ cache  │ │ JetStream  │ │        │ backplane   │
    │ :5432   │ │ :6379  │ │ :4222      │ │        │             │
    └─────────┘ └────────┘ └─────┬──────┘ │        └─────────────┘
                                 │        │
        ┌────────────────────────▼────────▼──────────────────────┐
        │  WORKERS (.NET 9 Worker Service)                       │
        │  Provisioner · Migrator · Seeder · Backup · Introspect │
        │  · Codegen · AI Jobs · Metering                        │
        └────────────────────────┬───────────────────────────────┘
                                 │
        ┌────────────────────────▼───────────────────────────────┐
        │  DATA PLANE (tenant veritabanları)                     │
        │  ├─ Managed Postgres (Neon / RDS / kendi PG cluster)   │
        │  ├─ Managed MySQL / SQL Server                         │
        │  ├─ Ephemeral sandbox (gVisor izole container)         │
        │  └─ BYODB (kullanıcının kendi DB'si) / Bridge agent    │
        └────────────────────────────────────────────────────────┘

        ┌────────────────────────────────────────────────────────┐
        │  DESTEK: MinIO/S3 (export,backup) · ClickHouse (usage) │
        │  · OpenTelemetry Collector · Grafana/Loki/Tempo        │
        └────────────────────────────────────────────────────────┘
```

---

## 2. Servis envanteri

| # | Servis | Teknoloji | Port | Sorumluluk | Ölçekleme |
|---|---|---|---|---|---|
| 1 | `namines-web` | Next.js 16 | 3000 | Pazarlama + Studio | Vercel / statik |
| 2 | `namines-console` | Next.js 16 | 3001 | Otomatik admin panel (runtime renderer) | Vercel / statik |
| 3 | `namines-api` | .NET 9 ASP.NET Core | 8080 | Control plane: proje, şema, auth, faturalama | Yatay (stateless) |
| 4 | `namines-gateway` | .NET 9 Minimal API | 8081 | Tenant DB üzerinde REST/GraphQL | Yatay (stateless) |
| 5 | `namines-realtime` | .NET 9 + SignalR | 8082 | Presence, CRDT relay | Yatay + Redis backplane |
| 6 | `namines-worker` | .NET 9 Worker | — | Provisioning, migration, seed, codegen, AI job | Yatay (kuyruk tüketici) |
| 7 | `namines-bot` | .NET 9 Minimal API | 8083 | GitHub App webhook | Yatay |
| 8 | `namines-bridge` | .NET 9 self-contained | — | Müşteri tarafında agent | Tek instance/müşteri |
| 9 | `postgres-control` | PostgreSQL 17 | 5432 | Control plane veritabanı | Primary + replica |
| 10 | `redis` | Redis 7.4 | 6379 | Cache, kota, backplane, kilit | Cluster |
| 11 | `nats` | NATS 2.11 + JetStream | 4222 | İş kuyruğu, event bus | Cluster |
| 12 | `minio` | MinIO | 9000 | Export, backup, dosya alanları | Dağıtık |
| 13 | `clickhouse` | ClickHouse 25 | 8123 | Kullanım/analitik olayları | Tek node → cluster |
| 14 | `otel-collector` | OpenTelemetry | 4317 | Telemetri toplama | DaemonSet |

> **Neden .NET 9?** Faz 1 .NET 8. .NET 9 LTS değil ama Native AOT, daha iyi `System.Text.Json` ve `HybridCache` getiriyor. **Karar: .NET 10 LTS'e geç** (2025 Kasım'da çıktı, 2026'da olgun). Tüm paket listeleri [15-PACKAGES.md](15-PACKAGES.md)'te .NET 10 varsayımıyla yazıldı.

---

## 3. Katmanlı proje yapısı (backend)

```
Namines.Contracts      → DTO'lar, event şemaları (bağımlılıksız)
Namines.Nsl            → NSL parser, IR, validator, differ  ★ yeni çekirdek
Namines.Compiler       → NSL → DDL/ORM/docs derleyicileri (6 motor + 12 hedef)
Namines.Core           → Domain modelleri, arayüzler, iş kuralları
Namines.Ai             → Copilot ajanları, prompt'lar, eval
Namines.DataPlane      → Provisioning, introspection, migration yürütme
Namines.Infrastructure → EF Core, Redis, NATS, S3, Stripe, sağlayıcı adaptörleri
Namines.Api            → Control plane HTTP
Namines.Gateway        → Tenant runtime API
Namines.Realtime       → SignalR hub
Namines.Worker         → Arka plan işleri
Namines.Bot            → GitHub App
Namines.Bridge         → On-prem agent
Namines.Cli            → dotnet tool
```

**Bağımlılık kuralı (zorunlu):**
```
Contracts ← Nsl ← Compiler ← Core ← Ai/DataPlane ← Infrastructure ← (Api|Gateway|Worker|...)
```
Ters yönde referans yasak. `Namines.Nsl` ve `Namines.Compiler` **hiçbir I/O yapmaz** — saf fonksiyon, bu yüzden test edilebilir ve WASM'a derlenebilir (tarayıcıda anlık DDL önizlemesi için).

---

## 4. Ana veri akışları

### 4.1 "Fikirden çalışan backend'e" (aha momenti)

```
1. Kullanıcı Studio'da prompt yazar
2. api → Copilot Agent (Plan → Generate → Validate → Optimize)
3. NSL IR üretilir, sunucuda doğrulanır (Namines.Nsl.Validator)
4. Canvas'ta render edilir (CRDT dokümanına yazılır)
5. Kullanıcı "Deploy" der
6. api → NATS'e ProvisionRequested olayı
7. worker → sağlayıcıdan DB alır (Neon API / kendi PG cluster)
8. worker → Namines.Compiler ile DDL üretir, uygular
9. worker → Data Factory ile seed atar (opsiyonel)
10. worker → SchemaDeployed olayı yayınlar
11. gateway metadata cache'ini yeniler → REST/GraphQL anında canlı
12. console metadata'yı çeker → admin panel anında canlı
13. Kullanıcıya 3 URL döner:
    console.namines.com/p/{slug} · api.namines.com/v1/{slug} · postgres://...
```

**Hedef süre: < 90 saniye (soğuk), < 20 saniye (sıcak havuzdan).**

### 4.2 Şema değişikliği yayılımı (senkron vaadi)

```
NSL değişti
  ├→ Migration Safety Engine: diff + risk sınıflandırma
  ├→ [tehlikeli ise] onay iste + rollback script üret
  ├→ worker: migration uygula (branch → staging → prod sırası)
  ├→ event: SchemaVersionChanged { projectId, version }
  ├→ gateway: metadata reload (hot, restart yok)
  ├→ console: metadata reload (websocket push, sayfa yenilenmez)
  ├→ codegen: TS tipleri / OpenAPI / SDK yeniden üretilir
  └→ bot: bağlı GitHub repo'suna PR açar (`.nsl` + üretilmiş tipler)
```

### 4.3 Console'da bir kayıt okuma

```
Tarayıcı → console (Next.js RSC)
  → gateway /v1/{proje}/tables/{tablo}/rows?filter=...
    → API key / JWT doğrula
    → metadata cache'ten tablo tanımı al (Redis, 60s TTL)
    → RBAC: kullanıcının rolü bu tabloyu okuyabilir mi?
    → RLS: satır filtresi enjekte et (SET LOCAL app.user_id = ...)
    → parametreli SQL üret (asla string birleştirme)
    → tenant connection pool'undan bağlantı al (PgBouncer)
    → çalıştır, satırları döndür
    → audit log yaz (async, NATS)
```

---

## 5. Kritik mimari kararlar (ADR özetleri)

| # | Karar | Neden | Reddedilen alternatif |
|---|---|---|---|
| ADR-01 | **Console runtime-rendered, codegen değil** | Şema değişince panel anında güncellenir; codegen'de kullanıcı yeniden deploy etmek zorunda | Streamlit/Next ZIP üretimi → *eject özelliği olarak korunur* |
| ADR-02 | **NSL ayrı, saf bir çekirdek kütüphane** | Test edilebilirlik + WASM'da tarayıcıda çalıştırma + CLI'da yeniden kullanım | Modelin API projesinde kalması |
| ADR-03 | **Control plane DB = PostgreSQL** (SQLite değil) | Yatay ölçekleme, eşzamanlı yazma, JSONB, gerçek indexler | SQLite (Faz 1) — çok instance'ta imkânsız |
| ADR-04 | **Provisioning'i satın al, yazma** (Neon/RDS) | Kendi PG cluster'ını işletmek 1 kişilik ekip için ölümcül | Kendi Kubernetes PG operatörü → v3'e ertelendi |
| ADR-05 | **docker.sock ASLA mount edilmez** | Host root eşdeğeri; çok kiracılı SaaS'ta kabul edilemez | Faz 1'deki yaklaşım |
| ADR-06 | **Ephemeral sandbox = gVisor/Firecracker + broker** | Kullanıcı tetiklemeli container'lar güçlü izolasyon ister | Ham Docker API |
| ADR-07 | **CRDT (Yjs) collab için** | Son-yazan-kazanır veri kaybettiriyor; CRDT çakışmasız | Faz 1 broadcast rölesi |
| ADR-08 | **NATS JetStream iş kuyruğu** | Hafif, .NET desteği iyi, at-least-once, Kafka'dan basit | Hangfire (DB polling, ölçeksiz), RabbitMQ (ağır) |
| ADR-09 | **Gateway ayrı servis** | Tenant trafiği control plane'i etkilememeli; farklı ölçekleme profili | Tek monolit |
| ADR-10 | **Tenant izolasyonu: DB-per-project** (schema-per-project değil) | Gerçek izolasyon, ayrı yedek, ayrı kota | Paylaşılan DB + RLS → daha ucuz ama sızıntı riski |
| ADR-11 | **Metadata cache Redis'te, TTL 60s + event invalidation** | Gateway'in her istekte control DB'ye gitmemesi | Her istekte DB okuma |
| ADR-12 | **Golden-file test + gerçek DB container'ı (Testcontainers)** | Codegen doğruluğu ürünün kendisi | Mock/unit test yetersiz |
| ADR-13 | **Prompt'lar versiyonlu dosyalar, kodda gömülü değil** | Eval + A/B + geri alma | Faz 1: kod içinde string |
| ADR-14 | **Frontend: Next.js App Router + RSC + Zustand + TanStack Query** | Faz 1 zaten Next 16/Zustand; TanStack Query sunucu state'i için eklenir | Redux, tam client-side |

---

## 6. Multi-tenancy modeli

| Katman | İzolasyon |
|---|---|
| Control plane | Tek DB, `organization_id` ile satır bazlı ayrım + RLS |
| Tenant veri | **Veritabanı başına proje** (ayrı DB, ayrı kullanıcı, ayrı pool) |
| Ephemeral sandbox | Container başına, gVisor, ağ yalıtımı, 60 dk TTL |
| Object storage | Bucket prefix: `org/{orgId}/project/{projectId}/...` |
| Cache | Redis key prefix: `n:{env}:{orgId}:{projectId}:...` |
| Metering | ClickHouse'da `org_id` partition |

Her tenant DB'si için ayrı düşük yetkili DB kullanıcısı: `namines_app_{projectId}` — sadece kendi DB'sine `CONNECT`, `SUPERUSER` yok, `pg_read_server_files` yok.

---

## 7. Performans bütçesi

| İşlem | p50 | p95 | p99 |
|---|---|---|---|
| Studio ilk yükleme (LCP) | 1.2 s | 2.5 s | 4 s |
| Canvas 200 tablo render | 300 ms | 800 ms | 1.5 s |
| Gateway satır listesi (100 satır) | 40 ms | 150 ms | 400 ms |
| Console sayfa geçişi | 150 ms | 400 ms | 800 ms |
| NSL → DDL derleme (100 tablo) | 15 ms | 50 ms | 120 ms |
| AI şema üretimi (ilk token) | 800 ms | 2 s | 5 s |
| DB provisioning (sıcak havuz) | 8 s | 20 s | 45 s |
| Migration uygulama (küçük) | 2 s | 8 s | 30 s |
| CRDT güncelleme yayılımı | 30 ms | 100 ms | 250 ms |

---

## 8. Bölgeler ve veri ikametgâhı

| Bölge | Kod | Ne zaman |
|---|---|---|
| EU (Frankfurt) | `eu-central-1` | Varsayılan (KVKK/GDPR) |
| US (Virginia) | `us-east-1` | v2.1 |
| TR (İstanbul) | `eu-tr-1` | v2.2 — KVKK'da yerel ikametgâh isteyen kurumsal müşteri için satış argümanı |

Control plane EU'da tek; data plane bölgesel.
