namespace Namines.Infrastructure.Migrations;

/// <summary>
/// <c>Phase14_2B_DecomposeCloudProjects</c> migration'ının SQL metinleri. Bu dosya DONDURULMUŞTUR:
/// migration geçmişte ne yaptıysa onu yapmaya devam etmeli, modele göre güncellenmez.
///
/// Yaklaşım kopya değil yerinde taşıma: yeni tablolar doldurulur, her alan kaynağıyla
/// karşılaştırılır, kaynak kolonlar fiziksel olarak düşer, tablo <c>studio.projects</c> olur.
/// Hepsi EF'in açtığı tek işlemde; doğrulama patlarsa hiçbir şey değişmemiş olur.
/// </summary>
public static class Phase14_2BDecompositionScripts
{
    // C# string.Trim() ile aynı boşluk kümesi: ProjectTagRules ile aynı sonucu vermek zorunda.
    private const string Space = @"[\s \u0085  -     　]";

    /// <summary>
    /// ProjectTagRules.Key'in SQL karşılığı. ICU varsa onunla (veritabanı C yerelinde olsa bile
    /// Türkçe harfler küçülür). .NET ToLowerInvariant harf harf eşler, ICU ise bağlam kullanır: harf
    /// harf küçültülmezse sondaki Yunanca Σ 'ς' olurdu, C#'ta 'σ'. 'İ' korunur çünkü .NET onu
    /// değiştirmiyor, ICU iki karaktere açardı.
    /// </summary>
    private static string TagKeyFunctionSql => $$"""
        DO $fn$
        DECLARE coll text := CASE WHEN EXISTS (SELECT 1 FROM pg_collation WHERE collname = 'und-x-icu')
                                  THEN '"und-x-icu"' ELSE '"default"' END;
        BEGIN
            EXECUTE format($f$
                CREATE FUNCTION pg_temp.namines_tag_trim(t text) RETURNS text LANGUAGE sql IMMUTABLE AS $b$
                    SELECT regexp_replace(t, '^{{Space}}+|{{Space}}+$', '', 'g')
                $b$;
                CREATE FUNCTION pg_temp.namines_tag_key(t text) RETURNS text LANGUAGE sql IMMUTABLE AS $b$
                    SELECT string_agg(CASE WHEN c = 'İ' THEN c ELSE lower(c COLLATE %s) END, '' ORDER BY o)
                    FROM regexp_split_to_table(t, '') WITH ORDINALITY AS x(c, o)
                $b$;
            $f$, coll);
        END $fn$;
        """;

    public static string Up => string.Join("\n", UpPrepare, UpVerify, UpContract);

    public static string Down => string.Join("\n", DownRestore, DownVerify, DownContract);

