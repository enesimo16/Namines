# 21 — Gözlemlenebilirlik & Ürün Analitiği

> Faz 1: Serilog → console + **yerel dosya** (`logs/namines-.log`). PaaS'te her deploy'da uçuyor, çok instance'ta parçalı, PII filtresi yok, metrik yok, trace yok. Faz 2'de üç sütun: **log, metrik, trace** + ürün analitiği.

---

## 1. Yığın

| Katman | Araç | Not |
|---|---|---|
| Enstrümantasyon | **OpenTelemetry** (.NET + JS SDK) | Satıcı-bağımsız |
| Toplama | OTel Collector | Tek giriş noktası |
| Log | Grafana Loki | Ucuz, etiket tabanlı |
| Metrik | Prometheus / Grafana Mimir | |
| Trace | Grafana Tempo (veya Jaeger yerelde) | |
| Dashboard | Grafana | |
| Hata | Sentry | Stack trace + release takibi |
| Uptime | Better Stack | Harici probe |
| Ürün analitiği | PostHog (EU) | Funnel, retention, feature flag |
| Kullanım/faturalama | ClickHouse | Yüksek hacimli olay |
| Durum sayfası | Better Stack Status | `status.namines.com` |

---

## 2. Yapılandırılmış log

**Kural: stdout'a JSON. Dosyaya yazma yok.**

```jsonc
{
  "ts": "2026-08-08T12:04:11.482Z",
  "level": "Information",
  "msg": "Migration applied",
  "service": "namines-worker",
  "version": "2.1.4",
  "env": "production",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "spanId": "00f067aa0ba902b7",
  "requestId": "req_01J8XYZ",
  "orgId": "org_01J...",
  "projectId": "prj_01J...",
  "userId": "usr_01J...",
  "migrationId": "mig_01J...",
  "durationMs": 4231,
  "riskLevel": "risky",
  "operationCount": 3
}
```

**Zorunlu alanlar:** `ts, level, msg, service, env, traceId, requestId`
**Bağlam (varsa):** `orgId, projectId, userId`

### PII redaksiyonu (zorunlu enricher)

