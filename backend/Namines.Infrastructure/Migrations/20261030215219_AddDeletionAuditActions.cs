using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletionAuditActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ChangeRequestAuditLogs_Action",
                table: "ChangeRequestAuditLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChangeRequestAuditLogs_Action",
                table: "ChangeRequestAuditLogs",
                sql: "\"Action\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ChangeRequestAuditLogs_Action",
                table: "ChangeRequestAuditLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChangeRequestAuditLogs_Action",
                table: "ChangeRequestAuditLogs",
                sql: "\"Action\" IN (0, 1, 2, 3)");
        }
    }
}