    public static string UpPrepare => $$"""
        -- Kaynak, doğrulama ile DROP arasında değişmesin (READ COMMITTED'da aksi hâlde kaybolur).
        LOCK TABLE "CloudProjects" IN ACCESS EXCLUSIVE MODE;

        -- Bağlanmamış eski üreteçlerin (ThreeNf/Phase14_2A/2B SQL) artıkları: prod'da hiç
        -- çalışmadı; bir yerde çalıştıysa şekli farklıdır ve sessizce üstüne yazmak veri kaybettirir.
        DO $guard$
        BEGIN
            IF to_regclass('studio.projects') IS NOT NULL
               OR to_regclass('studio.project_connections') IS NOT NULL
               OR to_regclass('studio.project_shares') IS NOT NULL
               OR to_regclass('hub.repositories') IS NOT NULL
               OR to_regclass('hub.project_tags') IS NOT NULL THEN
                RAISE EXCEPTION 'Phase14_2B: studio/hub tables already exist (left over from the unwired generator SQL). Inspect and remove them, then re-run; see docs/architecture/MIGRATION_PLAN.md.';
            END IF;
            -- CREATE SCHEMA IF NOT EXISTS, şema varken bile veritabanı CREATE yetkisi ister;
            -- roles.sql şemaları önceden açtıysa migrator'ın o yetkisi yok.
            IF to_regnamespace('studio') IS NULL THEN CREATE SCHEMA studio; END IF;
            IF to_regnamespace('hub') IS NULL THEN CREATE SCHEMA hub; END IF;
        END $guard$;

        CREATE TABLE studio.project_connections (
            "ProjectId" text NOT NULL,
            "EncryptedConnectionString" text NULL,
            "ConnectionDbType" text NULL,
            "AllowDeskSql" boolean NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            CONSTRAINT "PK_project_connections" PRIMARY KEY ("ProjectId"),
            CONSTRAINT "FK_project_connections_projects_ProjectId" FOREIGN KEY ("ProjectId")
                REFERENCES public."CloudProjects" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE studio.project_shares (
            "Id" text NOT NULL DEFAULT (gen_random_uuid()::text),
            "ProjectId" text NOT NULL,
            "ShareTokenHash" text NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "RevokedAt" timestamp with time zone NULL,
            CONSTRAINT "PK_project_shares" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_project_shares_projects_ProjectId" FOREIGN KEY ("ProjectId")
                REFERENCES public."CloudProjects" ("Id") ON DELETE CASCADE
        );

        CREATE TABLE hub.repositories (
            "ProjectId" text NOT NULL,
            "OwnerUserId" text NOT NULL,
            "Slug" text NULL,
            "Description" text NULL,
            "Visibility" integer NOT NULL,
            "Category" text NOT NULL,
            "StarCount" integer NOT NULL,
            "ForkCount" integer NOT NULL,
            "ForkedFromProjectId" text NULL,
            "LinkedGithubRepo" text NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            CONSTRAINT "PK_repositories" PRIMARY KEY ("ProjectId"),
            CONSTRAINT "FK_repositories_projects_ProjectId" FOREIGN KEY ("ProjectId")
                REFERENCES public."CloudProjects" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_repositories_repositories_ForkedFromProjectId" FOREIGN KEY ("ForkedFromProjectId")
                REFERENCES hub.repositories ("ProjectId") ON DELETE SET NULL
        );

        CREATE TABLE hub.project_tags (
            "ProjectId" text NOT NULL,
            "Tag" text NOT NULL,
            "DisplayTag" text NOT NULL,
            "Position" integer NOT NULL,
            CONSTRAINT "PK_project_tags" PRIMARY KEY ("ProjectId", "Tag"),
            CONSTRAINT "FK_project_tags_repositories_ProjectId" FOREIGN KEY ("ProjectId")
                REFERENCES hub.repositories ("ProjectId") ON DELETE CASCADE
        );

        -- ── Backfill ────────────────────────────────────────────────────────────
        -- Şifreli metin bayt bayt kopyalanır: AAD proje kimliği ve o değişmiyor, yeniden şifreleme gerekmez.
        -- SQL konsolu onayı veya motor bilgisi olan bağlantısız satırlar da taşınır (kayıpsız).
        INSERT INTO studio.project_connections
            ("ProjectId", "EncryptedConnectionString", "ConnectionDbType", "AllowDeskSql", "CreatedAt", "UpdatedAt")
        SELECT "Id", NULLIF("EncryptedConnectionString", ''), "ConnectionDbType", "AllowDeskSql", "CreatedAt", "UpdatedAt"
        FROM "CloudProjects"
        WHERE NULLIF("EncryptedConnectionString", '') IS NOT NULL
           OR "ConnectionDbType" IS NOT NULL
           OR "AllowDeskSql";

        INSERT INTO studio.project_shares ("ProjectId", "ShareTokenHash", "CreatedAt")
        SELECT "Id", "ShareTokenHash", "UpdatedAt"
        FROM "CloudProjects"
        WHERE NULLIF("ShareTokenHash", '') IS NOT NULL;

        -- Her projeye repo satırı. Kaynağı artık olmayan fork işaretçisi NULL olur (eski SET NULL
        -- davranışının sonucu; eski FK varken zaten oluşamaz).
        INSERT INTO hub.repositories
            ("ProjectId", "OwnerUserId", "Slug", "Description", "Visibility", "Category", "StarCount", "ForkCount",
             "ForkedFromProjectId", "LinkedGithubRepo", "CreatedAt", "UpdatedAt")
        SELECT p."Id", p."UserId", p."Slug", p."Description", p."Visibility", p."Category", p."StarCount", p."ForkCount",
               CASE WHEN src."Id" IS NOT NULL THEN p."ForkedFromProjectId" END,
               p."LinkedGithubRepo", p."CreatedAt", p."UpdatedAt"
        FROM "CloudProjects" p
        LEFT JOIN "CloudProjects" src ON src."Id" = p."ForkedFromProjectId";

        {{TagKeyFunctionSql}}

        -- Etiket: kırp, boşları at, büyük/küçük harf yinelemesinde ilk yazımı tut, sırayı koru.
        INSERT INTO hub.project_tags ("ProjectId", "Tag", "DisplayTag", "Position")
        SELECT pid, key, display, (row_number() OVER (PARTITION BY pid ORDER BY ord) - 1)::integer
        FROM (
            SELECT pid, key, display, ord,
                   row_number() OVER (PARTITION BY pid, key ORDER BY ord) AS first_seen
            FROM (
                SELECT p."Id" AS pid, u.ord,
                       pg_temp.namines_tag_trim(u.part) AS display,
                       pg_temp.namines_tag_key(pg_temp.namines_tag_trim(u.part)) AS key
                FROM "CloudProjects" p
                CROSS JOIN LATERAL unnest(string_to_array(p."Tags", ',')) WITH ORDINALITY AS u(part, ord)
                WHERE p."Tags" IS NOT NULL
            ) parsed
            WHERE display <> ''
        ) keyed
        WHERE first_seen = 1;
        """;

