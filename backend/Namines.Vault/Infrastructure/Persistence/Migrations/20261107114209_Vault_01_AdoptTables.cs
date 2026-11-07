using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Vault.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Vault tablolarının zincir sahipliğini AuthDbContext'ten devralır. Veri ve tablolar yerinde kalır, yalnızca birincil anahtar adları modele hizalanır: tablolar `namines_vault`
    /// şemasında, 14.2 öncesi Auth migration'larıyla kuruldu (geçmiş yeniden yazılmaz) ve veri yerinde kalır. Bu
    /// migration modelin anlık görüntüsünü bu zincire taşır; bundan sonraki her DDL değişikliği Vault zincirinin.
    /// Auth tarafındaki eşi `Phase14_2D2_HandoffModules` da boş. Bkz. MigrationHandoffTests.
    /// </summary>
    public partial class Vault_01_AdoptTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Birincil anahtar adlari 14.2 oncesi Auth migration'larindan PascalCase kaldi; model snake_case bekler.
            // Yalnizca ad degisir (katalog), veri ve kilit etkilenmez; Down eski adlari geri verir.
            migrationBuilder.Sql("""ALTER TABLE namines_vault."vault_backups" RENAME CONSTRAINT "PK_VaultBackups" TO "PK_vault_backups";""");
            migrationBuilder.Sql("""ALTER TABLE namines_vault."vault_restores" RENAME CONSTRAINT "PK_VaultRestores" TO "PK_vault_restores";""");
            migrationBuilder.Sql("""ALTER TABLE namines_vault."vault_schedules" RENAME CONSTRAINT "PK_VaultSchedules" TO "PK_vault_schedules";""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE namines_vault."vault_backups" RENAME CONSTRAINT "PK_vault_backups" TO "PK_VaultBackups";""");
            migrationBuilder.Sql("""ALTER TABLE namines_vault."vault_restores" RENAME CONSTRAINT "PK_vault_restores" TO "PK_VaultRestores";""");
            migrationBuilder.Sql("""ALTER TABLE namines_vault."vault_schedules" RENAME CONSTRAINT "PK_vault_schedules" TO "PK_VaultSchedules";""");
        }
    }
}
