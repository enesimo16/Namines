using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationsAndMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrganizationId",
                table: "CloudProjects",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsPersonal = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Organizations_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMembers",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMembers", x => new { x.OrganizationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudProjects_OrganizationId",
                table: "CloudProjects",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_UserId",
                table: "OrganizationMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_CreatedByUserId",
                table: "Organizations",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CloudProjects_Organizations_OrganizationId",
                table: "CloudProjects",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // ── BACKFILL ────────────────────────────────────────────────────
            // Şema değişikliği tek başına yetmez: mevcut her kullanıcıya kişisel
            // organizasyon açılıp projeleri oraya taşınmazsa, OrgAccess yetkiyi
            // OrganizationId üzerinden aradığı için TÜM eski projeler erişilemez
            // hâle gelirdi. (OrgAccess'te UserId'ye düşen bir geri-uyum yolu var
            // ama ona güvenip veriyi taşımamak, sorunu ileriye ertelemek olurdu.)
            //
            // Idempotent: NOT EXISTS kontrolleriyle tekrar çalıştırılabilir.
            migrationBuilder.Sql("""
                INSERT INTO "Organizations" ("Id", "Name", "IsPersonal", "CreatedByUserId", "CreatedAt")
                SELECT gen_random_uuid()::text,
                       COALESCE(u."UserName", 'Personal') || '''s workspace',
                       TRUE, u."Id", NOW()
                FROM "AspNetUsers" u
                WHERE NOT EXISTS (
                    SELECT 1 FROM "Organizations" o
                    WHERE o."CreatedByUserId" = u."Id" AND o."IsPersonal" = TRUE
                );
            """);

            // Kişisel org'un sahibi = kullanıcının kendisi (OrgRole.Owner = 3).
            migrationBuilder.Sql("""
                INSERT INTO "OrganizationMembers" ("OrganizationId", "UserId", "Role", "JoinedAt")
                SELECT o."Id", o."CreatedByUserId", 3, NOW()
                FROM "Organizations" o
                WHERE o."IsPersonal" = TRUE
                  AND NOT EXISTS (
                      SELECT 1 FROM "OrganizationMembers" m
                      WHERE m."OrganizationId" = o."Id" AND m."UserId" = o."CreatedByUserId"
                  );
            """);

            // Projeleri sahiplerinin kişisel org'una bağla.
            migrationBuilder.Sql("""
                UPDATE "CloudProjects" p
                SET "OrganizationId" = o."Id"
                FROM "Organizations" o
                WHERE o."IsPersonal" = TRUE
                  AND o."CreatedByUserId" = p."UserId"
                  AND p."OrganizationId" IS NULL;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CloudProjects_Organizations_OrganizationId",
                table: "CloudProjects");

            migrationBuilder.DropTable(
                name: "OrganizationMembers");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_CloudProjects_OrganizationId",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "CloudProjects");
        }
    }
}
