using System;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>
/// Phase 14.2B: CloudProjects God Entity Decomposition SQL Üreteci.
/// 
/// 5 Aşamalı Güvenli Veri Taşıma Stratejisi:
/// 1. Expand: <c>studio.projects</c>, <c>studio.project_shares</c>, <c>hub.repositories</c>, <c>hub.project_tags</c> tablolarını oluşturur.
/// 2. Backfill: <c>CloudProjects</c> verilerini yeni 3NF ve modül tablolarına taşır; CSV etiketleri ilişkisel tablolara ayıklar.
/// 3. Verify: Transaction içinde kaynak ve hedef satır sayılarını doğrular; uyuşmazlıkta <c>RAISE EXCEPTION</c> ile geri alır.
/// 4. Cutover: Uygulama servisleri yeni tablolara yönelir.
/// 5. Contract: Eski <c>CloudProjects</c> üzerindeki gereksiz kolonları null yapar ve eski kısıtları temizler.
/// </summary>
public static class Phase14_2B_DecompositionSql
{
    /// <summary>
    /// Aşama 1 (Expand): studio ve hub şemalarını ve yeni hedef tabloları oluşturur.
    /// Var olan çalışan uygulamayı bozmaz.
    /// </summary>
    public static string BuildExpandSql() => """
        CREATE SCHEMA IF NOT EXISTS studio;
        CREATE SCHEMA IF NOT EXISTS hub;

        -- 1. studio.projects (Core Canvas & Project Lifecycle)
        CREATE TABLE IF NOT EXISTS studio.projects (
            "Id" character varying(64) NOT NULL,
            "Name" character varying(512) NOT NULL,
            "DbType" character varying(32) NOT NULL,
            "SchemaJson" text NOT NULL,
            "NodePositionsJson" text NOT NULL,
            "UserId" character varying(64) NOT NULL,
            "OrganizationId" character varying(64) NULL,
            "AutoApproveSafeChanges" boolean NOT NULL DEFAULT false,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            CONSTRAINT "PK_projects" PRIMARY KEY ("Id")
        );

        CREATE INDEX IF NOT EXISTS "IX_projects_UserId" ON studio.projects ("UserId");
        CREATE INDEX IF NOT EXISTS "IX_projects_OrganizationId" ON studio.projects ("OrganizationId");

        -- 2. studio.project_connections (Data Plane Live Database Connection Secrets)
        CREATE TABLE IF NOT EXISTS studio.project_connections (
            "ProjectId" character varying(64) NOT NULL,
            "ConnectionDbType" character varying(32) NOT NULL DEFAULT 'PostgreSQL',
            "EncryptedConnectionString" text NOT NULL,
            "AllowDeskSql" boolean NOT NULL DEFAULT false,
            "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            CONSTRAINT "PK_project_connections" PRIMARY KEY ("ProjectId")
        );

        -- 3. studio.project_shares (Anonymous Read-Only Share Token Hashes)
        CREATE TABLE IF NOT EXISTS studio.project_shares (
            "ProjectId" character varying(64) NOT NULL,
            "ShareTokenHash" character varying(64) NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            CONSTRAINT "PK_project_shares" PRIMARY KEY ("ProjectId"),
            CONSTRAINT "FK_project_shares_projects_ProjectId"
                FOREIGN KEY ("ProjectId") REFERENCES studio.projects ("Id") ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS "UX_project_shares_ShareTokenHash"
            ON studio.project_shares ("ShareTokenHash");

        -- 4. hub.repositories (Architecture Showcase, Vitrin & Community Metrics)
        CREATE TABLE IF NOT EXISTS hub.repositories (
            "ProjectId" character varying(64) NOT NULL,
            "Slug" character varying(128) NOT NULL,
            "Description" character varying(1000) NULL,
            "Visibility" integer NOT NULL DEFAULT 0,
            "Category" character varying(64) NOT NULL DEFAULT 'General',
            "StarCount" integer NOT NULL DEFAULT 0,
            "ForkCount" integer NOT NULL DEFAULT 0,
            "ForkedFromProjectId" character varying(64) NULL,
            "LinkedGithubRepo" character varying(140) NULL,
            "PublishedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            CONSTRAINT "PK_repositories" PRIMARY KEY ("ProjectId"),
            CONSTRAINT "CK_repositories_StarCount" CHECK ("StarCount" >= 0),
            CONSTRAINT "CK_repositories_ForkCount" CHECK ("ForkCount" >= 0)
        );

        CREATE INDEX IF NOT EXISTS "IX_repositories_Slug" ON hub.repositories ("Slug");
        CREATE INDEX IF NOT EXISTS "IX_repositories_Explore"
            ON hub.repositories ("StarCount" DESC, "PublishedAt" DESC)
            WHERE "Visibility" = 2;

        -- 5. hub.project_tags (Normalized 1:N B-Tree Indexed Search Tags)
        CREATE TABLE IF NOT EXISTS hub.project_tags (
            "ProjectId" character varying(64) NOT NULL,
            "Tag" character varying(50) NOT NULL,
            CONSTRAINT "PK_project_tags" PRIMARY KEY ("ProjectId", "Tag"),
            CONSTRAINT "FK_project_tags_repositories_ProjectId"
                FOREIGN KEY ("ProjectId") REFERENCES hub.repositories ("ProjectId") ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS "IX_project_tags_Tag" ON hub.project_tags ("Tag");
        """;

