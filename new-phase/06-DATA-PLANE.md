# 06 — Data Plane (Gerçek Veritabanları)

> Faz 1'de "Docker sandbox" vardı ve `/var/run/docker.sock` container'a mount ediliyordu — bu, host üzerinde root yetkisi demektir ve çok kiracılı bir SaaS'ta kabul edilemez. **Özellik korunuyor, uygulaması tamamen değişiyor.**

---

## 1. Dört veritabanı modu

| Mod | Ne | Kime | Ömür | Faz 1 karşılığı |
|---|---|---|---|---|
| **Ephemeral** | Tek kullanımlık test DB'si | Free tier, "dene" akışı | 60 dk | Docker sandbox |
| **Managed** | Namines'in sağladığı kalıcı DB | Pro/Team | Sınırsız | — (yeni) |
| **Branch** | Managed DB'nin copy-on-write kopyası | Team, PR önizleme | PR kapanınca | — (yeni) |
| **BYODB** | Kullanıcının kendi DB'si | Herkes, Enterprise | Kullanıcının | DbConnectionPanel |

---

## 2. Ephemeral Sandbox (güvenli yeniden yazım)

### Faz 1 sorunu → Faz 2 çözümü

| Sorun | Çözüm |
|---|---|
| `docker.sock` mount | ❌ Kaldırıldı. Provisioning **ayrı bir broker servisi** üzerinden, Kubernetes Job API ile. |
| Container escape riski | **gVisor** (`runsc`) runtime class — syscall yüzeyi izole |
| Ağ erişimi | NetworkPolicy: `deny-all` egress, sadece Gateway'den ingress |
| Kaynak tükenmesi | `limits: cpu 500m, memory 512Mi, ephemeral-storage 1Gi` |
| Sonsuz yaşam | 60 dk TTL + `activeDeadlineSeconds` + sweeper CronJob |
| Kötüye kullanım | Kullanıcı başına aynı anda 1 sandbox; saatte 3 provision |

### Akış

```
POST /v1/projects/{id}/sandbox
  → Kota kontrol (org planı, aktif sandbox sayısı)
  → NATS: sandbox.provision.requested
  → worker:
      1. K8s Job oluştur (namespace: nam-sandbox, runtimeClass: gvisor)
         image: postgres:17-alpine | mysql:8.4 | mcr.microsoft.com/mssql/server:2022-latest
      2. Rastgele kimlik bilgisi üret, Vault'a yaz
      3. Hazır olmasını bekle (readiness probe, max 90 sn)
      4. DDL uygula (Namines.Compiler çıktısı)
      5. Seed uygula (Data Factory)
      6. sandbox.ready olayı
  → Kullanıcıya: geçici connection string + Console URL + kalan süre
```

**Sıcak havuz optimizasyonu:** Her motor için 3 adet önceden başlatılmış boş instance beklemede tutulur. Talep gelince DDL uygulanıp atanır → **20 saniye yerine 90 saniye**.

---

## 3. Managed Database

### 3.1 Sağlayıcı stratejisi

**Karar: Kendi PostgreSQL cluster'ını işletme. Satın al.**

| Sağlayıcı | Motor | Neden | Maliyet (yaklaşık) |
|---|---|---|---|
| **Neon** | PostgreSQL | **Birincil.** Serverless, gerçek branching (copy-on-write), scale-to-zero, API ile provisioning | $0 (free tier) → $0.16/GB-ay depolama + $0.16/CU-saat |
| **PlanetScale** | MySQL | MySQL isteyen için, branching var | $39+/ay |
| **Azure SQL / RDS** | SQL Server | .NET kurumsal segmenti için | $15+/ay (Basic) |
| **Supabase** | PostgreSQL | Alternatif/yedek sağlayıcı | $25/ay |
| Kendi PG (Kubernetes + CloudNativePG) | PostgreSQL | v3, ölçek ekonomisi anlamlı olunca | — |

**Neon seçilme gerekçesi:** Branch DB özelliği (madde 4) Neon'un native yeteneği. Kendin yazsan aylar sürer. `scale-to-zero` ile uykuda maliyet ~0 — free tier ekonomisi bununla mümkün olur.

**Soyutlama:** `IDatabaseProvider` arayüzü, sağlayıcı değiştirilebilir olsun:

