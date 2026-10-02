using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CacheChangeRequestAiOutputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiExplanation",
                table: "ChangeRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MigrationResultJson",
                table: "ChangeRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiExplanation",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "MigrationResultJson",
                table: "ChangeRequests");
        }
    }
}
