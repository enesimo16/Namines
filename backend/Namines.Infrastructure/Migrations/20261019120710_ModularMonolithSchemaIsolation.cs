using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModularMonolithSchemaIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AutomationActions_AutomationRules_RuleId",
                table: "AutomationActions");

            migrationBuilder.DropForeignKey(
                name: "FK_AutomationRules_CloudProjects_ProjectId",
                table: "AutomationRules");

            migrationBuilder.DropForeignKey(
                name: "FK_AutomationRunLogs_AutomationRules_RuleId",
                table: "AutomationRunLogs");

            migrationBuilder.EnsureSchema(
                name: "flow");

            migrationBuilder.EnsureSchema(
                name: "desk");

            migrationBuilder.EnsureSchema(
                name: "ground");

            migrationBuilder.EnsureSchema(
                name: "vault");

            migrationBuilder.RenameTable(
                name: "VaultSchedules",
                newName: "vault_schedules",
                newSchema: "vault");

            migrationBuilder.RenameTable(
                name: "VaultRestores",
                newName: "vault_restores",
                newSchema: "vault");

            migrationBuilder.RenameTable(
                name: "VaultBackups",
                newName: "vault_backups",
                newSchema: "vault");

            migrationBuilder.RenameTable(
                name: "SqlQueryHistory",
                newName: "sql_query_history",
                newSchema: "desk");

            migrationBuilder.RenameTable(
                name: "SavedQueries",
                newName: "saved_queries",
                newSchema: "desk");

            migrationBuilder.RenameTable(
                name: "GroundDatabases",
                newName: "ground_databases",
                newSchema: "ground");

            migrationBuilder.RenameTable(
                name: "DeskHandoffTokens",
                newName: "desk_handoff_tokens",
                newSchema: "desk");

            migrationBuilder.RenameTable(
                name: "AutomationRunLogs",
                newName: "automation_run_logs",
                newSchema: "flow");

            migrationBuilder.RenameTable(
                name: "AutomationRules",
                newName: "automation_rules",
                newSchema: "flow");

            migrationBuilder.RenameTable(
                name: "AutomationActions",
                newName: "automation_actions",
                newSchema: "flow");

            migrationBuilder.RenameIndex(
                name: "IX_VaultSchedules_ProjectId",
                schema: "vault",
                table: "vault_schedules",
                newName: "IX_vault_schedules_ProjectId");

            migrationBuilder.RenameIndex(
                name: "IX_VaultRestores_ProjectId_StartedAt",
                schema: "vault",
                table: "vault_restores",
                newName: "IX_vault_restores_ProjectId_StartedAt");

            migrationBuilder.RenameIndex(
                name: "IX_VaultBackups_ProjectId_CreatedAt",
                schema: "vault",
                table: "vault_backups",
                newName: "IX_vault_backups_ProjectId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_SqlQueryHistory_UserId_ProjectId_CreatedAt",
                schema: "desk",
                table: "sql_query_history",
                newName: "IX_sql_query_history_UserId_ProjectId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_SavedQueries_UserId_ProjectId_Name",
                schema: "desk",
                table: "saved_queries",
                newName: "IX_saved_queries_UserId_ProjectId_Name");

            migrationBuilder.RenameIndex(
                name: "IX_GroundDatabases_ProjectId",
                schema: "ground",
                table: "ground_databases",
                newName: "IX_ground_databases_ProjectId");

            migrationBuilder.RenameIndex(
                name: "IX_DeskHandoffTokens_TokenHash",
                schema: "desk",
                table: "desk_handoff_tokens",
                newName: "IX_desk_handoff_tokens_TokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_AutomationRunLogs_RuleId",
                schema: "flow",
                table: "automation_run_logs",
                newName: "IX_automation_run_logs_RuleId");

            migrationBuilder.RenameIndex(
                name: "IX_AutomationRules_ProjectId_Enabled",
                schema: "flow",
                table: "automation_rules",
                newName: "IX_automation_rules_ProjectId_Enabled");

            migrationBuilder.RenameIndex(
                name: "IX_AutomationActions_RuleId_SortOrder",
                schema: "flow",
                table: "automation_actions",
                newName: "IX_automation_actions_RuleId_SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_automation_actions_automation_rules_RuleId",
                schema: "flow",
                table: "automation_actions",
                column: "RuleId",
                principalSchema: "flow",
                principalTable: "automation_rules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_automation_rules_CloudProjects_ProjectId",
                schema: "flow",
                table: "automation_rules",
                column: "ProjectId",
                principalTable: "CloudProjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_automation_run_logs_automation_rules_RuleId",
                schema: "flow",
                table: "automation_run_logs",
                column: "RuleId",
                principalSchema: "flow",
                principalTable: "automation_rules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_automation_actions_automation_rules_RuleId",
                schema: "flow",
                table: "automation_actions");

            migrationBuilder.DropForeignKey(
                name: "FK_automation_rules_CloudProjects_ProjectId",
                schema: "flow",
                table: "automation_rules");

            migrationBuilder.DropForeignKey(
                name: "FK_automation_run_logs_automation_rules_RuleId",
                schema: "flow",
                table: "automation_run_logs");

            migrationBuilder.RenameTable(
                name: "vault_schedules",
                schema: "vault",
                newName: "VaultSchedules");

            migrationBuilder.RenameTable(
                name: "vault_restores",
                schema: "vault",
                newName: "VaultRestores");

            migrationBuilder.RenameTable(
                name: "vault_backups",
                schema: "vault",
                newName: "VaultBackups");

            migrationBuilder.RenameTable(
                name: "sql_query_history",
                schema: "desk",
                newName: "SqlQueryHistory");

            migrationBuilder.RenameTable(
                name: "saved_queries",
                schema: "desk",
                newName: "SavedQueries");

            migrationBuilder.RenameTable(
                name: "ground_databases",
                schema: "ground",
                newName: "GroundDatabases");

            migrationBuilder.RenameTable(
                name: "desk_handoff_tokens",
                schema: "desk",
                newName: "DeskHandoffTokens");

            migrationBuilder.RenameTable(
                name: "automation_run_logs",
                schema: "flow",
                newName: "AutomationRunLogs");

            migrationBuilder.RenameTable(
                name: "automation_rules",
                schema: "flow",
                newName: "AutomationRules");

            migrationBuilder.RenameTable(
                name: "automation_actions",
                schema: "flow",
                newName: "AutomationActions");

            migrationBuilder.RenameIndex(
                name: "IX_vault_schedules_ProjectId",
                table: "VaultSchedules",
                newName: "IX_VaultSchedules_ProjectId");

            migrationBuilder.RenameIndex(
                name: "IX_vault_restores_ProjectId_StartedAt",
                table: "VaultRestores",
                newName: "IX_VaultRestores_ProjectId_StartedAt");

            migrationBuilder.RenameIndex(
                name: "IX_vault_backups_ProjectId_CreatedAt",
                table: "VaultBackups",
                newName: "IX_VaultBackups_ProjectId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_sql_query_history_UserId_ProjectId_CreatedAt",
                table: "SqlQueryHistory",
                newName: "IX_SqlQueryHistory_UserId_ProjectId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_saved_queries_UserId_ProjectId_Name",
                table: "SavedQueries",
                newName: "IX_SavedQueries_UserId_ProjectId_Name");

            migrationBuilder.RenameIndex(
                name: "IX_ground_databases_ProjectId",
                table: "GroundDatabases",
                newName: "IX_GroundDatabases_ProjectId");

            migrationBuilder.RenameIndex(
                name: "IX_desk_handoff_tokens_TokenHash",
                table: "DeskHandoffTokens",
                newName: "IX_DeskHandoffTokens_TokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_automation_run_logs_RuleId",
                table: "AutomationRunLogs",
                newName: "IX_AutomationRunLogs_RuleId");

            migrationBuilder.RenameIndex(
                name: "IX_automation_rules_ProjectId_Enabled",
                table: "AutomationRules",
                newName: "IX_AutomationRules_ProjectId_Enabled");

            migrationBuilder.RenameIndex(
                name: "IX_automation_actions_RuleId_SortOrder",
                table: "AutomationActions",
                newName: "IX_AutomationActions_RuleId_SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationActions_AutomationRules_RuleId",
                table: "AutomationActions",
                column: "RuleId",
                principalTable: "AutomationRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationRules_CloudProjects_ProjectId",
                table: "AutomationRules",
                column: "ProjectId",
                principalTable: "CloudProjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationRunLogs_AutomationRules_RuleId",
                table: "AutomationRunLogs",
                column: "RuleId",
                principalTable: "AutomationRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
