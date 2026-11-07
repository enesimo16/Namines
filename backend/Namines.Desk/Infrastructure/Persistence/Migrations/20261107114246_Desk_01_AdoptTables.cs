using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Desk.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Desk tablolarının zincir sahipliğini AuthDbContext'ten devralır. Veri ve tablolar yerinde kalır, yalnızca birincil anahtar adları modele hizalanır: tablolar `desk`
    /// şemasında, 14.2 öncesi Auth migration'larıyla kuruldu (geçmiş yeniden yazılmaz) ve veri yerinde kalır. Bu
    /// migration modelin anlık görüntüsünü bu zincire taşır; bundan sonraki her DDL değişikliği Desk zincirinin.
    /// Auth tarafındaki eşi `Phase14_2D2_HandoffModules` da boş. Bkz. MigrationHandoffTests.
    /// </summary>
    public partial class Desk_01_AdoptTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Birincil anahtar adlari 14.2 oncesi Auth migration'larindan PascalCase kaldi; model snake_case bekler.
            // Yalnizca ad degisir (katalog), veri ve kilit etkilenmez; Down eski adlari geri verir.
            migrationBuilder.Sql("""ALTER TABLE desk."desk_handoff_tokens" RENAME CONSTRAINT "PK_DeskHandoffTokens" TO "PK_desk_handoff_tokens";""");
            migrationBuilder.Sql("""ALTER TABLE desk."saved_queries" RENAME CONSTRAINT "PK_SavedQueries" TO "PK_saved_queries";""");
            migrationBuilder.Sql("""ALTER TABLE desk."sql_query_history" RENAME CONSTRAINT "PK_SqlQueryHistory" TO "PK_sql_query_history";""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE desk."desk_handoff_tokens" RENAME CONSTRAINT "PK_desk_handoff_tokens" TO "PK_DeskHandoffTokens";""");
            migrationBuilder.Sql("""ALTER TABLE desk."saved_queries" RENAME CONSTRAINT "PK_saved_queries" TO "PK_SavedQueries";""");
            migrationBuilder.Sql("""ALTER TABLE desk."sql_query_history" RENAME CONSTRAINT "PK_sql_query_history" TO "PK_SqlQueryHistory";""");
        }
    }
}
