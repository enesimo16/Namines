using System.Collections.Generic;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>
/// 3NF (Üçüncü Normal Form) ve 11 PostgreSQL şemasına geçiş için
/// <c>Expand -&gt; Backfill -&gt; Verify -&gt; Contract</c> aşamalı, işlem içi (transactional)
/// ve <c>DO $$ ... RAISE EXCEPTION</c> satır sayısı doğrulamalı SQL üreteci (Kritik Kontrol #2 ve #3).
/// </summary>
public static class ThreeNfAndSchemaNormalizationSql
{
    public static readonly IReadOnlyList<string> AllModuleSchemas = new[]
    {
        "identity",
        "studio",
        "billing",
        "governance",
        "ground",
        "namines_vault",
        "flow",
        "desk",
        "hub",
        "gateway",
        "ops"
    };

    /// <summary>
    /// Aşama 1 (Expand): 11 modül şemasını ve 4 yeni 3NF tablosunu oluşturur.
    /// </summary>
    public static string BuildExpandSchemasAndTablesSql() => """
        CREATE SCHEMA IF NOT EXISTS identity;
        CREATE SCHEMA IF NOT EXISTS studio;
        CREATE SCHEMA IF NOT EXISTS billing;
        CREATE SCHEMA IF NOT EXISTS governance;
        CREATE SCHEMA IF NOT EXISTS ground;
        CREATE SCHEMA IF NOT EXISTS namines_vault;
        CREATE SCHEMA IF NOT EXISTS flow;
        CREATE SCHEMA IF NOT EXISTS desk;
        CREATE SCHEMA IF NOT EXISTS hub;
        CREATE SCHEMA IF NOT EXISTS gateway;
        CREATE SCHEMA IF NOT EXISTS ops;

        CREATE TABLE IF NOT EXISTS studio.project_connections (
            "ProjectId" character varying(64) NOT NULL,
            "ConnectionDbType" character varying(32) NOT NULL DEFAULT 'PostgreSQL',
            "EncryptedConnectionString" text NOT NULL,
            "AllowDeskSql" boolean NOT NULL DEFAULT false,
            "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            CONSTRAINT "PK_project_connections" PRIMARY KEY ("ProjectId")
        );

        CREATE TABLE IF NOT EXISTS studio.project_git_repositories (
            "ProjectId" character varying(64) NOT NULL,
            "RepositoryFullName" character varying(140) NOT NULL,
            "DefaultBranch" character varying(128) NOT NULL DEFAULT 'main',
            "InstallationId" bigint NULL,
            "LinkedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            CONSTRAINT "PK_project_git_repositories" PRIMARY KEY ("ProjectId")
        );

        CREATE INDEX IF NOT EXISTS "IX_project_git_repositories_RepositoryFullName"
            ON studio.project_git_repositories ("RepositoryFullName");

        CREATE TABLE IF NOT EXISTS hub.project_publications (
            "ProjectId" character varying(64) NOT NULL,
            "Slug" character varying(128) NOT NULL,
            "Description" character varying(1000) NULL,
            "Category" character varying(64) NULL,
            "Tags" character varying(256) NULL,
            "StarCount" integer NOT NULL DEFAULT 0,
            "ForkCount" integer NOT NULL DEFAULT 0,
            "ForkedFromProjectId" character varying(64) NULL,
            "PublishedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            CONSTRAINT "PK_project_publications" PRIMARY KEY ("ProjectId"),
            CONSTRAINT "CK_project_publications_StarCount" CHECK ("StarCount" >= 0),
            CONSTRAINT "CK_project_publications_ForkCount" CHECK ("ForkCount" >= 0)
        );

        CREATE INDEX IF NOT EXISTS "IX_project_publications_Slug"
            ON hub.project_publications ("Slug");

        CREATE TABLE IF NOT EXISTS studio.schema_share_links (
            "Id" character varying(64) NOT NULL DEFAULT (gen_random_uuid()::text),
            "ProjectId" character varying(64) NOT NULL,
            "TokenHash" character varying(64) NOT NULL,
            "AccessMode" integer NOT NULL DEFAULT 1,
            "ExpiresAt" timestamp with time zone NULL,
            "RevokedAt" timestamp with time zone NULL,
            "CreatedByUserId" character varying(64) NULL,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
            CONSTRAINT "PK_schema_share_links" PRIMARY KEY ("Id")
        );

        CREATE UNIQUE INDEX IF NOT EXISTS "UX_schema_share_links_TokenHash"
            ON studio.schema_share_links ("TokenHash");
        CREATE INDEX IF NOT EXISTS "IX_schema_share_links_ProjectId"
            ON studio.schema_share_links ("ProjectId");
        """;

