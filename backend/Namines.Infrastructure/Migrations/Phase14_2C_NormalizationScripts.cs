namespace Namines.Infrastructure.Migrations;

/// <summary>
/// <c>Phase14_2C_NormalizeCollections</c> migration'ının SQL metinleri. Bu dosya DONDURULMUŞTUR:
/// kalıplar <c>GatewayAllowlistRules</c>'tan bilerek kopyalanmıyor, sabit metin; kural ileride
/// değişirse geçmiş migration'ın davranışı değişmemeli.
///
/// Hepsi EF'in açtığı tek işlemde ve ACCESS EXCLUSIVE kilit altında: eski kolon okunurken kimse
/// yazamaz, doğrulama patlarsa hiçbir şey değişmemiş olur, maskesiz bir an oluşmaz.
///
/// <b>Kapalıya düşme kuralı:</b> geçersiz allowlist girdisi karantinaya gider ve asla erişimi
/// genişletmez. Geçerli girdisi kalmayan liste "kısıt yok" OLMAZ: bayrak açık kalır, her istek
/// reddedilir. Maskeleme girdisi ise hiçbir koşulda atılmaz (tek istisna, gerçek bir kolon adı
/// olamayacak uzunluktaki metin), çünkü bir maskeyi atmak veri sızdırır.
/// </summary>
public static class Phase14_2CNormalizationScripts
{
    // C# string.Trim() ile aynı küme, karakter karakter. \s yerele bağlı olduğu için kullanılmıyor.
    private const string Space = @"[\u0009-\u000D \u0085   -     　]";

    private const string OriginPattern =
        @"^[a-z][a-z0-9+.-]*://([a-z0-9_]([a-z0-9_-]*[a-z0-9_])?(\.[a-z0-9_]([a-z0-9_-]*[a-z0-9_])?)*|\[[0-9a-f:.]+\])(:[1-9][0-9]{0,4})?$";

    private const string Ipv4Pattern =
        @"^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}$";

    private const string Ipv6CharsPattern = @"^[0-9a-fA-F:.]*:[0-9a-fA-F:.]*$";

    private const string PrefixPattern = @"^(0|[1-9][0-9]{0,2})$";

    private const int MaxMaskedColumnLength = 512;

    private const string OriginsSource = "public.GatewayApiKeys.AllowedOrigins";
    private const string IpsSource = "public.GatewayApiKeys.AllowedIps";
    private const string MaskedSource = "public.GatewayTablePermissions.MaskedColumns";

    /// <summary>Up ve Down'ın ortak yardımcıları. pg_temp: oturumla ölür, şemada iz bırakmaz.</summary>
    private static string HelpersSql => $$"""
        CREATE OR REPLACE FUNCTION pg_temp.namines_2c_trim(t text) RETURNS text LANGUAGE sql IMMUTABLE AS $b$
            SELECT regexp_replace(t, '^{{Space}}+|{{Space}}+$', '', 'g')
        $b$;

        -- Eski Split(',', RemoveEmpty | Trim) ile aynı. ord: boşlar atılmadan önceki sıra.
        CREATE OR REPLACE FUNCTION pg_temp.namines_2c_items(raw text) RETURNS TABLE(ord bigint, item text) LANGUAGE sql IMMUTABLE AS $b$
            SELECT o, pg_temp.namines_2c_trim(p)
            FROM regexp_split_to_table(raw, ',') WITH ORDINALITY AS x(p, o)
            WHERE pg_temp.namines_2c_trim(p) <> ''
        $b$;

        CREATE OR REPLACE FUNCTION pg_temp.namines_2c_origin(item text) RETURNS text LANGUAGE plpgsql IMMUTABLE AS $b$
        DECLARE
            v text;
            m text[];
            addr inet;
        BEGIN
            IF char_length(item) > 512 OR item !~ '^[!-~]+$' THEN RETURN NULL; END IF;
            v := lower(item COLLATE "C");
            m := regexp_match(v, '{{OriginPattern}}');
            IF m IS NULL THEN RETURN NULL; END IF;
            IF left(m[1], 1) = '[' THEN
                BEGIN
                    addr := substr(m[1], 2, char_length(m[1]) - 2)::inet;
                EXCEPTION WHEN invalid_text_representation THEN RETURN NULL;
                END;
                IF family(addr) <> 6 THEN RETURN NULL; END IF;
            END IF;
            IF m[5] IS NOT NULL AND substr(m[5], 2)::int > 65535 THEN RETURN NULL; END IF;
            RETURN v;
        END $b$;

        CREATE OR REPLACE FUNCTION pg_temp.namines_2c_origin_reason(item text) RETURNS text LANGUAGE sql IMMUTABLE AS $b$
            SELECT CASE
                WHEN position('*' in item) > 0 THEN 'wildcard_not_supported'
                WHEN lower(item COLLATE "C") = 'null' THEN 'null_origin_not_supported'
                WHEN char_length(item) > 512 THEN 'too_long'
                ELSE 'invalid_origin'
            END
        $b$;

        CREATE OR REPLACE FUNCTION pg_temp.namines_2c_ip(item text) RETURNS text LANGUAGE plpgsql IMMUTABLE AS $b$
        DECLARE
            slash int := position('/' in item);
            addr_text text := CASE WHEN position('/' in item) = 0 THEN item ELSE left(item, position('/' in item) - 1) END;
            prefix_text text := CASE WHEN position('/' in item) = 0 THEN NULL ELSE substr(item, position('/' in item) + 1) END;
            addr inet;
            max_bits int;
            bits int;
        BEGIN
            IF addr_text !~ '{{Ipv4Pattern}}' AND addr_text !~ '{{Ipv6CharsPattern}}' THEN RETURN NULL; END IF;
            IF slash > 0 AND prefix_text !~ '{{PrefixPattern}}' THEN RETURN NULL; END IF;
            BEGIN
                addr := addr_text::inet;
            EXCEPTION WHEN invalid_text_representation THEN RETURN NULL;
            END;

            -- IPv4-eşlenmiş adres v4 kuralı sayılır; önekli hâli eski kodda tutarsızdı, kabul edilmez.
            IF family(addr) = 6 AND addr <<= '::ffff:0:0/96'::inet THEN
                IF slash > 0 THEN RETURN NULL; END IF;
                addr := '0.0.0.0'::inet + (addr - '::ffff:0:0'::inet);
            END IF;

            max_bits := CASE family(addr) WHEN 4 THEN 32 ELSE 128 END;
            bits := COALESCE(prefix_text::int, max_bits);
            IF bits > max_bits THEN RETURN NULL; END IF;
            RETURN network(set_masklen(addr, bits))::text;
        END $b$;

        -- NOT VALID CHECK'ler UPDATE'te yeniden denetlenir: tavanı aşan eski bir satır (RateLimit=0
        -- gibi) doldurmayı düşürürdü. Bu kolonlarla ilgileri yok; aynı işlemde kaldırılıp aynen eklenir.
        CREATE OR REPLACE FUNCTION pg_temp.namines_2c_lift(rel regclass) RETURNS text[] LANGUAGE plpgsql AS $b$
        DECLARE
            c record;
            saved text[] := ARRAY[]::text[];
        BEGIN
            FOR c IN
                SELECT conname, pg_get_constraintdef(oid) AS definition FROM pg_constraint
                WHERE conrelid = rel AND contype = 'c' AND NOT convalidated
            LOOP
                saved := saved || format('ADD CONSTRAINT %I %s', c.conname, c.definition);
                EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', rel, c.conname);
            END LOOP;
            RETURN saved;
        END $b$;

        CREATE OR REPLACE FUNCTION pg_temp.namines_2c_restore(rel regclass, saved text[]) RETURNS void LANGUAGE plpgsql AS $b$
        DECLARE def text;
        BEGIN
            FOREACH def IN ARRAY saved LOOP
                EXECUTE format('ALTER TABLE %s %s', rel, def);
            END LOOP;
        END $b$;
        """;

