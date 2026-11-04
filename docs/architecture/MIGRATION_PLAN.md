# MIGRATION PLAN — 11 DbContext, Şema İzolasyonu ve Sıfır Veri Kayıplı 3NF Geçiş Planı

> Tarih: 2026-11-02 · Kapsam: EF Core Multi-DbContext ve PostgreSQL Veri Taşıma Stratejisi

---

## 1. Çoklu DbContext ve Migration Geçmişi Mimarisi

Aynı PostgreSQL veritabanı üzerinde çalışan modül `DbContext`'lerinin birbirini ezmemesi için uygulanacak 4 katı kural:

1. **Şema Bazlı `__EFMigrationsHistory`:**
   - Her modül `DbContext`'i kendi şemasında ayrı bir migration geçmiş tablosu tutar:
     - `identity.__EFMigrationsHistory`
     - `studio.__EFMigrationsHistory`
     - `governance.__EFMigrationsHistory`
     - `hub.__EFMigrationsHistory`
     - `gateway.__EFMigrationsHistory`
     - `desk.__EFMigrationsHistory`
     - `namines_vault.__EFMigrationsHistory`
     - `ground.__EFMigrationsHistory`
     - `flow.__EFMigrationsHistory`
     - `billing.__EFMigrationsHistory`
     - `ops.__EFMigrationsHistory`
2. **Ayrı `MigrationsAssembly`:**
   - Her modülün migration sınıfları ve `DbContextModelSnapshot`'ı o modülün kendi derlemesinde (`Namines.Vault`, `Namines.Ground`, `Namines.Flow`, `Namines.Desk`, `Namines.Hub`, vb.) yaşar.
3. **Deterministik Sıralı Migration Koşucusu (`ModuleMigrationOrchestrator`):**
   - `Program.cs` içindeki `--migrate` ve başlangıç doğrulaması, tüm `IModuleDbContextMigrator` kayıtlarını `Order` sırasına göre çalıştırır:
     1. Mevcut `AuthDbContext` (geçiş tamamlanana kadar geriye uyumluluk için Order = 0)
     2. `Identity` (Order = 10)
     3. `Studio` (Order = 20)
     4. `Governance` (Order = 30)
     5. `Hub` (Order = 40)
     6. `Gateway` (Order = 50)
     7. `Desk` (Order = 60)
     8. `Vault` (Order = 70)
     9. `Ground` (Order = 80)
     10. `Flow` (Order = 90)
     11. `Billing` (Order = 100)
     12. `Ops` (Order = 110)
4. **Mevcut Şemalardaki Tabloları Sahiplenme (Idempotent Baseline Handover):**
   - `namines_vault`, `ground`, `desk`, `flow` şemalarındaki tablolar zaten `AuthDbContext` tarafından oluşturulmuş durumdadır.
   - Modül `DbContext`'lerinin ilk (baseline) migration'ı `CREATE SCHEMA IF NOT EXISTS ...` ve `CREATE TABLE IF NOT EXISTS ...` (veya tablo mevcutsa yalnızca indeks/kısıt doğrulaması) şeklinde **idempotent** yazılır. Böylece:
     - **Mevcut üretim/test veritabanında:** Tablolar ve içindeki veriler olduğu gibi korunur, modülün `<schema>.__EFMigrationsHistory` tablosuna ilk kayıt düşülür.
     - **Sıfırdan kurulan boş bir veritabanında:** Modül kendi şemasını ve tablolarını eksiksiz oluşturur.

---

## 2. Sıfır Veri Kayıplı 3NF Veri Migrasyonu Protokolü (Expand → Backfill → Verify → Contract)

Her 3NF dönüşümü **tek bir PostgreSQL transaction'ı** içinde şu 4 adımı izler:

```sql
-- ADIM 1: EXPAND (Yeni şema ve normalize tabloyu oluştur)
CREATE SCHEMA IF NOT EXISTS hub;
CREATE TABLE IF NOT EXISTS hub.project_tags (
    "ProjectId" text NOT NULL,
    "Tag" character varying(64) NOT NULL,
    CONSTRAINT "PK_project_tags" PRIMARY KEY ("ProjectId", "Tag"),
    CONSTRAINT "FK_project_tags_CloudProjects_ProjectId"
        FOREIGN KEY ("ProjectId") REFERENCES public."CloudProjects" ("Id") ON DELETE CASCADE
);

-- ADIM 2: BACKFILL (Mevcut veriyi kayıpsız dönüştürerek aktar)
INSERT INTO hub.project_tags ("ProjectId", "Tag")
SELECT p."Id", lower(trim(t.tag_val))
FROM public."CloudProjects" p
CROSS JOIN LATERAL regexp_split_to_table(COALESCE(p."Tags", ''), '\s*,\s*') AS t(tag_val)
WHERE length(trim(t.tag_val)) > 0
  AND length(trim(t.tag_val)) <= 64
ON CONFLICT ("ProjectId", "Tag") DO NOTHING;

-- ADIM 3: VERIFY (Satır/veri bütünlüğünü doğrula; hata varsa RAISE EXCEPTION ile ROLLBACK et)
DO $$
DECLARE
    src_projects_with_tags integer;
    dst_projects_with_tags integer;
BEGIN
    SELECT count(*) INTO src_projects_with_tags
    FROM public."CloudProjects"
    WHERE "Tags" IS NOT NULL AND length(trim("Tags", ' ,')) > 0;

    SELECT count(DISTINCT "ProjectId") INTO dst_projects_with_tags
    FROM hub.project_tags;

    IF src_projects_with_tags <> dst_projects_with_tags THEN
        RAISE EXCEPTION '3NF Migration verification failed for hub.project_tags: expected % projects, got %',
            src_projects_with_tags, dst_projects_with_tags;
    END IF;
END $$;
```

---

## 3. Kurtarma (Recovery) Garantisi

1. PostgreSQL DDL ifadeleri (`CREATE SCHEMA`, `CREATE TABLE`, `ALTER TABLE ... SET SCHEMA`, `INSERT INTO ... SELECT`, `ALTER TABLE ... DROP COLUMN`) **transactional**'dır.
2. `DO $$ ... RAISE EXCEPTION ... $$` doğrulama bloğu başarısız olduğu anda tüm migration otomatik olarak `ROLLBACK` edilir; veritabanı migration öncesi durumuna **sıfır veri kaybıyla** geri döner.
3. Geri alma (`Down()`) metotları da veriyi normalize tablodan `string_agg(...)` ile eski sütuna geri birleştirerek (`Reverse Backfill`) tam geri dönüş sağlar.
