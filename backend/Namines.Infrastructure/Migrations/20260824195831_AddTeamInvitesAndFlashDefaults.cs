using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamInvitesAndFlashDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdvancedJson",
                table: "UserAIPolicies",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TeamInvites",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedByUserId = table.Column<string>(type: "text", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamInvites_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamInvites_OrganizationId",
                table: "TeamInvites",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamInvites_TokenHash",
                table: "TeamInvites",
                column: "TokenHash",
                unique: true);

            // Var olan kullanicilarin AI tercihi Flash'a cekiliyor.
            //
            // Neden gerekli: eski varsayilan HighMixtral (6) idi ve o secenek ARTIK
            // kullaniciya gosterilmiyor. Hicbir ayara dokunmamis bir kullanici, en
            // ucuz isi bile en pahali modelde calistiriyor ve gunluk butcesini iki
            // kat hizli tuketiyordu -- yeni varsayilani yalnizca yeni hesaplara
            // vermek, var olan herkesi o durumda birakirdi.
            //
            // Yalnizca ARTIK LISTEDE OLMAYAN degerler tasianiyor (0,3,5,6,7,8).
            // 1/2/4 kullanicinin uc NAI modelinden bilerek sectikleri -- onlara
            // dokunmak, kullanicinin tercihini sessizce ezmek olurdu.
            migrationBuilder.Sql(@"
                UPDATE ""UserAIPolicies"" SET
                    ""SmartSeed""        = CASE WHEN ""SmartSeed""        IN (0,3,5,6,7,8) THEN 1 ELSE ""SmartSeed""        END,
                    ""Documentation""    = CASE WHEN ""Documentation""    IN (0,3,5,6,7,8) THEN 1 ELSE ""Documentation""    END,
                    ""Scaffolding""      = CASE WHEN ""Scaffolding""      IN (0,3,5,6,7,8) THEN 1 ELSE ""Scaffolding""      END,
                    ""SchemaGeneration"" = CASE WHEN ""SchemaGeneration"" IN (0,3,5,6,7,8) THEN 2 ELSE ""SchemaGeneration"" END,
                    ""SchemaRevision""   = CASE WHEN ""SchemaRevision""   IN (0,3,5,6,7,8) THEN 1 ELSE ""SchemaRevision""   END,
                    ""DbaAnalysis""      = CASE WHEN ""DbaAnalysis""      IN (0,3,5,6,7,8) THEN 1 ELSE ""DbaAnalysis""      END,
                    ""Migration""        = CASE WHEN ""Migration""        IN (0,3,5,6,7,8) THEN 1 ELSE ""Migration""        END,
                    ""Voice""            = CASE WHEN ""Voice""            IN (0,3,5,6,7,8) THEN 1 ELSE ""Voice""            END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamInvites");

            migrationBuilder.DropColumn(
                name: "AdvancedJson",
                table: "UserAIPolicies");
        }
    }
}
