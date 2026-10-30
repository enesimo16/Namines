using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenDeleteBehavior : Migration
    {
        // DB-09/10/11/12a — silme davranışı:
        //  * 9 RESTRICT atıf FK'si (Organizations/Branches/ChangeRequests/ChangeRequestApprovals/
        //    ChangeRequestAuditLogs/GatewayApiKeys/CrossDatabaseRelations/RepoIssues/RepoIssueComments)
        //    -> kolon nullable + ON DELETE SET NULL: hesap silme bunlar yüzünden imkânsız olmasın.
        //  * UsageEvents.UserId CASCADE -> SET NULL: faturalama kaydı kullanıcıdan uzun yaşar.
        //  * ChangeRequestAuditLogs.ChangeRequestId FK kaldırıldı + ProjectId/ChangeRequestTitle anlık
        //    kopyası: proje/CR silinse de denetim izi kalır.
        //  * ChangeRequests.Head/BaseVersionId RESTRICT -> NO ACTION (DB-11): RESTRICT anında kontrol
        //    edilir ve ertelenemez; proje/branch silinirken SchemaVersions ile ChangeRequests aynı
        //    ifadede CASCADE ile silindiğinden sıra motora bağlıydı (nondeterministik FK ihlali).
        //    NO ACTION ifade SONUNDA kontrol edilir: tek başına bir SchemaVersion silmek (CR hâlâ
        //    ona bakıyorsa) yine engellenir, ama cascade ile birlikte giden CR'lar sorun çıkarmaz.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Branches_AspNetUsers_CreatedByUserId",
                table: "Branches");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequestApprovals_AspNetUsers_UserId",
                table: "ChangeRequestApprovals");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequestAuditLogs_AspNetUsers_ActorUserId",
                table: "ChangeRequestAuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequestAuditLogs_ChangeRequests_ChangeRequestId",
                table: "ChangeRequestAuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequests_AspNetUsers_CreatedByUserId",
                table: "ChangeRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequests_SchemaVersions_BaseVersionId",
                table: "ChangeRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequests_SchemaVersions_HeadVersionId",
                table: "ChangeRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_CrossDatabaseRelations_AspNetUsers_CreatedByUserId",
                table: "CrossDatabaseRelations");

            migrationBuilder.DropForeignKey(
                name: "FK_GatewayApiKeys_AspNetUsers_CreatedByUserId",
                table: "GatewayApiKeys");

            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_AspNetUsers_CreatedByUserId",
                table: "Organizations");

            migrationBuilder.DropForeignKey(
                name: "FK_RepoIssueComments_AspNetUsers_AuthorUserId",
                table: "RepoIssueComments");

            migrationBuilder.DropForeignKey(
                name: "FK_RepoIssues_AspNetUsers_CreatedByUserId",
                table: "RepoIssues");

            migrationBuilder.DropForeignKey(
                name: "FK_UsageEvents_AspNetUsers_UserId",
                table: "UsageEvents");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "UsageEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "RepoIssues",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorUserId",
                table: "RepoIssueComments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "Organizations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "GatewayApiKeys",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "CrossDatabaseRelations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "ChangeRequests",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "ChangeRequestTitle",
                table: "ChangeRequestAuditLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectId",
                table: "ChangeRequestAuditLogs",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Anlık kopyaları mevcut satırlar için doldur (CR hâlâ var: eski FK bunu garanti ediyordu).
            // ChangeRequestId üzerindeki FK bilinçli olarak kaldırıldı — bkz. AuthDbContext.
            migrationBuilder.Sql(@"
                UPDATE ""ChangeRequestAuditLogs"" a
                SET ""ProjectId"" = c.""ProjectId"", ""ChangeRequestTitle"" = c.""Title""
                FROM ""ChangeRequests"" c
                WHERE c.""Id"" = a.""ChangeRequestId"";");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "ChangeRequestApprovals",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "Branches",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequestAuditLogs_ProjectId_CreatedAt",
                table: "ChangeRequestAuditLogs",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_AspNetUsers_CreatedByUserId",
                table: "Branches",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequestApprovals_AspNetUsers_UserId",
                table: "ChangeRequestApprovals",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequestAuditLogs_AspNetUsers_ActorUserId",
                table: "ChangeRequestAuditLogs",
                column: "ActorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequests_AspNetUsers_CreatedByUserId",
                table: "ChangeRequests",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequests_SchemaVersions_BaseVersionId",
                table: "ChangeRequests",
                column: "BaseVersionId",
                principalTable: "SchemaVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequests_SchemaVersions_HeadVersionId",
                table: "ChangeRequests",
                column: "HeadVersionId",
                principalTable: "SchemaVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CrossDatabaseRelations_AspNetUsers_CreatedByUserId",
                table: "CrossDatabaseRelations",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GatewayApiKeys_AspNetUsers_CreatedByUserId",
                table: "GatewayApiKeys",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Organizations_AspNetUsers_CreatedByUserId",
                table: "Organizations",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RepoIssueComments_AspNetUsers_AuthorUserId",
                table: "RepoIssueComments",
                column: "AuthorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RepoIssues_AspNetUsers_CreatedByUserId",
                table: "RepoIssues",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_UsageEvents_AspNetUsers_UserId",
                table: "UsageEvents",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Branches_AspNetUsers_CreatedByUserId",
                table: "Branches");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequestApprovals_AspNetUsers_UserId",
                table: "ChangeRequestApprovals");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequestAuditLogs_AspNetUsers_ActorUserId",
                table: "ChangeRequestAuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequests_AspNetUsers_CreatedByUserId",
                table: "ChangeRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequests_SchemaVersions_BaseVersionId",
                table: "ChangeRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ChangeRequests_SchemaVersions_HeadVersionId",
                table: "ChangeRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_CrossDatabaseRelations_AspNetUsers_CreatedByUserId",
                table: "CrossDatabaseRelations");

            migrationBuilder.DropForeignKey(
                name: "FK_GatewayApiKeys_AspNetUsers_CreatedByUserId",
                table: "GatewayApiKeys");

            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_AspNetUsers_CreatedByUserId",
                table: "Organizations");

            migrationBuilder.DropForeignKey(
                name: "FK_RepoIssueComments_AspNetUsers_AuthorUserId",
                table: "RepoIssueComments");

            migrationBuilder.DropForeignKey(
                name: "FK_RepoIssues_AspNetUsers_CreatedByUserId",
                table: "RepoIssues");

            migrationBuilder.DropForeignKey(
                name: "FK_UsageEvents_AspNetUsers_UserId",
                table: "UsageEvents");

            migrationBuilder.DropIndex(
                name: "IX_ChangeRequestAuditLogs_ProjectId_CreatedAt",
                table: "ChangeRequestAuditLogs");

            migrationBuilder.DropColumn(
                name: "ChangeRequestTitle",
                table: "ChangeRequestAuditLogs");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "ChangeRequestAuditLogs");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "UsageEvents",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "RepoIssues",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AuthorUserId",
                table: "RepoIssueComments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "Organizations",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "GatewayApiKeys",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "CrossDatabaseRelations",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "ChangeRequests",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "ChangeRequestApprovals",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedByUserId",
                table: "Branches",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_AspNetUsers_CreatedByUserId",
                table: "Branches",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequestApprovals_AspNetUsers_UserId",
                table: "ChangeRequestApprovals",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequestAuditLogs_AspNetUsers_ActorUserId",
                table: "ChangeRequestAuditLogs",
                column: "ActorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Down NOTU: eski şema (RESTRICT FK + NOT NULL atıf) yetim denetim satırlarını ve
            // SET NULL ile boşalmış atıfları TEMSİL EDEMEZ. Yetim denetim satırları silinir;
            // boşalmış atıf kolonları varsa yukarıdaki NOT NULL dönüşümü "" yazar ve FK eklemesi
            // patlar — bu yönde veri kaybı olmadan geri dönüş yoktur (yedekten dön).
            migrationBuilder.Sql(@"
                DELETE FROM ""ChangeRequestAuditLogs"" a
                WHERE NOT EXISTS (SELECT 1 FROM ""ChangeRequests"" c WHERE c.""Id"" = a.""ChangeRequestId"");");

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequestAuditLogs_ChangeRequests_ChangeRequestId",
                table: "ChangeRequestAuditLogs",
                column: "ChangeRequestId",
                principalTable: "ChangeRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequests_AspNetUsers_CreatedByUserId",
                table: "ChangeRequests",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequests_SchemaVersions_BaseVersionId",
                table: "ChangeRequests",
                column: "BaseVersionId",
                principalTable: "SchemaVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeRequests_SchemaVersions_HeadVersionId",
                table: "ChangeRequests",
                column: "HeadVersionId",
                principalTable: "SchemaVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CrossDatabaseRelations_AspNetUsers_CreatedByUserId",
                table: "CrossDatabaseRelations",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GatewayApiKeys_AspNetUsers_CreatedByUserId",
                table: "GatewayApiKeys",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Organizations_AspNetUsers_CreatedByUserId",
                table: "Organizations",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RepoIssueComments_AspNetUsers_AuthorUserId",
                table: "RepoIssueComments",
                column: "AuthorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RepoIssues_AspNetUsers_CreatedByUserId",
                table: "RepoIssues",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UsageEvents_AspNetUsers_UserId",
                table: "UsageEvents",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
