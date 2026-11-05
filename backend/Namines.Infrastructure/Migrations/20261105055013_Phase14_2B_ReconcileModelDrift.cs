using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase14_2B_ReconcileModelDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Yalnızca PostgreSQL: SQLite tabanlı testler EnsureCreated ile modelden kuruyor.
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL") return;

            // Düz metin kolon düşmeden ÖNCE özeti olmayan eski token'lar özetlenir; böylece hiçbir
            // link kaybolmaz ve hiçbir düz metin kalmaz. Özeti zaten olan satırda o özet kalır:
            // anonim uçlar ona bakıyor, yani çalışan bağlantı odur. Çakışmada IX_CloudProjects_ShareTokenHash
            // migration'ı durdurur (192-bit token'da beklenmez; sessizce bağlantı iptal edilmez).
            migrationBuilder.Sql("""
                UPDATE "CloudProjects"
                SET "ShareTokenHash" = encode(sha256(convert_to("ShareToken", 'UTF8')), 'hex')
                WHERE NULLIF("ShareToken", '') IS NOT NULL
                  AND NULLIF("ShareTokenHash", '') IS NULL;

                DO $verify$
                DECLARE n bigint;
                BEGIN
                    SELECT count(*) INTO n FROM "CloudProjects"
                    WHERE NULLIF("ShareToken", '') IS NOT NULL AND NULLIF("ShareTokenHash", '') IS NULL;
                    IF n > 0 THEN
                        RAISE EXCEPTION 'Phase14_2B drift: % share token(s) left without a hash; aborting before the plaintext column is dropped.', n;
                    END IF;
                END $verify$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ground_databases_Organizations_OrganizationId",
                schema: "ground",
                table: "ground_databases");

            migrationBuilder.DropForeignKey(
                name: "FK_vault_backups_Organizations_OrganizationId",
                schema: "namines_vault",
                table: "vault_backups");

            migrationBuilder.DropForeignKey(
                name: "FK_vault_restores_Organizations_OrganizationId",
                schema: "namines_vault",
                table: "vault_restores");

            migrationBuilder.DropForeignKey(
                name: "FK_vault_schedules_Organizations_OrganizationId",
                schema: "namines_vault",
                table: "vault_schedules");

            migrationBuilder.DropIndex(
                name: "IX_vault_schedules_OrganizationId",
                schema: "namines_vault",
                table: "vault_schedules");

            migrationBuilder.DropIndex(
                name: "IX_vault_restores_OrganizationId",
                schema: "namines_vault",
                table: "vault_restores");

            migrationBuilder.DropIndex(
                name: "IX_vault_backups_OrganizationId",
                schema: "namines_vault",
                table: "vault_backups");

            migrationBuilder.DropIndex(
                name: "IX_ground_databases_OrganizationId",
                schema: "ground",
                table: "ground_databases");

            migrationBuilder.DropColumn(
                name: "ShareToken",
                table: "CloudProjects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL") return;

            // Düz metin geri GELMEZ (özetten türetilemez): kolon boş açılır, linkler özetle çalışır.
            migrationBuilder.AddColumn<string>(
                name: "ShareToken",
                table: "CloudProjects",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vault_schedules_OrganizationId",
                schema: "namines_vault",
                table: "vault_schedules",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_vault_restores_OrganizationId",
                schema: "namines_vault",
                table: "vault_restores",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_vault_backups_OrganizationId",
                schema: "namines_vault",
                table: "vault_backups",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ground_databases_OrganizationId",
                schema: "ground",
                table: "ground_databases",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ground_databases_Organizations_OrganizationId",
                schema: "ground",
                table: "ground_databases",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vault_backups_Organizations_OrganizationId",
                schema: "namines_vault",
                table: "vault_backups",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vault_restores_Organizations_OrganizationId",
                schema: "namines_vault",
                table: "vault_restores",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_vault_schedules_Organizations_OrganizationId",
                schema: "namines_vault",
                table: "vault_schedules",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
