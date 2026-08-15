# 04 — NSL: Namines Schema Language & IR

> **Bu, tüm projenin temelidir.** Faz 1'in `DatabaseSchema` modeli (tablo/kolon/PK/FK/nullable/default) gerçek bir veritabanını ifade edemiyordu — index yok, unique yok, check yok, cascade politikası yok. NSL bunu çözer.

---

## 1. Tasarım hedefleri

1. **İfade gücü** — gerçek üretim şemalarının %95'ini kayıpsız temsil et
2. **Motor-bağımsız** — 6+ motora derlenebilir, motora özel kaçış kapıları olsun
3. **İnsan okunur** — `.nsl` metin formatı, git'te diff'lenebilir
4. **Makine dostu** — kanonik JSON IR + JSON Schema, deterministik serileştirme
5. **Kararlı kimlik** — her nesnenin `uuid`'si var, yeniden adlandırma diff'i bozmaz
6. **Genişletilebilir** — `x-` önekli özel metadata alanları
7. **Saf** — parser/validator/compiler I/O yapmaz, WASM'a derlenir

---

## 2. `.nsl` metin formatı — tam örnek

```nsl
// namines.nsl — sürüm 1
nsl 1.0

project "shopfront" {
  engine      postgres            // varsayılan derleme hedefi
  default_schema "public"
  naming      snake_case          // snake_case | PascalCase | camelCase
}

enum order_status {
  pending
  paid
  shipped
  cancelled
}

schema public {

  table users {
    id            uuid          pk default(gen_random_uuid())
    email         varchar(255)  not null
    display_name  varchar(120)
    password_hash text          not null  @tag(sensitive)
    country_code  char(2)       not null default('TR')
    created_at    timestamptz   not null default(now())
    deleted_at    timestamptz              @tag(soft_delete)

    unique (email)                        name: uq_users_email
    index  (country_code, created_at desc) name: ix_users_country_created
    index  (email) where "deleted_at is null" unique name: uq_users_email_active
    check  "char_length(email) > 3"        name: ck_users_email_len

    note "Uygulamanın kimlik tablosu. E-posta büyük/küçük harf duyarsız saklanır."

    @ui(label: display_name, icon: "user", order: 1)
    @rls(select: "id = current_setting('app.user_id')::uuid")
  }

  table addresses {
    id        bigserial   pk
    user_id   uuid        not null
    line1     varchar(200) not null
    city      varchar(80)  not null
    is_default boolean     not null default(false)

    fk (user_id) -> users(id) on delete cascade on update no action name: fk_addresses_user
    index (user_id)
    unique (user_id, is_default) where "is_default = true"

    @ui(label: line1, parent: users)
  }

  table orders {
    id          bigserial      pk
    user_id     uuid           not null
    address_id  bigint         not null
    status      order_status   not null default('pending')
    total       numeric(12,2)  not null check "total >= 0"
    currency    char(3)        not null default('TRY')
    placed_at   timestamptz    not null default(now())
    // hesaplanmış kolon
    total_try   numeric(12,2)  generated always as "total * fx_rate(currency)" stored

    fk (user_id)    -> users(id)     on delete restrict   name: fk_orders_user
    fk (address_id) -> addresses(id) on delete restrict   name: fk_orders_address

    index (user_id, placed_at desc)
    index (status) where "status in ('pending','paid')" name: ix_orders_open

    partition by range (placed_at)

    @ui(label: id, badge: status, default_sort: "placed_at desc")
  }

  table order_items {
    order_id   bigint         not null
    product_id bigint         not null
    quantity   int            not null default(1) check "quantity > 0"
    unit_price numeric(12,2)  not null

    pk (order_id, product_id)                    // bileşik PK
    fk (order_id) -> orders(id) on delete cascade
  }

  view active_users {
    sql "select * from users where deleted_at is null"
    materialized false
  }
}

// motora özel kaçış kapısı
raw postgres {
  "CREATE EXTENSION IF NOT EXISTS pg_trgm;"
}
```

---

## 3. Kanonik JSON IR

`.nsl` parse edilince aşağıdaki yapıya dönüşür. **Bu, sistemdeki tek doğruluk kaynağıdır.**

