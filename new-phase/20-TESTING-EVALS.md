# 20 — Test Stratejisi & AI Eval

> Faz 1'de **sıfır test** vardı: 37.000 satır kod, 0 test dosyası. Ürünün tamamı kod üretimi olduğu için doğruluk = ürünün kendisi. Bu, projenin en büyük tekil riskiydi.

---

## 1. Test piramidi ve hedefler

| Katman | Adet hedefi | Kapsam hedefi | Süre |
|---|---|---|---|
| Birim (`Namines.Nsl`, `Namines.Compiler`) | ~1.500 | **≥ %90** | < 30 sn |
| Golden-file (snapshot) | ~600 | tüm backend × fixture | < 60 sn |
| Integration (Testcontainers, gerçek DB) | ~300 | tüm DDL + migration | < 8 dk |
| API integration | ~250 | tüm endpoint | < 4 dk |
| Güvenlik / kiracı izolasyonu | ~120 | kritik | < 3 dk |
| Frontend birim (Vitest) | ~400 | ≥ %70 | < 60 sn |
| E2E (Playwright) | ~60 | kritik akışlar | < 12 dk |
| AI eval | ~400 senaryo | tüm ajanlar | < 20 dk (nightly) |
| Yük (NBomber/k6) | ~10 senaryo | — | nightly |

**Genel kapsam kapısı: %70. `Namines.Nsl` ve `Namines.Compiler` için %90 (pazarlık edilemez).**

---

## 2. Golden-file testleri (codegen doğruluğu)

En kritik test tipi. `Verify.Xunit` ile.

```
tests/Namines.Compiler.Tests/
├── Fixtures/                      # 25 gerçekçi test şeması
│   ├── 01-minimal.nsl
│   ├── 02-ecommerce.nsl           # ★ Faz 1 şablonu
│   ├── 03-saas-multitenant.nsl    # ★
│   ├── 04-crm.nsl                 # ★
│   ├── 05-blog.nsl                # ★
│   ├── 06-healthcare.nsl          # ★
│   ├── 07-composite-keys.nsl
│   ├── 08-self-referencing.nsl
│   ├── 09-circular-fk.nsl
│   ├── 10-multi-cascade-path.nsl  # ← MSSQL'i patlatan senaryo
│   ├── 11-all-types.nsl
│   ├── 12-partial-indexes.nsl
│   ├── 13-enums.nsl
│   ├── 14-views-materialized.nsl
│   ├── 15-rls-policies.nsl
│   ├── 16-generated-columns.nsl
│   ├── 17-partitioned.nsl
│   ├── 18-reserved-words.nsl      # "order", "user", "select"...
│   ├── 19-unicode-turkish.nsl     # "Müşteriler", "Ürünler"
│   ├── 20-long-names.nsl          # Oracle 128 / PG 63 limiti
│   ├── 21-1000-tables.nsl         # performans
│   ├── 22-schemas-namespaces.nsl
│   ├── 23-legacy-v1-import.json   # Faz 1 formatı
│   ├── 24-soft-delete.nsl
│   └── 25-vector-embeddings.nsl
└── Golden/
    ├── postgres/02-ecommerce.sql
    ├── mssql/02-ecommerce.sql
    ├── ... (25 fixture × 6 motor × 12 hedef)
```

**Test:**
```csharp
[Theory]
[MemberData(nameof(AllFixtures))]
public Task Ddl_matches_golden(string fixture, EngineTarget engine)
{
    var doc = NslParser.ParseText(File.ReadAllText(fixture));
    var result = NslCompiler.Compile(doc, new CompileOptions { Engine = engine, Target = CompileTarget.Ddl });
    return Verify(result.Artifacts.Single().Content)
        .UseDirectory($"Golden/{engine}")
        .UseFileName(Path.GetFileNameWithoutExtension(fixture));
}
```

Çıktı değiştiğinde test kırılır, geliştirici diff'i görür ve **bilinçli olarak** onaylar (`*.received.sql` → `*.verified.sql`).

### Determinizm testi
```csharp
[Theory][MemberData(nameof(AllFixtures))]
public void Compilation_is_deterministic(string fixture, EngineTarget engine) {
    var a = Compile(fixture, engine);
    var b = Compile(fixture, engine);
    a.Should().Be(b);   // byte-identical
}
```

---

## 3. Integration testleri — "DDL gerçekten çalışıyor mu?"

Bu, ürünün en önemli kalite iddiası. **Testcontainers ile gerçek veritabanı motorları.**

```csharp
public class DdlExecutionTests : IClassFixture<PostgresFixture>
{
    [Theory][MemberData(nameof(AllFixtures))]
    public async Task Generated_ddl_executes_without_error(string fixture)
    {
        var ddl = Compile(fixture, EngineTarget.Postgres);
        await using var conn = await _fixture.OpenAsync();
        await conn.ExecuteAsync(ddl);                      // hata olursa test kırılır

        // ── Round-trip doğrulaması ─────────────────────
        var introspected = await new PostgresIntrospector().ReadAsync(conn);
        var original     = NslParser.ParseText(File.ReadAllText(fixture));
        NslComparer.SemanticEquals(original, introspected).Should().BeTrue();
    }
}
```