    /// <summary>ProjectTagRules.Key'in SQL karşılığı; gerekçe 14.2B betiğinde.</summary>
    private static string LabelKeyFunctionSql => """
        DO $fn$
        DECLARE coll text := CASE WHEN EXISTS (SELECT 1 FROM pg_collation WHERE collname = 'und-x-icu')
                                  THEN '"und-x-icu"' ELSE '"default"' END;
        BEGIN
            EXECUTE format($f$
                CREATE OR REPLACE FUNCTION pg_temp.namines_2c_key(t text) RETURNS text LANGUAGE sql IMMUTABLE AS $b$
                    SELECT string_agg(CASE WHEN c = 'İ' THEN c ELSE lower(c COLLATE %s) END, '' ORDER BY o)
                    FROM regexp_split_to_table(t, '') WITH ORDINALITY AS x(c, o)
                $b$;
            $f$, coll);
        END $fn$;
        """;

    public static string Up => string.Join("\n", UpPrepare, UpVerify, UpContract);

    public static string Down => string.Join("\n", DownRestore, DownVerify, DownContract);

    public static string UpPrepare => $$"""
        LOCK TABLE "RepoIssues", "GatewayApiKeys", "GatewayTablePermissions" IN ACCESS EXCLUSIVE MODE;

        DO $guard$
        BEGIN
            IF to_regclass('hub.issue_labels') IS NOT NULL OR to_regclass('hub.repo_issue_labels') IS NOT NULL
               OR to_regclass('gateway.api_key_allowed_origins') IS NOT NULL
               OR to_regclass('gateway.api_key_allowed_ips') IS NOT NULL
               OR to_regclass('gateway.table_masked_columns') IS NOT NULL THEN
                RAISE EXCEPTION 'Phase14_2C: target tables already exist. Inspect and remove them, then re-run.';
            END IF;
            -- CREATE SCHEMA IF NOT EXISTS, şema varken bile veritabanı CREATE yetkisi ister.
            IF to_regnamespace('gateway') IS NULL THEN CREATE SCHEMA gateway; END IF;
        END $guard$;

        {{HelpersSql}}
        {{LabelKeyFunctionSql}}

        -- ── Genişlet ──────────────────────────────────────────────────────────────
        ALTER TABLE "GatewayApiKeys"
            ADD COLUMN "RestrictOrigins" boolean NOT NULL DEFAULT false,
            ADD COLUMN "RestrictIps" boolean NOT NULL DEFAULT false;

        CREATE TABLE gateway.api_key_allowed_origins (
            "Id" text NOT NULL DEFAULT (gen_random_uuid()::text),
            "ApiKeyId" text NOT NULL,
            "Origin" character varying(512) NOT NULL,
            "Position" integer NOT NULL,
            CONSTRAINT "PK_api_key_allowed_origins" PRIMARY KEY ("Id"),
            CONSTRAINT "CK_api_key_allowed_origins_Origin" CHECK ("Origin" ~ '{{OriginPattern}}'),
            CONSTRAINT "FK_api_key_allowed_origins_GatewayApiKeys_ApiKeyId" FOREIGN KEY ("ApiKeyId")
                REFERENCES "GatewayApiKeys" ("Id") ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX "IX_api_key_allowed_origins_ApiKeyId_Origin"
            ON gateway.api_key_allowed_origins ("ApiKeyId", "Origin");

        CREATE TABLE gateway.api_key_allowed_ips (
            "Id" text NOT NULL DEFAULT (gen_random_uuid()::text),
            "ApiKeyId" text NOT NULL,
            "Cidr" character varying(64) NOT NULL,
            "Position" integer NOT NULL,
            CONSTRAINT "PK_api_key_allowed_ips" PRIMARY KEY ("Id"),
            CONSTRAINT "CK_api_key_allowed_ips_Cidr" CHECK (position('/' in "Cidr") > 0 AND ("Cidr"::cidr) IS NOT NULL),
            CONSTRAINT "FK_api_key_allowed_ips_GatewayApiKeys_ApiKeyId" FOREIGN KEY ("ApiKeyId")
                REFERENCES "GatewayApiKeys" ("Id") ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX "IX_api_key_allowed_ips_ApiKeyId_Cidr"
            ON gateway.api_key_allowed_ips ("ApiKeyId", "Cidr");

        CREATE TABLE gateway.table_masked_columns (
            "Id" text NOT NULL DEFAULT (gen_random_uuid()::text),
            "TablePermissionId" text NOT NULL,
            "ColumnName" character varying(512) NOT NULL,
            "Position" integer NOT NULL,
            CONSTRAINT "PK_table_masked_columns" PRIMARY KEY ("Id"),
            CONSTRAINT "CK_table_masked_columns_ColumnName" CHECK ("ColumnName" <> ''),
            CONSTRAINT "FK_table_masked_columns_GatewayTablePermissions_TablePermissio~" FOREIGN KEY ("TablePermissionId")
                REFERENCES "GatewayTablePermissions" ("Id") ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX "IX_table_masked_columns_TablePermissionId_ColumnName"
            ON gateway.table_masked_columns ("TablePermissionId", "ColumnName");

        CREATE TABLE hub.issue_labels (
            "Id" text NOT NULL DEFAULT (gen_random_uuid()::text),
            "ProjectId" text NOT NULL,
            "Name" character varying(256) NOT NULL,
            "NormalizedName" character varying(256) NOT NULL,
            CONSTRAINT "PK_issue_labels" PRIMARY KEY ("Id"),
            CONSTRAINT "CK_issue_labels_NormalizedName" CHECK ("NormalizedName" <> ''),
            CONSTRAINT "FK_issue_labels_projects_ProjectId" FOREIGN KEY ("ProjectId")
                REFERENCES studio.projects ("Id") ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX "IX_issue_labels_ProjectId_NormalizedName"
            ON hub.issue_labels ("ProjectId", "NormalizedName");

        CREATE TABLE hub.repo_issue_labels (
            "RepoIssueId" text NOT NULL,
            "IssueLabelId" text NOT NULL,
            "DisplayName" character varying(256) NOT NULL,
            "Position" integer NOT NULL,
            CONSTRAINT "PK_repo_issue_labels" PRIMARY KEY ("RepoIssueId", "IssueLabelId"),
            CONSTRAINT "FK_repo_issue_labels_RepoIssues_RepoIssueId" FOREIGN KEY ("RepoIssueId")
                REFERENCES "RepoIssues" ("Id") ON DELETE CASCADE,
            CONSTRAINT "FK_repo_issue_labels_issue_labels_IssueLabelId" FOREIGN KEY ("IssueLabelId")
                REFERENCES hub.issue_labels ("Id") ON DELETE CASCADE
        );
        CREATE INDEX "IX_repo_issue_labels_IssueLabelId_RepoIssueId"
            ON hub.repo_issue_labels ("IssueLabelId", "RepoIssueId");

        -- ── Doldur: origin ────────────────────────────────────────────────────────
        INSERT INTO gateway.api_key_allowed_origins ("ApiKeyId", "Origin", "Position")
        SELECT s.key_id, s.canonical, (row_number() OVER (PARTITION BY s.key_id ORDER BY s.first_ord) - 1)::int
        FROM (
            SELECT k."Id" AS key_id, pg_temp.namines_2c_origin(i.item) AS canonical, min(i.ord) AS first_ord
            FROM "GatewayApiKeys" k
            CROSS JOIN LATERAL pg_temp.namines_2c_items(k."AllowedOrigins") i
            WHERE pg_temp.namines_2c_origin(i.item) IS NOT NULL
            GROUP BY 1, 2
        ) s;

        INSERT INTO migration_quarantine.orphaned_rows (source_table, reason, row_data)
        SELECT '{{OriginsSource}}', pg_temp.namines_2c_origin_reason(i.item),
               jsonb_build_object('key_id', k."Id", 'project_id', k."ProjectId", 'entry', i.item, 'ord', i.ord, 'raw', k."AllowedOrigins")
        FROM "GatewayApiKeys" k
        CROSS JOIN LATERAL pg_temp.namines_2c_items(k."AllowedOrigins") i
        WHERE pg_temp.namines_2c_origin(i.item) IS NULL;

        -- ── Doldur: IP ────────────────────────────────────────────────────────────
        INSERT INTO gateway.api_key_allowed_ips ("ApiKeyId", "Cidr", "Position")
        SELECT s.key_id, s.canonical, (row_number() OVER (PARTITION BY s.key_id ORDER BY s.first_ord) - 1)::int
        FROM (
            SELECT k."Id" AS key_id, pg_temp.namines_2c_ip(i.item) AS canonical, min(i.ord) AS first_ord
            FROM "GatewayApiKeys" k
            CROSS JOIN LATERAL pg_temp.namines_2c_items(k."AllowedIps") i
            WHERE pg_temp.namines_2c_ip(i.item) IS NOT NULL
            GROUP BY 1, 2
        ) s;

        INSERT INTO migration_quarantine.orphaned_rows (source_table, reason, row_data)
        SELECT '{{IpsSource}}', 'invalid_ip',
               jsonb_build_object('key_id', k."Id", 'project_id', k."ProjectId", 'entry', i.item, 'ord', i.ord, 'raw', k."AllowedIps")
        FROM "GatewayApiKeys" k
        CROSS JOIN LATERAL pg_temp.namines_2c_items(k."AllowedIps") i
        WHERE pg_temp.namines_2c_ip(i.item) IS NULL;

        -- Yalnızca virgülden oluşan liste eski kodda da "kısıt yok" idi; davranış korunur ama iz bırakılır:
        -- bu anahtarların sahibi kısıtladığını sanıyor olabilir.
        INSERT INTO migration_quarantine.orphaned_rows (source_table, reason, row_data)
        SELECT '{{OriginsSource}}', 'blank_list_means_no_restriction',
               jsonb_build_object('key_id', k."Id", 'project_id', k."ProjectId", 'raw', k."AllowedOrigins")
        FROM "GatewayApiKeys" k
        WHERE k."AllowedOrigins" IS NOT NULL AND k."AllowedOrigins" <> ''
          AND NOT EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(k."AllowedOrigins"));

        INSERT INTO migration_quarantine.orphaned_rows (source_table, reason, row_data)
        SELECT '{{IpsSource}}', 'blank_list_means_no_restriction',
               jsonb_build_object('key_id', k."Id", 'project_id', k."ProjectId", 'raw', k."AllowedIps")
        FROM "GatewayApiKeys" k
        WHERE k."AllowedIps" IS NOT NULL AND k."AllowedIps" <> ''
          AND NOT EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(k."AllowedIps"));

        -- Bayrak GEÇERLİ girdiye değil HERHANGİ bir girdiye bakar: kapalıya düşmenin tamamı bu satır.
        DO $flags$
        DECLARE saved text[] := pg_temp.namines_2c_lift('"GatewayApiKeys"'::regclass);
        BEGIN
            UPDATE "GatewayApiKeys" k SET
                "RestrictOrigins" = EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(k."AllowedOrigins")),
                "RestrictIps" = EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(k."AllowedIps"));
            PERFORM pg_temp.namines_2c_restore('"GatewayApiKeys"'::regclass, saved);
        END $flags$;

        -- ── Doldur: maskeler ──────────────────────────────────────────────────────
        INSERT INTO gateway.table_masked_columns ("TablePermissionId", "ColumnName", "Position")
        SELECT s.perm_id, s.item, (row_number() OVER (PARTITION BY s.perm_id ORDER BY s.first_ord) - 1)::int
        FROM (
            SELECT p."Id" AS perm_id, i.item, min(i.ord) AS first_ord
            FROM "GatewayTablePermissions" p
            CROSS JOIN LATERAL pg_temp.namines_2c_items(p."MaskedColumns") i
            WHERE char_length(i.item) <= {{MaxMaskedColumnLength}}
            GROUP BY 1, 2
        ) s;

        INSERT INTO migration_quarantine.orphaned_rows (source_table, reason, row_data)
        SELECT '{{MaskedSource}}', 'column_name_too_long',
               jsonb_build_object('permission_id', p."Id", 'project_id', p."ProjectId", 'entry', i.item, 'ord', i.ord)
        FROM "GatewayTablePermissions" p
        CROSS JOIN LATERAL pg_temp.namines_2c_items(p."MaskedColumns") i
        WHERE char_length(i.item) > {{MaxMaskedColumnLength}};

        -- ── Doldur: etiketler ─────────────────────────────────────────────────────
        INSERT INTO hub.issue_labels ("ProjectId", "Name", "NormalizedName")
        SELECT DISTINCT ON (r."ProjectId", pg_temp.namines_2c_key(i.item))
               r."ProjectId", i.item, pg_temp.namines_2c_key(i.item)
        FROM "RepoIssues" r
        CROSS JOIN LATERAL pg_temp.namines_2c_items(r."Labels") i
        ORDER BY r."ProjectId", pg_temp.namines_2c_key(i.item), r."IssueNumber", i.ord;

        INSERT INTO hub.repo_issue_labels ("RepoIssueId", "IssueLabelId", "DisplayName", "Position")
        SELECT s.issue_id, l."Id", s.item, (row_number() OVER (PARTITION BY s.issue_id ORDER BY s.ord) - 1)::int
        FROM (
            SELECT DISTINCT ON (r."Id", pg_temp.namines_2c_key(i.item))
                   r."Id" AS issue_id, r."ProjectId" AS project_id, i.item, i.ord, pg_temp.namines_2c_key(i.item) AS label_key
            FROM "RepoIssues" r
            CROSS JOIN LATERAL pg_temp.namines_2c_items(r."Labels") i
            ORDER BY r."Id", pg_temp.namines_2c_key(i.item), i.ord
        ) s
        JOIN hub.issue_labels l ON l."ProjectId" = s.project_id AND l."NormalizedName" = s.label_key;

        -- Yaşam döngüsü: son bağ silinince katalog satırı da gider. Issue tek tek silinmiyor (uç yok),
        -- ama FK ile giden her yol buradan geçer ve etiket metni sahipsiz kalmaz.
        CREATE FUNCTION hub.issue_label_gc() RETURNS trigger LANGUAGE plpgsql AS $gc$
        BEGIN
            DELETE FROM hub.issue_labels l
            WHERE l."Id" = OLD."IssueLabelId"
              AND NOT EXISTS (SELECT 1 FROM hub.repo_issue_labels r WHERE r."IssueLabelId" = OLD."IssueLabelId");
            RETURN NULL;
        END $gc$;
        CREATE TRIGGER trg_repo_issue_labels_gc AFTER DELETE ON hub.repo_issue_labels
            FOR EACH ROW EXECUTE FUNCTION hub.issue_label_gc();

        -- roles.sql gateway şemasını bilmeyen eski bir kurulumda çalıştırılmışsa uygulama rolü bu
        -- tablolara erişemezdi; erişim hatası fail-closed ama kesintidir.
        DO $grants$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'namines_app') THEN
                GRANT USAGE ON SCHEMA gateway TO namines_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA gateway TO namines_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON hub.issue_labels, hub.repo_issue_labels TO namines_app;
            END IF;
        END $grants$;
        """;

