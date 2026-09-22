# 15 — Tüm Paketler, Bağımlılıklar ve Sürümler

> Sürümler 2026-08 itibarıyla makul hedeflerdir; kurulumda `latest stable` doğrulanmalı. `★` = Faz 1'de zaten var, korunuyor.

---

## 1. Backend — .NET 10 (LTS)

### 1.1 `Namines.Contracts`
| Paket | Sürüm | Amaç |
|---|---|---|
| — | — | Bağımlılıksız (sadece POCO/record) |

### 1.2 `Namines.Nsl` (parser, validator, differ)
| Paket | Sürüm | Amaç |
|---|---|---|
| `System.Text.Json` | 10.0.* | Kanonik serileştirme |
| `Superpower` | 3.1.0 | `.nsl` gramer parser'ı (alternatif: `Pidgin` 3.4.0) |
| `Microsoft.CodeAnalysis.CSharp` | 4.14.* | Sadece source generator için (opsiyonel) |
| `JsonSchema.Net` | 7.3.* | NSL JSON Schema doğrulaması |

### 1.3 `Namines.Compiler` (DDL/ORM/docs backend'leri)
| Paket | Sürüm | Amaç |
|---|---|---|
| `Microsoft.SqlServer.TransactSql.ScriptDom` | 170.* | T-SQL parse/format (DDL import) |
| `SqlParser` (`SqlParserCS`) | 0.5.* | Motor-bağımsız SQL parse |
| `Scriban` | 6.2.* | Şablon motoru (ORM/app iskeletleri için) |
| `QuestPDF` ★ | 2026.* | Veri sözlüğü PDF |
| `ClosedXML` | 0.105.* | Excel veri sözlüğü |
| `SharpZipLib` / `System.IO.Compression` | — | ZIP paketleme ★ |

### 1.4 `Namines.Core`
| Paket | Sürüm | Amaç |
|---|---|---|
| `FluentValidation` | 12.0.* | İş kuralı doğrulaması |
| `MediatR` | 13.0.* | CQRS (opsiyonel — basit tutulacaksa atla) |
| `Ardalis.GuardClauses` | 5.0.* | Guard'lar |
| `NodaTime` | 3.2.* | Zaman dilimi doğruluğu (opsiyonel) |
| `Ulid` | 1.3.* | ULID kimlikler |

### 1.5 `Namines.Ai`
| Paket | Sürüm | Amaç |
|---|---|---|
| `Anthropic.SDK` | 5.* | Claude API |
| `OpenAI` | 2.* | OpenAI + uyumlu endpoint'ler (Groq ★) |
| `Google_GenerativeAI` veya `Mscc.GenerativeAI` | 2.* | Gemini ★ |
| `OllamaSharp` | 5.* | Yerel model ★ |
| `Microsoft.Extensions.AI` | 10.0.* | Birleşik AI soyutlaması (sağlayıcı-agnostik) |
| `Microsoft.Extensions.AI.Abstractions` | 10.0.* | |
| `Microsoft.Extensions.VectorData.Abstractions` | 10.0.* | Vektör deposu soyutlaması |
| `Polly` | 8.6.* | Retry, circuit breaker, timeout |
| `Tiktoken` veya `SharpToken` | 2.* | Token sayımı ★ (kota için) |

### 1.6 `Namines.DataPlane`
| Paket | Sürüm | Amaç |
|---|---|---|
| `Npgsql` | 10.0.* | PostgreSQL sürücüsü |
| `Microsoft.Data.SqlClient` | 6.1.* | SQL Server ★ |
| `MySqlConnector` | 2.4.* | MySQL/MariaDB |
| `Microsoft.Data.Sqlite` | 10.0.* | SQLite ★ |
| `Oracle.ManagedDataAccess.Core` | 23.* | Oracle |
| `KubernetesClient` | 17.* | Sandbox Job provisioning (docker.sock yerine) |
| `Docker.DotNet` ★ | 3.125.* | **Sadece yerel geliştirmede**, prod'da kullanılmaz |
| `Renci.SshNet` | 2025.* | Bridge tünel yardımcıları (opsiyonel) |

