-- =============================================================================
-- Namines kontrol DB — en az yetkili rol kurulumu (DB-38, ilk adım). OPSİYONEL.
--
--   namines_migrator : şema SAHİBİ. Yalnızca `--migrate` adımı kullanır
--                      (ConnectionStrings__Migrations).
--   namines_app      : uygulama çalışma zamanı. Yalnızca DML (SELECT/INSERT/UPDATE/DELETE);
--                      DDL, DROP, TRUNCATE yetkisi YOK (ConnectionStrings__DefaultConnection).
--
-- Çalıştırma (superuser veya CREATEROLE yetkili rolle, hedef veritabanına bağlıyken):
--   psql "$ADMIN_URL" -v ON_ERROR_STOP=1 \
--        -v migrator_password="'...'" -v app_password="'...'" -v dbname=namines_control \
--        -f deploy/sql/roles.sql
--
-- Geçiş sırası (mevcut kurulum, kesintisiz):
--   1) Bu betiği çalıştırın (idempotent; tekrar çalıştırılabilir). Süper kullanıcıyla kurulmuş bir
--      veritabanında her yükseltmeden önce yeniden çalıştırın: sahipliği eski rolde kalan nesneyi değiştiren
--      migration (ör. Hub_02 ile tetikleyici fonksiyonu) migrator ile 42501 alır.
--   2) Migration'ı ConnectionStrings__Migrations (namines_migrator) ile çalıştırın:
--      yeni tablolar migrator'a ait olur, default privileges app'e DML verir.
--   3) DefaultConnection'ı namines_app'e çevirin.
-- =============================================================================

DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'namines_migrator') THEN
    CREATE ROLE namines_migrator LOGIN;
  END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'namines_app') THEN
    CREATE ROLE namines_app LOGIN;
  END IF;
END $$;

ALTER ROLE namines_migrator PASSWORD :migrator_password;
ALTER ROLE namines_app      PASSWORD :app_password;
ALTER ROLE namines_app NOSUPERUSER NOCREATEDB NOCREATEROLE;

GRANT CONNECT, CREATE ON DATABASE :"dbname" TO namines_migrator;
GRANT CONNECT ON DATABASE :"dbname" TO namines_app;

-- Namines şemaları: modül tabloları kendi şemalarında (flow, namines_vault, ground, desk, studio,
-- hub, gateway, ops), kalanı 'public'te. Şemalar burada migrator'a ait açılır ki aşağıdaki app yetkileri bugünden
-- geçerli olsun. Migrator'ın veritabanı CREATE yetkisi de var: migration'lar yeni şema (EnsureSchema,
-- geçici karantina vb.) açabilmeli. Yeni şema eklenirse listeye ekleyin; yoksa app ona erişemez.
DO $$
DECLARE s text;
BEGIN
  FOREACH s IN ARRAY ARRAY['public', 'flow', 'namines_vault', 'ground', 'desk', 'studio', 'hub', 'gateway', 'ops'] LOOP
    EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION namines_migrator', s);
    EXECUTE format('GRANT USAGE, CREATE ON SCHEMA %I TO namines_migrator', s);
    EXECUTE format('GRANT USAGE ON SCHEMA %I TO namines_app', s);
    EXECUTE format('REVOKE CREATE ON SCHEMA %I FROM namines_app', s);

    -- Mevcut nesneler: app'e yalnızca DML + sequence kullanımı.
    EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO namines_app', s);
    EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO namines_app', s);

    -- Gelecekte migrator'ın oluşturacağı nesneler için otomatik yetki.
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE namines_migrator IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO namines_app', s);
    EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE namines_migrator IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO namines_app', s);
  END LOOP;
END $$;

-- Mevcut nesnelerin sahipliğini migrator'a devret (yoksa migration'daki ALTER/CREATE OR REPLACE sahibi
-- olmayan rolle 42501 ile düşer). Yalnızca bu betiği çalıştıran rol sahipse işe yarar.
-- Tablo ve dizi yetmez: süper kullanıcıyla kurulmuş bir veritabanında fonksiyonlar (ör. hub.issue_label_gc
-- tetikleyici fonksiyonu), tipler, görünümler ve şemaların kendisi de eski sahibinde kalır; sonraki migration
-- onları değiştiremez (denetim m-2 ops, R2-M1 fonksiyon). Eklentiye ait nesneler eklentiyle yönetilir, atlanır.
-- migration_quarantine yalnız sahiplik listesinde: uygulama rolü ona erişmez, ama 14.2C Down'ı onu okur.
DO $$
DECLARE
  owned text[] := ARRAY['public','flow','namines_vault','ground','desk','studio','hub','gateway','ops','migration_quarantine'];
  r record;
