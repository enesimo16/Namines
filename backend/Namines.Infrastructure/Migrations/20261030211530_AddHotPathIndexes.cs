using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHotPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CloudProjects_Visibility",
                table: "CloudProjects");

            migrationBuilder.DropIndex(
                name: "IX_automation_run_logs_RuleId",
                schema: "flow",
                table: "automation_run_logs");

            migrationBuilder.CreateIndex(
                name: "IX_CloudProjects_Explore_Public",
                table: "CloudProjects",
                columns: new[] { "StarCount", "CreatedAt" },
                descending: new bool[0],
                filter: "\"Visibility\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_CloudProjects_UserId",
                table: "CloudProjects",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_automation_run_logs_RuleId_TriggeredAt",
                schema: "flow",
                table: "automation_run_logs",
                columns: new[] { "RuleId", "TriggeredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_automation_run_logs_TriggeredAt",
                schema: "flow",
                table: "automation_run_logs",
                column: "TriggeredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CloudProjects_Explore_Public",
                table: "CloudProjects");

            migrationBuilder.DropIndex(
                name: "IX_CloudProjects_UserId",
                table: "CloudProjects");

            migrationBuilder.DropIndex(
                name: "IX_automation_run_logs_RuleId_TriggeredAt",
                schema: "flow",
                table: "automation_run_logs");

            migrationBuilder.DropIndex(
                name: "IX_automation_run_logs_TriggeredAt",
                schema: "flow",
                table: "automation_run_logs");

            migrationBuilder.CreateIndex(
                name: "IX_CloudProjects_Visibility",
                table: "CloudProjects",
                column: "Visibility");

            migrationBuilder.CreateIndex(
                name: "IX_automation_run_logs_RuleId",
                schema: "flow",
                table: "automation_run_logs",
                column: "RuleId");
        }
    }
}
