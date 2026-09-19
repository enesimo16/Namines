using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <summary>
    /// Namines Flow: kurala koşul (<c>ConditionsJson</c>) ve ad alanı ekler,
    /// tek aksiyonu <c>AutomationActions</c> tablosuna taşır, çalışma
    /// kayıtlarına hangi adıma ait olduğunu ve test olup olmadığını yazar.
    ///
    /// <b>Bu dosya ELLE düzeltildi.</b> Scaffold, <c>ActionType → Name</c> ve
    /// <c>ActionConfigJson → ConditionsJson</c> dönüşümlerini birer SÜTUN ADI
    /// DEĞİŞİKLİĞİ sandı; öyle bırakılsaydı her kuralın adı "Toast"/"Webhook"
    /// olur, koşul alanına <c>{"url":...}</c> yazılır ve aksiyon verisi
    /// TAMAMEN KAYBOLURDU (yeni tabloya hiçbir satır taşınmadan eski sütunlar
    /// gider). Sıra bu yüzden önemli: önce kopyala.
    ///
    /// <b>İKİ AŞAMALI:</b> bu migration eski <c>ActionType</c>/
    /// <c>ActionConfigJson</c> sütunlarını DÜŞÜRMÜYOR (gerekçe aşağıda).
    /// Aşama 2 — yani o sütunların silinmesi — yeni kod üretime çıktıktan
    /// SONRA, ayrı bir sürümde yapılacak. O migration'ı <c>dotnet ef
    /// migrations add</c> ÜRETMEZ: model bu alanları zaten içermediği için
    /// EF'in anlık görüntüsünde de yoklar, dolayısıyla elle yazılması gerekir.
    /// </summary>
    public partial class AddNaminesFlowConditionsAndActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Çalışma kayıtları ──────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "ActionType",
                table: "AutomationRunLogs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsTest",
                table: "AutomationRunLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Geçmiş kayıtlar hangi adımın sonucu olduğunu bilmiyordu; kural
            // tek aksiyonluyken bu bilgi kuralın kendisindeydi. Sütun
            // düşürülmeden ÖNCE geri yazılıyor, yoksa geçmiş boş string
            // dolu bir sütuna dönerdi.
            migrationBuilder.Sql("""
                UPDATE "AutomationRunLogs" AS l
                SET "ActionType" = r."ActionType"
                FROM "AutomationRules" AS r
                WHERE l."RuleId" = r."Id";
                """);

            // ── Kurala ad ve koşul ─────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "AutomationRules",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ConditionsJson",
                table: "AutomationRules",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            // ── Aksiyonlar ayrı tabloya ────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "AutomationActions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RuleId = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ActionType = table.Column<string>(type: "text", nullable: false),
                    ActionConfigJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationActions_AutomationRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "AutomationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationActions_RuleId_SortOrder",
                table: "AutomationActions",
                columns: new[] { "RuleId", "SortOrder" });

            // Mevcut her kuralın TEK aksiyonu, zincirin ilk adımı olarak taşınıyor.
            //
            // Aksiyonun Id'si olarak kuralın Id'si kullanılıyor: bu noktada kural
            // başına tam olarak bir aksiyon var, dolayısıyla çakışma imkânsız —
            // ve bu, veritabanı sürümüne göre var olup olmadığı değişen bir UUID
            // fonksiyonuna (gen_random_uuid) bağımlılığı ortadan kaldırıyor.
            migrationBuilder.Sql("""
                INSERT INTO "AutomationActions" ("Id", "RuleId", "SortOrder", "ActionType", "ActionConfigJson")
                SELECT "Id", "Id", 0, "ActionType", COALESCE("ActionConfigJson", '{}')
                FROM "AutomationRules";
                """);

            // ── Eski sütunlar BİLEREK DÜŞÜRÜLMÜYOR ─────────────────────────
            //
            // deploy/MIGRATION-VE-GERI-ALMA.md ve MigrationCompatibilityTests:
            // geri alma kodu eski sürüme döndürür, VERİTABANINI döndürmez.
            // Bu sürüm geri alınırsa eski kod hâlâ "ActionType"/"ActionConfigJson"
            // okur; şimdi düşürülseler o sorgular patlardı. Sütunlar bir sonraki
            // SÜRÜMDE, yeni kod dağıtıldıktan sonra ayrı bir migration'la
            // silinecek (iki aşamalı desen, §3.1).
            //
            // Ama yeni kodun modelinde bu iki alan artık YOK; EF onları INSERT'e
            // hiç koymuyor ve sütunlar NOT NULL/varsayılansız olduğu için her
            // yeni kural eklemesi patlardı. Varsayılan vermek bunu çözüyor.
            //
            // EF'in kolon değiştirme yardımcısı yerine ham SQL kullanılıyor:
            // o yardımcının C# çağrısı kolonun tüm tanımını yeniden yazmayı
            // (dolayısıyla "nullable" durumunu tekrar belirtmeyi) gerektiriyor
            // ve MigrationCompatibilityTests bu deseni — kolonu SIKILAŞTIRAN
            // değişikliği — haklı olarak yasaklıyor. Buradaki işlem tam tersi:
            // varsayılan ekleyerek kısıtı GEVŞETİYOR, eski kodu bozmuyor.
            migrationBuilder.Sql("""
                ALTER TABLE "AutomationRules" ALTER COLUMN "ActionType" SET DEFAULT '';
                ALTER TABLE "AutomationRules" ALTER COLUMN "ActionConfigJson" SET DEFAULT '{}';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eski sütunlar hiç düşürülmediği için geri eklenmiyor; yalnızca
            // zincirin İLK adımı onlara geri yazılıyor ki geri alındıktan sonra
            // eski kod kuralı doğru aksiyonla görsün. Sonraki adımlar
            // kaybolur — çoklu aksiyonu tek sütuna sıkıştırmanın kayıpsız bir
            // yolu yok ve bu bilinçli bir kabul.
            migrationBuilder.Sql("""
                UPDATE "AutomationRules" AS r
                SET "ActionType" = a."ActionType", "ActionConfigJson" = a."ActionConfigJson"
                FROM "AutomationActions" AS a
                WHERE a."RuleId" = r."Id" AND a."SortOrder" = 0;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "AutomationRules" ALTER COLUMN "ActionType" DROP DEFAULT;
                ALTER TABLE "AutomationRules" ALTER COLUMN "ActionConfigJson" DROP DEFAULT;
                """);

            migrationBuilder.DropTable(name: "AutomationActions");

            migrationBuilder.DropColumn(name: "ConditionsJson", table: "AutomationRules");
            migrationBuilder.DropColumn(name: "Name", table: "AutomationRules");
            migrationBuilder.DropColumn(name: "ActionType", table: "AutomationRunLogs");
            migrationBuilder.DropColumn(name: "IsTest", table: "AutomationRunLogs");
        }
    }
}
