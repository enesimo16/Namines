using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <summary>
    /// Gateway (GatewayApiKeys, GatewayTablePermissions, GatewayAuditEntries ve çocuk listeler) tablolarının Auth modelinden çıkışı. Up/Down BİLEREK boş: tablolar ve verisi yerinde
    /// kalır, modül zincirinin ilk migration'ı onları kendi şemasına taşır. EF'in burada ürettiği DROP
    /// TABLE silindi; snapshot modelin yeni hâlini tutar. Bkz. MigrationHandoffTests.
    /// </summary>
    public partial class Phase14_2D1_HandoffGateway : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
