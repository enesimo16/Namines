-- Kontrol DB'de yavaş sorgu analizi için pg_stat_statements (DB-46).
-- docker-entrypoint-initdb.d yalnızca BOŞ veri dizininde çalışır; mevcut kurulumda
-- aynı komutu elle çalıştırın:
--   docker compose exec namines-control-db psql -U namines -d namines_control -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements;"
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