    /// <summary>
    /// Aşama 2 (Backfill): <c>CloudProjects</c> verilerini yeni tablolara idempotent şekilde aktarır.
    /// </summary>
    public static string BuildBackfillSql() => """
        -- 1. studio.projects aktarımı
        INSERT INTO studio.projects ("Id", "Name", "DbType", "SchemaJson", "NodePositionsJson", "UserId", "OrganizationId", "AutoApproveSafeChanges", "CreatedAt", "UpdatedAt")
        SELECT
            p."Id",
            p."Name",
            p."DbType",
            p."SchemaJson",
            p."NodePositionsJson",
            p."UserId",
            p."OrganizationId",
            p."AutoApproveSafeChanges",
            p."CreatedAt",
            p."UpdatedAt"
        FROM "CloudProjects" p
        ON CONFLICT ("Id") DO UPDATE
        SET "Name" = EXCLUDED."Name",
            "DbType" = EXCLUDED."DbType",
            "SchemaJson" = EXCLUDED."SchemaJson",
            "NodePositionsJson" = EXCLUDED."NodePositionsJson",
            "UserId" = EXCLUDED."UserId",
            "OrganizationId" = EXCLUDED."OrganizationId",
            "AutoApproveSafeChanges" = EXCLUDED."AutoApproveSafeChanges",
            "UpdatedAt" = EXCLUDED."UpdatedAt";

        -- 2. studio.project_connections aktarımı
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

        -- 3. studio.project_shares aktarımı (Yalnızca SHA-256 hash'lenmiş jetonlar)
        INSERT INTO studio.project_shares ("ProjectId", "ShareTokenHash", "CreatedAt", "UpdatedAt")
        SELECT
            p."Id",
            COALESCE(NULLIF(p."ShareTokenHash", ''), encode(sha256(convert_to(p."ShareToken", 'UTF8')), 'hex')),
            p."CreatedAt",
            p."UpdatedAt"
        FROM "CloudProjects" p
        WHERE (p."ShareTokenHash" IS NOT NULL AND p."ShareTokenHash" <> '')
           OR (p."ShareToken" IS NOT NULL AND p."ShareToken" <> '')
        ON CONFLICT ("ProjectId") DO UPDATE
        SET "ShareTokenHash" = EXCLUDED."ShareTokenHash",
            "UpdatedAt" = EXCLUDED."UpdatedAt";

        -- 4. hub.repositories aktarımı
        INSERT INTO hub.repositories ("ProjectId", "Slug", "Description", "Visibility", "Category", "StarCount", "ForkCount", "ForkedFromProjectId", "LinkedGithubRepo", "PublishedAt", "UpdatedAt")
        SELECT
            p."Id",
            COALESCE(NULLIF(p."Slug", ''), p."Id"),
            p."Description",
            p."Visibility",
            COALESCE(NULLIF(p."Category", ''), 'General'),
            p."StarCount",
            p."ForkCount",
            p."ForkedFromProjectId",
            p."LinkedGithubRepo",
            p."CreatedAt",
            p."UpdatedAt"
        FROM "CloudProjects" p
        ON CONFLICT ("ProjectId") DO UPDATE
        SET "Slug" = EXCLUDED."Slug",
            "Description" = EXCLUDED."Description",
            "Visibility" = EXCLUDED."Visibility",
            "Category" = EXCLUDED."Category",
            "StarCount" = EXCLUDED."StarCount",
            "ForkCount" = EXCLUDED."ForkCount",
            "ForkedFromProjectId" = EXCLUDED."ForkedFromProjectId",
            "LinkedGithubRepo" = EXCLUDED."LinkedGithubRepo",
            "UpdatedAt" = EXCLUDED."UpdatedAt";

        -- 5. hub.project_tags aktarımı (CSV string -> ilişkisel satırlar)
        INSERT INTO hub.project_tags ("ProjectId", "Tag")
        SELECT
            p."Id",
            trim(t.tag)
        FROM "CloudProjects" p,
             unnest(string_to_array(p."Tags", ',')) AS t(tag)
        WHERE trim(t.tag) <> ''
        ON CONFLICT ("ProjectId", "Tag") DO NOTHING;
        """;