    public static string UpVerify => $$"""
        DO $verify$
        DECLARE
            missing bigint;
            extra bigint;
            dup bigint;
            orphan bigint;
            flags bigint;
            pos bigint;
            canon bigint;
            quarantine bigint;
        BEGIN
            -- ── Origin: beklenen küme ekleme sorgusundan bağımsız, ham metinden yeniden ayrıştırılır.
            WITH expected AS (
                SELECT DISTINCT k."Id" AS key_id, pg_temp.namines_2c_origin(pg_temp.namines_2c_trim(p)) AS v
                FROM "GatewayApiKeys" k
                CROSS JOIN LATERAL regexp_split_to_table(k."AllowedOrigins", ',') AS p
                WHERE pg_temp.namines_2c_trim(p) <> '' AND pg_temp.namines_2c_origin(pg_temp.namines_2c_trim(p)) IS NOT NULL
            ), actual AS (SELECT "ApiKeyId" AS key_id, "Origin"::text AS v FROM gateway.api_key_allowed_origins)
            SELECT count(*) FILTER (WHERE a.key_id IS NULL), count(*) FILTER (WHERE e.key_id IS NULL) INTO missing, extra
            FROM expected e FULL JOIN actual a ON a.key_id = e.key_id AND a.v = e.v;
            SELECT count(*) INTO dup FROM (SELECT 1 FROM gateway.api_key_allowed_origins GROUP BY "ApiKeyId", "Origin" HAVING count(*) > 1) d;
            SELECT count(*) INTO orphan FROM gateway.api_key_allowed_origins c LEFT JOIN "GatewayApiKeys" k ON k."Id" = c."ApiKeyId" WHERE k."Id" IS NULL;
            SELECT count(*) INTO flags FROM "GatewayApiKeys" k
            WHERE k."RestrictOrigins" IS DISTINCT FROM EXISTS (
                SELECT 1 FROM regexp_split_to_table(k."AllowedOrigins", ',') AS p WHERE pg_temp.namines_2c_trim(p) <> '');
            SELECT count(*) INTO pos FROM (SELECT 1 FROM gateway.api_key_allowed_origins GROUP BY "ApiKeyId"
                HAVING min("Position") <> 0 OR max("Position") <> count(*) - 1 OR count(DISTINCT "Position") <> count(*)) d;
            SELECT count(*) INTO canon FROM gateway.api_key_allowed_origins WHERE pg_temp.namines_2c_origin("Origin") IS DISTINCT FROM "Origin";
            SELECT count(*) INTO quarantine FROM "GatewayApiKeys" k
            CROSS JOIN LATERAL pg_temp.namines_2c_items(k."AllowedOrigins") i
            WHERE pg_temp.namines_2c_origin(i.item) IS NULL AND NOT EXISTS (
                SELECT 1 FROM migration_quarantine.orphaned_rows q
                WHERE q.source_table = '{{OriginsSource}}' AND q.row_data->>'key_id' = k."Id" AND q.row_data->>'entry' = i.item);
            IF missing + extra + dup + orphan + flags + pos + canon + quarantine > 0 THEN
                RAISE EXCEPTION 'Phase14_2C verify origins: missing=%, extra=%, duplicate=%, orphan=%, restrict_flag=%, position=%, canonical=%, quarantine=%',
                    missing, extra, dup, orphan, flags, pos, canon, quarantine;
            END IF;

            -- ── IP ────────────────────────────────────────────────────────────────
            WITH expected AS (
                SELECT DISTINCT k."Id" AS key_id, pg_temp.namines_2c_ip(pg_temp.namines_2c_trim(p)) AS v
                FROM "GatewayApiKeys" k
                CROSS JOIN LATERAL regexp_split_to_table(k."AllowedIps", ',') AS p
                WHERE pg_temp.namines_2c_trim(p) <> '' AND pg_temp.namines_2c_ip(pg_temp.namines_2c_trim(p)) IS NOT NULL
            ), actual AS (SELECT "ApiKeyId" AS key_id, "Cidr"::text AS v FROM gateway.api_key_allowed_ips)
            SELECT count(*) FILTER (WHERE a.key_id IS NULL), count(*) FILTER (WHERE e.key_id IS NULL) INTO missing, extra
            FROM expected e FULL JOIN actual a ON a.key_id = e.key_id AND a.v = e.v;
            SELECT count(*) INTO dup FROM (SELECT 1 FROM gateway.api_key_allowed_ips GROUP BY "ApiKeyId", "Cidr" HAVING count(*) > 1) d;
            SELECT count(*) INTO orphan FROM gateway.api_key_allowed_ips c LEFT JOIN "GatewayApiKeys" k ON k."Id" = c."ApiKeyId" WHERE k."Id" IS NULL;
            SELECT count(*) INTO flags FROM "GatewayApiKeys" k
            WHERE k."RestrictIps" IS DISTINCT FROM EXISTS (
                SELECT 1 FROM regexp_split_to_table(k."AllowedIps", ',') AS p WHERE pg_temp.namines_2c_trim(p) <> '');
            SELECT count(*) INTO pos FROM (SELECT 1 FROM gateway.api_key_allowed_ips GROUP BY "ApiKeyId"
                HAVING min("Position") <> 0 OR max("Position") <> count(*) - 1 OR count(DISTINCT "Position") <> count(*)) d;
            SELECT count(*) INTO canon FROM gateway.api_key_allowed_ips WHERE pg_temp.namines_2c_ip("Cidr") IS DISTINCT FROM "Cidr";
            SELECT count(*) INTO quarantine FROM "GatewayApiKeys" k
            CROSS JOIN LATERAL pg_temp.namines_2c_items(k."AllowedIps") i
            WHERE pg_temp.namines_2c_ip(i.item) IS NULL AND NOT EXISTS (
                SELECT 1 FROM migration_quarantine.orphaned_rows q
                WHERE q.source_table = '{{IpsSource}}' AND q.row_data->>'key_id' = k."Id" AND q.row_data->>'entry' = i.item);
            IF missing + extra + dup + orphan + flags + pos + canon + quarantine > 0 THEN
                RAISE EXCEPTION 'Phase14_2C verify ips: missing=%, extra=%, duplicate=%, orphan=%, restrict_flag=%, position=%, canonical=%, quarantine=%',
                    missing, extra, dup, orphan, flags, pos, canon, quarantine;
            END IF;

            -- ── Maskeler: bayt bayt. Eksik satır burada veri sızıntısı demek.
            WITH expected AS (
                SELECT DISTINCT p."Id" AS perm_id, pg_temp.namines_2c_trim(x) AS v
                FROM "GatewayTablePermissions" p
                CROSS JOIN LATERAL regexp_split_to_table(p."MaskedColumns", ',') AS x
                WHERE pg_temp.namines_2c_trim(x) <> '' AND char_length(pg_temp.namines_2c_trim(x)) <= {{MaxMaskedColumnLength}}
            ), actual AS (SELECT "TablePermissionId" AS perm_id, "ColumnName"::text AS v FROM gateway.table_masked_columns)
            SELECT count(*) FILTER (WHERE a.perm_id IS NULL), count(*) FILTER (WHERE e.perm_id IS NULL) INTO missing, extra
            FROM expected e FULL JOIN actual a ON a.perm_id = e.perm_id AND a.v = e.v;
            SELECT count(*) INTO dup FROM (SELECT 1 FROM gateway.table_masked_columns GROUP BY "TablePermissionId", "ColumnName" HAVING count(*) > 1) d;
            SELECT count(*) INTO orphan FROM gateway.table_masked_columns c LEFT JOIN "GatewayTablePermissions" p ON p."Id" = c."TablePermissionId" WHERE p."Id" IS NULL;
            SELECT count(*) INTO pos FROM (SELECT 1 FROM gateway.table_masked_columns GROUP BY "TablePermissionId"
                HAVING min("Position") <> 0 OR max("Position") <> count(*) - 1 OR count(DISTINCT "Position") <> count(*)) d;
            -- Maskesi olup hiç satırı kalmayan izin: atılan tek meşru girdi çok uzun metin.
            SELECT count(*) INTO flags FROM "GatewayTablePermissions" p
            WHERE EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(p."MaskedColumns") i WHERE char_length(i.item) <= {{MaxMaskedColumnLength}})
              AND NOT EXISTS (SELECT 1 FROM gateway.table_masked_columns m WHERE m."TablePermissionId" = p."Id");
            IF missing + extra + dup + orphan + pos + flags > 0 THEN
                RAISE EXCEPTION 'Phase14_2C verify masked columns: missing=%, extra=%, duplicate=%, orphan=%, position=%, unmasked_permission=%',
                    missing, extra, dup, orphan, pos, flags;
            END IF;

            -- ── Etiketler: yazım dâhil.
            WITH expected AS (
                SELECT DISTINCT ON (r."Id", pg_temp.namines_2c_key(pg_temp.namines_2c_trim(x.p)))
                       r."Id" AS issue_id, pg_temp.namines_2c_key(pg_temp.namines_2c_trim(x.p)) AS label_key, pg_temp.namines_2c_trim(x.p) AS display
                FROM "RepoIssues" r
                CROSS JOIN LATERAL regexp_split_to_table(r."Labels", ',') WITH ORDINALITY AS x(p, o)
                WHERE pg_temp.namines_2c_trim(x.p) <> ''
                ORDER BY r."Id", pg_temp.namines_2c_key(pg_temp.namines_2c_trim(x.p)), x.o
            ), actual AS (
                SELECT l."RepoIssueId" AS issue_id, c."NormalizedName"::text AS label_key, l."DisplayName"::text AS display
                FROM hub.repo_issue_labels l JOIN hub.issue_labels c ON c."Id" = l."IssueLabelId"
            )
            SELECT count(*) FILTER (WHERE a.issue_id IS NULL), count(*) FILTER (WHERE e.issue_id IS NULL) INTO missing, extra
            FROM expected e FULL JOIN actual a ON a.issue_id = e.issue_id AND a.label_key = e.label_key AND a.display = e.display;
            SELECT count(*) INTO dup FROM (SELECT 1 FROM hub.issue_labels GROUP BY "ProjectId", "NormalizedName" HAVING count(*) > 1) d;
            SELECT count(*) INTO orphan FROM hub.repo_issue_labels l
            LEFT JOIN "RepoIssues" r ON r."Id" = l."RepoIssueId"
            LEFT JOIN hub.issue_labels c ON c."Id" = l."IssueLabelId"
            WHERE r."Id" IS NULL OR c."Id" IS NULL OR c."ProjectId" <> r."ProjectId";
            SELECT count(*) INTO pos FROM (SELECT 1 FROM hub.repo_issue_labels GROUP BY "RepoIssueId"
                HAVING min("Position") <> 0 OR max("Position") <> count(*) - 1 OR count(DISTINCT "Position") <> count(*)) d;
            SELECT count(*) INTO canon FROM hub.issue_labels c
            WHERE c."NormalizedName" <> pg_temp.namines_2c_key(c."Name")
               OR NOT EXISTS (SELECT 1 FROM hub.repo_issue_labels l WHERE l."IssueLabelId" = c."Id");
            IF missing + extra + dup + orphan + pos + canon > 0 THEN
                RAISE EXCEPTION 'Phase14_2C verify labels: missing=%, extra=%, duplicate=%, orphan_or_cross_project=%, position=%, catalog=%',
                    missing, extra, dup, orphan, pos, canon;
            END IF;
        END $verify$;
        """;

