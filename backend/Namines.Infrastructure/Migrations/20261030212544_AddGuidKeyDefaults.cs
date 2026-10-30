using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <summary>
    /// DB-24: Guid-dizgesi birincil anahtarlara <c>DEFAULT gen_random_uuid()::text</c>.
    /// Uygulama anahtarı hâlâ kendi üretir; varsayılan elle SQL / migration INSERT'leri içindir.
    ///
    /// EF bunu <c>AlterColumn</c> olarak üretir ama sonuç yalnızca <c>SET DEFAULT</c> (tip/NULL
    /// değişmez, tablo yeniden yazılmaz); "ileriye uyumluluk" testi AlterColumn+nullable:false
    /// desenine takıldığı için aynı SQL doğrudan yazıldı.
    /// </summary>
    public partial class AddGuidKeyDefaults : Migration
    {
        private static readonly (string Schema, string Table)[] Tables =
        {
            ("namines_vault", "vault_schedules"),
            ("namines_vault", "vault_restores"),
            ("namines_vault", "vault_backups"),
            (null, "TeamInvites"),
            (null, "SqlExecutionAudits"),
            ("desk", "sql_query_history"),
            (null, "SchemaVersions"),
            ("desk", "saved_queries"),
            (null, "RepoIssues"),
            (null, "RepoIssueComments"),
            (null, "Organizations"),
            ("ground", "ground_databases"),
            (null, "GatewayTablePermissions"),
            (null, "GatewayApiKeys"),
            ("desk", "desk_handoff_tokens"),
            (null, "CrossDatabaseRelations"),
            (null, "CloudProjects"),
            (null, "ChangeRequests"),
            (null, "ChangeRequestAuditLogs"),
            (null, "ChangeRequestApprovals"),
            (null, "Branches"),
            ("flow", "automation_run_logs"),
            ("flow", "automation_rules"),
            ("flow", "automation_actions"),
        };

        private static string Qualified(string schema, string table) =>
            schema is null ? $"\"{table}\"" : $"{schema}.\"{table}\"";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (schema, table) in Tables)
            {
                migrationBuilder.Sql(
                    $"ALTER TABLE {Qualified(schema, table)} ALTER COLUMN \"Id\" SET DEFAULT (gen_random_uuid()::text);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (schema, table) in Tables)
            {
                migrationBuilder.Sql(
                    $"ALTER TABLE {Qualified(schema, table)} ALTER COLUMN \"Id\" DROP DEFAULT;");
            }
        }
    }
}
