# 12 — Kod Üretimi, Dokümantasyon ve Eject

Faz 1'in en olgun parçası buydu (6 DDL motoru, EF Core, Prisma, PDF, Mermaid, Streamlit). **Hepsi korunuyor**, NSL üzerine yeniden inşa ediliyor ve genişletiliyor.

---

## 1. Derleyici mimarisi

```
NSL IR
  │
  ├─▶ IEngineBackend        → DDL (6+ motor)
  ├─▶ IOrmBackend           → ORM modelleri (8 hedef)
  ├─▶ IDocBackend           → Dokümantasyon (7 format)
  ├─▶ IAppBackend           → Uygulama iskeleti (5 hedef)
  ├─▶ ITypeBackend          → Tip tanımları (4 dil)
  └─▶ IMigrationBackend     → Migration dosyaları (5 araç)
```

Tümü `Namines.Compiler` içinde, **saf fonksiyon** (I/O yok) → birim test edilebilir, WASM'a derlenir.

```csharp
public interface ICompilerBackend {
    string Id { get; }                          // "ddl.postgres"
    BackendCapabilities Capabilities { get; }
    CompileResult Compile(NslDocument doc, CompileOptions options);
}

public sealed record CompileResult(
    IReadOnlyList<Artifact> Artifacts,          // { Path, Content, ContentType }
    IReadOnlyList<Diagnostic> Diagnostics);     // { Code, Severity, Message, NodePath }
```

---

## 2. DDL hedefleri

| Backend | Motor | Sürüm hedefi | Faz 1 | Faz 2 eklemeleri |
|---|---|---|---|---|
| `ddl.postgres` | PostgreSQL | 13-18 | ✔ | index (partial/expr/GIN), enum tipi, RLS politikası, view, partition, comment, generated column, `CONCURRENTLY` |
| `ddl.mssql` | SQL Server | 2017-2022, Azure SQL | ✔ | filtered index, INCLUDE kolonları, computed column, extended property, schema, **cascade yolu doğrulaması** |
| `ddl.mysql` | MySQL | 8.0-9.x | ✔ | fulltext, generated column, charset/collation, index prefix |
| `ddl.mariadb` | MariaDB | 10.6-11.x | ✔ | UUID tipi, invisible column |
| `ddl.sqlite` | SQLite | 3.40+ | ✔ | partial index, generated column, STRICT tablolar |
| `ddl.oracle` | Oracle | 19c-23ai | ✔ | identity, comment, tablespace, JSON tipi |
| `ddl.cockroach` | CockroachDB | 24+ | ✖ | P3 |
| `ddl.duckdb` | DuckDB | 1.x | ✖ | P3 — analitik senaryolar |

**Her backend için zorunlu:** golden-file testleri + Testcontainers ile gerçek motorda çalıştırma ([20-TESTING-EVALS.md](20-TESTING-EVALS.md)).

### Faz 1'den düzeltilen kritik hatalar

| Hata | Düzeltme |
|---|---|
| Tüm FK'lara sabit `ON DELETE CASCADE` | NSL'den gelen `onDelete` kullanılır, varsayılan `NO ACTION` |
| MSSQL çoklu cascade yolu → DDL patlıyordu | `FkCascadeAnalyzer` derleme öncesi hata verir |
| Index üretimi hiç yok | Tam index desteği |
| `col.Type.ToUpper()` ile ham tip yazımı | Kanonik tip → motor tipi eşleme matrisi |
| Tablo oluşturma sırası rastgele | **Topolojik sıralama** (FK bağımlılığına göre) |
| Identity sadece INT/BIGINT PK'da | NSL `identity` özelliği ile açık kontrol |
| Rezerve kelime kaçışı tutarsız | Motor başına rezerve kelime listesi + tutarlı quoting |
| Şema/namespace yok | `schema` desteği tüm motorlarda |

---

## 3. ORM hedefleri

| Backend | Çıktı | Faz 1 | Öncelik |
|---|---|---|---|
| `orm.efcore` | Entity sınıfları + `DbContext` + Fluent API konfigürasyonu | ✔ | P0 |
| `orm.efcore.migrations` | `Migration` sınıfları + Designer + snapshot | ✔ | P0 |
| `orm.prisma` | `schema.prisma` (relations, `@@index`, `@@unique`, enum) | ✔ (export) | P0 |
| `orm.drizzle` | `schema.ts` (pgTable/mysqlTable + relations) | ✖ | P1 |
| `orm.typeorm` | Entity dekoratörleri | ✖ | P2 |
| `orm.sqlalchemy` | Declarative modeller (2.0 stili) | ✖ | P2 |
| `orm.django` | `models.py` + `migrations/` | ✖ | P2 |
| `orm.gorm` | Go struct + tag | ✖ | P3 |
| `orm.sequelize` | Model tanımları | ✖ | P3 |

---

## 4. Tip ve sözleşme hedefleri