    public static string UpContract => """
        ALTER TABLE "GatewayApiKeys" DROP COLUMN "AllowedOrigins", DROP COLUMN "AllowedIps";
        ALTER TABLE "GatewayTablePermissions" DROP COLUMN "MaskedColumns";
        ALTER TABLE "RepoIssues" DROP COLUMN "Labels";
        """;

    public static string DownRestore => $$"""
        LOCK TABLE "RepoIssues", "GatewayApiKeys", "GatewayTablePermissions",
            gateway.api_key_allowed_origins, gateway.api_key_allowed_ips, gateway.table_masked_columns,
            hub.issue_labels, hub.repo_issue_labels IN ACCESS EXCLUSIVE MODE;

        {{HelpersSql}}
        {{LabelKeyFunctionSql}}

        ALTER TABLE "GatewayApiKeys" ADD COLUMN "AllowedOrigins" text NULL, ADD COLUMN "AllowedIps" text NULL;
        ALTER TABLE "GatewayTablePermissions" ADD COLUMN "MaskedColumns" text NULL;
        ALTER TABLE "RepoIssues" ADD COLUMN "Labels" character varying(256) NULL;

        DO $restore$
        DECLARE
            keys_saved text[] := pg_temp.namines_2c_lift('"GatewayApiKeys"'::regclass);
            perms_saved text[] := pg_temp.namines_2c_lift('"GatewayTablePermissions"'::regclass);
            issues_saved text[] := pg_temp.namines_2c_lift('"RepoIssues"'::regclass);
        BEGIN
            -- Anahtar listeleri oluştuktan sonra değişmiyor (güncelleme ucu yok), bu yüzden karantinadaki
            -- ham metin hâlâ doğru ve bayt bayt geri döner. Eski kod boş olmayan listeyi uyguladığı için
            -- geçersiz girdilerin geri dönmesi kapalı kalmayı da korur. Karantina kaydı yoksa ama bayrak
            -- açıksa, hiçbir şeyle eşleşmeyen bir girdi aynı sonucu verir: boş liste eski kodda "serbest".
            UPDATE "GatewayApiKeys" k SET
                "AllowedOrigins" = COALESCE(
                    (SELECT q.row_data->>'raw' FROM migration_quarantine.orphaned_rows q
                     WHERE q.source_table = '{{OriginsSource}}' AND q.row_data->>'key_id' = k."Id" LIMIT 1),
                    (SELECT string_agg(o."Origin", ',' ORDER BY o."Position") FROM gateway.api_key_allowed_origins o WHERE o."ApiKeyId" = k."Id"),
                    CASE WHEN k."RestrictOrigins" THEN 'invalid:deny-all' END),
                "AllowedIps" = COALESCE(
                    (SELECT q.row_data->>'raw' FROM migration_quarantine.orphaned_rows q
                     WHERE q.source_table = '{{IpsSource}}' AND q.row_data->>'key_id' = k."Id" LIMIT 1),
                    (SELECT string_agg(regexp_replace(i."Cidr", CASE WHEN position(':' in i."Cidr") > 0 THEN '/128$' ELSE '/32$' END, ''), ',' ORDER BY i."Position")
                     FROM gateway.api_key_allowed_ips i WHERE i."ApiKeyId" = k."Id"),
                    CASE WHEN k."RestrictIps" THEN 'invalid' END);

            -- Çok uzun adlar da geri gelir: bir maske listesini kısaltarak geri dönmek olmaz.
            UPDATE "GatewayTablePermissions" p SET "MaskedColumns" = NULLIF(concat_ws(',',
                (SELECT string_agg(m."ColumnName", ',' ORDER BY m."Position") FROM gateway.table_masked_columns m WHERE m."TablePermissionId" = p."Id"),
                (SELECT string_agg(q.row_data->>'entry', ',' ORDER BY (q.row_data->>'ord')::bigint) FROM migration_quarantine.orphaned_rows q
                 WHERE q.source_table = '{{MaskedSource}}' AND q.row_data->>'permission_id' = p."Id")), '');

            UPDATE "RepoIssues" r SET "Labels" = (
                SELECT string_agg(l."DisplayName", ',' ORDER BY l."Position") FROM hub.repo_issue_labels l WHERE l."RepoIssueId" = r."Id");

            PERFORM pg_temp.namines_2c_restore('"GatewayApiKeys"'::regclass, keys_saved);
            PERFORM pg_temp.namines_2c_restore('"GatewayTablePermissions"'::regclass, perms_saved);
            PERFORM pg_temp.namines_2c_restore('"RepoIssues"'::regclass, issues_saved);
        END $restore$;
        """;