    /// <summary>Satır sayısı değil, alan alan. Patlarsa işlem geri alınır, kaynak yerinde kalır.</summary>
    public const string UpVerify = """
        DO $verify$
        DECLARE missing bigint; dup bigint; orphan bigint; diff bigint;
        BEGIN
            -- Bağlantılar
            SELECT count(*) INTO missing FROM "CloudProjects" p
            LEFT JOIN studio.project_connections c ON c."ProjectId" = p."Id"
            WHERE c."ProjectId" IS NULL
              AND (NULLIF(p."EncryptedConnectionString", '') IS NOT NULL OR p."ConnectionDbType" IS NOT NULL OR p."AllowDeskSql");
            SELECT count(*) INTO orphan FROM studio.project_connections c
            LEFT JOIN "CloudProjects" p ON p."Id" = c."ProjectId" WHERE p."Id" IS NULL;
            SELECT count(*) INTO diff FROM studio.project_connections c
            JOIN "CloudProjects" p ON p."Id" = c."ProjectId"
            WHERE c."EncryptedConnectionString" IS DISTINCT FROM NULLIF(p."EncryptedConnectionString", '')
               OR c."ConnectionDbType" IS DISTINCT FROM p."ConnectionDbType"
               OR c."AllowDeskSql" IS DISTINCT FROM p."AllowDeskSql";
            IF missing + orphan + diff > 0 THEN
                RAISE EXCEPTION 'Phase14_2B verify connections: missing=%, orphan=%, field_diff=%', missing, orphan, diff;
            END IF;

            -- Paylaşımlar
            SELECT count(*) INTO missing FROM "CloudProjects" p
            LEFT JOIN studio.project_shares s ON s."ProjectId" = p."Id" AND s."ShareTokenHash" = p."ShareTokenHash"
            WHERE NULLIF(p."ShareTokenHash", '') IS NOT NULL AND s."Id" IS NULL;
            SELECT count(*) INTO dup FROM (
                SELECT "ShareTokenHash" FROM studio.project_shares GROUP BY 1 HAVING count(*) > 1) d;
            SELECT count(*) INTO orphan FROM studio.project_shares s
            LEFT JOIN "CloudProjects" p ON p."Id" = s."ProjectId" AND p."ShareTokenHash" = s."ShareTokenHash"
            WHERE p."Id" IS NULL;
            IF missing + dup + orphan > 0 THEN
                RAISE EXCEPTION 'Phase14_2B verify shares: missing=%, duplicate=%, orphan=%', missing, dup, orphan;
            END IF;

            -- Repolar
            SELECT count(*) INTO missing FROM "CloudProjects" p
            LEFT JOIN hub.repositories r ON r."ProjectId" = p."Id" WHERE r."ProjectId" IS NULL;
            SELECT count(*) INTO orphan FROM hub.repositories r
            LEFT JOIN "CloudProjects" p ON p."Id" = r."ProjectId" WHERE p."Id" IS NULL;
            SELECT count(*) INTO diff FROM hub.repositories r
            JOIN "CloudProjects" p ON p."Id" = r."ProjectId"
            LEFT JOIN "CloudProjects" src ON src."Id" = p."ForkedFromProjectId"
            WHERE r."OwnerUserId" IS DISTINCT FROM p."UserId"
               OR r."Slug" IS DISTINCT FROM p."Slug"
               OR r."Description" IS DISTINCT FROM p."Description"
               OR r."Visibility" IS DISTINCT FROM p."Visibility"
               OR r."Category" IS DISTINCT FROM p."Category"
               OR r."StarCount" IS DISTINCT FROM p."StarCount"
               OR r."ForkCount" IS DISTINCT FROM p."ForkCount"
               OR r."LinkedGithubRepo" IS DISTINCT FROM p."LinkedGithubRepo"
               OR r."CreatedAt" IS DISTINCT FROM p."CreatedAt"
               OR r."ForkedFromProjectId" IS DISTINCT FROM (CASE WHEN src."Id" IS NOT NULL THEN p."ForkedFromProjectId" END);
            IF missing + orphan + diff > 0 THEN
                RAISE EXCEPTION 'Phase14_2B verify repositories: missing=%, orphan=%, field_diff=%', missing, orphan, diff;
            END IF;

            -- Etiketler: sayım, ekleme sorgusundan bağımsız bir ayrıştırmayla.
            SELECT count(*) INTO diff FROM (
                SELECT p."Id", count(DISTINCT pg_temp.namines_tag_key(pg_temp.namines_tag_trim(part))) AS expected
                FROM "CloudProjects" p
                CROSS JOIN LATERAL regexp_split_to_table(p."Tags", ',') AS part
                WHERE pg_temp.namines_tag_trim(part) <> ''
                GROUP BY p."Id"
            ) e
            FULL JOIN (SELECT "ProjectId", count(*) AS actual FROM hub.project_tags GROUP BY 1) a
                ON a."ProjectId" = e."Id"
            WHERE COALESCE(e.expected, 0) <> COALESCE(a.actual, 0);
            -- Position kesintisiz, anahtar görünen yazımdan türemiş olmalı.
            SELECT count(*) INTO dup FROM (
                SELECT "ProjectId" FROM hub.project_tags GROUP BY 1
                HAVING max("Position") <> count(*) - 1 OR min("Position") <> 0) d;
            SELECT count(*) INTO orphan FROM hub.project_tags
            WHERE "Tag" <> pg_temp.namines_tag_key("DisplayTag") OR "DisplayTag" = '';
            IF diff + dup + orphan > 0 THEN
                RAISE EXCEPTION 'Phase14_2B verify tags: count_diff=%, position_gaps=%, bad_key=%', diff, dup, orphan;
            END IF;
        END $verify$;
        """;