    /// <summary>
    /// Aşama 3 (Verify): İşlem içinde kaynak ve hedef satır sayılarını doğrular.
    /// Uyuşmazlık durumunda <c>RAISE EXCEPTION</c> fırlatarak tüm işlemi geri alır (ROLLBACK).
    /// </summary>
    public static string BuildVerifySql() => """
        DO $$
        DECLARE
            v_expected_projects bigint;
            v_actual_projects bigint;
            v_expected_connections bigint;
            v_actual_connections bigint;
            v_expected_shares bigint;
            v_actual_shares bigint;
            v_expected_repos bigint;
            v_actual_repos bigint;
        BEGIN
            -- 1. studio.projects doğrulaması
            SELECT COUNT(*) INTO v_expected_projects FROM "CloudProjects";
            SELECT COUNT(*) INTO v_actual_projects FROM studio.projects;
            IF v_actual_projects < v_expected_projects THEN
                RAISE EXCEPTION 'Phase 14.2B Dogrulama Hatasi: studio.projects eksik! Beklenen: %, Gerceklesen: %',
                    v_expected_projects, v_actual_projects;
            END IF;

            -- 2. studio.project_connections doğrulaması
            SELECT COUNT(*) INTO v_expected_connections
            FROM "CloudProjects"
            WHERE "EncryptedConnectionString" IS NOT NULL AND "EncryptedConnectionString" <> '';
            SELECT COUNT(*) INTO v_actual_connections FROM studio.project_connections;
            IF v_actual_connections < v_expected_connections THEN
                RAISE EXCEPTION 'Phase 14.2B Dogrulama Hatasi: studio.project_connections eksik! Beklenen: %, Gerceklesen: %',
                    v_expected_connections, v_actual_connections;
            END IF;

            -- 3. studio.project_shares doğrulaması
            SELECT COUNT(*) INTO v_expected_shares
            FROM "CloudProjects"
            WHERE ("ShareTokenHash" IS NOT NULL AND "ShareTokenHash" <> '')
               OR ("ShareToken" IS NOT NULL AND "ShareToken" <> '');
            SELECT COUNT(*) INTO v_actual_shares FROM studio.project_shares;
            IF v_actual_shares < v_expected_shares THEN
                RAISE EXCEPTION 'Phase 14.2B Dogrulama Hatasi: studio.project_shares eksik! Beklenen: %, Gerceklesen: %',
                    v_expected_shares, v_actual_shares;
            END IF;

            -- 4. hub.repositories doğrulaması
            SELECT COUNT(*) INTO v_expected_repos FROM "CloudProjects";
            SELECT COUNT(*) INTO v_actual_repos FROM hub.repositories;
            IF v_actual_repos < v_expected_repos THEN
                RAISE EXCEPTION 'Phase 14.2B Dogrulama Hatasi: hub.repositories eksik! Beklenen: %, Gerceklesen: %',
                    v_expected_repos, v_actual_repos;
            END IF;
        END $$;
        """;

    /// <summary>
    /// Aşama 5 (Contract): Eski CloudProjects tablosundaki gereksiz kolonları temizler ve eski kısıtları düşürür.
    /// </summary>
    public static string BuildContractSql() => """
        -- 1. Taşınmış alanları CloudProjects üzerinde sıfırla
        UPDATE "CloudProjects"
        SET "EncryptedConnectionString" = NULL,
            "ConnectionDbType" = NULL,
            "AllowDeskSql" = false,
            "ShareToken" = NULL,
            "ShareTokenHash" = NULL,
            "Tags" = NULL,
            "Slug" = NULL,
            "Description" = NULL,
            "LinkedGithubRepo" = NULL;

        -- 2. Eski kolonlara bağlı gereksiz kısıtları ve indeksleri düşür
        ALTER TABLE IF EXISTS "CloudProjects"
            DROP CONSTRAINT IF EXISTS "CK_CloudProjects_StarCount",
            DROP CONSTRAINT IF EXISTS "CK_CloudProjects_ForkCount",
            DROP CONSTRAINT IF EXISTS "CK_CloudProjects_Description_Length",
            DROP CONSTRAINT IF EXISTS "CK_CloudProjects_Category_Length",
            DROP CONSTRAINT IF EXISTS "CK_CloudProjects_Tags_Length",
            DROP CONSTRAINT IF EXISTS "CK_CloudProjects_LinkedGithubRepo_Length";

        DROP INDEX IF EXISTS "IX_CloudProjects_Explore_Public";
        DROP INDEX IF EXISTS "IX_CloudProjects_ShareTokenHash";
        DROP INDEX IF EXISTS "IX_CloudProjects_Slug";
        """;

    /// <summary>
    /// Geri Alma (Rollback): Beklenmedik durumda yeni tabloları temizler.
    /// </summary>
    public static string BuildRollbackSql() => """
        DROP TABLE IF EXISTS hub.project_tags;
        DROP TABLE IF EXISTS hub.repositories;
        DROP TABLE IF EXISTS studio.project_shares;
        DROP TABLE IF EXISTS studio.projects;
        """;
}
