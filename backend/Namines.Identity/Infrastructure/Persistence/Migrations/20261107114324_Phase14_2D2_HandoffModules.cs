using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Billing, Governance, Vault, Ground, Flow ve Desk varlıklarının Auth modelinden çıkışı ve Stripe alanlarının
    /// kullanıcı modelinden çıkışı. Up/Down BİLEREK boş: tablolar ve kolonlar yerinde kalır, modül zincirlerinin ilk
    /// migration'ları onları devralır (Billing kolonları taşıyıp doğrulayarak düşürür). EF'in burada ürettiği
    /// DROP TABLE / DROP COLUMN işlemleri silindi, çünkü veri giderdi; snapshot modelin yeni hâlini tutar.
    /// Uygulama sırası: Auth (bu migration, boş) sonra modül zincirleri. Bkz. MigrationHandoffTests.
    /// </summary>
    public partial class Phase14_2D2_HandoffModules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