    /// <summary>
    /// Aşama 2 &amp; 3 (Backfill + Verify): <c>CloudProjects</c> içindeki kısmi/geçişli bağımlılıkları
    /// 3NF tablolarına taşır ve işlem içinde (transactional) satır sayılarını doğrular.
    /// Sayı uyuşmazlığı durumunda <c>RAISE EXCEPTION</c> ile işlemi geri alır (sıfır veri kaybı).
    /// </summary>
    public static string BuildBackfillAndVerify3NfSql() => """
        -- 1. Backfill studio.project_connections
        INSERT INTO studio.project_connections ("ProjectId", "ConnectionDbType", "EncryptedConnectionString", "AllowDeskSql", "UpdatedAt")
        SELECT p."Id", COALESCE(NULLIF(p."ConnectionDbType", ''), p."DbType", 'PostgreSQL'), p."EncryptedConnectionString", p."AllowDeskSql", p."UpdatedAt"
        FROM "CloudProjects" p
        WHERE p."EncryptedConnectionString" IS NOT NULL AND p."EncryptedConnectionString" <> ''
        ON CONFLICT ("ProjectId") DO UPDATE
        SET "ConnectionDbType" = EXCLUDED."ConnectionDbType",
            "EncryptedConnectionString" = EXCLUDED."EncryptedConnectionString",
            "AllowDeskSql" = EXCLUDED."AllowDeskSql",
            "UpdatedAt" = EXCLUDED."UpdatedAt";

        -- 2. Backfill studio.project_git_repositories
        INSERT INTO studio.project_git_repositories ("ProjectId", "RepositoryFullName", "DefaultBranch", "LinkedAt")
        SELECT p."Id", p."LinkedGithubRepo", 'main', p."UpdatedAt"
        FROM "CloudProjects" p
        WHERE p."LinkedGithubRepo" IS NOT NULL AND p."LinkedGithubRepo" <> ''
        ON CONFLICT ("ProjectId") DO UPDATE
        SET "RepositoryFullName" = EXCLUDED."RepositoryFullName";

        -- 3. Backfill hub.project_publications
        INSERT INTO hub.project_publications ("ProjectId", "Slug", "Description", "Category", "Tags", "StarCount", "ForkCount", "ForkedFromProjectId", "PublishedAt")
        SELECT p."Id", p."Slug", p."Description", p."Category", p."Tags", p."StarCount", p."ForkCount", p."ForkedFromProjectId", p."CreatedAt"
        FROM "CloudProjects" p
        WHERE p."Slug" IS NOT NULL AND p."Slug" <> ''
        ON CONFLICT ("ProjectId") DO UPDATE
        SET "Slug" = EXCLUDED."Slug",
            "Description" = EXCLUDED."Description",
            "Category" = EXCLUDED."Category",
            "Tags" = EXCLUDED."Tags",
            "StarCount" = EXCLUDED."StarCount",
            "ForkCount" = EXCLUDED."ForkCount",
            "ForkedFromProjectId" = EXCLUDED."ForkedFromProjectId";

        -- 4. Backfill studio.schema_share_links (ShareTokenHash veya SHA-256(ShareToken), düz metin token ASLA saklanmaz)
        INSERT INTO studio.schema_share_links ("Id", "ProjectId", "TokenHash", "CreatedByUserId", "CreatedAt")
        SELECT
            gen_random_uuid()::text,
            p."Id",
            COALESCE(NULLIF(p."ShareTokenHash", ''), encode(sha256(convert_to(p."ShareToken", 'UTF8')), 'hex')),
            p."UserId",
            p."UpdatedAt"
        FROM "CloudProjects" p
        WHERE (p."ShareTokenHash" IS NOT NULL AND p."ShareTokenHash" <> '')
           OR (p."ShareToken" IS NOT NULL AND p."ShareToken" <> '')
        ON CONFLICT ("TokenHash") DO NOTHING;

        -- 5. Transactional Verification (Kritik Kontrol #2: satır sayısı uyuşmazlığında RAISE EXCEPTION ile ROLLBACK)
        DO $$
        DECLARE
            v_expected_conn bigint;
            v_actual_conn bigint;
            v_expected_git bigint;
            v_actual_git bigint;
            v_expected_pub bigint;
            v_actual_pub bigint;
            v_expected_share bigint;
            v_actual_share bigint;
        BEGIN
            SELECT COUNT(*) INTO v_expected_conn FROM "CloudProjects" WHERE "EncryptedConnectionString" IS NOT NULL AND "EncryptedConnectionString" <> '';
            SELECT COUNT(*) INTO v_actual_conn FROM studio.project_connections;
            IF v_actual_conn < v_expected_conn THEN
                RAISE EXCEPTION '3NF Migration verification failed for studio.project_connections: expected %, actual %', v_expected_conn, v_actual_conn;
            END IF;

            SELECT COUNT(*) INTO v_expected_git FROM "CloudProjects" WHERE "LinkedGithubRepo" IS NOT NULL AND "LinkedGithubRepo" <> '';
            SELECT COUNT(*) INTO v_actual_git FROM studio.project_git_repositories;
            IF v_actual_git < v_expected_git THEN
                RAISE EXCEPTION '3NF Migration verification failed for studio.project_git_repositories: expected %, actual %', v_expected_git, v_actual_git;
            END IF;

            SELECT COUNT(*) INTO v_expected_pub FROM "CloudProjects" WHERE "Slug" IS NOT NULL AND "Slug" <> '';
            SELECT COUNT(*) INTO v_actual_pub FROM hub.project_publications;
            IF v_actual_pub < v_expected_pub THEN
                RAISE EXCEPTION '3NF Migration verification failed for hub.project_publications: expected %, actual %', v_expected_pub, v_actual_pub;
            END IF;

            SELECT COUNT(*) INTO v_expected_share FROM "CloudProjects"
            WHERE ("ShareTokenHash" IS NOT NULL AND "ShareTokenHash" <> '')
               OR ("ShareToken" IS NOT NULL AND "ShareToken" <> '');
            SELECT COUNT(*) INTO v_actual_share FROM studio.schema_share_links;
            IF v_actual_share < v_expected_share THEN
                RAISE EXCEPTION '3NF Migration verification failed for studio.schema_share_links: expected %, actual %', v_expected_share, v_actual_share;
            END IF;
        END $$;
        """;
}
