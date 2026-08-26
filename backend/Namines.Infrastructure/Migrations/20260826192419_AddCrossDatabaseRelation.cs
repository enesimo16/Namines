using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrossDatabaseRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrossDatabaseRelations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SourceProjectId = table.Column<string>(type: "text", nullable: false),
                    SourceTableId = table.Column<string>(type: "text", nullable: false),
                    SourceColumnId = table.Column<string>(type: "text", nullable: false),
                    TargetProjectId = table.Column<string>(type: "text", nullable: false),
                    TargetTableId = table.Column<string>(type: "text", nullable: false),
                    TargetColumnId = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossDatabaseRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrossDatabaseRelations_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDatabaseRelations_CloudProjects_SourceProjectId",
                        column: x => x.SourceProjectId,
                        principalTable: "CloudProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrossDatabaseRelations_CloudProjects_TargetProjectId",
                        column: x => x.TargetProjectId,
                        principalTable: "CloudProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrossDatabaseRelations_CreatedByUserId",
                table: "CrossDatabaseRelations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDatabaseRelations_SourceProjectId",
                table: "CrossDatabaseRelations",
                column: "SourceProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CrossDatabaseRelations_TargetProjectId",
                table: "CrossDatabaseRelations",
                column: "TargetProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrossDatabaseRelations");
        }
    }
}
