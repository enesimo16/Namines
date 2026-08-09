# Namines.Tests

## Çalıştırma

```bash
# Ana paket — her zaman YEŞİL olmalı
dotnet test --filter "Category!=KnownIssue"

# Bilinen hatalar — şu an KIRMIZI, düzeltildikçe yeşile dönecek
dotnet test --filter "Category=KnownIssue"

# Hepsi
dotnet test
```

## Test türleri

### 1. Golden-file (snapshot) testleri — `Ddl/DdlGoldenTests.cs`

5 fixture × 6 motor = 30 DDL çıktısı `Golden/{Motor}/{fixture}.verified.sql` altında kayıtlı.

Bir üreticiyi değiştirdiğinde test kırılır ve yanına `.received.sql` yazılır. Yapman gereken:

```bash
# 1. Diff'i incele
diff Golden/MSSQL/02-ecommerce.verified.sql Golden/MSSQL/02-ecommerce.received.sql

# 2. Değişiklik DOĞRUYSA kabul et
mv Golden/MSSQL/02-ecommerce.received.sql Golden/MSSQL/02-ecommerce.verified.sql
```

**Tüm baseline'ları toplu kabul (dikkatli kullan — diff'i okumadan yapma):**
```bash
find Golden -name "*.received.sql" -exec sh -c 'mv "$1" "${1%.received.sql}.verified.sql"' _ {} \;
```

> ⚠️ Golden dosyalar **doğru DDL'i değil, bugünkü DDL'i** temsil eder. İçlerinde bilinen hatalar
> var (her FK'da `ON DELETE CASCADE`, index üretimi yok). Amaç baseline oluşturmak — böylece
> yapılan her değişikliğin etkisi görünür oluyor.

### 2. Değişmez (invariant) testleri

Golden dosyalara ek olarak her fixture × motor için:
- **Determinizm** — aynı girdi iki kez → byte-identical çıktı
- **Boş değil** — en az bir `CREATE TABLE` var
- **Tablo kaybı yok** — şemadaki her tablo çıktıda görünüyor

### 3. Bilinen hata testleri — `Ddl/CascadePathTests.cs`

`[Trait("Category", "KnownIssue")]` ile işaretli. **Arzu edilen davranışı** ifade ederler,
bu yüzden şu an kırmızıdırlar. Kırmızı olmaları hatanın kanıtıdır.

| Test | Ne kanıtlıyor | Ne zaman yeşile döner |
|---|---|---|
| `Mssql_ddl_must_not_contain_multiple_cascade_paths` | `Orders → Users: 2 yol` → SQL Server Msg 1785 ile reddeder | G3 |
| `Self_referencing_fk_must_not_cascade` | `parent_id` self-FK + CASCADE = döngü | G3 |
| `Cascade_must_not_be_the_unconditional_default` | 6 motorda da kullanıcı istemeden CASCADE yazılıyor | G3 |

## Fixture'lar — `Fixtures/SchemaFixtures.cs`

| # | Fixture | Neyi kapsıyor |
|---|---|---|
| 01 | `minimal` | En küçük geçerli şema: CREATE TABLE + PK + identity |
| 02 | `ecommerce` | 4 tablo, 3 FK — tipik gerçek kullanım |
| 03 | `composite-key` | Bileşik PK (iki kolon birlikte) |
| 04 | `self-referencing` | `parent_id` deseni |
| 05 | `multi-cascade-path` | SQL Server'ı patlatan senaryo |

**Fixture'lar değişmez.** Bir fixture'ı değiştirmek ona bağlı 6 golden dosyanın anlamını bozar.
Yeni senaryo gerekiyorsa yeni fixture ekle.

Şemalar deterministik olmalı — `Guid.NewGuid()` gibi rastgelelik kullanma, `StableUuid`'i
açıkça ver. Aksi halde her çalıştırmada farklı çıktı üretilir ve snapshot testleri anlamsızlaşır.

## Henüz yapılmadı

- **Gerçek veritabanında çalıştırma (Testcontainers)** — G5. Bugünkü testler DDL'in *metnini*
  doğruluyor, gerçekten çalıştığını değil. Msg 1785 iddiası SQL Server dokümantasyonuna
  dayanıyor; ampirik doğrulama G5'te Docker ile yapılacak.
- **Round-trip testi** — NSL → DDL → gerçek DB → introspect → NSL. G5.
- **Index / unique / check testleri** — model bunları henüz desteklemiyor. G4.