Asla loglanmayacaklar: şifre, token, API anahtarı, connection string, **tenant satır verisi**, e-posta (hash'lenmiş hariç), IP (son okteti maskeli).

```csharp
public sealed class PiiRedactionEnricher : ILogEventEnricher {
    private static readonly Regex[] Patterns = {
        new(@"(?i)(password|pwd|secret|token|api[_-]?key)\s*[=:]\s*\S+"),
        new(@"nam_(live|test)_[A-Za-z0-9]+"),
        new(@"(?i)(Host|Server)=[^;]+;.*Password=[^;]+"),
        new(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+"),   // JWT
        new(@"[\w\.-]+@[\w\.-]+\.\w+")                                // e-posta
    };
    // → "[REDACTED]"
}
```

### Log seviyeleri

| Seviye | Kullanım | Örnek |
|---|---|---|
| `Debug` | Sadece development | SQL sorguları |
| `Information` | İş olayları | proje oluşturuldu, migration uygulandı |
| `Warning` | Beklenen sorun | kota aşıldı, AI fallback, drift tespit edildi |
| `Error` | Beklenmeyen, istek başarısız | provisioning hatası |
| `Critical` | Sistem tehlikede | DB erişilemez, kiracı izolasyon ihlali şüphesi |

**Örnekleme:** `Information` %100, sağlık kontrolleri %1, `Debug` prod'da kapalı.

---

## 3. Metrikler

### RED (her servis için)
```
namines_http_requests_total{service,method,route,status}
namines_http_request_duration_seconds{service,route}   # histogram
namines_http_requests_in_flight{service}
```

### İş metrikleri
```
namines_schema_compilations_total{engine,target,result}
namines_schema_compilation_duration_seconds{engine,target}
namines_databases_provisioned_total{provider,engine,mode,result}
namines_database_provision_duration_seconds{provider,mode}
namines_databases_active{provider,mode}
namines_migrations_applied_total{risk,result}
namines_migration_duration_seconds{risk}
namines_migration_rollbacks_total{reason}
namines_ai_calls_total{agent,provider,model,result,cache_hit}
namines_ai_tokens_total{agent,provider,model,direction}
namines_ai_cost_usd_total{agent,provider,model}
namines_ai_latency_seconds{agent,model}
namines_gateway_queries_total{project,table,operation,status}
namines_gateway_query_duration_seconds{operation}
namines_gateway_rows_returned{table}                   # histogram
namines_console_page_views_total{project,pattern}
namines_realtime_connections{room_type}
namines_realtime_updates_total{type}
namines_crdt_document_bytes{project}
namines_quota_exceeded_total{resource,plan}
namines_sandbox_active{engine}
namines_sandbox_lifetime_seconds
```

### Altyapı
Standart .NET runtime (GC, thread pool, exception), Npgsql pool, Redis, NATS kuyruk derinliği, K8s pod metrikleri.

---

## 4. Dağıtık izleme (tracing)

```
[POST /v1/projects/{id}/deploy]                          traceId=4bf9...
 ├── auth.validate                                8 ms
 ├── db.query (project fetch)                    12 ms
 ├── nsl.validate                                31 ms
 ├── nsl.compile (postgres, ddl)                 44 ms
 ├── nats.publish (provision.requested)           3 ms
 └── [worker: ProvisionDatabaseJob]           18420 ms   ← aynı trace
      ├── neon.api.create_project             14200 ms   ← darboğaz burada
      ├── vault.write                            85 ms
      ├── db.execute (ddl)                     2100 ms
      ├── data_factory.seed                    1900 ms
      └── nats.publish (schema.deployed)          5 ms
```

**Enstrümante edilecekler:** ASP.NET Core, HttpClient, Npgsql, Redis, NATS, SignalR, EF Core, özel span'lar (nsl.*, ai.*, provision.*).

**Örnekleme:** Baş-tabanlı %10 + **kuyruk-tabanlı %100 hata/yavaş** (>2 sn).

**Trace ↔ Log korelasyonu:** `Serilog.Enrichers.Span` ile her log satırında `traceId`. Grafana'da bir log satırından trace'e tek tıkla geçiş.

---

## 5. Uyarılar (alert)

| Uyarı | Koşul | Şiddet | Kanal |
|---|---|---|---|
| API hata oranı yüksek | 5xx > %2, 5 dk | 🔴 SEV1 | PagerDuty + Slack |
| Gateway p95 yavaş | > 500 ms, 10 dk | 🟠 SEV2 | Slack |
| DB provisioning başarısız | > %10, 15 dk | 🟠 SEV2 | Slack |
| Migration başarısızlığı | herhangi (prod) | 🟠 SEV2 | Slack + e-posta |
| **Kiracı izolasyon ihlali şüphesi** | herhangi | 🔴 SEV1 | Hemen herkes |
| AI maliyet ani artışı | günlük > 2× ortalama | 🟠 SEV2 | Slack |
| AI sağlayıcı hataları | > %20, 5 dk | 🟡 SEV3 | Slack (fallback devrede) |
| NATS kuyruk birikmesi | > 500, 10 dk | 🟠 SEV2 | Slack |
| Redis bellek | > %85 | 🟠 SEV2 | Slack |
| Control DB bağlantı havuzu | > %90 | 🟠 SEV2 | Slack |
| Sertifika bitişi | < 14 gün | 🟡 SEV3 | E-posta |
| Yedek başarısız | herhangi | 🟠 SEV2 | Slack |
| Sandbox sweeper çalışmadı | 2 saat | 🟡 SEV3 | Slack |
| Ödeme başarısız | herhangi | 🟡 SEV3 | Slack (satış) |

**Uyarı hijyeni:** Her uyarının bir runbook linki olmalı. Aksiyon alınamayan uyarı silinir.

---

## 6. Dashboard'lar

| Dashboard | İçerik |
|---|---|
| **Genel Bakış** | Trafik, hata oranı, gecikme, aktif kullanıcı, aktif DB |
| **API** | Endpoint bazında RED, en yavaş 10 route |
| **Gateway** | Proje bazında rps, tablo bazında sorgu süresi, en pahalı sorgular |
| **Data Plane** | Provisioning başarı oranı ve süresi, aktif DB'ler, depolama |
| **Migration** | Uygulanan/başarısız, risk dağılımı, süre |
| **AI** | Ajan bazında çağrı/maliyet/gecikme, cache isabet oranı, eval skoru trendi |
| **Realtime** | Bağlantı, oda, güncelleme hızı, CRDT boyutu |
| **İş** | Kayıt, activation, MRR, churn, plan dağılımı |
| **Maliyet** | Servis bazında altyapı + AI maliyeti, kullanıcı başına maliyet |
| **SLO** | Hata bütçesi tüketimi |

---

## 7. SLO'lar

| Servis | SLI | SLO | Hata bütçesi (30 gün) |
|---|---|---|---|
| Control API | Başarı oranı | %99.9 | 43 dk |
| Gateway | Başarı oranı | %99.95 | 21 dk |
| Gateway | p95 < 150 ms | %99 | — |
| Console | Sayfa yüklendi | %99.9 | 43 dk |
| Realtime | Bağlantı başarısı | %99.5 | 3.6 sa |
| Provisioning | 90 sn içinde tamamlandı | %95 | — |
| AI | Yanıt üretildi (fallback dahil) | %99 | — |

Hata bütçesinin %50'si tükendiğinde: yeni özellik durur, güvenilirlik çalışılır.

---

## 8. Ürün analitiği (PostHog)

### Olay taksonomisi

```
# Aktivasyon hunisi
signed_up                  { method, referrer, utm_* }
email_verified
project_created            { source: template|blank|import|ai }
schema_generated           { source: prompt|url|image|voice|import, tableCount, durationMs }
schema_edited              { action: add_table|add_column|add_relation|... }
ddl_downloaded             { engine }
database_provisioned       { provider, engine, mode, durationMs }     ← ★ AHA MOMENTİ
console_opened             { projectId }
console_row_created        { table }                                  ← ★ İKİNCİ AHA
api_key_created
first_api_request

# Derinleşme
branch_created
migration_applied          { risk, environment }
github_connected
console_user_invited       { role }
team_member_invited
eject_downloaded           { target }

# Ticari
plan_viewed
checkout_started           { plan }
subscription_started       { plan, seats }
subscription_canceled      { plan, reason, tenureDays }
quota_exceeded             { resource, plan }
upgrade_prompt_shown       { trigger }
upgrade_prompt_clicked

# Kalite sinyalleri
error_shown                { code, surface }
feedback_submitted         { kind }                                   ★ Faz 1
ai_result_rejected         { agent }                                  ← kalite sinyali
ai_result_accepted         { agent }
help_opened                { topic }                                  ★ Faz 1 help center
```

### İzlenecek huniler

**Aktivasyon:**
```
signed_up → schema_generated → database_provisioned → console_row_created
   100%    →       ~65%       →        ~35%          →       ~25%
```
Her adımdaki düşüş bir ürün görevine dönüşür.

**Gelir:**
```
quota_exceeded → upgrade_prompt_shown → checkout_started → subscription_started
```

### Retention kohortları
- Haftalık, `database_provisioned` yapanlar vs yapmayanlar
- **Hipotez:** DB provision edenlerin 4. hafta retention'ı yapmayanların 3-4 katı olacak. Bu doğrulanırsa tüm onboarding bu adımı hedefler.

### Feature flag
PostHog flag'leri ile: kademeli yayın, A/B testi (özellikle onboarding ve prompt sürümleri), kill switch.

---

## 9. Kullanım metering (faturalama için)

Ayrı yol — ürün analitiğinden farklı, **kayıp kabul edilemez**.

```
Gateway/API/Worker → NATS (usage.recorded) → Worker → ClickHouse
                                                    ↓
                                        Günlük rollup → control DB
                                                    ↓
                                        Stripe usage records (kullanım bazlı kalemler)
```

Ölçülenler: API çağrısı, AI çağrısı + token + maliyet, DB saati, depolama GB, transfer GB, Console kullanıcı sayısı, aktif koltuk.

**Doğruluk kontrolü:** Günlük mutabakat işi — ClickHouse toplamı vs Stripe raporlanan. Sapma > %1 ise uyarı.

---

## 10. Kullanıcıya görünen gözlemlenebilirlik

Kullanıcı da kendi projesinin sağlığını görmeli (bu bir özellik, sadece iç araç değil):

| Yüzey | İçerik |
|---|---|
| Proje sağlık kartı | DB durumu, son migration, drift var mı, DBA skoru ★ |
| API kullanım grafiği | İstek/gün, en çok kullanılan endpoint, hata oranı |
| Sorgu içgörüleri | En yavaş sorgular, eksik index önerileri |
| AI kullanım paneli | Kalan kredi, ajan bazında dağılım ★ |
| Audit log görüntüleyici | Console + şema değişiklikleri |
| Durum sayfası | `status.namines.com` |
| DBA rozeti | `![](api.namines.com/v1/ai/dba/badge.svg?p=...)` ★ — README'ye eklenir, **viral kanal** |
