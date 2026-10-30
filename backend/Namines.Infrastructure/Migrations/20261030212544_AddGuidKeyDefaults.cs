using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGuidKeyDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "namines_vault",
                table: "vault_schedules",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "namines_vault",
                table: "vault_restores",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "namines_vault",
                table: "vault_backups",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "TeamInvites",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "SqlExecutionAudits",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "desk",
                table: "sql_query_history",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "SchemaVersions",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "desk",
                table: "saved_queries",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "RepoIssues",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "RepoIssueComments",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "Organizations",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "ground",
                table: "ground_databases",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "GatewayTablePermissions",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "GatewayApiKeys",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "desk",
                table: "desk_handoff_tokens",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "CrossDatabaseRelations",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "CloudProjects",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "ChangeRequests",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "ChangeRequestAuditLogs",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "ChangeRequestApprovals",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "Branches",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "flow",
                table: "automation_run_logs",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "flow",
                table: "automation_rules",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "flow",
                table: "automation_actions",
                type: "text",
                nullable: false,
                defaultValueSql: "gen_random_uuid()::text",
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "namines_vault",
                table: "vault_schedules",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "namines_vault",
                table: "vault_restores",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "namines_vault",
                table: "vault_backups",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "TeamInvites",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "SqlExecutionAudits",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "desk",
                table: "sql_query_history",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "SchemaVersions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "desk",
                table: "saved_queries",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "RepoIssues",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "RepoIssueComments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "Organizations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "ground",
                table: "ground_databases",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "GatewayTablePermissions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "GatewayApiKeys",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "desk",
                table: "desk_handoff_tokens",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "CrossDatabaseRelations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "CloudProjects",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "ChangeRequests",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "ChangeRequestAuditLogs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "ChangeRequestApprovals",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "Branches",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "flow",
                table: "automation_run_logs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "flow",
                table: "automation_rules",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                schema: "flow",
                table: "automation_actions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValueSql: "gen_random_uuid()::text");
        }
    }
}
