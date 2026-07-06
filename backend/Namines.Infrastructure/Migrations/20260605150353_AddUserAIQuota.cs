using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAIQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAIQuotas",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DailyLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyUsageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastResetDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAIQuotas", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserAIQuotas_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAIQuotas");
        }
    }
}
