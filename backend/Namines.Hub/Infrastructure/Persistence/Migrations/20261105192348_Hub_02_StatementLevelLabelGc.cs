using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Hub.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Etiket temizliği satır başına çalışıyordu: 50.500 bağ silmek 16 sn, 200.500 bağ 25 sn sürdü ve 30 sn'lik
    /// komut sınırına yaklaşan proje silmeyi düşürebiliyordu (denetim m-8). Deyim düzeyi tetikleyici silinen bağları
    /// geçiş tablosundan alır, etiketleri tek seferde kilitler ve siler. Eşzamanlı bağ ekleyen işlemi bekleme kuralı
    /// aynı: önce kilit, sonra NOT EXISTS. Kilit sırası kimliğe göre, iki silme birbirini kilitlenmeye sokmasın.
    /// Tablo artık Hub zincirinde; 14.2C'nin Down'ı tetikleyiciyi adıyla düşürdüğü için ad korunur ve Down satır
    /// düzeyi sürümü geri koyar.
    /// </summary>
    public partial class Hub_02_StatementLevelLabelGc : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '30s';");
            migrationBuilder.Sql("""
                DROP TRIGGER trg_repo_issue_labels_gc ON hub.repo_issue_labels;
                CREATE OR REPLACE FUNCTION hub.issue_label_gc() RETURNS trigger LANGUAGE plpgsql AS $gc$
                BEGIN
                    PERFORM 1 FROM hub.issue_labels l
                    WHERE l."Id" IN (SELECT DISTINCT o."IssueLabelId" FROM removed_links o)
                    ORDER BY l."Id"
                    FOR UPDATE;
                    DELETE FROM hub.issue_labels l
                    WHERE l."Id" IN (SELECT DISTINCT o."IssueLabelId" FROM removed_links o)
                      AND NOT EXISTS (SELECT 1 FROM hub.repo_issue_labels r WHERE r."IssueLabelId" = l."Id");
                    RETURN NULL;
                END $gc$;
                CREATE TRIGGER trg_repo_issue_labels_gc AFTER DELETE ON hub.repo_issue_labels
                    REFERENCING OLD TABLE AS removed_links
                    FOR EACH STATEMENT EXECUTE FUNCTION hub.issue_label_gc();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '30s';");
            migrationBuilder.Sql("""
                DROP TRIGGER trg_repo_issue_labels_gc ON hub.repo_issue_labels;
                CREATE OR REPLACE FUNCTION hub.issue_label_gc() RETURNS trigger LANGUAGE plpgsql AS $gc$
                BEGIN
                    PERFORM 1 FROM hub.issue_labels WHERE "Id" = OLD."IssueLabelId" FOR UPDATE;
                    DELETE FROM hub.issue_labels l
                    WHERE l."Id" = OLD."IssueLabelId"
                      AND NOT EXISTS (SELECT 1 FROM hub.repo_issue_labels r WHERE r."IssueLabelId" = OLD."IssueLabelId");
                    RETURN NULL;
                END $gc$;
                CREATE TRIGGER trg_repo_issue_labels_gc AFTER DELETE ON hub.repo_issue_labels
                    FOR EACH ROW EXECUTE FUNCTION hub.issue_label_gc();
                """);
        }
    }
}
