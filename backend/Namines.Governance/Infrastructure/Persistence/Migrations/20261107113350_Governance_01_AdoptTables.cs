using Microsoft.EntityFrameworkCore.Migrations;
using Namines.BuildingBlocks.Persistence;

#nullable disable

namespace Namines.Governance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Governance tablolarını AuthDbContext zincirinden devralır (satır kopyalanmaz, <c>SET SCHEMA</c>) ve denetim
    /// izini DEĞİŞTİRİLEMEZ yapar: <c>ChangeRequestAuditLogs</c> üzerinde UPDATE/DELETE/TRUNCATE tetikleyicisi.
    ///
    /// Tetikleyicinin iki bilinçli istisnası var: (1) hesap silinince <c>ActorUserId → NULL</c> yapan FK anonimleştirmesi
    /// (başka hiçbir alan değişmeyen UPDATE); (2) saklama süresi temizliği: DELETE yalnızca tablo sahibinin (migrator)
    /// çalıştırdığı <c>governance.purge_change_request_audit</c> SECURITY DEFINER işlevinden geçer ve işlev 90 günden yeni
    /// satıra dokunmaz. Uygulama rolü oturum ayarını kendisi açsa bile <c>current_user</c> sahip olmadığı için reddedilir.
    /// Tablo sahibi tetikleyiciyi kapatabilir: bu veritabanı
    /// yöneticisine karşı değil, uygulama koduna ve yanlış yazılmış bir sorguya karşı koruma.
    ///
    /// Ayrıca eylem kısıtına yeni üye (8, abonelik iptali tamamlandı) eklenir. NOT VALID + VALIDATE: yeni yazımlar hemen
    /// zorunlu, tablo taraması yazmaları engellemeyen kilitle.
    /// </summary>
    public partial class Governance_01_AdoptTables : Migration
    {
        private static readonly string[] Tables = { "ChangeRequests", "ChangeRequestApprovals", "ChangeRequestAuditLogs", "UserAIPolicies" };

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ModuleTableHandoff.BeginMove(migrationBuilder, "governance");
            ModuleTableHandoff.MoveTables(migrationBuilder, "public", "governance", Tables);

            migrationBuilder.Sql("""
                ALTER TABLE governance."ChangeRequestAuditLogs" DROP CONSTRAINT IF EXISTS "CK_ChangeRequestAuditLogs_Action";
                ALTER TABLE governance."ChangeRequestAuditLogs"
                  ADD CONSTRAINT "CK_ChangeRequestAuditLogs_Action" CHECK ("Action" IN (0, 1, 2, 3, 4, 5, 6, 7, 8)) NOT VALID;
                ALTER TABLE governance."ChangeRequestAuditLogs" VALIDATE CONSTRAINT "CK_ChangeRequestAuditLogs_Action";
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION governance.change_request_audit_guard() RETURNS trigger
                LANGUAGE plpgsql AS $fn$
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    -- Ayar tek başına yetmez: silen kullanıcı tablo sahibi (SECURITY DEFINER işlevi) olmalı.
                    IF current_setting('namines.audit_retention', true) = 'on'
                       AND current_user::name = (SELECT pg_get_userbyid(relowner) FROM pg_class WHERE oid = TG_RELID)::name THEN
                      RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'ChangeRequestAuditLogs is append-only: DELETE is only allowed for retention' USING ERRCODE = '42501';
                  ELSIF TG_OP = 'UPDATE' THEN
                    IF OLD."ActorUserId" IS NOT NULL AND NEW."ActorUserId" IS NULL
                       AND NEW."Id" = OLD."Id"
                       AND NEW."ChangeRequestId" = OLD."ChangeRequestId"
                       AND NEW."ProjectId" = OLD."ProjectId"
                       AND NEW."ChangeRequestTitle" IS NOT DISTINCT FROM OLD."ChangeRequestTitle"
                       AND NEW."Action" = OLD."Action"
                       AND NEW."Details" IS NOT DISTINCT FROM OLD."Details"
                       AND NEW."CreatedAt" = OLD."CreatedAt" THEN
                      RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'ChangeRequestAuditLogs is append-only: UPDATE is not allowed' USING ERRCODE = '42501';
                  END IF;
                  RAISE EXCEPTION 'ChangeRequestAuditLogs is append-only: % is not allowed', TG_OP USING ERRCODE = '42501';
                END
                $fn$;

                CREATE TRIGGER change_request_audit_row_guard
                  BEFORE UPDATE OR DELETE ON governance."ChangeRequestAuditLogs"
                  FOR EACH ROW EXECUTE FUNCTION governance.change_request_audit_guard();

                CREATE TRIGGER change_request_audit_truncate_guard
                  BEFORE TRUNCATE ON governance."ChangeRequestAuditLogs"
                  FOR EACH STATEMENT EXECUTE FUNCTION governance.change_request_audit_guard();
                """);

            // Saklama süresi temizliğinin TEK yolu. SECURITY DEFINER: işlev sahibi (migrator) olarak çalışır, uygulama rolü
            // ise yalnızca bu işlevi çağırabilir ve 90 günden yeni denetim satırını silemez.
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
                REVOKE ALL ON FUNCTION governance.purge_change_request_audit(timestamptz, integer) FROM PUBLIC;
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'namines_app') THEN
                    GRANT EXECUTE ON FUNCTION governance.purge_change_request_audit(timestamptz, integer) TO namines_app;
                  END IF;
                END $$;
                """);

            ModuleTableHandoff.GrantAppRole(migrationBuilder, "governance");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"SET LOCAL lock_timeout = '{ModuleTableHandoff.LockTimeout}';");

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS change_request_audit_truncate_guard ON governance."ChangeRequestAuditLogs";
                DROP TRIGGER IF EXISTS change_request_audit_row_guard ON governance."ChangeRequestAuditLogs";
                DROP FUNCTION IF EXISTS governance.purge_change_request_audit(timestamptz, integer);
                DROP FUNCTION IF EXISTS governance.change_request_audit_guard();
                """);

            // Yeni eylemle yazılmış satır varsa kısıt genişliği korunur: denetim satırı silmek ya da bozmak kabul edilemez.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF NOT EXISTS (SELECT 1 FROM governance."ChangeRequestAuditLogs" WHERE "Action" = 8) THEN
                    ALTER TABLE governance."ChangeRequestAuditLogs" DROP CONSTRAINT IF EXISTS "CK_ChangeRequestAuditLogs_Action";
                    ALTER TABLE governance."ChangeRequestAuditLogs"
                      ADD CONSTRAINT "CK_ChangeRequestAuditLogs_Action" CHECK ("Action" IN (0, 1, 2, 3, 4, 5, 6, 7)) NOT VALID;
                    ALTER TABLE governance."ChangeRequestAuditLogs" VALIDATE CONSTRAINT "CK_ChangeRequestAuditLogs_Action";
                  ELSE
                    RAISE NOTICE 'Audit rows with Action = 8 exist; the CHECK constraint keeps the wider list.';
                  END IF;
                END $$;
                """);

            ModuleTableHandoff.MoveTables(migrationBuilder, "governance", "public", Tables);
        }
    }
}
