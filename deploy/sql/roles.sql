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
--   1) Bu betiği çalıştırın (idempotent; tekrar çalıştırılabilir).
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

GRANT CONNECT ON DATABASE :"dbname" TO namines_migrator, namines_app;

-- Namines şemaları: kontrol DB'de uygulama tabloları 'public' şemasındadır.
-- Başka şema eklenirse listeye ekleyin.
DO $$
DECLARE s text;
BEGIN
  FOREACH s IN ARRAY ARRAY['public'] LOOP
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

-- Mevcut tabloların sahipliğini migrator'a devret (yoksa migration'daki ALTER TABLE
-- sahibi olmayan rolle başarısız olur). Yalnızca bu betiği çalıştıran rol sahipse işe yarar.
DO $$
DECLARE r record;
BEGIN
  FOR r IN SELECT schemaname, tablename FROM pg_tables WHERE schemaname = 'public' LOOP
    EXECUTE format('ALTER TABLE %I.%I OWNER TO namines_migrator', r.schemaname, r.tablename);
  END LOOP;
  FOR r IN SELECT sequence_schema AS s, sequence_name AS n FROM information_schema.sequences WHERE sequence_schema = 'public' LOOP
    EXECUTE format('ALTER SEQUENCE %I.%I OWNER TO namines_migrator', r.s, r.n);
  END LOOP;
END $$;

-- pg_stat_statements görünürlüğü (yavaş sorgu analizi için, opsiyonel):
-- GRANT pg_read_all_stats TO namines_migrator;

-- NEXT STEP (plan, bu betikte YOK): Postgres Row Level Security (RLS) ile kiracı
-- izolasyonu — her tabloya UserId/OrganizationId politikası ve uygulama rolünün
-- `SET app.current_user_id` ile oturum değişkeni verilmesi. Ayrı tasarım gerektirir.
