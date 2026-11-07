using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Vault.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Vault tablolarının zincir sahipliğini AuthDbContext'ten devralır. Up/Down BİLEREK boş: tablolar `namines_vault`
    /// şemasında, 14.2 öncesi Auth migration'larıyla kuruldu (geçmiş yeniden yazılmaz) ve veri yerinde kalır. Bu
    /// migration modelin anlık görüntüsünü bu zincire taşır; bundan sonraki her DDL değişikliği Vault zincirinin.
    /// Auth tarafındaki eşi `Phase14_2D2_HandoffModules` da boş. Bkz. MigrationHandoffTests.
    /// </summary>
    public partial class Vault_01_AdoptTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
