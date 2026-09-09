using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSqlExecutionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SqlExecutionAudits",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: true),
                    TargetHost = table.Column<string>(type: "text", nullable: true),
                    TargetDatabase = table.Column<string>(type: "text", nullable: true),
                    DbType = table.Column<string>(type: "text", nullable: false),
                    ScriptHash = table.Column<string>(type: "text", nullable: false),
                    ScriptPreview = table.Column<string>(type: "text", nullable: false),
                    ScriptLength = table.Column<int>(type: "integer", nullable: false),
                    ContainsDestructiveKeyword = table.Column<bool>(type: "boolean", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    StatementsExecuted = table.Column<int>(type: "integer", nullable: false),
                    PartialApplyPossible = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SqlExecutionAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SqlExecutionAudits_CreatedAt",
                table: "SqlExecutionAudits",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SqlExecutionAudits_ProjectId",
                table: "SqlExecutionAudits",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SqlExecutionAudits_UserId_CreatedAt",
                table: "SqlExecutionAudits",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SqlExecutionAudits");
        }
    }
}
