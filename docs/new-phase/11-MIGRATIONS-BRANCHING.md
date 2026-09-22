# 11 — Migration Güvenliği & Sürümleme

> Bu, projenin **para eden** ama sıkıcı kısmıdır. Rakiplerin çoğu (dbdiagram, ChartDB, DrawSQL) burayı hiç ele almaz. Bytebase/Atlas ele alır ama tasarım katmanı yoktur. İkisini birleştirmek gerçek bir ayrıştırıcıdır.

---

## 1. Migration yaşam döngüsü

```
NSL v46  ──diff──▶  NSL v47
                      │
                      ▼
            ┌─────────────────────┐
            │ 1. PLAN             │  SchemaDiff → MigrationPlan
            │    operasyon listesi│  her op: risk, kilit, süre, geri alınabilirlik
            └──────────┬──────────┘
                       ▼
            ┌─────────────────────┐
            │ 2. ANALYZE          │  canlı DB istatistikleri + AI açıklama
            │    gerçek satır say.│  "128k satır etkilenecek, 4.2 sn kilit"
            └──────────┬──────────┘
                       ▼
            ┌─────────────────────┐
            │ 3. GATE             │  risk seviyesine göre onay
            │                     │  safe→otomatik, destructive→2 kişi onayı
            └──────────┬──────────┘
                       ▼
            ┌─────────────────────┐
            │ 4. DRY RUN          │  branch DB'de gerçekten çalıştır
            │                     │  başarısızsa hiç prod'a gitmez
            └──────────┬──────────┘
                       ▼
            ┌─────────────────────┐
            │ 5. APPLY            │  prod'a, adım adım, checkpoint'li
            │                     │  advisory lock ile eşzamanlılık koruması
            └──────────┬──────────┘
                       ▼
            ┌─────────────────────┐
            │ 6. VERIFY           │  şema introspect + beklenenle karşılaştır
            │                     │  smoke sorguları
            └──────────┬──────────┘
                       ▼
            ┌─────────────────────┐
            │ 7. RECORD           │  _namines_migrations tablosuna yaz
            │                     │  rollback script'i sakla
            └─────────────────────┘
```

Herhangi bir adımda hata → otomatik rollback (mümkünse) + olay kaydı + bildirim.

---

## 2. Risk sınıflandırma tablosu