```csharp
public interface IDatabaseProvider {
    Task<ProvisionedDatabase> CreateAsync(ProvisionSpec spec, CancellationToken ct);
    Task<ProvisionedDatabase> CreateBranchAsync(string parentId, string branchName, CancellationToken ct);
    Task DeleteAsync(string databaseId, CancellationToken ct);
    Task<DatabaseMetrics> GetMetricsAsync(string databaseId, CancellationToken ct);
    Task<BackupHandle> CreateBackupAsync(string databaseId, CancellationToken ct);
    Task RestoreAsync(string databaseId, BackupHandle handle, DateTimeOffset? pointInTime, CancellationToken ct);
    ProviderCapabilities Capabilities { get; }
}
```
Uygulamalar: `NeonProvider`, `PlanetScaleProvider`, `AzureSqlProvider`, `RdsProvider`, `SelfHostedPgProvider`, `EphemeralK8sProvider`.

### 3.2 Provisioning spesifikasyonu

```jsonc
{
  "projectId": "prj_...",
  "environment": "production",
  "engine": "postgres",
  "version": "17",
  "region": "eu-central-1",
  "size": "small",              // nano | small | medium | large
  "extensions": ["uuid-ossp", "pg_trgm", "pgcrypto", "vector"],
  "backup": { "enabled": true, "retentionDays": 7, "pitr": true },
  "networking": { "publicAccess": false, "allowedIps": [], "requireSsl": true },
  "poolMode": "transaction"     // PgBouncer modu
}
```

### 3.3 Bağlantı yönetimi

```
Gateway → PgBouncer (transaction pooling) → tenant DB
```
- Her tenant DB'si için ayrı pool, `max_client_conn` kiracı planına göre
- Bağlantı bilgileri **asla** control DB'de düz metin değil → Vault/KMS referansı
- Gateway sırrı sadece bellekte tutar, 15 dk cache, disk'e yazmaz
- Uygulama kullanıcısı: `namines_app_{projectId}` — `SUPERUSER` değil, sadece kendi şeması üzerinde DML/DDL

---

## 4. Branch Database (yeni — Team planının satış argümanı)

```
main branch          →  db_prod   (Neon branch: main)
feature/orders-v2    →  db_br_x   (Neon branch: br-orders-v2, copy-on-write, ~0 maliyet)
PR #142 preview      →  db_br_y   (otomatik, PR kapanınca silinir)
```

**Akış:**
1. Studio'da branch oluştur → NSL dokümanı çatallanır
2. Aynı anda Neon branch API'si çağrılır → veri kopyalanır (CoW, saniyeler)
3. Branch'te migration uygulanır ve test edilir
4. PR'da Namines Bot: *"Bu PR 3 migration içeriyor: 2 güvenli, 1 yıkıcı. Önizleme DB'si: `br-142`. Rollback script'i hazır."*
5. Merge → main branch'e migration uygulanır → önizleme DB'si silinir

**PII maskeleme (P2):** Branch oluştururken `@tag(pii)` işaretli kolonlar otomatik anonimleştirilir (Faker ile deterministik). Böylece geliştiriciler prod verisinin şekliyle çalışır ama gerçek kişisel veriye erişmez. Bu **KVKK/GDPR açısından güçlü bir kurumsal satış argümanıdır.**

---

## 5. BYODB (Kendi veritabanını bağla)

Faz 1'deki `DbConnectionPanel` + `DbIntrospectionService` korunur, sertleştirilir.

| Kontrol | Detay |
|---|---|
| SSRF koruması | KORUNDU (`SsrfGuard`) — loopback, private range, link-local, metadata IP'leri (169.254.169.254) reddedilir |
| DNS rebinding | **YENİ** — hostname çözümlemesi bağlantı anında tekrar doğrulanır, IP pinlenir |
| Zorunlu TLS | `sslmode=require` minimum, `verify-full` önerilir |
| Salt-okunur mod | Varsayılan. Yazma için açık onay. |
| Kimlik bilgisi | Vault'ta, AES-256-GCM + KMS zarf şifreleme |
| İzin kontrolü | Bağlanınca kullanıcının yetkileri raporlanır ("bu kullanıcı DROP TABLE yapabiliyor — daha dar bir rol öneriyoruz") |
| IP izin listesi | Namines'in çıkış IP'leri sabit (NAT gateway), kullanıcı firewall'una eklenir |

---

## 6. Namines Bridge (on-prem agent, P2)

Kurumsal müşterinin DB'si internete açık değil. Çözüm: outbound-only tünel agent'ı.

```
[Müşteri ağı]                        [Namines Cloud]
  namines-bridge  ──── mTLS WSS ────→  bridge-relay
       │                                    │
       └→ localhost:1433 (SQL Server)       └→ gateway / worker
```

- Tek dosya, self-contained .NET binary (`namines-bridge.exe`, ~25 MB, Native AOT)
- **Sadece outbound** 443 bağlantısı — firewall'da hiçbir port açılmaz
- mTLS + kısa ömürlü token
- Yapılandırma: hangi DB'lere, hangi işlemlere izin var (allowlist)
- Denetim: her sorgu müşteri tarafında loglanır
- Windows Service / systemd / Docker olarak kurulur

