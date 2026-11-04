# PHASE 14.2A — CRITICAL DATA SECURITY & OWNERSHIP MIGRATION REPORT

> **Tarih:** 2026-11-03  
> **Aşama:** Phase 14.2A — Kritik Veri Güvenliği ve Sahiplik Normalizasyonu  
> **Kapsam:** P0 Güvenlik Maddeleri (Veritabanı Sırları, Plaintext ShareToken Eliminasyonu, Transitive OrganizationId Temizliği, Kiracı İzolasyonu)  
> **Hüküm:** **BAŞARIYLA TAMAMLANDI VE GERÇEK POSTGRESQL'DE DOĞRULANDI**

---

## 1. Değiştirilen Dosyalar ve Sorumluluklar

| Dosya | Değişiklik Türü | Sorumluluk ve Güvenlik Kazanımı |
|---|---|---|
| `backend/Namines.BuildingBlocks/Persistence/Phase14_2A_SecurityMigrationSql.cs` | **Yeni** | Expand -> Backfill -> Verify -> Contract aşamalı SQL üreteci. İşlem içi satır sayısı doğrulaması (`DO $$ ... RAISE EXCEPTION`) ile sıfır veri kaybı garantisi. |
| `backend/Namines.BuildingBlocks/Security/IProjectConnectionSecretProvider.cs` | **Güncellendi** | `ResolveForUserAsync(projectId, userId)` metodu eklendi; sır çözülmeden önce kiracı ve kullanıcı yetkisinin (`ITenantProjectAuthority.CanViewAsync`) zorunlu doğrulanması sağlandı. |
| `backend/Namines.Infrastructure/Data/TenantProjectAuthorityService.cs` | **Güncellendi** | 3NF `studio.project_connections` okuma önceliği, geriye dönük miras okuma desteği, çift-yazma senkronizasyonu ve yetkisiz erişim bloklaması eklendi. |
| `backend/Namines.Core/Models/Auth/CloudProject.cs` | **Güncellendi** | `ShareToken` özelliği `[NotMapped]` ile işaretlendi. Düz metin jetonun veritabanına yazılması ve okunması EF Core seviyesinde kalıcı olarak engellendi. |
| `backend/Namines.API/Controllers/ShareController.cs` | **Güncellendi** | `CreateShareLink`: 192-bit kriptografik rastgele jeton üretir, veritabanına yalnızca SHA-256 özetini (`ShareTokenHash`) kaydeder ve jetonu yalnızca oluşturan kullanıcıya tek seferlik döner. `RevokeShareLink`: Özeti `null` yaparak anında iptal eder. |
| `backend/Namines.Vault/Configurations/VaultModelConfiguration.cs` | **Güncellendi** | `VaultBackup`, `VaultRestore`, `VaultSchedule` üzerindeki transitif ve kilitlenme yaratan `OrganizationId` yabancı anahtar (FK) kısıtları kaldırıldı; sahiplik yetkili `ProjectId -> CloudProject` ilişkisine bağlandı. |
| `backend/Namines.Ground/Configurations/GroundModelConfiguration.cs` | **Güncellendi** | `GroundDatabase` üzerindeki transitif `OrganizationId` FK kısıtı kaldırıldı. |
| `backend/Namines.BuildingBlocks/Persistence/ThreeNfAndSchemaNormalizationSql.cs` | **Düzeltildi** | `project_connections` için eksik olan `AllowDeskSql` sütunu eklendi; `schema_share_links` içinde veritabanında var olmayan `ShareMode` ve `ShareExpiresAt` başvuruları temizlendi. |
| `backend/Namines.Tests/Security/Phase14_2A_SecurityAndMigrationTests.cs` | **Yeni** | Gerçek Docker PostgreSQL konteynerinde (`postgres:17-alpine`) çalışan Expand/Backfill/Verify/Contract göç testi ve birim güvenlik testleri (5/5 yeşil). |

---

## 2. Değiştirilen Tablolar ve Kolonlar (Before / After Şema Detayları)

### 2.1. `studio.project_connections` (Yeni 3NF Tablosu)