```jsonc
{
  "nsl": "1.0",
  "project": {
    "id": "prj_01J8...",
    "name": "shopfront",
    "engine": "postgres",
    "defaultSchema": "public",
    "naming": "snake_case"
  },
  "enums": [
    {
      "uuid": "b1f4...", "name": "order_status", "schema": "public",
      "values": ["pending", "paid", "shipped", "cancelled"],
      "note": null
    }
  ],
  "tables": [
    {
      "uuid": "9c2e...",
      "name": "users",
      "schema": "public",
      "note": "Uygulamanın kimlik tablosu...",
      "tags": [],
      "columns": [
        {
          "uuid": "a1...", "name": "id",
          "type": { "base": "uuid", "length": null, "precision": null, "scale": null,
                    "array": false, "enumRef": null, "raw": null },
          "nullable": false,
          "default": { "kind": "expression", "value": "gen_random_uuid()" },
          "identity": null,
          "generated": null,
          "collation": null,
          "note": null,
          "tags": [],
          "ui": { "widget": "readonly", "hidden": false }
        },
        {
          "uuid": "a2...", "name": "email",
          "type": { "base": "varchar", "length": 255 },
          "nullable": false,
          "tags": ["pii"],
          "ui": { "widget": "email" }
        }
      ],
      "primaryKey": { "uuid": "pk1...", "name": "pk_users", "columns": ["a1..."], "clustered": true },
      "uniques": [
        { "uuid": "u1...", "name": "uq_users_email", "columns": ["a2..."], "where": null, "nullsNotDistinct": false }
      ],
      "indexes": [
        {
          "uuid": "i1...", "name": "ix_users_country_created",
          "columns": [ { "columnUuid": "a4...", "order": "asc", "nulls": "last" },
                       { "columnUuid": "a6...", "order": "desc", "nulls": "last" } ],
          "unique": false,
          "method": "btree",          // btree|hash|gin|gist|brin|fulltext|spatial
          "where": null,              // partial index
          "include": [],              // covering index (MSSQL/PG)
          "expression": null,         // ifade index'i
          "concurrent": true
        }
      ],
      "checks": [
        { "uuid": "c1...", "name": "ck_users_email_len", "expression": "char_length(email) > 3" }
      ],
      "foreignKeys": [],
      "partition": null,
      "rls": {
        "enabled": true,
        "policies": [
          { "uuid": "p1...", "name": "own_rows", "command": "select",
            "using": "id = current_setting('app.user_id')::uuid", "withCheck": null, "roles": ["app_user"] }
        ]
      },
      "ui": { "labelColumn": "a3...", "icon": "user", "order": 1, "defaultSort": null, "badgeColumn": null },
      "position": { "x": 120, "y": 40, "color": "#3b82f6", "subjectArea": "identity" },
      "x-custom": {}
    }
  ],
  "views": [
    { "uuid": "v1...", "name": "active_users", "schema": "public",
      "sql": "select * from users where deleted_at is null", "materialized": false }
  ],
  "sequences": [],
  "domains": [],
  "raw": [ { "engine": "postgres", "phase": "pre", "sql": "CREATE EXTENSION IF NOT EXISTS pg_trgm;" } ],
  "subjectAreas": [ { "uuid": "sa1", "name": "identity", "color": "#3b82f6", "collapsed": false } ],
  "notes": [ { "uuid": "n1", "text": "Ödeme akışı v2'de değişecek", "x": 800, "y": 200 } ],
  "meta": {
    "version": 47,
    "createdAt": "2026-08-08T10:00:00Z",
    "updatedAt": "2026-08-08T11:32:00Z",
    "checksum": "sha256:1f3a..."
  }
}
```

**JSON Schema dosyası:** `packages/nsl-spec/nsl-1.0.schema.json` — hem C# hem TS tarafında doğrulama için.

---

## 4. Tip sistemi

### 4.1 Kanonik tipler

| NSL tipi | Parametre | Açıklama |
|---|---|---|
| `bool` | — | |
| `int8` `int16` `int32` `int64` | — | |
| `serial` `bigserial` | — | otomatik artan |
| `decimal(p,s)` / `numeric(p,s)` | precision, scale | |
| `float32` `float64` | — | |
| `money` | — | |
| `char(n)` `varchar(n)` `text` | length | |
| `bytes(n)` `blob` | length | |
| `uuid` | — | |
| `date` `time` `timestamp` `timestamptz` `interval` | precision | |
| `json` `jsonb` | — | |
| `xml` | — | |
| `enum<name>` | enum referansı | |
| `array<T>` | eleman tipi | PG native, diğerlerinde JSON'a düşer |
| `geometry` `geography` | SRID | PostGIS / MSSQL spatial |
| `vector(n)` | boyut | pgvector — AI uygulamaları için |
| `raw("...")` | motora özel ham tip | kaçış kapısı |