**Round-trip testi kritik:** NSL → DDL → gerçek DB → introspect → NSL. Başlangıç ve bitiş semantik olarak aynı olmalı. Bu tek test, tip eşleme hatalarının %90'ını yakalar.

**Test matrisi (nightly):**

| Motor | Sürümler |
|---|---|
| PostgreSQL | 14, 16, 17 |
| SQL Server | 2019, 2022 |
| MySQL | 8.0, 9.x |
| MariaDB | 10.11, 11.4 |
| SQLite | 3.45 |
| Oracle | 21c XE, 23ai Free |

= 25 fixture × 11 motor sürümü = **275 gerçek DB doğrulaması** her gece.

### Migration testleri
```csharp
[Theory][MemberData(nameof(MigrationScenarios))]
public async Task Migration_applies_and_rolls_back(string from, string to)
{
    await Apply(from);                          // v1 şemasını kur
    await SeedRows(1000);                       // veri koy
    var plan = MigrationPlanner.Plan(Diff(from, to), Engine);
    await ApplyMigration(plan.Up);
    (await CountRows()).Should().Be(1000);      // veri kaybı yok
    await ApplyMigration(plan.Down);            // geri al
    (await IntrospectSchema()).Should().MatchSchema(from);
}
```

---

## 4. Güvenlik testleri (kiracı izolasyonu)

Çok kiracılı bir üründe en olası felaket cross-tenant sızıntıdır. Regresyon testi olmadan er ya da geç olur.

```csharp
public class TenantIsolationTests
{
    [Theory]
    [InlineData("GET",    "/v1/projects/{otherProjectId}")]
    [InlineData("PUT",    "/v1/projects/{otherProjectId}/schema")]
    [InlineData("DELETE", "/v1/projects/{otherProjectId}")]
    [InlineData("GET",    "/v1/databases/{otherDbId}/credentials")]
    [InlineData("POST",   "/v1/migrations/{otherMigrationId}/apply")]
    // ... 100+ kombinasyon (her endpoint × her kaynak tipi)
    public async Task TenantA_cannot_access_TenantB(string method, string path)
    {
        var response = await _tenantA.SendAsync(method, path.WithTenantBIds());
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        // 200 dönerse → BÜYÜK GÜVENLİK HATASI
    }

    [Fact] public async Task Gateway_api_key_scoped_to_its_project() { ... }
    [Fact] public async Task Rls_prevents_row_leak_across_console_roles() { ... }
    [Fact] public async Task Column_masking_applied_server_side() { ... }
    [Fact] public async Task Share_token_cannot_write_when_view_only() { ... }
    [Fact] public async Task Realtime_hub_rejects_unauthorized_room_join() { ... }
}
```

**Diğer güvenlik testleri:**
- SQL injection: 200+ payload, tüm Gateway filtre parametrelerinde
- SSRF: özel IP, DNS rebinding, redirect zinciri, IPv6, metadata endpoint'leri
- Prompt injection: 60 senaryo (URL kazıma, tablo adı, görsel OCR üzerinden)
- Rate limit atlatma: header spoofing, IP rotasyonu
- Yetki yükseltme: viewer → editor, console user → org admin
- Dosya yükleme: zip bomb, polyglot, path traversal

---

## 5. Frontend testleri

| Tip | Araç | Kapsam |
|---|---|---|
| Birim | Vitest + Testing Library | Hook'lar, store'lar, saf fonksiyonlar, `@namines/nsl` |
| Bileşen | Vitest + Testing Library | Renderer widget'ları (her tip → doğru widget) |
| Görsel regresyon | Playwright screenshot | Canvas, Console sayfaları (light + dark) |
| Erişilebilirlik | `@axe-core/playwright` | Tüm ana sayfalar, WCAG 2.2 AA |
| E2E | Playwright | Kritik akışlar |

**Kritik E2E akışları:**
1. Kayıt ol → e-posta doğrula → giriş yap
2. Prompt yaz → şema üret → canvas'ta düzenle → DDL indir
3. Şema → DB provision → Console'da kayıt ekle → Gateway'den oku
4. İki tarayıcı → aynı şemayı eş zamanlı düzenle → çakışmasız senkron
5. Branch aç → değişiklik → PR → bot yorumu → merge → migration uygulanır
6. Kolon sil → yıkıcı uyarısı → onay → uygulanır → rollback çalışır
7. SQL dosyası import → şema oluşur → tekrar export → eşleşir
8. Ücretsizden Pro'ya yükselt → Stripe → limitler açılır
9. Console rolü oluştur → kısıtlı kullanıcı gir → maskeli kolon görünmez
10. Eject → ZIP indir → `docker compose up` → çalışır

---

## 6. AI Eval Harness