| Backend | Çıktı | Öncelik |
|---|---|---|
| `types.typescript` | `interface`/`type` + `Database` haritası (Gateway SDK için) | P0 |
| `types.zod` | Zod şemaları (form doğrulama) | P1 |
| `types.csharp` | POCO record'lar | P1 |
| `types.python` | Pydantic v2 modelleri | P2 |
| `contract.openapi` | OpenAPI 3.1 (Gateway'in yayınladığı) | P0 |
| `contract.graphql` | GraphQL SDL | P1 |
| `contract.jsonschema` | JSON Schema draft 2020-12 | P2 |
| `contract.protobuf` | `.proto` tanımları | P3 |

---

## 5. Migration aracı hedefleri

| Backend | Çıktı | Öncelik |
|---|---|---|
| `mig.namines` | Namines'in kendi formatı (up/down SQL) | P0 |
| `mig.efcore` | EF Core migration | P0 |
| `mig.flyway` | `V{n}__{name}.sql` | P2 |
| `mig.liquibase` | XML/YAML changelog | P2 |
| `mig.atlas` | HCL + migration dizini | P2 |
| `mig.prisma` | `prisma/migrations/` | P1 |
| `mig.golang-migrate` | `{n}_{name}.up.sql`/`.down.sql` | P3 |

---

## 6. Dokümantasyon motoru

| Backend | Çıktı | Faz 1 | Öncelik |
|---|---|---|---|
| `doc.datadictionary.pdf` | Veri sözlüğü PDF (QuestPDF) | ✔ | P1 |
| `doc.readme` | `README.md` (tablolar, ilişkiler, kullanım) | ✔ | P1 |
| `doc.mermaid.er` | Mermaid ER diyagramı | ✔ | P1 |
| `doc.mermaid.class` | Mermaid class diyagramı | ✔ | P2 |
| `doc.mermaid.flow` | Mermaid akış diyagramı | ✔ | P2 |
| `doc.plantuml` | PlantUML | ✖ | P2 |
| `doc.excel` | Veri sözlüğü XLSX | ✖ | P1 |
| `doc.docusaurus` | Tam dokümantasyon sitesi | ✖ | P3 |
| `doc.image` | PNG/SVG/PDF canvas görseli | ✔ | P1 |
| `doc.dbml` | DBML (dbdiagram uyumlu) | ✖ | **P0** |

**Dil desteği:** Tüm doküman çıktıları TR/EN (Faz 1'de vardı, korunur) + i18n altyapısıyla genişletilebilir.

**AI destekli doküman:** `DocWriter` ajanı eksik tablo/kolon açıklamalarını doldurur; kullanıcı onaylayınca NSL'e `note` olarak yazılır — yani bir kez üretilir, kalıcı olur, her export'ta tekrar token harcanmaz.

---

## 7. Uygulama iskeleti hedefleri (Eject)

| Backend | Çıktı | Faz 1 | Öncelik |
|---|---|---|---|
| `app.nextjs` | Next.js 16 + shadcn/ui admin paneli, tam CRUD | ✖ | P1 |
| `app.streamlit` | **Streamlit admin app** (Faz 1 özelliği) | ✔ | P1 |
| `app.react` | Vite + React SPA | ✖ | P2 |
| `app.blazor` | Blazor Server/WASM admin | ✖ | P2 |
| `app.aspnet-api` | ASP.NET Core CRUD API iskeleti | ✖ | P2 |
| `app.fastapi` | FastAPI + SQLAlchemy iskeleti | ✖ | P3 |
| `app.retool` | Retool uygulama JSON'u | ✖ | P3 |

Her eject paketi içerir: kaynak kod, `README.md`, `.env.example`, `Dockerfile`, `docker-compose.yml`, CI workflow'u, `namines.lock` (hangi şema sürümünden üretildi).

---

## 8. Developer Package (Faz 1'in "dev package" özelliği, genişletilmiş)

Tek ZIP'te tam bir başlangıç projesi:

```
shopfront-package/
├── README.md
├── namines.nsl                  # şema kaynağı
├── namines.lock
├── db/
│   ├── schema.postgres.sql
│   ├── schema.mssql.sql
│   ├── seed.sql
│   └── migrations/
├── backend/                     # seçilen hedef (EF Core / Prisma / SQLAlchemy)
│   ├── src/
│   └── Dockerfile
├── admin/                       # seçilen hedef (Next.js / Streamlit / Blazor)
│   ├── src/
│   └── Dockerfile
├── types/
│   ├── namines.types.ts
│   └── openapi.json
├── docs/
│   ├── data-dictionary.pdf
│   ├── er-diagram.svg
│   └── schema.dbml
├── docker-compose.yml           # DB + backend + admin, tek komutla ayağa kalkar
├── .github/workflows/
│   └── namines-schema-review.yml
└── .env.example
```

`docker compose up` → 60 saniyede çalışan bir sistem. **Bu, "eject" vaadinin somut kanıtıdır ve demo olarak çok güçlüdür.**

---

## 9. Determinizm ve kararlılık

Codegen çıktısı **deterministik** olmalı — aynı NSL, aynı byte'lar. Yoksa git diff'leri gürültülü olur ve kullanıcı güvenmez.

Kurallar:
- Tablolar/kolonlar/index'ler kanonik sırada (topolojik → alfabetik)
- Hiçbir çıktıda zaman damgası veya GUID yok (dosya adı hariç, o da `version` bazlı)
- Satır sonu `\n` (Windows'ta bile), UTF-8 BOM'suz
- Formatlama sabit (indent 2 boşluk / SQL'de 4)
- `namines.lock` dosyası: `{ schemaVersion, checksum, generatedBy, backends: {...} }`

Test: her backend için "aynı girdi iki kez → byte-identical" testi.

---

## 10. WASM derlemesi (tarayıcıda anlık önizleme)

`Namines.Nsl` + `Namines.Compiler` → WebAssembly (`.NET WASM` veya TS port).

Kazanç:
- Studio'da kullanıcı kolon eklerken DDL önizlemesi **anında** güncellenir (sunucuya gitmez)
- Ücretsiz kullanıcı DDL üretimi için hiç sunucu kaynağı tüketmez
- Offline çalışır

Karar: **Önce TypeScript port** (`@namines/compiler-web`) — sadece DDL + DBML + tipler için. Tam derleyici .NET'te kalır. İki implementasyonun tutarlılığı ortak golden-file testleriyle garanti edilir.
