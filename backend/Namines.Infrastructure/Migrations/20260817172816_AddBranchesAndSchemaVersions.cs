using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchesAndSchemaVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ParentBranchId = table.Column<string>(type: "text", nullable: true),
                    ForkedFromVersion = table.Column<int>(type: "integer", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Branches_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Branches_Branches_ParentBranchId",
                        column: x => x.ParentBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Branches_CloudProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "CloudProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SchemaVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    BranchId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Checksum = table.Column<string>(type: "text", nullable: false),
                    SchemaJson = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    TableCount = table.Column<short>(type: "smallint", nullable: false),
                    AuthorUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemaVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchemaVersions_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SchemaVersions_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SchemaVersions_CloudProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "CloudProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_CreatedByUserId",
                table: "Branches",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_ParentBranchId",
                table: "Branches",
                column: "ParentBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_ProjectId",
                table: "Branches",
                column: "ProjectId",
                unique: true,
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_ProjectId_Name",
                table: "Branches",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchemaVersions_AuthorUserId",
                table: "SchemaVersions",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SchemaVersions_BranchId_Version",
                table: "SchemaVersions",
                columns: new[] { "BranchId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchemaVersions_ProjectId_CreatedAt",
                table: "SchemaVersions",
                columns: new[] { "ProjectId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchemaVersions");

            migrationBuilder.DropTable(
                name: "Branches");
        }
    }
}
