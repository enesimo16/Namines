using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Feedbacks, SqlExecutionAudits, FeatureFlags, AiToolRoutes tablolarının Auth modelinden çıkışı. Up/Down BİLEREK boş: tablolar ve verisi yerinde
    /// kalır, modül zincirinin ilk migration'ı onları kendi şemasına taşır. EF'in burada ürettiği DROP
    /// TABLE silindi; snapshot modelin yeni hâlini tutar. Bkz. MigrationHandoffTests.
    /// </summary>
    public partial class Phase14_2D1_HandoffOps : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
