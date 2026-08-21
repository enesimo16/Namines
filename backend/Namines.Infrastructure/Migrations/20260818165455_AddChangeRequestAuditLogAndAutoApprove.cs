using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequestAuditLogAndAutoApprove : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoApproveSafeChanges",
                table: "CloudProjects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ChangeRequestAuditLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ChangeRequestId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequestAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeRequestAuditLogs_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeRequestAuditLogs_ChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "ChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequestAuditLogs_ActorUserId",
                table: "ChangeRequestAuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequestAuditLogs_ChangeRequestId_CreatedAt",
                table: "ChangeRequestAuditLogs",
                columns: new[] { "ChangeRequestId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeRequestAuditLogs");

            migrationBuilder.DropColumn(
                name: "AutoApproveSafeChanges",
                table: "CloudProjects");
        }
    }
}