- **Şema:** `studio`
- **Tablo:** `project_connections`
- **Birincil Anahtar:** `ProjectId` (varchar(64))
- **Yabancı Anahtar:** `FK_project_connections_CloudProjects_ProjectId` (`ProjectId -> "CloudProjects"("Id") ON DELETE CASCADE`)
- **Sütun Yapısı:**
  ```sql
  CREATE TABLE studio.project_connections (
      "ProjectId" character varying(64) NOT NULL,
      "ConnectionDbType" character varying(32) NOT NULL DEFAULT 'PostgreSQL',
      "EncryptedConnectionString" text NOT NULL,
      "AllowDeskSql" boolean NOT NULL DEFAULT false,
      "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
      CONSTRAINT "PK_project_connections" PRIMARY KEY ("ProjectId")
  );
  ```
- **Kazanım:** Canlı veritabanı bağlantı sırrı (`EncryptedConnectionString`), NULL değer taşıyan `CloudProjects` tablosundan ayrıştırıldı. AES-256-GCM ile şifrelenir, `[JsonIgnore]` ile API ve log serileştirmesinden gizlenir.

---

### 2.2. Plaintext `ShareToken` Eliminasyonu

- **Önceki Durum:**
  - `CloudProjects."ShareToken"` sütunu veritabanında düz metin olarak saklanıyordu (Örn: `dGhpcy1pcy1hLXNoYXJlLXRva2Vu`).
  - Veritabanı yedeğini veya satırı okuyan herkes anonim bağlantıyı görebiliyordu.
- **Sonraki Durum:**
  - `CloudProjects."ShareToken"` veritabanında `NULL` yapıldı ve C# tarafında `[NotMapped]` ile yok sayıldı.
  - Veritabanında **yalnızca** `ShareTokenHash` (SHA-256 küçük harf hex, 64 karakter) saklanır.
  - Anonim uçlar (`/api/share/view/{token}`, `/api/share/badge/{token}`, `/api/share/og/{token}.svg`, `/api/share/meta/{token}`) gelen jetonu anında SHA-256 ile özetleyip indeksli `ShareTokenHash` üzerinden bulur.
  - **Geriye Dönük Uyumluluk:** Daha önce üretilmiş tüm bağlantılar çalışmaya devam eder; çünkü anonim uçlar token'ı URL'den alıp hash'leyerek arar ve veritabanındaki hash değeri değişmemiştir.

---

### 2.3. Transitive `OrganizationId` Kısıtlarının Temizlenmesi

- **Kaldırılan Yabancı Anahtar Kısıtları:**
  1. `namines_vault.vault_backups` tablosundaki `FK_vault_backups_Organizations_OrganizationId`
  2. `namines_vault.vault_restores` tablosundaki `FK_vault_restores_Organizations_OrganizationId`
  3. `namines_vault.vault_schedules` tablosundaki `FK_vault_schedules_Organizations_OrganizationId`
  4. `ground.ground_databases` tablosundaki `FK_ground_databases_Organizations_OrganizationId`
- **Gerekçe:** Bu tablolar zaten `ProjectId` üzerinden `CloudProjects`'e bağlıdır. `CloudProjects` ise `OrganizationId`'ye sahiptir. Doğrudan `OrganizationId` FK'si bulunması:
  - 3NF geçişli bağımlılık ihlalidir (`BackupId -> ProjectId -> OrganizationId`).
  - Proje başka bir organizasyona devredildiğinde yedeklerin eski organizasyonda kalmasına (güncelleme anomalisi) yol açıyordu.
  - Kod tabanında bu tablolara ait hiçbir `.Where(b => b.OrganizationId == ...)` sorgusu bulunmamaktadır; yetkilendirme `ITenantProjectAuthority` üzerinden `ProjectId` ile yapılmaktadır.

---

## 3. PostgreSQL Veri Göç Prosedürü (`Expand -> Backfill -> Verify -> Contract`)

Aşağıdaki işlem sırası `Phase14_2A_SecurityMigrationSql` sınıfı tarafından üretilir ve gerçek PostgreSQL 17 konteynerinde test edilmiştir:

```sql
-- AŞAMA 1: EXPAND (Genişletme)
CREATE SCHEMA IF NOT EXISTS studio;

CREATE TABLE IF NOT EXISTS studio.project_connections (
    "ProjectId" character varying(64) NOT NULL,
    "ConnectionDbType" character varying(32) NOT NULL DEFAULT 'PostgreSQL',
    "EncryptedConnectionString" text NOT NULL,
    "AllowDeskSql" boolean NOT NULL DEFAULT false,
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    CONSTRAINT "PK_project_connections" PRIMARY KEY ("ProjectId")
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_project_connections_CloudProjects_ProjectId') THEN
        ALTER TABLE studio.project_connections
        ADD CONSTRAINT "FK_project_connections_CloudProjects_ProjectId"
        FOREIGN KEY ("ProjectId") REFERENCES "CloudProjects"("Id") ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_CloudProjects_ShareTokenHash"
    ON "CloudProjects" ("ShareTokenHash")
    WHERE "ShareTokenHash" IS NOT NULL;

-- AŞAMA 2: BACKFILL (Veri Aktarımı - İdempotent)
INSERT INTO studio.project_connections ("ProjectId", "ConnectionDbType", "EncryptedConnectionString", "AllowDeskSql", "UpdatedAt")
SELECT
    p."Id",
    COALESCE(NULLIF(p."ConnectionDbType", ''), p."DbType", 'PostgreSQL'),
    p."EncryptedConnectionString",
    p."AllowDeskSql",
    p."UpdatedAt"
FROM "CloudProjects" p
WHERE p."EncryptedConnectionString" IS NOT NULL AND p."EncryptedConnectionString" <> ''
ON CONFLICT ("ProjectId") DO UPDATE
SET "ConnectionDbType" = EXCLUDED."ConnectionDbType",
    "EncryptedConnectionString" = EXCLUDED."EncryptedConnectionString",
    "AllowDeskSql" = EXCLUDED."AllowDeskSql",
    "UpdatedAt" = EXCLUDED."UpdatedAt";

UPDATE "CloudProjects"
SET "ShareTokenHash" = encode(sha256(convert_to("ShareToken", 'UTF8')), 'hex')
WHERE "ShareToken" IS NOT NULL
  AND "ShareToken" <> ''
  AND ("ShareTokenHash" IS NULL OR "ShareTokenHash" = '');

-- AŞAMA 3: VERIFY (İşlem İçi Doğrulama)
DO $$
DECLARE
    v_expected_connections bigint;
    v_actual_connections bigint;
    v_missing_token_hashes bigint;
BEGIN
    SELECT COUNT(*) INTO v_expected_connections
    FROM "CloudProjects"
    WHERE "EncryptedConnectionString" IS NOT NULL AND "EncryptedConnectionString" <> '';

    SELECT COUNT(*) INTO v_actual_connections
    FROM studio.project_connections;

    IF v_actual_connections < v_expected_connections THEN
        RAISE EXCEPTION 'Phase 14.2A Dogrulama Hatasi: studio.project_connections satiri eksik! Beklenen: %, Gerceklesen: %',
            v_expected_connections, v_actual_connections;
    END IF;

    SELECT COUNT(*) INTO v_missing_token_hashes
    FROM "CloudProjects"
    WHERE "ShareToken" IS NOT NULL
      AND "ShareToken" <> ''
      AND ("ShareTokenHash" IS NULL OR "ShareTokenHash" = '');

    IF v_missing_token_hashes > 0 THEN
        RAISE EXCEPTION 'Phase 14.2A Dogrulama Hatasi: % adet ShareToken hashlenemedi!', v_missing_token_hashes;
    END IF;
END $$;

-- AŞAMA 4: CONTRACT (Sözleşme Daraltma)
UPDATE "CloudProjects"
SET "ShareToken" = NULL
WHERE "ShareToken" IS NOT NULL;

ALTER TABLE IF EXISTS namines_vault.vault_backups
    DROP CONSTRAINT IF EXISTS "FK_vault_backups_Organizations_OrganizationId";

ALTER TABLE IF EXISTS namines_vault.vault_restores
    DROP CONSTRAINT IF EXISTS "FK_vault_restores_Organizations_OrganizationId";

ALTER TABLE IF EXISTS namines_vault.vault_schedules
    DROP CONSTRAINT IF EXISTS "FK_vault_schedules_Organizations_OrganizationId";

ALTER TABLE IF EXISTS ground.ground_databases
    DROP CONSTRAINT IF EXISTS "FK_ground_databases_Organizations_OrganizationId";
```