BEGIN
  -- Şemalar. public paylaşılır (PG15+ sahibi pg_database_owner), devredilmez; migrator'ın orada CREATE'i var.
  FOR r IN SELECT nspname FROM pg_namespace WHERE nspname = ANY (owned) AND nspname <> 'public' LOOP
    EXECUTE format('ALTER SCHEMA %I OWNER TO namines_migrator', r.nspname);
  END LOOP;
  IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'migration_quarantine') THEN
    GRANT USAGE, CREATE ON SCHEMA migration_quarantine TO namines_migrator;
  END IF;

  -- Tablolar, bölümlü tablolar, görünümler, gerçekleştirilmiş görünümler, yabancı tablolar.
  FOR r IN
    SELECT n.nspname AS s, c.relname AS n, c.relkind AS k
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = ANY (owned) AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
      AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e')
  LOOP
    EXECUTE format(CASE r.k
        WHEN 'v' THEN 'ALTER VIEW %I.%I OWNER TO namines_migrator'
        WHEN 'm' THEN 'ALTER MATERIALIZED VIEW %I.%I OWNER TO namines_migrator'
        WHEN 'f' THEN 'ALTER FOREIGN TABLE %I.%I OWNER TO namines_migrator'
        ELSE 'ALTER TABLE %I.%I OWNER TO namines_migrator' END, r.s, r.n);
  END LOOP;

  FOR r IN SELECT sequence_schema AS s, sequence_name AS n FROM information_schema.sequences WHERE sequence_schema = ANY (owned) LOOP
    EXECUTE format('ALTER SEQUENCE %I.%I OWNER TO namines_migrator', r.s, r.n);
  END LOOP;

  -- Fonksiyon, tetikleyici fonksiyonu, prosedür, toplama.
  FOR r IN
    SELECT p.oid::regprocedure::text AS sig, p.prokind AS k
    FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
    WHERE n.nspname = ANY (owned)
      AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_proc'::regclass AND d.objid = p.oid AND d.deptype = 'e')
  LOOP
    EXECUTE format(CASE r.k
        WHEN 'a' THEN 'ALTER AGGREGATE %s OWNER TO namines_migrator'
        WHEN 'p' THEN 'ALTER PROCEDURE %s OWNER TO namines_migrator'
        ELSE 'ALTER FUNCTION %s OWNER TO namines_migrator' END, r.sig);
  END LOOP;

  -- Kullanıcı tipleri: enum, domain, range ve tek başına bileşik tip. Dizi, çoklu aralık ve tablo satır
  -- tipleri sahibini kendi nesnesinden alır, doğrudan değiştirilemez.
  FOR r IN
    SELECT t.oid::regtype::text AS t, t.typtype AS k
    FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
    WHERE n.nspname = ANY (owned)
      AND (t.typtype IN ('e', 'd', 'r')
           OR (t.typtype = 'c' AND EXISTS (SELECT 1 FROM pg_class c WHERE c.oid = t.typrelid AND c.relkind = 'c')))
      AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid = 'pg_type'::regclass AND d.objid = t.oid AND d.deptype = 'e')
  LOOP
    EXECUTE format(CASE r.k WHEN 'd' THEN 'ALTER DOMAIN %s OWNER TO namines_migrator' ELSE 'ALTER TYPE %s OWNER TO namines_migrator' END, r.t);
  END LOOP;
END $$;

-- pg_stat_statements görünürlüğü (yavaş sorgu analizi için, opsiyonel):
-- GRANT pg_read_all_stats TO namines_migrator;

-- NEXT STEP (plan, bu betikte YOK): Postgres Row Level Security (RLS) ile kiracı
-- izolasyonu — her tabloya UserId/OrganizationId politikası ve uygulama rolünün
-- `SET app.current_user_id` ile oturum değişkeni verilmesi. Ayrı tasarım gerektirir.
