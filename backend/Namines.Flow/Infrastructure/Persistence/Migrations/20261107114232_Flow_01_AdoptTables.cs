using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Flow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Flow tablolarının zincir sahipliğini AuthDbContext'ten devralır. Veri ve tablolar yerinde kalır, yalnızca birincil anahtar adları modele hizalanır: tablolar `flow`
    /// şemasında, 14.2 öncesi Auth migration'larıyla kuruldu (geçmiş yeniden yazılmaz) ve veri yerinde kalır. Bu
    /// migration modelin anlık görüntüsünü bu zincire taşır; bundan sonraki her DDL değişikliği Flow zincirinin.
    /// Auth tarafındaki eşi `Phase14_2D2_HandoffModules` da boş. Bkz. MigrationHandoffTests.
    /// </summary>
    public partial class Flow_01_AdoptTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Birincil anahtar adlari 14.2 oncesi Auth migration'larindan PascalCase kaldi; model snake_case bekler.
            // Yalnizca ad degisir (katalog), veri ve kilit etkilenmez; Down eski adlari geri verir.
            migrationBuilder.Sql("""ALTER TABLE flow."automation_actions" RENAME CONSTRAINT "PK_AutomationActions" TO "PK_automation_actions";""");
            migrationBuilder.Sql("""ALTER TABLE flow."automation_rules" RENAME CONSTRAINT "PK_AutomationRules" TO "PK_automation_rules";""");
            migrationBuilder.Sql("""ALTER TABLE flow."automation_run_logs" RENAME CONSTRAINT "PK_AutomationRunLogs" TO "PK_automation_run_logs";""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE flow."automation_actions" RENAME CONSTRAINT "PK_automation_actions" TO "PK_AutomationActions";""");
            migrationBuilder.Sql("""ALTER TABLE flow."automation_rules" RENAME CONSTRAINT "PK_automation_rules" TO "PK_AutomationRules";""");
            migrationBuilder.Sql("""ALTER TABLE flow."automation_run_logs" RENAME CONSTRAINT "PK_automation_run_logs" TO "PK_AutomationRunLogs";""");
        }
    }
}