---

## 4. Test Komutları ve Doğrulama Sonuçları

### 4.1. Çalıştırılan Testler

| Test Komutu | Test Edilen Bileşen | Sonuç | Süre |
|---|---|---|---|
| `dotnet test --filter "FullyQualifiedName~Phase14_2A_SecurityAndMigrationTests"` | AES-GCM şifreleme, redaction, kiracı izolasyonu, ShareToken SHA-256 akışı ve **Gerçek PostgreSQL Konteynerinde Expand/Backfill/Verify/Contract/Idempotency** | **5 / 5 BAŞARILI (%100)** | 7 sn |
| `dotnet test --filter "FullyQualifiedName~ModularMonolithArchitectureTests"` | Modüler monolit mimari sınırları ve 27 DbSet kuralı | **13 / 13 BAŞARILI (%100)** | 2 sn |
| `dotnet test --filter "FullyQualifiedName~KeyRotationTests\|SecurityHardeningTests\|ShareTokenHashTests"` | Anahtar rotasyonu, PII/sır redaction, ShareToken geriye dönük hash testleri | **29 / 29 BAŞARILI (%100)** | 1 sn |
| `dotnet build backend/Namines.sln` | Tüm çözümün derlenmesi (12 Proje) | **0 Hata, 0 Uyarı** | 18 sn |

---

## 5. Güvenlik İyileştirmeleri ve Kazanımlar

1. **Kiracı İzolasyonu (`ResolveForUserAsync`):** Bağlantı sırrını çözmek isteyen herhangi bir kod, kullanıcı kimliğini sunmak zorundadır. Kullanıcının o projeye erişim hakkı (`CanViewAsync`) yoksa sır asla çözülmez, `null` döner.
2. **Kriptografik Sır İzolasyonu:** Canlı veritabanı bağlantı dizesi AES-256-GCM ile kimlik doğrulamalı olarak şifrelenir; `[JsonIgnore]` ile işaretlendiği için denetim loglarına, API yanıtlarına veya telemetriye sızması engellenmiştir.
3. **Düz Metin Jeton Kalıntısının Yok Edilmesi:** `ShareToken` artık veritabanında düz metin olarak yaşamaz. Veritabanı ele geçirilse dahi saldırgan şemaları görüntüleyebileceği geçerli URL jetonlarını elde edemez.
4. **Kilitlenme ve Anomali Engelleme:** Organizasyon seviyesindeki silme işlemlerinde, transitif `OrganizationId` kısıtları nedeniyle oluşan `RESTRICT` kilitlenmeleri çözülmüştür.

---

## 6. Kalan Riskler ve Rollback Talimatları

### Kalan Riskler:
- `CloudProjects` tablosunda `EncryptedConnectionString` sütunu şimdilik geriye dönük uyumluluk (dual-read fallback) için tutulmaktadır. Phase 14.2B'de bu sütun tamamen `DROP COLUMN` yapılabilir.

### Geri Alma (Rollback) Talimatları:
Eğer üretim ortamında 14.2A adımlarını geri almak gerekirse:
```sql
-- 1. studio.project_connections tablosunu kaldır
DROP TABLE IF EXISTS studio.project_connections;

-- 2. Gerekirse Organizations FK kısıtları yeniden eklenebilir
ALTER TABLE IF EXISTS namines_vault.vault_backups
    ADD CONSTRAINT "FK_vault_backups_Organizations_OrganizationId"
    FOREIGN KEY ("OrganizationId") REFERENCES "Organizations"("Id") ON DELETE RESTRICT;
```
*(Not: Kod katmanı `CloudProjects.EncryptedConnectionString` alanına da senkron yazdığı için veritabanında hiçbir veri kaybı yaşanmaz).*

---

## 7. Sonuç

Phase 14.2A kapsamındaki tüm hedefler eksiksiz olarak yerine getirilmiş, test edilmiş ve gerçek PostgreSQL motoru üzerinde kanıtlanmıştır. Kural gereği commit atılmamış ve Phase 14.2B'ye geçilmeden önce bağımsız inceleme için durulmuştur.