```
packages/evals/
├── datasets/
│   ├── schema-generation.jsonl      (120 senaryo)
│   ├── schema-revision.jsonl        (80)
│   ├── migration-risk.jsonl         (60)
│   ├── nl-to-sql.jsonl              (100)
│   ├── vision-erd.jsonl             (40)
│   ├── dba-advisor.jsonl            (50)
│   └── prompt-injection.jsonl       (60) — güvenlik
├── graders/
│   ├── structural.ts                # deterministik
│   ├── ddl-executes.ts              # gerçek DB'de çalıştır
│   ├── normalization.ts             # 3NF kontrolü
│   ├── coverage.ts                  # istenen varlıklar üretildi mi
│   └── llm-judge.ts                 # rubrik tabanlı hakem
└── runner.ts
```

**Dataset kaydı örneği:**
```jsonc
{
  "id": "gen-042",
  "prompt": "Bir hastane randevu sistemi: doktorlar, hastalar, randevular, poliklinikler, reçeteler",
  "engine": "postgres",
  "expect": {
    "tablesMin": 5,
    "requiredTables": ["doctors","patients","appointments","clinics","prescriptions"],
    "requiredRelations": [
      { "from": "appointments", "to": "doctors" },
      { "from": "appointments", "to": "patients" },
      { "from": "prescriptions", "to": "appointments" }
    ],
    "requiredIndexes": [ { "table": "appointments", "columns": ["doctor_id","scheduled_at"] } ],
    "forbidden": { "cascadeOnDelete": ["appointments->patients"] },
    "piiTags": ["patients.national_id", "patients.phone"],
    "ddlMustExecute": true
  }
}
```

**Skor kartı (her PR'da):**
```
AI Eval Report — prompt v2.3 vs v2.2 (baseline)
────────────────────────────────────────────────
Geçerlilik           100.0%  (=)        ✅
DDL çalışabilirlik    99.2%  (+1.7)     ✅
Kapsam                 0.91  (+0.03)    ✅
Normalizasyon ihlali   0.08  (-0.02)    ✅
Index isabeti          0.74  (+0.09)    ✅
İlişki doğruluğu       0.94  (+0.01)    ✅
Adlandırma tutarlılık  0.96  (=)        ✅
Halüsinasyon           0.03  (=)        ✅
Prompt injection dir.  100%   (=)       ✅
Ort. maliyet         $0.021  (-$0.004)  ✅
p95 gecikme           4.1 s  (+0.3)     ⚠️
────────────────────────────────────────────────
GENEL: 0.913 (baseline 0.887) → MERGE EDİLEBİLİR
```

**Kapı:** Genel skor baseline'ın %2'sinden fazla düşerse merge bloke.

**Maliyet kontrolü:** Tam eval nightly çalışır. PR'da sadece 30 senaryoluk hızlı alt küme (~$0.60).

---

## 7. Yük testleri (nightly)

| Senaryo | Hedef |
|---|---|
| Gateway satır listesi | 2.000 rps, p95 < 150 ms |
| Gateway yazma | 500 rps, p95 < 250 ms |
| Studio canvas 1000 tablo | 60 FPS pan/zoom |
| Realtime 50 kullanıcı/oda | p95 yayılım < 100 ms |
| Eşzamanlı provisioning | 20 paralel, hepsi < 90 sn |
| NSL derleme 1000 tablo | < 500 ms |
| Console liste sayfası | p95 < 400 ms |

---

## 8. CI kalite kapıları

| Kapı | Zorunlu | Eşik |
|---|---|---|
| Tüm testler geçiyor | ✔ | %100 |
| Kod kapsamı (genel) | ✔ | ≥ %70 |
| Kod kapsamı (Nsl/Compiler) | ✔ | ≥ %90 |
| Golden-file | ✔ | %100 |
| Integration (6 motor) | ✔ | %100 |
| Kiracı izolasyon | ✔ | %100 |
| Determinizm | ✔ | %100 |
| AI eval regresyonu | ✔ | ≤ %2 düşüş |
| SAST (CodeQL) yüksek/kritik | ✔ | 0 |
| Bağımlılık açığı yüksek/kritik | ✔ | 0 |
| Sır taraması | ✔ | 0 |
| Erişilebilirlik (axe) kritik | ✔ | 0 |
| Bundle boyutu artışı | ⚠ | ≤ %5 |
| Lighthouse performans | ⚠ | ≥ 90 |

---

## 9. Faz 1 kodunu test altına alma sırası

Mevcut kod test edilirken hangi sırayla gidilmeli:

| Sıra | Alan | Neden önce |
|---|---|---|
| 1 | DDL generator'lar (6 motor) | Ürünün çekirdek doğruluğu; bilinen hatalar burada |
| 2 | Tip eşleme matrisi | Sessiz veri bozulmasının kaynağı |
| 3 | FK/cascade analizi | Bilinen kritik hata |
| 4 | SQL DDL import parser'ı | Kullanıcı verisi kaybı riski |
| 5 | EF Core / Prisma üreticileri | Yaygın kullanım |
| 6 | Migration diff/merge | Veri kaybı riski |
| 7 | Auth + yetkilendirme | Güvenlik |
| 8 | Introspection | Round-trip doğruluğu |
| 9 | AI JSON ayrıştırma | Kırılganlık |
| 10 | Frontend store'ları | Regresyon önleme |
