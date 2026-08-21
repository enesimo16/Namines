using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GatewayApiKeys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Prefix = table.Column<string>(type: "text", nullable: false),
                    KeyHash = table.Column<string>(type: "text", nullable: false),
                    CanWrite = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GatewayApiKeys_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GatewayApiKeys_CloudProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "CloudProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GatewayTablePermissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    TableName = table.Column<string>(type: "text", nullable: false),
                    CanRead = table.Column<bool>(type: "boolean", nullable: false),
                    CanWrite = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayTablePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GatewayTablePermissions_CloudProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "CloudProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayApiKeys_CreatedByUserId",
                table: "GatewayApiKeys",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayApiKeys_Prefix",
                table: "GatewayApiKeys",
                column: "Prefix");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayApiKeys_ProjectId",
                table: "GatewayApiKeys",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayTablePermissions_ProjectId_TableName",
                table: "GatewayTablePermissions",
                columns: new[] { "ProjectId", "TableName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GatewayApiKeys");

            migrationBuilder.DropTable(
                name: "GatewayTablePermissions");
        }
    }
}
