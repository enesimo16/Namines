using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayKeyRestrictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedIps",
                table: "GatewayApiKeys",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedOrigins",
                table: "GatewayApiKeys",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitPerMinute",
                table: "GatewayApiKeys",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedIps",
                table: "GatewayApiKeys");

            migrationBuilder.DropColumn(
                name: "AllowedOrigins",
                table: "GatewayApiKeys");

            migrationBuilder.DropColumn(
                name: "RateLimitPerMinute",
                table: "GatewayApiKeys");
        }
    }
}