    public const string UpContract = """
        DROP FUNCTION pg_temp.namines_tag_key(text);
        DROP FUNCTION pg_temp.namines_tag_trim(text);

        -- ── Kontrat: kolonlar FİZİKSEL olarak düşer (indeks/CHECK/FK'leri de onlarla gider) ─────
        ALTER TABLE "CloudProjects"
            DROP COLUMN "EncryptedConnectionString",
            DROP COLUMN "ConnectionDbType",
            DROP COLUMN "AllowDeskSql",
            DROP COLUMN "ShareTokenHash",
            DROP COLUMN "Slug",
            DROP COLUMN "Description",
            DROP COLUMN "Visibility",
            DROP COLUMN "Category",
            DROP COLUMN "Tags",
            DROP COLUMN "StarCount",
            DROP COLUMN "ForkCount",
            DROP COLUMN "ForkedFromProjectId",
            DROP COLUMN "LinkedGithubRepo";

        ALTER TABLE public."CloudProjects" SET SCHEMA studio;
        ALTER TABLE studio."CloudProjects" RENAME TO projects;

        -- EF'in beklediği adlar: gelecekteki migration'lar kısıtları bu adlarla arıyor. Diğer
        -- tablolardan gelen FK'ler tabloyu otomatik izliyor, yalnızca adları eski.
        ALTER TABLE studio.projects RENAME CONSTRAINT "PK_CloudProjects" TO "PK_projects";
        ALTER INDEX studio."IX_CloudProjects_UserId" RENAME TO "IX_projects_UserId";
        ALTER INDEX studio."IX_CloudProjects_OrganizationId" RENAME TO "IX_projects_OrganizationId";
        DO $rename$
        DECLARE c record;
        BEGIN
            FOR c IN
                SELECT conrelid::regclass AS tbl, conname
                FROM pg_constraint
                WHERE conname LIKE '%CloudProjects%'
                  AND (confrelid = 'studio.projects'::regclass OR conrelid = 'studio.projects'::regclass)
            LOOP
                EXECUTE format('ALTER TABLE %s RENAME CONSTRAINT %I TO %I',
                    c.tbl, c.conname, replace(c.conname, 'CloudProjects', 'projects'));
            END LOOP;
        END $rename$;

        -- ── İndeksler ve CHECK'ler (satırlar taşındıktan sonra) ─────────────────
        CREATE UNIQUE INDEX "IX_project_shares_ShareTokenHash" ON studio.project_shares ("ShareTokenHash");
        CREATE INDEX "IX_project_shares_ProjectId" ON studio.project_shares ("ProjectId");
        CREATE UNIQUE INDEX "IX_repositories_OwnerUserId_Slug" ON hub.repositories ("OwnerUserId", "Slug")
            WHERE "Slug" IS NOT NULL AND "Slug" <> '';
        CREATE INDEX "IX_repositories_Slug" ON hub.repositories ("Slug");
        CREATE INDEX "IX_repositories_ForkedFromProjectId" ON hub.repositories ("ForkedFromProjectId");
        CREATE INDEX "IX_repositories_Explore_Popular" ON hub.repositories ("StarCount" DESC, "CreatedAt" DESC, "ProjectId" DESC)
            WHERE "Visibility" = 2;
        CREATE INDEX "IX_repositories_Explore_Recent" ON hub.repositories ("CreatedAt" DESC, "ProjectId" DESC)
            WHERE "Visibility" = 2;
        CREATE INDEX "IX_project_tags_Tag_ProjectId" ON hub.project_tags ("Tag", "ProjectId");

        -- Eski tablodaki gibi NOT VALID + ihlal yoksa VALIDATE: tavanı aşan eski satır migration'ı
        -- düşürmesin, yeni yazımlar yine de denetlensin.
        ALTER TABLE hub.repositories
            ADD CONSTRAINT "CK_repositories_Visibility" CHECK ("Visibility" IN (0, 1, 2)) NOT VALID,
            ADD CONSTRAINT "CK_repositories_StarCount" CHECK ("StarCount" >= 0) NOT VALID,
            ADD CONSTRAINT "CK_repositories_ForkCount" CHECK ("ForkCount" >= 0) NOT VALID,
            ADD CONSTRAINT "CK_repositories_Description_Length" CHECK (char_length("Description") <= 1000) NOT VALID,
            ADD CONSTRAINT "CK_repositories_Category_Length" CHECK (char_length("Category") <= 64) NOT VALID,
            ADD CONSTRAINT "CK_repositories_LinkedGithubRepo_Length" CHECK (char_length("LinkedGithubRepo") <= 140) NOT VALID;
        ALTER TABLE studio.project_shares
            ADD CONSTRAINT "CK_project_shares_ShareTokenHash_Length" CHECK (char_length("ShareTokenHash") = 64) NOT VALID;
        DO $validate$
        DECLARE c record;
        BEGIN
            FOR c IN
                SELECT conrelid::regclass AS tbl, conname FROM pg_constraint
                WHERE contype = 'c' AND NOT convalidated
                  AND conrelid IN ('hub.repositories'::regclass, 'studio.project_shares'::regclass)
            LOOP
                BEGIN
                    EXECUTE format('ALTER TABLE %s VALIDATE CONSTRAINT %I', c.tbl, c.conname);
                EXCEPTION WHEN check_violation THEN
                    RAISE NOTICE 'CHECK %: existing rows violate it; left NOT VALID (enforced for new writes only).', c.conname;
                END;
            END LOOP;
        END $validate$;
        """;