| Operasyon | Risk | Kilit (PG) | Kilit (MSSQL) | Geri alınabilir | Not |
|---|---|---|---|---|---|
| `ADD COLUMN` (nullable) | 🟢 safe | ACCESS EXCLUSIVE (anlık) | Sch-M (anlık) | ✔ | |
| `ADD COLUMN NOT NULL DEFAULT` | 🟢 safe (PG11+) | anlık | tablo yeniden yazımı | ✔ | Eski PG'de tablo taraması |
| `ADD COLUMN NOT NULL` (default'suz) | 🔴 destructive | — | — | ✔ | Mevcut satırlar varsa **başarısız olur** |
| `DROP COLUMN` | 🔴 destructive | anlık | anlık | ✖ | **Veri kalıcı gider** |
| `RENAME COLUMN` | 🟠 breaking | anlık | anlık | ✔ | API/istemci kırılır |
| `ALTER TYPE` (genişleyen) | 🟡 risky | tablo yeniden yazımı | yeniden yazım | ✔ | Süre satır sayısıyla orantılı |
| `ALTER TYPE` (daraltan) | 🔴 destructive | yeniden yazım | yeniden yazım | ✖ | Veri kesilir/hata |
| `SET NOT NULL` | 🟡 risky | tam tarama | tarama | ✔ | NULL varsa başarısız |
| `DROP NOT NULL` | 🟢 safe | anlık | anlık | ✔ | |
| `CREATE INDEX` | 🟡 risky | **CONCURRENTLY ile safe** | ONLINE=ON (Enterprise) | ✔ | Süre uzun ama kilitsiz |
| `DROP INDEX` | 🟡 risky | anlık | anlık | ✔ | Performans çöker |
| `ADD PRIMARY KEY` | 🟡 risky | tam tarama | tarama | ✔ | |
| `ADD FOREIGN KEY` | 🟡 risky | her iki tabloda kilit | kilit | ✔ | `NOT VALID` + `VALIDATE` ile kilitsiz |
| `ADD CHECK` | 🟡 risky | tam tarama | tarama | ✔ | `NOT VALID` tekniği |
| `DROP TABLE` | 🔴 destructive | anlık | anlık | ✖ | |
| `RENAME TABLE` | 🟠 breaking | anlık | anlık | ✔ | |
| `ADD UNIQUE` | 🟡 risky | index oluşumu | index | ✔ | Yinelenen varsa başarısız |
| `ALTER FK onDelete` | 🟡 risky | kilit | kilit | ✔ | |
| Enum değeri ekle | 🟢 safe | anlık | — | ✖ (PG'de) | PG'de enum değeri silinemez |
| Enum değeri sil | 🔴 destructive | — | — | ✖ | PG'de doğrudan mümkün değil, tip yeniden oluşturulur |
| `CREATE VIEW` | 🟢 safe | — | — | ✔ | |
| Partitioning ekle | 🔴 destructive | tam yeniden yazım | — | ✖ | Genelde yeni tablo + veri taşıma gerekir |

**Kilit süresi tahmini formülü:** `estimatedMs = rowCount × costPerRow(op, engine) + fixedOverhead`
`costPerRow` gerçek ölçümlerden kalibre edilir (kendi telemetrimizden) — başlangıçta muhafazakâr sabitler.

---

## 3. Güvenli desenler (otomatik uygulanır)

Namines riskli operasyonları otomatik olarak güvenli desenlere çevirir:

| Naif | Namines'in ürettiği |
|---|---|
| `CREATE INDEX` | `CREATE INDEX CONCURRENTLY` (PG) / `WITH (ONLINE=ON)` (MSSQL Ent.) |
| `ALTER TABLE ADD FOREIGN KEY` | `ADD CONSTRAINT ... NOT VALID;` + ayrı `VALIDATE CONSTRAINT` |
| `ADD CHECK` | Aynı `NOT VALID` tekniği |
| `SET NOT NULL` | Önce `CHECK (col IS NOT NULL) NOT VALID` → `VALIDATE` → `SET NOT NULL` (PG12+: tarama atlanır) |
| `RENAME COLUMN` | **Expand-contract**: yeni kolon ekle → çift yazma → geri doldur → eskiyi bırak (opt-in, çok adımlı) |
| `ALTER TYPE` (büyük tablo) | Gölge kolon + trigger ile senkron + atomik takas (opt-in) |
| `DROP COLUMN` | Önce `RENAME TO _deprecated_x` (7 gün) → sonra gerçek DROP (opt-in "yumuşak silme") |

Her migration'a `SET lock_timeout = '3s'` ve `SET statement_timeout` eklenir → uzun kilit üretimi kilitlemez, migration başarısız olur ve geri alınır. **Bu tek satır, çoğu üretim kazasını önler.**

---

## 4. Rollback stratejisi

| Op | Rollback | Otomatik mi |
|---|---|---|
| ADD COLUMN | DROP COLUMN | ✔ |
| DROP COLUMN | ✖ (veri gitti) → **yedekten geri yükleme** | ✖ |
| CREATE INDEX | DROP INDEX | ✔ |
| ALTER TYPE (genişleme) | ters ALTER (veri sığarsa) | ⚠ |
| RENAME | ters RENAME | ✔ |
| ADD CONSTRAINT | DROP CONSTRAINT | ✔ |
| DROP TABLE | ✖ → yedekten | ✖ |

Her migration için **rollback script'i önceden üretilir ve saklanır**. Destructive operasyonlardan önce otomatik yedek alınır (`preMigrationBackup: true`, Pro+).

```sql
-- 20260808_120000_v47_up.sql
-- 20260808_120000_v47_down.sql        ← her zaman üretilir
-- 20260808_120000_v47_backup.marker   ← destructive ise
```

---

## 5. Migration kayıt tablosu (tenant DB'de)

```sql
CREATE TABLE _namines_migrations (
  id              bigserial PRIMARY KEY,
  version         integer      NOT NULL,
  checksum        char(64)     NOT NULL,
  name            varchar(200) NOT NULL,
  applied_at      timestamptz  NOT NULL DEFAULT now(),
  applied_by      varchar(200) NOT NULL,
  duration_ms     integer      NOT NULL,
  operations      jsonb        NOT NULL,
  rollback_sql    text,
  status          varchar(20)  NOT NULL,   -- applied | rolled_back | failed
  error           text,
  namines_version varchar(40)  NOT NULL
);
CREATE UNIQUE INDEX ux_namines_migrations_version ON _namines_migrations(version);
```

**Drift tespiti:** Her Gateway metadata yenilemesinde tenant DB introspect edilir ve NSL ile karşılaştırılır. Fark varsa (biri elle DDL çalıştırmış) → uyarı + "drift'i NSL'e al" veya "NSL'i zorla uygula" seçeneği.

---

## 6. Ortam promosyon akışı

```
feature branch DB  →  development  →  staging  →  production
    (dry run)          (otomatik)      (onaylı)   (onaylı + pencere)
```

| Ortam | Kim uygular | Onay | Zaman penceresi |
|---|---|---|---|
| Branch/preview | otomatik | ✖ | her zaman |
| Development | otomatik (merge'de) | ✖ | her zaman |
| Staging | otomatik | ✖ | her zaman |
| Production | manuel veya CI | safe→✖, risky→1, destructive→2 kişi | opsiyonel bakım penceresi |

Bir migration production'a **ancak** staging'de başarıyla uygulanmışsa gidebilir (kanıt: aynı checksum).

---

## 7. Namines Bot (GitHub App) — detay

| Yetenek | Açıklama |
|---|---|
| PR yorumu | Risk tablosu + kilit tahmini + rollback linki ([10-REALTIME-COLLAB.md §9](10-REALTIME-COLLAB.md)) |
| Status check | `namines/schema-review` — destructive varsa `required` ise merge bloke |
| Önizleme DB | PR açılınca branch DB provision, kapanınca sil |
| `.nsl` senkron | Studio'da yapılan değişiklik PR olarak repo'ya |
| Tip senkron | Şema değişince `namines.types.ts` güncellenir, aynı PR'da |
| `/namines` komutları | `approve`, `plan`, `preview`, `rollback-plan`, `types` |
| Kırılma analizi | Değişen kolonu kullanan kod satırlarını bulur (ripgrep + AST) |

---

## 8. Data migration (şema dışı veri dönüşümü)

Bazı değişiklikler veri taşıma gerektirir. NSL'de bildirilebilir:

```nsl
migration "split_name" at version 48 {
  before  "ALTER TABLE users ADD COLUMN first_name varchar(80), ADD COLUMN last_name varchar(80)"
  data    "UPDATE users SET first_name = split_part(display_name,' ',1),
                            last_name  = split_part(display_name,' ',2)"
  after   "ALTER TABLE users DROP COLUMN display_name"
  batched by 5000            // büyük tablolarda parti parti, kilitsiz
  reversible false
}
```
Batched mod: `WHERE id > :cursor ORDER BY id LIMIT 5000` döngüsü, ilerleme takipli, durdurulabilir/devam ettirilebilir.

---

## 9. CLI ile CI/CD entegrasyonu

```bash
# Şemayı doğrula (kod incelemesinde)
npx namines validate schema.nsl --engine postgres

# Diff üret
npx namines diff --from git:main --to ./schema.nsl --format markdown

# Migration planı (uygulamadan)
npx namines plan --env production --out plan.json

# Kuru çalıştırma
npx namines apply --env preview --dry-run

# Uygula
npx namines apply --env production --approve-destructive=false

# Tipleri üret
npx namines codegen --target typescript --out ./src/namines.types.ts

# Canlı DB'yi NSL'e çevir
npx namines pull --connection "$DATABASE_URL" --out schema.nsl

# Drift kontrolü
npx namines drift --env production   # exit 1 if drift
```

Örnek GitHub Actions:
```yaml
- uses: namines/setup-action@v1
- run: npx namines validate schema.nsl
- run: npx namines diff --from origin/main --to HEAD --fail-on destructive
- run: npx namines apply --env preview
```

Faz 1'deki `namines-diff.mjs` bu CLI'ın içine taşınır (bağımlılıksız Markdown diff çıktısı korunur, `--format markdown`).
