# 19 — Ortam Değişkenleri (Tam Liste)

Faz 1 konvansiyonu korunur: `__` ayracı .NET hiyerarşisine map olur (`Jwt__Key` → `Jwt:Key`).

**Kritik kural (Faz 1'den ders):** `NEXT_PUBLIC_*` değişkenleri **build zamanında** gömülür. Ortam başına ayrı build gerekir.

---

## `.env.example` (kök — yerel geliştirme)

```bash
# ═══════════════════════════════════════════════════════════════
# GENEL
# ═══════════════════════════════════════════════════════════════
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:8080
App__Environment=local
App__BaseUrl=http://localhost:8080
App__FrontendUrl=http://localhost:3000          # ★ CORS için kritik
App__ConsoleUrl=http://localhost:3001
App__GatewayUrl=http://localhost:8081
App__RealtimeUrl=ws://localhost:8082
App__DocsUrl=http://localhost:3002
App__SupportEmail=destek@namines.com

# ═══════════════════════════════════════════════════════════════
# CONTROL PLANE VERİTABANI  (Faz 1: SQLite → Faz 2: PostgreSQL)
# ═══════════════════════════════════════════════════════════════
ConnectionStrings__Control=Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=dev
ConnectionStrings__ControlReadReplica=
Database__CommandTimeoutSeconds=30
Database__MaxPoolSize=100
Database__EnableSensitiveDataLogging=false

# ═══════════════════════════════════════════════════════════════
# KİMLİK & JWT   (★ Faz 1'den korundu)
# ═══════════════════════════════════════════════════════════════
Jwt__Key=CHANGE_ME_min_32_chars_random_secret_here!!
Jwt__Issuer=NaminesServer
Jwt__Audience=NaminesClient
Jwt__AccessTokenMinutes=15
Jwt__RefreshTokenDays=30
Auth__CrossSiteCookie=false                     # ★ farklı site ise true (SameSite=None)
Auth__CookieName=namines_token
Auth__CookieDomain=
Auth__RequireEmailVerification=true
Auth__PasswordMinLength=10
Auth__MaxFailedAttempts=5
Auth__LockoutMinutes=15
Auth__Enable2fa=true

# OAuth
Auth__GitHub__ClientId=
Auth__GitHub__ClientSecret=
Auth__Google__ClientId=
Auth__Google__ClientSecret=

# ═══════════════════════════════════════════════════════════════
# REDIS
# ═══════════════════════════════════════════════════════════════
Redis__ConnectionString=localhost:6379
Redis__InstanceName=namines
Redis__Database=0
Redis__BackplaneDatabase=1

# ═══════════════════════════════════════════════════════════════
# NATS (iş kuyruğu / event bus)
# ═══════════════════════════════════════════════════════════════
Nats__Url=nats://localhost:4222
Nats__StreamName=NAMINES
Nats__MaxDeliver=5
Nats__AckWaitSeconds=300

# ═══════════════════════════════════════════════════════════════
# OBJECT STORAGE
# ═══════════════════════════════════════════════════════════════
Storage__Provider=minio                          # minio | s3 | r2
Storage__Endpoint=http://localhost:9000
Storage__Region=eu-central-1
Storage__AccessKey=minio
Storage__SecretKey=minio123
Storage__BucketSchemas=namines-schemas
Storage__BucketExports=namines-exports
Storage__BucketBackups=namines-backups
Storage__BucketUploads=namines-uploads
Storage__PublicUrl=http://localhost:9000
Storage__ForcePathStyle=true

# ═══════════════════════════════════════════════════════════════
# SIR YÖNETİMİ
# ═══════════════════════════════════════════════════════════════
Vault__Enabled=false                             # yerelde kapalı, prod'da zorunlu
Vault__Address=http://localhost:8200
Vault__Token=
Vault__MountPath=namines
Secrets__EncryptionKey=CHANGE_ME_base64_32_bytes  # Vault kapalıyken AES-256-GCM (★ BYOK)

# ═══════════════════════════════════════════════════════════════
# AI SAĞLAYICILARI  (★ Faz 1'den korundu, genişletildi)
# ═══════════════════════════════════════════════════════════════
Ai__DefaultProvider=groq
Ai__EnableSemanticCache=true
Ai__SemanticCacheThreshold=0.94
Ai__SemanticCacheTtlDays=30
Ai__MaxContextTokens=30000
Ai__RequestTimeoutSeconds=120
Ai__PromptVersionOverride=

Groq__ApiKey=                                    # ★
Groq__Model=llama-3.3-70b-versatile              # ★ (8b varsayılanı kaldırıldı)
Groq__WhisperModel=whisper-large-v3
Groq__BaseUrl=https://api.groq.com/openai/v1

Anthropic__ApiKey=
Anthropic__Model=claude-sonnet-5
Anthropic__FastModel=claude-haiku-4-5
Anthropic__EnablePromptCaching=true

Gemini__ApiKey=                                  # ★
Gemini__Model=gemini-2.5-flash

OpenAI__ApiKey=
OpenAI__Model=gpt-4.1
OpenAI__EmbeddingModel=text-embedding-3-small

Ollama__BaseUrl=http://localhost:11434           # ★
Ollama__Model=qwen2.5-coder:14b
Ollama__EmbeddingModel=nomic-embed-text

# AI kotası (★ Faz 1 token havuzu → çağrı kredisi)
AiQuota__FreeCallsPerMonth=20
AiQuota__ProCallsPerMonth=500
AiQuota__TeamCallsPerMonth=2000
AiQuota__BurstPerMinute=5

# ═══════════════════════════════════════════════════════════════
# DATA PLANE / PROVISIONING
# ═══════════════════════════════════════════════════════════════
DataPlane__DefaultProvider=neon
DataPlane__DefaultRegion=eu-central-1
DataPlane__EphemeralTtlMinutes=60
DataPlane__EphemeralWarmPoolSize=3
DataPlane__MaxConcurrentSandboxesPerUser=1
DataPlane__ProvisionTimeoutSeconds=180

Neon__ApiKey=
Neon__ProjectIdPrefix=namines
Neon__ApiUrl=https://console.neon.tech/api/v2

PlanetScale__ServiceTokenId=
PlanetScale__ServiceToken=
PlanetScale__Organization=

AzureSql__SubscriptionId=
AzureSql__ResourceGroup=
AzureSql__TenantId=
AzureSql__ClientId=
AzureSql__ClientSecret=

# Kubernetes sandbox (docker.sock YERİNE — ★ güvenlik düzeltmesi)
K8s__Enabled=false
K8s__Namespace=namines-sandbox
K8s__KubeConfigPath=
K8s__RuntimeClass=gvisor
K8s__CpuLimit=500m
K8s__MemoryLimit=512Mi
K8s__StorageLimit=1Gi

# BYODB güvenliği (★ Faz 1 SsrfGuard)
Executor__AllowPrivateHosts=true                 # ★ prod'da MUTLAKA false
Executor__RequireSsl=true
Executor__QueryTimeoutSeconds=5
Executor__MaxRowsReturned=10000
Executor__ReadOnlyByDefault=true

# ═══════════════════════════════════════════════════════════════
# GATEWAY
# ═══════════════════════════════════════════════════════════════
Gateway__Port=8081
Gateway__MetadataCacheTtlSeconds=60
Gateway__DefaultPageSize=50
Gateway__MaxPageSize=1000
Gateway__MaxExpandDepth=3
Gateway__StatementTimeoutMs=5000
Gateway__EnableGraphQl=true
Gateway__GraphQlMaxDepth=8
Gateway__GraphQlMaxComplexity=1000
Gateway__EnableRawQuery=true
Gateway__PgBouncerUrl=

# ═══════════════════════════════════════════════════════════════
# REALTIME
# ═══════════════════════════════════════════════════════════════
Realtime__Port=8082
Realtime__YjsUrl=ws://localhost:1234
Realtime__MaxUsersPerRoom=50
Realtime__PresenceTtlSeconds=30
Realtime__SnapshotIntervalSeconds=5
Realtime__CursorThrottleMs=50

# ═══════════════════════════════════════════════════════════════
# RATE LIMIT  (★ Faz 1 mantığı korundu, Redis'e taşındı)
# ═══════════════════════════════════════════════════════════════
RateLimit__Enabled=true
RateLimit__SensitivePermitPerMinute=5            # ★
RateLimit__ApiPermitPerMinute=300
RateLimit__AnonymousPermitPerMinute=60
RateLimit__GatewayDefaultRpm=600

# ═══════════════════════════════════════════════════════════════
# FATURALAMA  (★ Faz 1'den korundu)
# ═══════════════════════════════════════════════════════════════
Stripe__SecretKey=                               # ★
Stripe__PublishableKey=
Stripe__WebhookSecret=                           # ★ imza doğrulaması zorunlu
Stripe__PriceIdPro=price_...
Stripe__PriceIdTeam=price_...
Stripe__PriceIdTeamSeat=price_...
Stripe__TrialDays=14
Stripe__CustomerPortalUrl=

# ═══════════════════════════════════════════════════════════════
# GITHUB APP (Namines Bot)
# ═══════════════════════════════════════════════════════════════
GitHub__AppId=
GitHub__AppSlug=namines
GitHub__PrivateKeyPem=
GitHub__WebhookSecret=
GitHub__ClientId=
GitHub__ClientSecret=

# ═══════════════════════════════════════════════════════════════
# E-POSTA
# ═══════════════════════════════════════════════════════════════
Email__Provider=resend                            # resend | smtp
Email__ApiKey=
Email__FromAddress=noreply@namines.com
Email__FromName=Namines
Email__ReplyTo=destek@namines.com
Smtp__Host=localhost
Smtp__Port=1025
Smtp__Username=
Smtp__Password=
Smtp__UseSsl=false

# ═══════════════════════════════════════════════════════════════
# GÖZLEMLENEBİLİRLİK
# ═══════════════════════════════════════════════════════════════
Serilog__MinimumLevel__Default=Information
Serilog__WriteTo__Console=true
Serilog__WriteTo__File=false                     # ★ prod'da KAPALI (Faz 1'de açıktı)
Otel__Enabled=true
Otel__ServiceName=namines-api
Otel__Endpoint=http://localhost:4317
Otel__SamplingRatio=0.1
Sentry__Dsn=
Sentry__TracesSampleRate=0.1
PostHog__ApiKey=
PostHog__Host=https://eu.i.posthog.com
ClickHouse__ConnectionString=Host=localhost;Port=8123;Database=namines_analytics

# ═══════════════════════════════════════════════════════════════
# ÖZELLİK BAYRAKLARI
# ═══════════════════════════════════════════════════════════════
Features__Console=true
Features__Gateway=true
Features__GraphQl=true
Features__Provisioning=true
Features__Branching=false
Features__Bridge=false
Features__Realtime=true
Features__AiAgentMode=true
Features__VoiceInput=true                        # ★
Features__Marketplace=false
Features__CustomDomains=false
```

---

## `deploy/backend.env.example` (production)

> Yerel `.env` **kullanılmaz**. PaaS/K8s gerçek ortam değişkeni enjekte eder.

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080

# ── ZORUNLU (bunlar olmadan uygulama başlamaz) ──────────────────
Jwt__Key=<64+ karakter rastgele>
ConnectionStrings__Control=<postgres bağlantısı>
App__FrontendUrl=https://app.namines.com         # ★ tam origin, sonda / YOK
App__ConsoleUrl=https://console.namines.com
Redis__ConnectionString=<redis>
Vault__Address=<vault>
Vault__Token=<token>

# ── ÜÇ KRİTİK AYAR (★ Faz 1 README'sindeki uyarı korunur) ──────
# 1. App__FrontendUrl tam eşleşmeli — yanlışsa CORS her isteği bloklar
# 2. Auth__CrossSiteCookie: frontend ve API farklı site ise true
# 3. NEXT_PUBLIC_API_URL sonunda /api OLMAMALI (istemci ekliyor)
Auth__CrossSiteCookie=true
Cors__AllowedOrigins__0=https://app.namines.com
Cors__AllowedOrigins__1=https://console.namines.com
Cors__AllowedOrigins__2=https://namines.com

# ── GÜVENLİK (production'da farklı) ────────────────────────────
Executor__AllowPrivateHosts=false                # ★ MUTLAKA false
Serilog__WriteTo__File=false
Database__EnableSensitiveDataLogging=false
Auth__RequireEmailVerification=true
Vault__Enabled=true
K8s__Enabled=true

# ── Reverse proxy ──────────────────────────────────────────────
# ★ Faz 1'de KnownProxies temizleniyordu (IP spoof riski).
# Faz 2'de bilinen proxy CIDR'ları açıkça verilir:
ForwardedHeaders__KnownNetworks__0=10.0.0.0/8
ForwardedHeaders__KnownNetworks__1=100.64.0.0/10

# ── Diğer tüm değişkenler yukarıdaki listeyle aynı ─────────────
```

---

## `deploy/frontend.env.example`

```bash
# ★ DİKKAT: Bunlar BUILD zamanında gömülür. Ortam başına ayrı build.
NEXT_PUBLIC_API_URL=https://api.namines.com      # ★ sonunda /api YOK
NEXT_PUBLIC_GATEWAY_URL=https://gw.namines.com
NEXT_PUBLIC_REALTIME_URL=wss://rt.namines.com
NEXT_PUBLIC_YJS_URL=wss://yjs.namines.com
NEXT_PUBLIC_CONSOLE_URL=https://console.namines.com
NEXT_PUBLIC_APP_URL=https://app.namines.com
NEXT_PUBLIC_POSTHOG_KEY=phc_...
NEXT_PUBLIC_POSTHOG_HOST=https://eu.i.posthog.com
NEXT_PUBLIC_SENTRY_DSN=https://...
NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY=pk_live_...
NEXT_PUBLIC_ENVIRONMENT=production
NEXT_PUBLIC_ENABLE_CONSOLE=true
NEXT_PUBLIC_ENABLE_BRANCHING=false

# Sunucu tarafı (public değil)
SENTRY_AUTH_TOKEN=
INTERNAL_API_TOKEN=
```

---

## Doğrulama

Uygulama başlangıcında `EnvironmentValidator` çalışır ve **eksik/hatalı yapılandırmada fail-fast** yapar:

| Kural | Ortam | Davranış |
|---|---|---|
| `Jwt__Key` ≥ 32 karakter | Production | ★ Hata (Faz 1'de doğru yapılmıştı) |
| `ConnectionStrings__Control` dolu | Hepsi | Hata |
| `App__FrontendUrl` geçerli URL, sonda `/` yok | Production | Hata |
| `Executor__AllowPrivateHosts=false` | Production | Hata |
| `Vault__Enabled=true` | Production | Hata |
| `Stripe__WebhookSecret` dolu | Production | Uyarı |
| `Serilog__WriteTo__File=false` | Production | Uyarı |
| `Cors__AllowedOrigins` boş değil | Production | ★ Uyarı (Faz 1'de vardı) |
| AI sağlayıcılarından en az biri yapılandırılmış | Hepsi | Uyarı |

```bash
# CI'da doğrulama
dotnet run --project src/Namines.Api -- --validate-config
npx namines doctor
```