    public const string DownRestore = """
        LOCK TABLE studio.projects, studio.project_connections, studio.project_shares,
                   hub.repositories, hub.project_tags IN ACCESS EXCLUSIVE MODE;

        DO $rename$
        DECLARE c record;
        BEGIN
            FOR c IN
                SELECT conrelid::regclass AS tbl, conname
                FROM pg_constraint
                WHERE conname LIKE '%\_projects\_%'
                  AND conname NOT IN ('FK_project_connections_projects_ProjectId', 'FK_project_shares_projects_ProjectId', 'FK_repositories_projects_ProjectId')
                  AND confrelid = 'studio.projects'::regclass
            LOOP
                EXECUTE format('ALTER TABLE %s RENAME CONSTRAINT %I TO %I',
                    c.tbl, c.conname, replace(c.conname, '_projects_', '_CloudProjects_'));
            END LOOP;
        END $rename$;

        ALTER TABLE studio.projects RENAME CONSTRAINT "PK_projects" TO "PK_CloudProjects";
        ALTER TABLE studio.projects RENAME CONSTRAINT "FK_projects_AspNetUsers_UserId" TO "FK_CloudProjects_AspNetUsers_UserId";
        ALTER TABLE studio.projects RENAME CONSTRAINT "FK_projects_Organizations_OrganizationId" TO "FK_CloudProjects_Organizations_OrganizationId";
        ALTER TABLE studio.projects RENAME CONSTRAINT "CK_projects_Name_Length" TO "CK_CloudProjects_Name_Length";
        ALTER TABLE studio.projects RENAME CONSTRAINT "CK_projects_SchemaJson_Length" TO "CK_CloudProjects_SchemaJson_Length";
        ALTER INDEX studio."IX_projects_UserId" RENAME TO "IX_CloudProjects_UserId";
        ALTER INDEX studio."IX_projects_OrganizationId" RENAME TO "IX_CloudProjects_OrganizationId";
        ALTER TABLE studio.projects RENAME TO "CloudProjects";
        ALTER TABLE studio."CloudProjects" SET SCHEMA public;

        ALTER TABLE "CloudProjects"
            ADD COLUMN "EncryptedConnectionString" text NULL,
            ADD COLUMN "ConnectionDbType" text NULL,
            ADD COLUMN "AllowDeskSql" boolean NOT NULL DEFAULT false,
            ADD COLUMN "ShareTokenHash" text NULL,
            ADD COLUMN "Slug" text NULL,
            ADD COLUMN "Description" text NULL,
            ADD COLUMN "Visibility" integer NOT NULL DEFAULT 0,
            ADD COLUMN "Category" text NOT NULL DEFAULT '',
            ADD COLUMN "Tags" text NULL,
            ADD COLUMN "StarCount" integer NOT NULL DEFAULT 0,
            ADD COLUMN "ForkCount" integer NOT NULL DEFAULT 0,
            ADD COLUMN "ForkedFromProjectId" text NULL,
            ADD COLUMN "LinkedGithubRepo" text NULL;

        UPDATE "CloudProjects" p
        SET "EncryptedConnectionString" = c."EncryptedConnectionString",
            "ConnectionDbType" = c."ConnectionDbType",
            "AllowDeskSql" = c."AllowDeskSql"
        FROM studio.project_connections c
        WHERE c."ProjectId" = p."Id";

        UPDATE "CloudProjects" p
        SET "Slug" = r."Slug",
            "Description" = r."Description",
            "Visibility" = r."Visibility",
            "Category" = r."Category",
            "StarCount" = r."StarCount",
            "ForkCount" = r."ForkCount",
            "ForkedFromProjectId" = r."ForkedFromProjectId",
            "LinkedGithubRepo" = r."LinkedGithubRepo"
        FROM hub.repositories r
        WHERE r."ProjectId" = p."Id";

        UPDATE "CloudProjects" p
        SET "Tags" = t.csv
        FROM (
            SELECT "ProjectId", string_agg("DisplayTag", ',' ORDER BY "Position") AS csv
            FROM hub.project_tags GROUP BY 1
        ) t
        WHERE t."ProjectId" = p."Id";

        -- Eski tablo proje başına TEK bağlantı tutabilir: en yeni etkin bağlantı geri döner, diğer
        -- etkin bağlantılar bu geri dönüşte geçersiz kalır (rapor: geri alma stratejisi).
        UPDATE "CloudProjects" p
        SET "ShareTokenHash" = s."ShareTokenHash"
        FROM (
            SELECT DISTINCT ON ("ProjectId") "ProjectId", "ShareTokenHash"
            FROM studio.project_shares
            WHERE "RevokedAt" IS NULL
            ORDER BY "ProjectId", "CreatedAt" DESC, "Id" DESC
        ) s
        WHERE s."ProjectId" = p."Id";
        """;