### 4.2 Tip eşleme matrisi (özet — tam matris `Namines.Compiler/TypeMaps/`)

| NSL | PostgreSQL | SQL Server | MySQL | MariaDB | SQLite | Oracle |
|---|---|---|---|---|---|---|
| `bool` | `boolean` | `BIT` | `TINYINT(1)` | `TINYINT(1)` | `INTEGER` | `NUMBER(1)` |
| `int32` | `integer` | `INT` | `INT` | `INT` | `INTEGER` | `NUMBER(10)` |
| `int64` | `bigint` | `BIGINT` | `BIGINT` | `BIGINT` | `INTEGER` | `NUMBER(19)` |
| `bigserial` | `bigserial` | `BIGINT IDENTITY(1,1)` | `BIGINT AUTO_INCREMENT` | aynı | `INTEGER AUTOINCREMENT` | `NUMBER GENERATED AS IDENTITY` |
| `decimal(p,s)` | `numeric(p,s)` | `DECIMAL(p,s)` | `DECIMAL(p,s)` | aynı | `NUMERIC` | `NUMBER(p,s)` |
| `varchar(n)` | `varchar(n)` | `NVARCHAR(n)` | `VARCHAR(n)` | aynı | `TEXT` | `VARCHAR2(n CHAR)` |
| `text` | `text` | `NVARCHAR(MAX)` | `LONGTEXT` | aynı | `TEXT` | `CLOB` |
| `uuid` | `uuid` | `UNIQUEIDENTIFIER` | `CHAR(36)` | `CHAR(36)`/`UUID` | `TEXT` | `RAW(16)` |
| `timestamptz` | `timestamptz` | `DATETIMEOFFSET` | `TIMESTAMP` | aynı | `TEXT` | `TIMESTAMP WITH TIME ZONE` |
| `jsonb` | `jsonb` | `NVARCHAR(MAX)` + CHECK ISJSON | `JSON` | `JSON`/`LONGTEXT` | `TEXT` | `JSON`/`CLOB` |
| `enum<x>` | `CREATE TYPE ... AS ENUM` | `VARCHAR + CHECK IN` | `ENUM(...)` | `ENUM(...)` | `TEXT + CHECK` | `VARCHAR2 + CHECK` |
| `array<T>` | `T[]` | `NVARCHAR(MAX)` (JSON) | `JSON` | `JSON` | `TEXT` | `JSON` |
| `vector(n)` | `vector(n)` (pgvector) | ❌ uyarı | ❌ | ❌ | ❌ | `VECTOR` (23ai) |

**Kayıp bilgi kuralı:** Bir özellik hedef motorda desteklenmiyorsa derleyici `CompilerDiagnostic` üretir (severity: `info` / `warning` / `error`) ve mümkünse emülasyon uygular. Sessizce düşürme **yasak**.

---

## 5. Referans bütünlüğü ve cascade politikası

> **Faz 1'in en kritik hatası buydu:** tüm FK'lara sabit `ON DELETE CASCADE` yazılıyordu. Bu, SQL Server'da çoklu cascade yolu hatası veriyor ve her yerde sessiz veri kaybı riski yaratıyor.

```jsonc
"foreignKeys": [{
  "uuid": "fk1...",
  "name": "fk_orders_user",
  "columns": ["c_user_id"],
  "referencedTable": "9c2e...",
  "referencedColumns": ["a1..."],
  "onDelete": "restrict",   // no_action | restrict | cascade | set_null | set_default
  "onUpdate": "no_action",
  "deferrable": false,
  "matchType": "simple"     // simple | full | partial
}]
```

**Varsayılan: `onDelete: "no_action"`.** Cascade sadece kullanıcı açıkça seçerse.

**Derleyici doğrulaması (`FkCascadeAnalyzer`):**
1. FK grafiğini kur
2. Aynı hedefe birden fazla cascade yolu var mı? → SQL Server'da **error**, PostgreSQL'de **warning**
3. Cascade döngüsü var mı? → tüm motorlarda **error**
4. `set_null` hedef kolonu `not null` mu? → **error**
5. `set_default` için default tanımlı mı? → **error**

Studio'da FK çizilirken kullanıcıya davranış seçtiren bir açılır menü çıkar; varsayılan `RESTRICT` seçili gelir.

---