    public static string DownVerify => $$"""
        DO $verify$
        DECLARE
            lost bigint;
            widened bigint;
        BEGIN
            -- Her alt tablo satırı geri dönen metnin içinde olmalı.
            SELECT count(*) INTO lost FROM gateway.api_key_allowed_origins o
            JOIN "GatewayApiKeys" k ON k."Id" = o."ApiKeyId"
            WHERE NOT EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(k."AllowedOrigins") i WHERE pg_temp.namines_2c_origin(i.item) = o."Origin");
            -- Kısıtlı anahtarın metni eski kodda da kısıtlı okunmalı: boş liste = serbest.
            SELECT count(*) INTO widened FROM "GatewayApiKeys" k
            WHERE k."RestrictOrigins" AND NOT EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(k."AllowedOrigins"));
            IF lost + widened > 0 THEN
                RAISE EXCEPTION 'Phase14_2C down verify origins: lost=%, widened=%', lost, widened;
            END IF;

            SELECT count(*) INTO lost FROM gateway.api_key_allowed_ips o
            JOIN "GatewayApiKeys" k ON k."Id" = o."ApiKeyId"
            WHERE NOT EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(k."AllowedIps") i WHERE pg_temp.namines_2c_ip(i.item) = o."Cidr");
            SELECT count(*) INTO widened FROM "GatewayApiKeys" k
            WHERE k."RestrictIps" AND NOT EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(k."AllowedIps"));
            IF lost + widened > 0 THEN
                RAISE EXCEPTION 'Phase14_2C down verify ips: lost=%, widened=%', lost, widened;
            END IF;

            SELECT count(*) INTO lost FROM gateway.table_masked_columns m
            JOIN "GatewayTablePermissions" p ON p."Id" = m."TablePermissionId"
            WHERE NOT EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(p."MaskedColumns") i WHERE i.item = m."ColumnName");
            IF lost > 0 THEN
                RAISE EXCEPTION 'Phase14_2C down verify masked columns: lost=%', lost;
            END IF;

            SELECT count(*) INTO lost FROM hub.repo_issue_labels l
            JOIN "RepoIssues" r ON r."Id" = l."RepoIssueId"
            WHERE NOT EXISTS (SELECT 1 FROM pg_temp.namines_2c_items(r."Labels") i WHERE i.item = l."DisplayName");
            IF lost > 0 THEN
                RAISE EXCEPTION 'Phase14_2C down verify labels: lost=%', lost;
            END IF;
        END $verify$;
        """;