    public const string DownVerify = """
        DO $verify$
        DECLARE diff bigint;
        BEGIN
            SELECT count(*) INTO diff FROM hub.repositories r
            LEFT JOIN "CloudProjects" p ON p."Id" = r."ProjectId"
            WHERE p."Id" IS NULL
               OR r."Slug" IS DISTINCT FROM p."Slug"
               OR r."Visibility" IS DISTINCT FROM p."Visibility"
               OR r."StarCount" IS DISTINCT FROM p."StarCount"
               OR r."ForkCount" IS DISTINCT FROM p."ForkCount";
            IF diff > 0 THEN
                RAISE EXCEPTION 'Phase14_2B down verify repositories: field_diff=%', diff;
            END IF;
            SELECT count(*) INTO diff FROM studio.project_connections c
            LEFT JOIN "CloudProjects" p ON p."Id" = c."ProjectId"
            WHERE p."Id" IS NULL OR c."EncryptedConnectionString" IS DISTINCT FROM p."EncryptedConnectionString";
            IF diff > 0 THEN
                RAISE EXCEPTION 'Phase14_2B down verify connections: field_diff=%', diff;
            END IF;
            SELECT count(*) INTO diff FROM "CloudProjects" p
            LEFT JOIN (
                SELECT "ProjectId", string_agg("DisplayTag", ',' ORDER BY "Position") AS csv
                FROM hub.project_tags GROUP BY 1
            ) t ON t."ProjectId" = p."Id"
            WHERE p."Tags" IS DISTINCT FROM t.csv;
            IF diff > 0 THEN
                RAISE EXCEPTION 'Phase14_2B down verify tags: field_diff=%', diff;
            END IF;
            SELECT count(*) INTO diff FROM "CloudProjects" p
            LEFT JOIN (
                SELECT DISTINCT ON ("ProjectId") "ProjectId", "ShareTokenHash"
                FROM studio.project_shares WHERE "RevokedAt" IS NULL
                ORDER BY "ProjectId", "CreatedAt" DESC, "Id" DESC
            ) s ON s."ProjectId" = p."Id"
            WHERE p."ShareTokenHash" IS DISTINCT FROM s."ShareTokenHash";
            IF diff > 0 THEN
                RAISE EXCEPTION 'Phase14_2B down verify shares: field_diff=%', diff;
            END IF;
        END $verify$;
        """;