**Satış etkisi:** Bu özellik olmadan Türkiye'deki bankalar, sigorta, kamu, holding IT'si ile konuşamazsın. Bu özellikle konuşursun.

---

## 7. Data Factory (Smart Seed'in ölçekli hali)

Faz 1'deki `SmartSeedService` korunur, genişletilir.

| Yetenek | Faz 1 | Faz 2 |
|---|---|---|
| Alan-farkında veri (isim, e-posta, adres) | ✔ | ✔ + 40 yerel ayar (tr-TR dahil) |
| Referans bütünlüğü (FK'lar tutarlı) | kısmi | **Topolojik sıralı üretim, garantili** |
| Hacim | küçük | 10M satır'a kadar, `COPY`/`BULK INSERT` ile |
| Dağılım kontrolü | ✖ | Zipf, normal, kategorik ağırlık ("siparişlerin %70'i paid") |
| Zaman serisi | ✖ | Tarih aralığı + mevsimsellik |
| Deterministik seed | ✖ | `seed=42` → aynı veri, tekrarlanabilir test |
| Constraint uyumu | kısmi | CHECK/UNIQUE/enum kısıtlarına uyar |
| PII-güvenli | ✖ | Gerçek kişi verisi asla üretilmez |
| Formatlar | SQL | SQL, CSV, JSON, Parquet, doğrudan DB'ye |

```csharp
var plan = DataFactory.Plan(nslDoc, new SeedOptions {
    Seed = 42,
    Volumes = new() { ["users"] = 10_000, ["orders"] = 120_000 },
    Locale = "tr-TR",
    Distributions = new() { ["orders.status"] = Weighted(("paid", .7), ("pending", .2), ("cancelled", .1)) },
    TimeRange = (new DateOnly(2024,1,1), new DateOnly(2026,8,1))
});
await DataFactory.ExecuteAsync(plan, connection, progress);
```

AI kullanımı: sadece **alan tahmini** için (bu kolon ne tür veri tutuyor?), üretimin kendisi deterministik ve ücretsiz. Bu, Faz 1'in "token bitince fallback" karmaşasını gereksiz kılar.

---

## 8. Introspection (tersine mühendislik)

Faz 1'deki `DbIntrospectionService` korunur ve derinleştirilir.

| Motor | Kaynak | Yeni yakalanan |
|---|---|---|
| PostgreSQL | `pg_catalog` + `information_schema` | index (partial/expression), check, enum, RLS politikaları, view, sequence, extension, partition, comment |
| SQL Server | `sys.*` | filtered index, included column, computed column, extended property, schema, identity seed |
| MySQL/MariaDB | `information_schema` | index prefix, fulltext, generated column, charset/collation, comment |
| SQLite | `pragma` | partial index, generated column |
| Oracle | `ALL_*` görünümleri | tablespace, sequence, trigger başlığı, comment |

**Çıktı:** Doğrudan NSL IR. Yani "canlı DB → NSL → görselleştir → belgelendir → migration üret" tek akışta.

**Büyük şema modu:** 1000+ tablolu şemalarda progressive loading + subject area otomatik kümeleme (FK grafiği üzerinde community detection).

---

## 9. Yedekleme & Kurtarma

| Katman | Yöntem | RPO | RTO |
|---|---|---|---|
| Managed DB (Neon) | Sağlayıcı PITR | 1 dk | 5 dk |
| Kullanıcı tetikli yedek | `pg_dump` / `BACKUP DATABASE` → MinIO/S3 | anlık | dakikalar |
| Control plane DB | WAL arşivleme + günlük snapshot | 5 dk | 15 dk |
| NSL sürümleri | Immutable, silinmez | 0 | anında |

Faz 1'deki `.bak` / `.sql` dump indirme özelliği **korunur** ve tüm modlara genişletilir.

---

## 10. Kota ve limitler (plan bazında)

| Kaynak | Free | Pro | Team | Enterprise |
|---|---|---|---|---|
| Ephemeral sandbox | 3/gün, 60 dk | 20/gün | sınırsız | sınırsız |
| Managed DB | ✖ | 1 (0.5 GB) | 5 (10 GB) | özel |
| Branch DB | ✖ | 2 | 20 | sınırsız |
| BYODB bağlantısı | 1 (salt-okunur) | 3 | 20 | sınırsız |
| Veri hacmi | — | 0.5 GB | 10 GB | özel |
| Yedek saklama | — | 7 gün | 30 gün | özel |
| Bölge seçimi | ✖ | ✖ | ✔ | ✔ + TR |
| Bridge agent | ✖ | ✖ | 1 | sınırsız |