    public static string DownContract => $$"""
        -- Geri dönen veriyi taşıyan karantina satırları kalkar; Up yeniden üretir. Anahtarı silinmiş
        -- satırlar iz olarak kalır, onlar için geri dönecek yer yok.
        DELETE FROM migration_quarantine.orphaned_rows q
        WHERE (q.source_table IN ('{{OriginsSource}}', '{{IpsSource}}')
               AND EXISTS (SELECT 1 FROM "GatewayApiKeys" k WHERE k."Id" = q.row_data->>'key_id'))
           OR (q.source_table = '{{MaskedSource}}'
               AND EXISTS (SELECT 1 FROM "GatewayTablePermissions" p WHERE p."Id" = q.row_data->>'permission_id'));

        DROP TRIGGER trg_repo_issue_labels_gc ON hub.repo_issue_labels;
        DROP FUNCTION hub.issue_label_gc();
        DROP TABLE hub.repo_issue_labels;
        DROP TABLE hub.issue_labels;
        DROP TABLE gateway.table_masked_columns;
        DROP TABLE gateway.api_key_allowed_ips;
        DROP TABLE gateway.api_key_allowed_origins;
        ALTER TABLE "GatewayApiKeys" DROP COLUMN "RestrictOrigins", DROP COLUMN "RestrictIps";
        """;
}
