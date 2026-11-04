using System;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>
/// Phase 14.2A: Kritik Veri Güvenliği ve Sahiplik Normalizasyonu için
/// Expand -&gt; Backfill -&gt; Verify -&gt; Contract aşamalı SQL üreteci.
///
/// Kapsam:
/// 1. studio.project_connections (Şifreli bağlantı sırlarının CloudProjects'ten 3NF tablosuna ayrılması)
/// 2. Plaintext ShareToken göçü ve temizliği (Yalnızca SHA-256 ShareTokenHash kalır)
/// 3. Vault ve Ground tablolarındaki transitif/mükerrer OrganizationId kısıtlarının kaldırılması
/// </summary>
public static class Phase14_2A_SecurityMigrationSql
{
    /// <summary>
    /// Aşama 1 (Expand): studio şemasını ve studio.project_connections tablosunu oluşturur.
    /// Var olan çalışan uygulamayı bozmaz (mevcut tablolar ve kolonlar yerinde kalır).
    /// </summary>
    public static string BuildExpandSql() => """
        CREATE SCHEMA IF NOT EXISTS studio;

        CREATE TABLE IF NOT EXISTS studio.project_connections (
            "ProjectId" character varying(64) NOT NULL,
            "ConnectionDbType" character varying(32) NOT NULL DEFAULT 'PostgreSQL',
            "EncryptedConnectionString" text NOT NULL,
            "AllowDeskSql" boolean NOT NULL DEFAULT false,
            "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            CONSTRAINT "PK_project_connections" PRIMARY KEY ("ProjectId")
        );

        -- Referans bütünlüğü: Proje silindiğinde bağlantı sırrı da güvenle cascade silinir
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'FK_project_connections_CloudProjects_ProjectId'
            ) THEN
                ALTER TABLE studio.project_connections
                ADD CONSTRAINT "FK_project_connections_CloudProjects_ProjectId"
                FOREIGN KEY ("ProjectId") REFERENCES "CloudProjects"("Id") ON DELETE CASCADE;
            END IF;
        END $$;

        -- ShareTokenHash indeksinin varlığını garantiye al
        CREATE INDEX IF NOT EXISTS "IX_CloudProjects_ShareTokenHash"
            ON "CloudProjects" ("ShareTokenHash")
            WHERE "ShareTokenHash" IS NOT NULL;
        """;

    /// <summary>
    /// Aşama 2 (Backfill): Mevcut verileri yeni 3NF tablosuna aktarır ve düz metin jetonları özetler.
    /// İdempotenttir: Tekrar çalıştırıldığında veri çoğaltmaz (ON CONFLICT DO UPDATE).
    /// </summary>
    public static string BuildBackfillSql() => """
        -- 1. studio.project_connections aktarımı
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

        -- 2. Düz metin ShareToken değerlerinin ShareTokenHash özetine dönüştürülmesi
        UPDATE "CloudProjects"
        SET "ShareTokenHash" = encode(sha256(convert_to("ShareToken", 'UTF8')), 'hex')
        WHERE "ShareToken" IS NOT NULL
          AND "ShareToken" <> ''
          AND ("ShareTokenHash" IS NULL OR "ShareTokenHash" = '');
        """;

    /// <summary>
    /// Aşama 3 (Verify): İşlem içinde kaynak ve hedef satır sayılarını doğrular.
    /// Uyuşmazlık durumunda RAISE EXCEPTION fırlatarak tüm işlemi geri alır (ROLLBACK).
    /// </summary>
    public static string BuildVerifySql() => """
        DO $$
        DECLARE
            v_expected_connections bigint;
            v_actual_connections bigint;
            v_missing_token_hashes bigint;
        BEGIN
            -- 1. Bağlantı dizesi olan proje sayısı ile project_connections satır sayısı eşit olmalı
            SELECT COUNT(*) INTO v_expected_connections
            FROM "CloudProjects"
            WHERE "EncryptedConnectionString" IS NOT NULL AND "EncryptedConnectionString" <> '';

            SELECT COUNT(*) INTO v_actual_connections
            FROM studio.project_connections;

            IF v_actual_connections < v_expected_connections THEN
                RAISE EXCEPTION 'Phase 14.2A Dogrulama Hatasi: studio.project_connections satiri eksik! Beklenen: %, Gerceklesen: %',
                    v_expected_connections, v_actual_connections;
            END IF;

            -- 2. Düz metin ShareToken olup da ShareTokenHash'i doldurulmamış kayıt kalmamalı
            SELECT COUNT(*) INTO v_missing_token_hashes
            FROM "CloudProjects"
            WHERE "ShareToken" IS NOT NULL
              AND "ShareToken" <> ''
              AND ("ShareTokenHash" IS NULL OR "ShareTokenHash" = '');

            IF v_missing_token_hashes > 0 THEN
                RAISE EXCEPTION 'Phase 14.2A Dogrulama Hatasi: % adet ShareToken hashlenemedi!', v_missing_token_hashes;
            END IF;
        END $$;
        """;

    /// <summary>
    /// Aşama 4 (Contract): Düz metin jetonları veritabanından kalıcı olarak temizler ve
    /// Vault/Ground üzerindeki gereksiz/mükerrer OrganizationId yabancı anahtar kısıtlarını kaldırır.
    /// </summary>
    public static string BuildContractSql() => """
        -- 1. Düz metin ShareToken verisini sıfırla (Artık sadece SHA-256 ShareTokenHash saklanır)
        UPDATE "CloudProjects"
        SET "ShareToken" = NULL
        WHERE "ShareToken" IS NOT NULL;

        -- 2. Vault ve Ground tablolarındaki transitif OrganizationId FK kısıtlarını kaldır
        -- (Yetki ve sahiplik zinciri CloudProjects.OrganizationId üzerinden yürütülür)
        ALTER TABLE IF EXISTS namines_vault.vault_backups
            DROP CONSTRAINT IF EXISTS "FK_vault_backups_Organizations_OrganizationId";

        ALTER TABLE IF EXISTS namines_vault.vault_restores
            DROP CONSTRAINT IF EXISTS "FK_vault_restores_Organizations_OrganizationId";

        ALTER TABLE IF EXISTS namines_vault.vault_schedules
            DROP CONSTRAINT IF EXISTS "FK_vault_schedules_Organizations_OrganizationId";

        ALTER TABLE IF EXISTS ground.ground_databases
            DROP CONSTRAINT IF EXISTS "FK_ground_databases_Organizations_OrganizationId";
        """;

    /// <summary>
    /// Geri Alma (Rollback): Beklenmedik bir durumda 14.2A değişikliklerini geri alır.
    /// </summary>
    public static string BuildRollbackSql() => """
        DROP TABLE IF EXISTS studio.project_connections;

        -- Gerekirse FK kısıtları yeniden oluşturulabilir (uygulama CloudProject üzerinden yürüdüğü için kritik değildir)
        """;
}