## 6. Doğrulama kuralları (Validator)

`Namines.Nsl.Validation` — her kural bir `NslRule`, kod + severity + otomatik düzeltme.

| Kod | Kural | Severity | Auto-fix |
|---|---|---|---|
| `NSL001` | Tablo adı benzersiz (schema içinde) | error | ✔ |
| `NSL002` | Kolon adı benzersiz (tablo içinde) | error | ✔ |
| `NSL003` | Her tabloda PK olmalı | warning | ✔ (id ekle) |
| `NSL004` | FK tipleri hedefle uyumlu | error | ✔ |
| `NSL005` | FK hedefi PK veya UNIQUE olmalı | error | ✖ |
| `NSL006` | Çoklu cascade yolu (MSSQL) | error | ✔ (restrict'e çevir) |
| `NSL007` | Cascade döngüsü | error | ✖ |
| `NSL008` | Rezerve kelime kullanımı | warning | ✔ (quote/rename) |
| `NSL009` | Ad uzunluğu motor limitini aşıyor (Oracle 128, PG 63) | error | ✔ |
| `NSL010` | Index'siz FK kolonu | warning | ✔ (index ekle) |
| `NSL011` | Yinelenen index (aynı kolon seti) | warning | ✔ |
| `NSL012` | Aşırı geniş index (>5 kolon) | info | ✖ |
| `NSL013` | `varchar` uzunluğu belirtilmemiş | warning | ✔ (255) |
| `NSL014` | Para için `float` kullanımı | warning | ✔ (decimal(19,4)) |
| `NSL015` | `timestamp` (tz'siz) kullanımı | info | ✔ |
| `NSL016` | Nullable PK | error | ✔ |
| `NSL017` | Enum değeri boş/yinelenen | error | ✔ |
| `NSL018` | İlişkisiz (yetim) tablo | info | ✖ |
| `NSL019` | 3NF ihlali şüphesi (tekrarlayan kolon grubu) | info | ✖ |
| `NSL020` | PII etiketli kolonda şifreleme/maskeleme yok | warning | ✖ |
| `NSL021` | RLS açık ama politika yok | error | ✖ |
| `NSL022` | View SQL'i parse edilemiyor | warning | ✖ |
| `NSL023` | Soft-delete kolonu var ama index yok | info | ✔ |
| `NSL024` | Hedef motorda desteklenmeyen özellik | warning/error | ✔ (emüle et) |
| `NSL025` | Tablo/kolon açıklaması eksik | info | ✔ (AI ile doldur) |

---

## 7. Diff ve Migration IR

```jsonc
{
  "from": { "version": 46, "checksum": "sha256:aa.." },
  "to":   { "version": 47, "checksum": "sha256:bb.." },
  "operations": [
    { "op": "add_column", "table": "orders", "column": {...},
      "risk": "safe", "lockLevel": "none", "reversible": true },
    { "op": "drop_column", "table": "users", "column": "legacy_field",
      "risk": "destructive", "dataLoss": true, "reversible": false,
      "estimatedRows": 128443, "warning": "128,443 satırda veri kalıcı olarak silinecek" },
    { "op": "alter_column_type", "table": "orders", "column": "total",
      "from": "numeric(10,2)", "to": "numeric(12,2)",
      "risk": "risky", "lockLevel": "access_exclusive", "estimatedDurationMs": 4200 },
    { "op": "create_index", "table": "orders", "index": {...},
      "risk": "safe", "concurrent": true, "estimatedDurationMs": 18000 },
    { "op": "add_fk", ... },
    { "op": "rename_table", "from": "user", "to": "users",
      "risk": "breaking", "note": "UUID eşleşmesiyle rename olarak algılandı (drop+create değil)" }
  ],
  "summary": { "safe": 2, "risky": 1, "destructive": 1, "breaking": 1 },
  "gate": "requires_approval"
}
```

**Risk sınıfları:** `safe` (otomatik uygulanır) · `risky` (kilit/süre uyarısı) · `destructive` (veri kaybı, onay şart) · `breaking` (API/istemci kırılır, onay + eski API sürümü tutulur)

**UUID tabanlı rename tespiti:** Faz 1'de `StableUuid` alanı vardı — bu doğru bir sezgiydi, NSL'de her nesneye genişletildi. Ad değişince UUID sabit kaldığı için `DROP + CREATE` yerine `RENAME` üretilir. **Bu tek başına veri kaybını önleyen en önemli özellik.**

---

## 8. Import / Export formatları

| Format | Import | Export | Not |
|---|---|---|---|
| `.nsl` (metin) | ✔ | ✔ | Kanonik |
| NSL JSON IR | ✔ | ✔ | Kanonik |
| **DBML** (dbdiagram) | ✔ | ✔ | **GTM için kritik** — dbdiagram kullanıcısını çekme yolu |
| SQL DDL (6 motor) | ✔ | ✔ | Faz 1'de vardı, genişletildi |
| Canlı DB introspection | ✔ | — | INFORMATION_SCHEMA + motor katalogları |
| Prisma `.prisma` | ✔ | ✔ | Faz 1'de sadece export vardı |
| Drizzle | ✔ | ✔ | |
| EF Core `DbContext` | ✔ | ✔ | Faz 1'de vardı |
| SQLAlchemy | ✖ | ✔ | |
| Django models | ✖ | ✔ | |
| Liquibase XML/YAML | ✖ | ✔ | |
| Flyway SQL | ✖ | ✔ | |
| Atlas HCL | ✔ | ✔ | |
| Mermaid ER | ✖ | ✔ | Faz 1'de vardı |
| PlantUML | ✖ | ✔ | |
| JSON Schema | ✖ | ✔ | |
| OpenAPI 3.1 | ✖ | ✔ | Gateway için |
| Excel/CSV veri sözlüğü | ✔ | ✔ | |
| Görsel (PNG/SVG/PDF) | ✖ | ✔ | Faz 1'de vardı |

---

## 9. NSL kütüphanesi API'si (C#)

```csharp
namespace Namines.Nsl;

// Parse
NslDocument doc = NslParser.ParseText(nslSource);      // .nsl → IR
NslDocument doc = NslParser.ParseJson(json);            // JSON → IR
string text     = NslWriter.ToText(doc);                // IR → .nsl
string json     = NslWriter.ToJson(doc, canonical: true); // deterministik

// Doğrulama
ValidationReport report = NslValidator.Validate(doc, EngineTarget.Postgres);
NslDocument fixedDoc    = NslValidator.ApplyAutoFixes(doc, report);

// Diff
SchemaDiff diff = NslDiffer.Diff(oldDoc, newDoc);
MigrationPlan plan = MigrationPlanner.Plan(diff, EngineTarget.Postgres, PlanOptions.Safe);

// Derleme
CompileResult ddl = NslCompiler.Compile(doc, new CompileOptions {
    Engine = EngineTarget.Postgres,
    Target = CompileTarget.Ddl,
    IncludeDropStatements = false,
    Concurrent = true
});
// → ddl.Artifacts: [{ FileName, Content, Kind }]
// → ddl.Diagnostics: [{ Code, Severity, Message, Path }]

// Birleştirme (collab)
MergeResult merged = NslMerger.ThreeWay(bas: baseDoc, ours: mine, theirs: yours);
```

Aynı API TypeScript'te de var: `@namines/nsl` (WASM veya saf TS port). Tarayıcıda anlık DDL önizlemesi bununla yapılır — sunucuya gitmeden.

---

## 10. Faz 1'den göç

Faz 1 `DatabaseSchema` JSON'ları otomatik dönüştürülür:

```csharp
NslDocument doc = LegacyMigrator.FromV1(legacySchema);
```

Kurallar:
- `SchemaTable.StableUuid` → `table.uuid` (korunur)
- `SchemaColumn.IsPK` → `primaryKey.columns` (bileşik olarak toplanır)
- `SchemaColumn.IsFK` + `SchemaRelation` → `foreignKeys[]`, **`onDelete: "no_action"`** (eski cascade davranışı korunmaz — kasıtlı, çünkü hatalıydı; kullanıcıya bildirilir)
- `SchemaColumn.Type` (string) → kanonik tip (sözlükle eşlenir, eşleşmezse `raw()`)
- Eksik tüm yeni özellikler boş bırakılır, `NSL003/NSL010/NSL013` uyarıları üretilir → kullanıcıya "şemanı iyileştir" akışı gösterilir

**Göç sırasında kullanıcıya gösterilecek ekran:** *"Şeman NSL 1.0'a yükseltildi. 7 iyileştirme önerisi bulundu (5'i tek tıkla düzeltilebilir). Ayrıca 12 yabancı anahtarın silme davranışı CASCADE'den RESTRICT'e çevrildi — bu veri kaybını önler. [İncele]"*
