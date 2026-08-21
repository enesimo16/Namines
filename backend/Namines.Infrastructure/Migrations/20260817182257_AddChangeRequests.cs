using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangeRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    BranchId = table.Column<string>(type: "text", nullable: false),
                    BaseVersionId = table.Column<string>(type: "text", nullable: true),
                    HeadVersionId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RiskLevel = table.Column<int>(type: "integer", nullable: false),
                    ImpactReportJson = table.Column<string>(type: "text", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_CloudProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "CloudProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_SchemaVersions_BaseVersionId",
                        column: x => x.BaseVersionId,
                        principalTable: "SchemaVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_SchemaVersions_HeadVersionId",
                        column: x => x.HeadVersionId,
                        principalTable: "SchemaVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChangeRequestApprovals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ChangeRequestId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequestApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeRequestApprovals_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeRequestApprovals_ChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "ChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequestApprovals_ChangeRequestId_UserId",
                table: "ChangeRequestApprovals",
                columns: new[] { "ChangeRequestId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequestApprovals_UserId",
                table: "ChangeRequestApprovals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_BaseVersionId",
                table: "ChangeRequests",
                column: "BaseVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_BranchId",
                table: "ChangeRequests",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_CreatedByUserId",
                table: "ChangeRequests",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_HeadVersionId",
                table: "ChangeRequests",
                column: "HeadVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_ProjectId_CreatedAt",
                table: "ChangeRequests",
                columns: new[] { "ProjectId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeRequestApprovals");

            migrationBuilder.DropTable(
                name: "ChangeRequests");
        }
    }
}
