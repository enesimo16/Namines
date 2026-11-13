using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Governance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Denetim saklama TABANI veritabanında, sahibe ait (14.2D-2 denetimi, kabul edilen borç 5).
    ///
    /// <b>Kapatılan açık:</b> <c>purge_change_request_audit</c> yalnızca 90 günlük sabit tabanı zorluyordu, kesim
    /// tarihini ise ÇAĞIRAN seçiyordu. Varsayılan saklama 730 gün olduğu hâlde ele geçirilmiş bir uygulama süreci ya da
    /// yanlış bir <c>Retention:ChangeRequestAuditDays</c> değeri, 90–730 gün arasındaki denetim satırlarını silebiliyordu.
    ///
    /// <b>Şimdi:</b> taban <c>governance.audit_retention_floor_days()</c> işlevinde. Uygulama rolü (namines_app) sahibi
    /// olmadığı işlevi değiştiremez ve <c>GrantAppRole</c> yalnızca tablo/dizi yetkisi verdiği için işlev bu kapsama hiç
    /// girmez. Temizlik işlevi çağıranın kesimini tabana KISTIRIR (hata atmaz): uygulama yalnızca politikadan DAHA AZ
    /// silebilir, daha fazla değil. Daha kısa saklama isteyen operatör tabanı sahip rolüyle değiştirir:
    /// <c>CREATE OR REPLACE FUNCTION governance.audit_retention_floor_days() RETURNS integer LANGUAGE sql IMMUTABLE AS 'SELECT 365';</c>
    /// (90'ın altı yine reddedilir).
    /// </summary>
    public partial class Governance_02_AuditRetentionFloor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION governance.audit_retention_floor_days() RETURNS integer
                LANGUAGE sql IMMUTABLE AS 'SELECT 730';
                REVOKE ALL ON FUNCTION governance.audit_retention_floor_days() FROM PUBLIC;

                CREATE OR REPLACE FUNCTION governance.purge_change_request_audit(cutoff timestamptz, batch_size integer) RETURNS integer
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = governance, pg_temp AS $fn$
                DECLARE
                  removed integer;
                  floor_days integer := GREATEST(governance.audit_retention_floor_days(), 90);
                  effective timestamptz;
                BEGIN
                  -- Sabit taban: 90 günden yeni satır hiçbir koşulda silinmez (önceki sözleşme korunuyor).
                  IF cutoff > now() - interval '90 days' THEN
                    RAISE EXCEPTION 'Audit rows newer than 90 days cannot be purged' USING ERRCODE = '22023';
                  END IF;
                  -- Politika tabanı: çağıranın kesimi daha yeniyse tabana çekilir; uygulama politikadan fazla silemez.
                  effective := LEAST(cutoff, now() - make_interval(days => floor_days));
                  PERFORM set_config('namines.audit_retention', 'on', true);
                  WITH doomed AS (
                    SELECT "Id" FROM governance."ChangeRequestAuditLogs" WHERE "CreatedAt" < effective ORDER BY "Id" LIMIT batch_size)
                  DELETE FROM governance."ChangeRequestAuditLogs" a USING doomed WHERE a."Id" = doomed."Id";
                  GET DIAGNOSTICS removed = ROW_COUNT;
                  PERFORM set_config('namines.audit_retention', 'off', true);
                  RETURN removed;
                END
                $fn$;
                REVOKE ALL ON FUNCTION governance.purge_change_request_audit(timestamptz, integer) FROM PUBLIC;
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'namines_app') THEN
                    GRANT EXECUTE ON FUNCTION governance.purge_change_request_audit(timestamptz, integer) TO namines_app;
                  END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Governance_01'deki işlev gövdesine dönülür (yalnızca 90 gün tabanı).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION governance.purge_change_request_audit(cutoff timestamptz, batch_size integer) RETURNS integer
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = governance, pg_temp AS $fn$
                DECLARE removed integer;
                BEGIN
                  IF cutoff > now() - interval '90 days' THEN
                    RAISE EXCEPTION 'Audit rows newer than 90 days cannot be purged' USING ERRCODE = '22023';
                  END IF;
                  PERFORM set_config('namines.audit_retention', 'on', true);
                  WITH doomed AS (
                    SELECT "Id" FROM governance."ChangeRequestAuditLogs" WHERE "CreatedAt" < cutoff ORDER BY "Id" LIMIT batch_size)
                  DELETE FROM governance."ChangeRequestAuditLogs" a USING doomed WHERE a."Id" = doomed."Id";
                  GET DIAGNOSTICS removed = ROW_COUNT;
                  PERFORM set_config('namines.audit_retention', 'off', true);
                  RETURN removed;
                END
                $fn$;
                DROP FUNCTION IF EXISTS governance.audit_retention_floor_days();
                """);
        }
    }
}
