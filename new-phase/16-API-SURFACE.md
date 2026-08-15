# 16 — Tüm API Yüzeyi (Endpoint, Port, Adres)

---

## 1. Alan adları ve portlar

| Adres | Servis | Yerel port | Prod port | Protokol |
|---|---|---|---|---|
| `namines.com` | Pazarlama sitesi | 3000 | 443 | HTTPS |
| `app.namines.com` | Studio | 3000 | 443 | HTTPS |
| `console.namines.com` | Console | 3001 | 443 | HTTPS |
| `{slug}.namines.app` | Console (Pro özel alt alan) | — | 443 | HTTPS |
| `api.namines.com` | Control Plane API | 8080 | 443 | HTTPS |
| `gw.namines.com` | Gateway (tenant API) | 8081 | 443 | HTTPS |
| `rt.namines.com` | Realtime | 8082 | 443 | WSS |
| `bot.namines.com` | GitHub App webhook | 8083 | 443 | HTTPS |
| `yjs.namines.com` | Yjs sidecar | 1234 | 443 | WSS |
| `docs.namines.com` | Dokümantasyon | 3002 | 443 | HTTPS |
| `status.namines.com` | Durum sayfası | — | 443 | HTTPS |
| `cdn.namines.com` | Statik varlıklar (R2) | — | 443 | HTTPS |
| `files.namines.app` | Kullanıcı yüklemeleri (ayrı origin!) | 9000 | 443 | HTTPS |