### 1.7 `Namines.Infrastructure`
| Paket | Sürüm | Amaç |
|---|---|---|
| `Microsoft.EntityFrameworkCore` ★ | 10.0.* | ORM |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.* | Control DB (SQLite'ın yerine) |
| `Microsoft.EntityFrameworkCore.Design` ★ | 10.0.* | Migration araçları |
| `StackExchange.Redis` | 2.9.* | Cache, kota, kilit |
| `Microsoft.Extensions.Caching.StackExchangeRedis` | 10.0.* | Dağıtık cache |
| `Microsoft.Extensions.Caching.Hybrid` | 10.0.* | L1+L2 hibrit cache |
| `NATS.Net` | 2.6.* | İş kuyruğu / event bus |
| `AWSSDK.S3` | 4.0.* | S3/MinIO/R2 |
| `Stripe.net` ★ | 48.* | Faturalama |
| `VaultSharp` | 1.17.* | Sır yönetimi |
| `MailKit` | 4.14.* | E-posta (veya `Resend` HTTP API) |
| `Octokit` | 15.* | GitHub App |
| `Octokit.Webhooks.AspNetCore` | 2.* | Webhook doğrulama |
| `RedLock.net` | 2.3.* | Dağıtık kilit |
| `Konscious.Security.Cryptography.Argon2` | 1.3.* | Şifre hash'i |
| `ClickHouse.Client` | 7.* | Kullanım analitiği |

### 1.8 `Namines.Api` (control plane)
| Paket | Sürüm | Amaç |
|---|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` ★ | 10.0.* | JWT |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` ★ | 10.0.* | Kimlik |
| `AspNet.Security.OAuth.GitHub` | 10.0.* | GitHub OAuth |
| `Microsoft.AspNetCore.Authentication.Google` | 10.0.* | Google OAuth |
| `Microsoft.AspNetCore.RateLimiting` ★ | (built-in) | Rate limit |
| `Microsoft.AspNetCore.OpenApi` | 10.0.* | OpenAPI (Swashbuckle yerine yerleşik) |
| `Scalar.AspNetCore` | 2.* | API dokümantasyon UI'ı (Swagger UI ★ yerine, daha modern) |
| `Serilog.AspNetCore` ★ | 9.* | Log |
| `Serilog.Sinks.Console` ★ | 6.* | stdout (dosya sink'i **kaldırılıyor**) |
| `Serilog.Sinks.OpenTelemetry` | 4.* | Merkezî log |
| `Serilog.Enrichers.Span` | 3.* | Trace korelasyonu |
| `AspNetCore.HealthChecks.NpgSql` | 10.* | DB sağlık ★ |
| `AspNetCore.HealthChecks.Redis` | 10.* | |
| `AspNetCore.HealthChecks.UI.Client` ★ | 10.* | |
| `DotNetEnv` ★ | 3.1.* | Yerel `.env` |
| `HtmlAgilityPack` ★ | 1.12.* | URL kazıma |
| `Asp.Versioning.Http` | 8.* | API sürümleme |

### 1.9 `Namines.Gateway`
| Paket | Sürüm | Amaç |
|---|---|---|
| `Npgsql` | 10.0.* | |
| `Dapper` | 2.1.* | Hafif, hızlı sorgu yürütme (EF değil) |
| `HotChocolate.AspNetCore` | 15.* | GraphQL |
| `HotChocolate.Data` | 15.* | Filtre/sıralama/sayfalama |
| `GreenDonut` | 15.* | DataLoader (N+1 önleme) |
| `Microsoft.AspNetCore.OutputCaching.StackExchangeRedis` | 10.0.* | Yanıt cache |
| `System.Text.Json` | 10.0.* | Streaming serileştirme |
| `Microsoft.Extensions.Http.Resilience` | 10.0.* | Dayanıklılık |

### 1.10 `Namines.Realtime`
| Paket | Sürüm | Amaç |
|---|---|---|
| `Microsoft.AspNetCore.SignalR` ★ | (built-in) | Hub |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | 10.0.* | **Backplane (Faz 1'de yoktu — kritik)** |
| `MessagePack` | 3.1.* | Binary protokol (bant genişliği) |
| `Ycs` | 0.5.* | Yjs C# portu (opsiyonel — Node sidecar tercih edilirse gerekmez) |

### 1.11 `Namines.Worker`
| Paket | Sürüm | Amaç |
|---|---|---|
| `Microsoft.Extensions.Hosting` | 10.0.* | Worker host |
| `NATS.Net` | 2.6.* | Kuyruk tüketimi |
| `Quartz.Extensions.Hosting` | 3.14.* | Zamanlanmış işler (sweeper, yedek) |
| `Polly` | 8.6.* | Retry |
| `Bogus` | 35.* | Data Factory sahte veri ★ |

### 1.12 `Namines.Bot`
| Paket | Sürüm | Amaç |
|---|---|---|
| `Octokit` | 15.* | GitHub API |
| `Octokit.Webhooks.AspNetCore` | 2.* | Webhook |
| `Microsoft.IdentityModel.Tokens` | 8.* | App JWT |

### 1.13 `Namines.Bridge` (on-prem agent)
| Paket | Sürüm | Amaç |
|---|---|---|
| `Microsoft.Extensions.Hosting.WindowsServices` | 10.0.* | Windows Service |
| `Microsoft.Extensions.Hosting.Systemd` | 10.0.* | Linux servis |
| `System.Net.WebSockets.Client` | (built-in) | Outbound tünel |
| Tüm DB sürücüleri | | Introspection |
| **PublishAot** | `true` | Tek dosya, ~25 MB |

### 1.14 `Namines.Cli` (dotnet tool)
| Paket | Sürüm | Amaç |
|---|---|---|
| `System.CommandLine` | 2.0.* | CLI çatısı |
| `Spectre.Console` | 0.51.* | Zengin terminal çıktısı |
| `Namines.Nsl` / `Namines.Compiler` | proje ref | Çekirdek |

### 1.15 Test projeleri
| Paket | Sürüm | Amaç |
|---|---|---|
| `xunit.v3` | 3.* | Test çatısı |
| `Microsoft.NET.Test.Sdk` | 17.* | |
| `FluentAssertions` | 8.* | Assert (lisans notu: v8 ticari — alternatif `Shouldly` 4.*) |
| `NSubstitute` | 5.* | Mock |
| `Testcontainers` | 4.* | Gerçek DB container'ları |
| `Testcontainers.PostgreSql` | 4.* | |
| `Testcontainers.MsSql` | 4.* | |
| `Testcontainers.MySql` | 4.* | |
| `Testcontainers.MariaDb` | 4.* | |
| `Testcontainers.Oracle` | 4.* | |
| `Testcontainers.Redis` | 4.* | |
| `Verify.Xunit` | 30.* | **Golden-file (snapshot) testleri — codegen için kritik** |
| `Bogus` | 35.* | Test verisi |
| `NBomber` | 6.* | Yük testi |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.* | Integration test |
| `coverlet.collector` | 6.* | Kapsam |

---

## 2. Frontend — Node 22 / pnpm 10

### 2.1 `apps/web` (marketing + Studio)
```jsonc
{
  "dependencies": {
    "next": "16.2.6",                          // ★
    "react": "19.2.4",                          // ★
    "react-dom": "19.2.4",                      // ★
    "@xyflow/react": "^12.10.2",                // ★ canvas
    "zustand": "^5.0.13",                       // ★ UI state
    "@tanstack/react-query": "^5.90.0",         // sunucu state
    "@tanstack/react-table": "^8.21.0",         // veri ızgarası
    "@tanstack/react-virtual": "^3.13.0",       // sanallaştırma
    "yjs": "^13.6.27",                          // CRDT
    "y-websocket": "^3.0.0",
    "y-indexeddb": "^9.0.12",                   // offline
    "y-protocols": "^1.0.6",
    "@monaco-editor/react": "^4.7.0",           // NSL kod editörü
    "monaco-editor": "^0.54.0",
    "elkjs": "^0.11.0",                         // auto-layout
    "d3-force": "^3.0.0",                       // alternatif layout
    "dagre": "^0.8.5",
    "@microsoft/signalr": "^10.0.0",            // ★ realtime
    "axios": "^1.16.0",                         // ★
    "mermaid": "^11.15.0",                      // ★ diyagram
    "prismjs": "^1.30.0",                       // ★ kod vurgulama
    "sql.js": "^1.14.1",                        // ★ WASM SQL
    "localforage": "^1.10.0",                   // ★
    "html-to-image": "^1.11.13",                // ★ canvas export
    "react-draggable": "^4.5.0",                // ★
    "lucide-react": "^1.14.0",                  // ★ ikonlar
    "@radix-ui/react-dialog": "^1.1.15",        // ★
    "@radix-ui/react-context-menu": "^2.2.16",  // ★
    "@radix-ui/react-tooltip": "^1.2.8",        // ★
    "@radix-ui/react-dropdown-menu": "^2.1.16",
    "@radix-ui/react-select": "^2.2.6",
    "@radix-ui/react-tabs": "^1.1.13",
    "@radix-ui/react-popover": "^1.1.15",
    "@radix-ui/react-accordion": "^1.2.12",
    "@radix-ui/react-switch": "^1.2.6",
    "@radix-ui/react-toast": "^1.2.15",
    "class-variance-authority": "^0.7.1",       // shadcn/ui deseni
    "clsx": "^2.1.1",
    "tailwind-merge": "^3.3.0",
    "cmdk": "^1.1.1",                           // ⌘K paleti ★
    "sonner": "^2.0.0",                         // toast ★
    "react-hook-form": "^7.64.0",
    "@hookform/resolvers": "^5.2.0",
    "zod": "^4.1.0",
    "date-fns": "^4.1.0",
    "recharts": "^3.2.0",                       // dashboard grafikleri
    "next-themes": "^0.4.6",                    // tema
    "next-intl": "^4.3.0",                      // i18n (TR/EN ★)
    "@namines/nsl": "workspace:*",              // kendi paketimiz
    "@namines/client": "workspace:*",
    "posthog-js": "^1.270.0",                   // ürün analitiği
    "@sentry/nextjs": "^10.0.0"                 // hata takibi
  },
  "devDependencies": {
    "typescript": "^5.9.0",
    "@types/node": "^22",
    "@types/react": "^19",
    "@types/react-dom": "^19",
    "@types/d3-force": "^3.0.10",
    "@types/dagre": "^0.7.53",
    "@types/prismjs": "^1.26.6",                // ★
    "@types/sql.js": "^1.4.11",                 // ★
    "tailwindcss": "^4.1.0",                    // ★
    "@tailwindcss/postcss": "^4.1.0",           // ★
    "eslint": "^9.38.0",                        // ★
    "eslint-config-next": "16.2.6",             // ★
    "prettier": "^3.6.0",
    "prettier-plugin-tailwindcss": "^0.6.14",
    "vitest": "^3.2.0",
    "@vitejs/plugin-react": "^5.0.0",
    "@testing-library/react": "^16.3.0",
    "@testing-library/user-event": "^14.6.0",
    "@playwright/test": "^1.56.0",              // E2E
    "@axe-core/playwright": "^4.10.0",          // erişilebilirlik testi
    "msw": "^2.11.0"                            // API mock
  }
}
```

### 2.2 `apps/console` (otomatik admin panel)
```jsonc
{
  "dependencies": {
    "next": "16.2.6",
    "react": "19.2.4",
    "react-dom": "19.2.4",
    "@tanstack/react-query": "^5.90.0",
    "@tanstack/react-table": "^8.21.0",
    "@tanstack/react-virtual": "^3.13.0",
    "react-hook-form": "^7.64.0",
    "@hookform/resolvers": "^5.2.0",
    "zod": "^4.1.0",
    "@monaco-editor/react": "^4.7.0",          // JSON kolon editörü
    "recharts": "^3.2.0",                      // dashboard
    "date-fns": "^4.1.0",
    "react-day-picker": "^9.11.0",             // tarih seçici
    "uploadthing" veya "@aws-sdk/client-s3": "*", // dosya alanları
    "lucide-react": "^1.14.0",
    "@radix-ui/*": "*",                         // web ile aynı set
    "cmdk": "^1.1.1",
    "sonner": "^2.0.0",
    "next-themes": "^0.4.6",
    "next-intl": "^4.3.0",
    "@namines/client": "workspace:*",
    "@namines/ui": "workspace:*"
  }
}
```

### 2.3 `apps/docs`
```jsonc
{ "dependencies": { "nextra": "^4.6.0", "nextra-theme-docs": "^4.6.0", "next": "16.2.6" } }
```

### 2.4 Paylaşılan paketler (`packages/`)

| Paket | Ad | İçerik | Bağımlılıklar |
|---|---|---|---|
| `packages/nsl` | `@namines/nsl` | NSL TS portu: parse, validate, diff, DBML/DDL derleme | `zod` |
| `packages/client` | `@namines/client` | Gateway SDK (tipli) | `zod` |
| `packages/ui` | `@namines/ui` | Paylaşılan bileşenler (shadcn tabanlı) | radix, tailwind, cva |
| `packages/cli` | `namines` (npm) | CLI (`npx namines`) | `commander`, `chalk`, `ora`, `execa` |
| `packages/eslint-config` | `@namines/eslint-config` | Ortak lint | |
| `packages/tsconfig` | `@namines/tsconfig` | Ortak TS config | |
| `packages/prompts` | `@namines/prompts` | Prompt dosyaları (md + meta) | |
| `packages/evals` | `@namines/evals` | AI eval harness | `vitest` |

### 2.5 `services/yjs` (Node sidecar)
```jsonc
{
  "dependencies": {
    "yjs": "^13.6.27",
    "y-websocket": "^3.0.0",
    "y-protocols": "^1.0.6",
    "ws": "^8.18.0",
    "ioredis": "^5.8.0",
    "lib0": "^0.2.99"
  }
}
```

### 2.6 npm CLI paketi (`namines`)
```jsonc
{
  "name": "namines",
  "bin": { "namines": "./dist/index.js" },
  "dependencies": {
    "commander": "^14.0.0",
    "chalk": "^5.6.0",
    "ora": "^9.0.0",
    "execa": "^9.6.0",
    "prompts": "^2.4.2",
    "@namines/nsl": "workspace:*",
    "undici": "^7.16.0"
  }
}
```

---

## 3. Docker imajları

| İmaj | Etiket | Kullanım |
|---|---|---|
| `mcr.microsoft.com/dotnet/sdk` | `10.0` | Build stage |
| `mcr.microsoft.com/dotnet/aspnet` | `10.0-alpine` | Runtime (api/gateway/realtime/bot) |
| `mcr.microsoft.com/dotnet/runtime-deps` | `10.0-alpine` | AOT runtime (worker/bridge) |
| `node` | `22-alpine` | Frontend build + yjs sidecar |
| `postgres` | `17-alpine` | Control DB + sandbox |
| `redis` | `7.4-alpine` | Cache/backplane |
| `nats` | `2.11-alpine` | Kuyruk |
| `minio/minio` | `latest` | Object storage (yerel) |
| `clickhouse/clickhouse-server` | `25-alpine` | Analitik |
| `mysql` | `8.4` | Sandbox |
| `mariadb` | `11.4` | Sandbox |
| `mcr.microsoft.com/mssql/server` | `2022-latest` | Sandbox |
| `gvisor.dev/images/runsc` | — | Sandbox runtime |
| `pgbouncer/pgbouncer` | `1.24` | Bağlantı havuzu |
| `traefik` | `v3.3` | Ingress |
| `grafana/grafana` `grafana/loki` `grafana/tempo` | `latest` | Gözlemlenebilirlik |
| `otel/opentelemetry-collector-contrib` | `latest` | Telemetri |
| `hashicorp/vault` | `1.18` | Sır yönetimi |
| `jaegertracing/all-in-one` | `latest` | Yerel trace |
| `aquasec/trivy` | `latest` | Güvenlik taraması (CI) |

---

## 4. Harici servisler ve SDK'lar

| Servis | Amaç | Plan | SDK |
|---|---|---|---|
| **Neon** | Managed PG + branching | Free → Scale | REST API |
| **PlanetScale** | Managed MySQL | Scaler | REST API |
| **Vercel** | Frontend hosting | Pro $20 | — |
| **Railway** / **Hetzner** | Backend hosting | $20 → €60 | — |
| **Cloudflare** | DNS, R2, CDN, WAF | Free → $20 | `AWSSDK.S3` (R2 uyumlu) |
| **Stripe** ★ | Faturalama | %2.9+30¢ | `Stripe.net` |
| **Anthropic** | Claude | kullanım | `Anthropic.SDK` |
| **Groq** ★ | Hızlı Llama + Whisper | free → kullanım | `OpenAI` (uyumlu) |
| **Google AI** ★ | Gemini | free → kullanım | `Mscc.GenerativeAI` |
| **Resend** | Transactional e-posta | $20 | HTTP |
| **PostHog** | Ürün analitiği | Free → $0.00005/olay | `posthog-js`, `PostHog` (.NET) |
| **Sentry** | Hata takibi | Free → $26 | `@sentry/nextjs`, `Sentry.AspNetCore` |
| **Grafana Cloud** | Log/metrik/trace | Free → $50 | OTLP |
| **Upstash** | Serverless Redis | Free → kullanım | `StackExchange.Redis` |
| **GitHub** | App, Actions, OAuth | Free | `Octokit` |
| **Better Stack** / **Statuspage** | Durum sayfası | $30 | — |
| **Algolia** / **Typesense** | Dokümantasyon araması | Free | — |
| **Crisp** / **Plain** | Müşteri desteği | $25 | — |

---

## 5. Geliştirme araçları

| Araç | Sürüm | Amaç |
|---|---|---|
| .NET SDK | 10.0.* | Backend |
| Node.js | 22 LTS | Frontend |
| pnpm | 10.* | Monorepo paket yöneticisi |
| Turborepo | 2.* | Monorepo görev orkestrasyonu |
| Docker Desktop / Podman | son | Yerel servisler |
| kind / k3d | son | Yerel Kubernetes (sandbox testi) |
| `dotnet-ef` | 10.0.* | Migration |
| `dotnet-format` / `csharpier` | son | Formatlama |
| `husky` + `lint-staged` | son | Git hook'ları |
| `commitlint` | son | Conventional commits |
| `changesets` | son | Sürümleme/changelog |
| `gitleaks` | son | Sır taraması |
| `k6` / `NBomber` | son | Yük testi |
| `Playwright` | 1.56 | E2E |
| `Bruno` / `Hoppscotch` | son | API testi |

---

## 6. Paket seçim gerekçeleri (tartışmalı olanlar)

| Seçim | Neden | Reddedilen |
|---|---|---|
| **Dapper** (Gateway'de) | EF Core dinamik şema için uygun değil; Gateway runtime'da tablo bilir, tip bilmez | EF Core |
| **HotChocolate** | .NET'te GraphQL standardı, filtre/sıralama built-in | GraphQL.NET |
| **NATS** | Hafif, JetStream ile kalıcı, .NET desteği iyi | Kafka (ağır), RabbitMQ (operasyonel yük), Hangfire (DB polling) |
| **Verify** | Golden-file testleri için en iyi .NET aracı | Elle string karşılaştırma |
| **Testcontainers** | "DDL gerçekten çalışıyor mu" sorusunun tek dürüst cevabı | Mock |
| **Yjs (Node sidecar)** | Olgun, savaş görmüş; C# portu riskli | Ycs, kendi OT implementasyonu |
| **Superpower/Pidgin** | `.nsl` için okunabilir combinator parser | ANTLR (ağır kod üretimi) |
| **Scalar** | Modern OpenAPI UI | Swagger UI ★ (eskimiş görünüm) |
| **PostgreSQL** (control) | Faz 1'deki SQLite yatay ölçeklenemez | SQLite |
| **pnpm + Turborepo** | Monorepo'da hız ve disk verimliliği | npm workspaces, Nx (ağır) |
| **Shouldly** veya FluentAssertions | FluentAssertions v8+ ticari lisans — dikkat | — |
| **Microsoft.Extensions.AI** | Sağlayıcı değiştirmeyi tek satıra indirir | Her sağlayıcı için ayrı kod ★ |
