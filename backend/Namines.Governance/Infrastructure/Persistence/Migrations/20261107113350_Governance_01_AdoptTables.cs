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
    /// (başka hiçbir alan değişmeyen UPDATE); (2) saklama süresi temizliği, yalnızca <c>namines.audit_retention = on</c>
    /// olan işlemde DELETE (GovernanceAuditService.PurgeAuditAsync). Tablo sahibi tetikleyiciyi kapatabilir: bu veritabanı
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
                    IF current_setting('namines.audit_retention', true) = 'on' THEN
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

            ModuleTableHandoff.GrantAppRole(migrationBuilder, "governance");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"SET LOCAL lock_timeout = '{ModuleTableHandoff.LockTimeout}';");

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS change_request_audit_truncate_guard ON governance."ChangeRequestAuditLogs";
                DROP TRIGGER IF EXISTS change_request_audit_row_guard ON governance."ChangeRequestAuditLogs";
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
