using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <summary>
    /// Vault/Ground'daki OrganizationId kopyaları hiç okunmuyordu, FK'siz ve projeden türetilebilir (3NF ihlali,
    /// BL-8). Sütunlar kalkıyor; geri dönüşte projeden yeniden doldurulur: tek kayıp, proje sonradan org değiştirdiyse
    /// eski org bilgisidir, onu okuyan kod zaten yoktu.
    /// </summary>
    public partial class Phase14_2D1_DropModuleOrganizationCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrganizationId",
                schema: "namines_vault",
                table: "vault_schedules");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                schema: "namines_vault",
                table: "vault_restores");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                schema: "namines_vault",
                table: "vault_backups");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                schema: "ground",
                table: "ground_databases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrganizationId",
                schema: "namines_vault",
                table: "vault_schedules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizationId",
                schema: "namines_vault",
                table: "vault_restores",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizationId",
                schema: "namines_vault",
                table: "vault_backups",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizationId",
                schema: "ground",
                table: "ground_databases",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""UPDATE namines_vault.vault_schedules t SET "OrganizationId" = p."OrganizationId" FROM studio.projects p WHERE p."Id" = t."ProjectId";""");
            migrationBuilder.Sql("""UPDATE namines_vault.vault_restores t SET "OrganizationId" = p."OrganizationId" FROM studio.projects p WHERE p."Id" = t."ProjectId";""");
            migrationBuilder.Sql("""UPDATE namines_vault.vault_backups t SET "OrganizationId" = p."OrganizationId" FROM studio.projects p WHERE p."Id" = t."ProjectId";""");
            migrationBuilder.Sql("""UPDATE ground.ground_databases t SET "OrganizationId" = p."OrganizationId" FROM studio.projects p WHERE p."Id" = t."ProjectId";""");
        }
    }
}