    public const string DownContract = """
        DROP TABLE hub.project_tags;
        DROP TABLE hub.repositories;
        DROP TABLE studio.project_shares;
        DROP TABLE studio.project_connections;
        -- Şemalar kalır: düşerse roles.sql'in şemaya bağlı DEFAULT PRIVILEGES kaydı da gider ve yeniden
        -- yükseltmede namines_app yeni tablolara erişemez. Boş şema zararsız, Up onu kullanır.

        ALTER TABLE "CloudProjects"
            ADD CONSTRAINT "FK_CloudProjects_CloudProjects_ForkedFromProjectId" FOREIGN KEY ("ForkedFromProjectId")
                REFERENCES "CloudProjects" ("Id") ON DELETE SET NULL;
        CREATE INDEX "IX_CloudProjects_ForkedFromProjectId" ON "CloudProjects" ("ForkedFromProjectId");
        CREATE UNIQUE INDEX "IX_CloudProjects_ShareTokenHash" ON "CloudProjects" ("ShareTokenHash") WHERE "ShareTokenHash" IS NOT NULL;
        CREATE INDEX "IX_CloudProjects_Slug" ON "CloudProjects" ("Slug");
        CREATE UNIQUE INDEX "IX_CloudProjects_UserId_Slug" ON "CloudProjects" ("UserId", "Slug")
            WHERE "Slug" IS NOT NULL AND "Slug" <> '';
        CREATE INDEX "IX_CloudProjects_Explore_Public" ON "CloudProjects" ("StarCount" DESC, "CreatedAt" DESC)
            WHERE "Visibility" = 2;

        ALTER TABLE "CloudProjects"
            ADD CONSTRAINT "CK_CloudProjects_Visibility" CHECK ("Visibility" IN (0, 1, 2)) NOT VALID,
            ADD CONSTRAINT "CK_CloudProjects_StarCount" CHECK ("StarCount" >= 0) NOT VALID,
            ADD CONSTRAINT "CK_CloudProjects_ForkCount" CHECK ("ForkCount" >= 0) NOT VALID,
            ADD CONSTRAINT "CK_CloudProjects_Description_Length" CHECK (char_length("Description") <= 1000) NOT VALID,
            ADD CONSTRAINT "CK_CloudProjects_Category_Length" CHECK (char_length("Category") <= 64) NOT VALID,
            ADD CONSTRAINT "CK_CloudProjects_Tags_Length" CHECK (char_length("Tags") <= 256) NOT VALID,
            ADD CONSTRAINT "CK_CloudProjects_LinkedGithubRepo_Length" CHECK (char_length("LinkedGithubRepo") <= 140) NOT VALID;
        DO $validate$
        DECLARE c record;
        BEGIN
            FOR c IN
                SELECT conname FROM pg_constraint
                WHERE contype = 'c' AND NOT convalidated AND conrelid = '"CloudProjects"'::regclass
            LOOP
                BEGIN
                    EXECUTE format('ALTER TABLE "CloudProjects" VALIDATE CONSTRAINT %I', c.conname);
                EXCEPTION WHEN check_violation THEN
                    RAISE NOTICE 'CHECK %: existing rows violate it; left NOT VALID (enforced for new writes only).', c.conname;
                END;
            END LOOP;
        END $validate$;
        """;
}