**Yerel geliştirme tabanı:** `http://localhost:8080` (Faz 1'de `:5000` idi — 8080'e standardize edilir, container'la aynı olsun diye)

---

## 2. Control Plane API — `api.namines.com/v1`

### 2.1 Kimlik ve hesap
| Metot | Yol | Auth | Faz 1 |
|---|---|---|---|
| POST | `/auth/register` | — | ★ |
| POST | `/auth/login` | — | ★ |
| POST | `/auth/logout` | user | ★ |
| POST | `/auth/refresh` | refresh token | yeni |
| POST | `/auth/verify-email` | — | yeni |
| POST | `/auth/resend-verification` | — | yeni |
| POST | `/auth/forgot-password` | — | yeni |
| POST | `/auth/reset-password` | — | yeni |
| POST | `/auth/magic-link` | — | yeni |
| GET | `/auth/oauth/{provider}` | — | yeni (github, google) |
| GET | `/auth/oauth/{provider}/callback` | — | yeni |
| GET | `/auth/me` | user | ★ |
| PATCH | `/auth/me` | user | yeni |
| POST | `/auth/2fa/enroll` | user | yeni |
| POST | `/auth/2fa/verify` | user | yeni |
| DELETE | `/auth/2fa` | user+2fa | yeni |
| GET | `/auth/sessions` | user | yeni |
| DELETE | `/auth/sessions/{id}` | user | yeni |
| DELETE | `/auth/account` | user+2fa | yeni |

### 2.2 Organizasyon
| Metot | Yol | Rol |
|---|---|---|
| GET | `/orgs` | user |
| POST | `/orgs` | user |
| GET | `/orgs/{orgId}` | member |
| PATCH | `/orgs/{orgId}` | admin |
| DELETE | `/orgs/{orgId}` | owner |
| GET | `/orgs/{orgId}/members` | member |
| POST | `/orgs/{orgId}/invites` | admin |
| GET | `/orgs/{orgId}/invites` | admin |
| DELETE | `/orgs/{orgId}/invites/{id}` | admin |
| POST | `/invites/{token}/accept` | user |
| PATCH | `/orgs/{orgId}/members/{userId}` | admin |
| DELETE | `/orgs/{orgId}/members/{userId}` | admin |
| GET | `/orgs/{orgId}/audit` | admin |
| GET | `/orgs/{orgId}/usage` | member |

### 2.3 Proje ve şema
| Metot | Yol | Faz 1 |
|---|---|---|
| GET | `/orgs/{orgId}/projects` | ★ (cloud projects) |
| POST | `/orgs/{orgId}/projects` | ★ |
| GET | `/projects/{id}` | ★ |
| PATCH | `/projects/{id}` | ★ |
| DELETE | `/projects/{id}` | ★ |
| POST | `/projects/{id}/transfer` | yeni |
| GET | `/projects/{id}/schema` | ★ |
| PUT | `/projects/{id}/schema` | ★ |
| GET | `/projects/{id}/schema/nsl` | yeni (`.nsl` metin) |
| PUT | `/projects/{id}/schema/nsl` | yeni |
| POST | `/projects/{id}/schema/validate` | yeni |
| POST | `/projects/{id}/schema/autofix` | yeni |
| GET | `/projects/{id}/versions` | yeni |
| GET | `/projects/{id}/versions/{v}` | yeni |
| POST | `/projects/{id}/versions/{v}/restore` | yeni |
| GET | `/projects/{id}/branches` | ★ (yereldi) |
| POST | `/projects/{id}/branches` | ★ |
| DELETE | `/projects/{id}/branches/{name}` | ★ |
| POST | `/projects/{id}/branches/{name}/merge` | ★ |
| GET | `/projects/{id}/diff?from=&to=` | ★ |
| GET | `/projects/{id}/comments` | yeni |
| POST | `/projects/{id}/comments` | yeni |
| PATCH | `/comments/{id}` | yeni |

### 2.4 AI / Copilot
| Metot | Yol | Faz 1 controller |
|---|---|---|
| POST | `/ai/schema/generate` | `SchemaController.generate` ★ |
| POST | `/ai/schema/revise` | `SchemaController.revise` ★ |
| POST | `/ai/schema/explain` | ★ |
| POST | `/ai/schema/from-image` | ★ (vision) |
| POST | `/ai/schema/from-url` | ★ |
| POST | `/ai/schema/from-dbcontext` | `ReverseEngineerController` ★ |
| POST | `/ai/dba/analyze` | `AIDbaController` ★ |
| GET | `/ai/dba/badge.svg` | ★ (SVG rozet) |
| POST | `/ai/seed/plan` | `SmartSeedController` ★ |
| POST | `/ai/docs/generate` | `DocumentationController` ★ |
| POST | `/ai/migration/analyze` | `MigrationController` ★ |
| POST | `/ai/query/nl2sql` | yeni |
| POST | `/ai/agent/run` | yeni (çok adımlı) |
| GET | `/ai/agent/runs/{id}` | yeni (SSE stream) |
| POST | `/ai/voice/transcribe` | `VoiceController` ★ |
| GET | `/ai/providers` | ★ |
| GET | `/ai/policy` | `PolicyController` ★ |
| PUT | `/ai/policy` | ★ |
| GET | `/ai/quota` | `QuotaController` ★ |
| PUT | `/ai/byok` | ★ (BYOK anahtarı) |
| DELETE | `/ai/byok` | ★ |

### 2.5 Derleme / export
| Metot | Yol | Faz 1 |
|---|---|---|
| POST | `/compile/ddl?engine=` | `CompileController` ★ |
| POST | `/compile/orm?target=` | ★ (efcore, prisma) |
| POST | `/compile/types?target=` | yeni |
| POST | `/compile/docs?format=` | ★ |
| POST | `/compile/app?target=` | ★ (streamlit) |
| POST | `/compile/package` | ★ (dev package ZIP) |
| POST | `/compile/migration?tool=` | ★ |
| GET | `/compile/targets` | yeni (yetenek keşfi) |
| POST | `/import/sql` | ★ (SQL DDL import) |
| POST | `/import/dbml` | yeni |
| POST | `/import/prisma` | yeni |
| POST | `/import/file` | yeni (dosya yükleme) |
| POST | `/export/dbml` | yeni |
| GET | `/lint` | `LintController` ★ |

### 2.6 Veritabanı (Data Plane)
| Metot | Yol | Faz 1 |
|---|---|---|
| GET | `/projects/{id}/databases` | yeni |
| POST | `/projects/{id}/databases` | yeni (provision) |
| GET | `/databases/{dbId}` | yeni |
| DELETE | `/databases/{dbId}` | yeni |
| GET | `/databases/{dbId}/credentials` | yeni (tek seferlik) |
| POST | `/databases/{dbId}/branch` | yeni |
| GET | `/databases/{dbId}/metrics` | yeni |
| POST | `/databases/{dbId}/backup` | ★ (`DockerBackupService`) |
| GET | `/databases/{dbId}/backups` | yeni |
| POST | `/databases/{dbId}/restore` | yeni |
| POST | `/projects/{id}/sandbox` | `DockerController` ★ |
| GET | `/sandbox/{jobId}` | ★ |
| DELETE | `/sandbox/{jobId}` | ★ |
| POST | `/introspect` | `DbIntrospectController` ★ |
| POST | `/introspect/test-connection` | ★ |
| POST | `/execute` | `DatabaseExecutorController` ★ |
| GET | `/projects/{id}/connections` | ★ |
| POST | `/projects/{id}/connections` | ★ |
| DELETE | `/connections/{id}` | ★ |

### 2.7 Migration
| Metot | Yol | Faz 1 |
|---|---|---|
| POST | `/projects/{id}/migrations/plan` | ★ |
| GET | `/projects/{id}/migrations` | yeni |
| GET | `/migrations/{id}` | yeni |
| POST | `/migrations/{id}/dry-run` | yeni |
| POST | `/migrations/{id}/apply` | yeni |
| POST | `/migrations/{id}/rollback` | yeni |
| GET | `/migrations/{id}/rollback-script` | yeni |
| GET | `/projects/{id}/drift?env=` | yeni |

### 2.8 Console yapılandırma
| Metot | Yol |
|---|---|
| GET | `/projects/{id}/console/config` |
| PUT | `/projects/{id}/console/config` |
| GET | `/projects/{id}/console/roles` |
| POST | `/projects/{id}/console/roles` |
| PATCH | `/console/roles/{id}` |
| DELETE | `/console/roles/{id}` |
| GET | `/projects/{id}/console/users` |
| POST | `/projects/{id}/console/users/invite` |
| POST | `/projects/{id}/console/eject?target=` |
| GET | `/projects/{id}/console/audit` |

### 2.9 Gateway yapılandırma
| Metot | Yol |
|---|---|
| GET | `/projects/{id}/api-keys` |
| POST | `/projects/{id}/api-keys` |
| DELETE | `/api-keys/{id}` |
| POST | `/api-keys/{id}/rotate` |
| GET | `/projects/{id}/api/config` |
| PUT | `/projects/{id}/api/config` |
| GET | `/projects/{id}/api/openapi.json` |
| GET | `/projects/{id}/api/usage` |

### 2.10 Paylaşım
| Metot | Yol | Faz 1 |
|---|---|---|
| POST | `/projects/{id}/share` | `ShareController` ★ |
| GET | `/share/{token}` | ★ |
| DELETE | `/share/{token}` | ★ |
| GET | `/share/{token}/embed` | yeni (iframe) |
| GET | `/s/{slug}` | yeni (public SEO sayfası) |

### 2.11 Faturalama
| Metot | Yol | Faz 1 |
|---|---|---|
| GET | `/billing/plans` | yeni |
| GET | `/orgs/{orgId}/billing` | ★ |
| POST | `/orgs/{orgId}/billing/checkout` | `SubscriptionController` ★ |
| POST | `/orgs/{orgId}/billing/portal` | ★ |
| GET | `/orgs/{orgId}/billing/invoices` | yeni |
| GET | `/orgs/{orgId}/billing/usage` | yeni |
| POST | `/webhooks/stripe` | `StripeWebhookController` ★ |

### 2.12 Diğer
| Metot | Yol | Faz 1 |
|---|---|---|
| POST | `/feedback` | `FeedbackController` ★ |
| GET | `/templates` | ★ (şablon galerisi) |
| GET | `/templates/{id}` | ★ |
| GET | `/hub/blueprints` | yeni |
| POST | `/hub/blueprints` | yeni |
| GET | `/health` `/health/ready` `/health/live` | ★ |
| GET | `/openapi.json` | ★ (swagger) |
| GET | `/metrics` | yeni (Prometheus) |

### 2.13 İç (servisler arası, public değil)
| Metot | Yol |
|---|---|
| GET | `/internal/v1/projects/{id}/metadata?env=` |
| POST | `/internal/v1/events` |
| GET | `/internal/v1/secrets/{ref}` |

---

## 3. Gateway API — `gw.namines.com/v1/{projectSlug}`

Tam liste [08-GATEWAY-API.md §2](08-GATEWAY-API.md).

```
GET    /tables
GET    /tables/{table}
GET    /tables/{table}/rows
GET    /tables/{table}/rows/{pk}
POST   /tables/{table}/rows
PATCH  /tables/{table}/rows/{pk}
PUT    /tables/{table}/rows/{pk}
DELETE /tables/{table}/rows/{pk}
GET    /tables/{table}/rows/{pk}/{relation}
POST   /tables/{table}/bulk
GET    /tables/{table}/count
GET    /tables/{table}/export?format=csv|json|parquet
POST   /tables/{table}/import
POST   /rpc/{function}
POST   /query
POST   /query/nl
POST   /graphql
GET    /openapi.json
GET    /graphql/schema
WS     /realtime
GET    /health
```

---

## 4. Realtime — `wss://rt.namines.com`

### SignalR Hub: `/hubs/canvas`

| Yön | Metot | Parametreler | Faz 1 |
|---|---|---|---|
| C→S | `JoinRoom` | `projectId, branch` | ★ (roomId, userName) |
| C→S | `LeaveRoom` | `projectId` | yeni |
| C→S | `MoveCursor` | `x, y` | ★ |
| C→S | `SetSelection` | `tableUuid, columnUuid` | yeni |
| C→S | `SetViewport` | `x, y, zoom` | yeni |
| C→S | `ApplyUpdate` | `yjsUpdate (binary)` | yeni (CRDT) |
| C→S | `RequestSync` | `stateVector` | yeni |
| S→C | `UserJoined` | `presence` | ★ |
| S→C | `UserLeft` | `connectionId` | ★ |
| S→C | `PresenceUpdate` | `presence[]` | ★ (cursor) |
| S→C | `ApplyUpdate` | `yjsUpdate` | ★ (ReceiveSchema yerine) |
| S→C | `SyncStep` | `update` | yeni |
| S→C | `SchemaVersionChanged` | `version` | yeni |
| S→C | `MigrationProgress` | `jobId, step, pct` | yeni |
| S→C | `SandboxReady` | `jobId, connection` | ★ |
| S→C | `AgentStep` | `runId, step, content` | yeni |

**Faz 1'den kaldırılanlar:** `UpdateSchema` (tam şema broadcast), `SendSchemaToPeer` — CRDT ile gereksiz.

### Yjs WebSocket: `wss://yjs.namines.com/{room}`
Standart `y-websocket` protokolü. Oda adı: `prj_{id}__{branch}`.

---

## 5. Bot — `bot.namines.com`

| Metot | Yol |
|---|---|
| POST | `/webhooks/github` (imza doğrulamalı) |
| GET | `/setup` (App kurulum callback'i) |
| GET | `/health` |

**Dinlenen GitHub olayları:** `pull_request` (opened/synchronize/closed), `issue_comment` (slash komutlar), `push` (main), `installation`, `check_run` (rerequested)

---

## 6. Hata kodları (standart)

| Kod | HTTP | Anlam |
|---|---|---|
| `AUTH_REQUIRED` | 401 | Token yok/geçersiz ★ |
| `AUTH_EXPIRED` | 401 | Token süresi doldu |
| `FORBIDDEN` | 403 | Yetki yok |
| `NOT_FOUND` | 404 | Kaynak yok |
| `VALIDATION_FAILED` | 422 | Girdi geçersiz (detayda alan listesi) |
| `SCHEMA_INVALID` | 422 | NSL doğrulama hatası (detayda `NSLxxx` kodları) |
| `CONFLICT` | 409 | Eşzamanlı değişiklik / benzersizlik ihlali |
| `QUOTA_EXCEEDED` | 402 | Plan limiti aşıldı ★ |
| `RATE_LIMITED` | 429 | Hız limiti ★ (`Retry-After` header'ı) |
| `AI_UNAVAILABLE` | 503 | Sağlayıcı erişilemez |
| `AI_QUOTA_EXCEEDED` | 402 | AI kredisi bitti ★ |
| `DB_UNAVAILABLE` | 503 | Tenant DB erişilemez |
| `MIGRATION_BLOCKED` | 409 | Onay bekliyor |
| `DESTRUCTIVE_NOT_APPROVED` | 409 | Yıkıcı işlem onaysız |
| `CONSTRAINT_VIOLATION` | 422 | DB kısıtı ihlali (insan-okunur mesajla) |
| `UNSUPPORTED_FEATURE` | 422 | Hedef motor bu özelliği desteklemiyor |
| `INTERNAL` | 500 | Beklenmeyen (requestId ile) |

Tüm yanıtlarda `X-Request-Id` header'ı. Tüm hata gövdeleri [08 §2.2](08-GATEWAY-API.md) formatında.

---

## 7. Rate limit header'ları

```
X-RateLimit-Limit: 600
X-RateLimit-Remaining: 412
X-RateLimit-Reset: 1754650800
Retry-After: 23
```
