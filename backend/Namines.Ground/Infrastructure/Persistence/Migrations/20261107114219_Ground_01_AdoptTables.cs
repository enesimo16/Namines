using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Ground.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Ground tablolarının zincir sahipliğini AuthDbContext'ten devralır. Veri ve tablolar yerinde kalır, yalnızca birincil anahtar adları modele hizalanır: tablolar `ground`
    /// şemasında, 14.2 öncesi Auth migration'larıyla kuruldu (geçmiş yeniden yazılmaz) ve veri yerinde kalır. Bu
    /// migration modelin anlık görüntüsünü bu zincire taşır; bundan sonraki her DDL değişikliği Ground zincirinin.
    /// Auth tarafındaki eşi `Phase14_2D2_HandoffModules` da boş. Bkz. MigrationHandoffTests.
    /// </summary>
    public partial class Ground_01_AdoptTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Birincil anahtar adlari 14.2 oncesi Auth migration'larindan PascalCase kaldi; model snake_case bekler.
            // Yalnizca ad degisir (katalog), veri ve kilit etkilenmez; Down eski adlari geri verir.
            migrationBuilder.Sql("""ALTER TABLE ground."ground_databases" RENAME CONSTRAINT "PK_GroundDatabases" TO "PK_ground_databases";""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE ground."ground_databases" RENAME CONSTRAINT "PK_ground_databases" TO "PK_GroundDatabases";""");
        }
    }
}
