using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeRequestTestRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TestRunAt",
                table: "ChangeRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TestRunDurationMs",
                table: "ChangeRequests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestRunFailedStatement",
                table: "ChangeRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestRunMessage",
                table: "ChangeRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TestRunSuccess",
                table: "ChangeRequests",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TestRunSupported",
                table: "ChangeRequests",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TestRunAt",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "TestRunDurationMs",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "TestRunFailedStatement",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "TestRunMessage",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "TestRunSuccess",
                table: "ChangeRequests");

            migrationBuilder.DropColumn(
                name: "TestRunSupported",
                table: "ChangeRequests");
        }
    }
}
