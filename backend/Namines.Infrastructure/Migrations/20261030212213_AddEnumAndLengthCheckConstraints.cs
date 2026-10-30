using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <summary>
    /// DB-21/DB-22: enum'a bağlı tamsayı kolonlar için CHECK ve kullanıcı girdisi metin
    /// kolonları için uzunluk tavanı.
    ///
    /// Kısıtlar <c>NOT VALID</c> eklenir: PostgreSQL bunları HER YENİ yazımda uygular ama
    /// mevcut satırları taramaz/reddetmez, yani gerçek veride migration ASLA patlamaz.
    /// Ardından her kısıt için ihlal eden satır var mı bakılır; YOKSA VALIDATE edilir (kısıt tam
    /// geçerli olur), VARSA NOTICE basılıp NOT VALID bırakılır — elle temizlenip
    /// <c>ALTER TABLE .. VALIDATE CONSTRAINT ..</c> çalıştırılabilir. Hiçbir satır değiştirilmez.
    ///
    /// Kısıt ifadeleri bu dosyada DONDURULMUŞTUR (migration geçmişte ne yaptıysa onu yapar);
    /// bir enum'a üye eklendiğinde EF yeni bir migration üretir (bkz. CheckConstraintSql).
    /// </summary>
    public partial class AddEnumAndLengthCheckConstraints : Migration
    {
        // (schema, table, constraint, expression) — AuthDbContext / modül yapılandırmasındakiyle aynı.
        private static readonly (string Schema, string Table, string Name, string Sql)[] Checks =
        {
            ("namines_vault", "vault_schedules", "CK_vault_schedules_Cadence", "\"Cadence\" IN (0, 1)"),
            ("namines_vault", "vault_schedules", "CK_vault_schedules_DayOfWeek", "\"DayOfWeek\" BETWEEN 0 AND 6"),
            ("namines_vault", "vault_schedules", "CK_vault_schedules_HourUtc", "\"HourUtc\" BETWEEN 0 AND 23"),
            ("namines_vault", "vault_schedules", "CK_vault_schedules_RetainCount", "\"RetainCount\" >= 1"),
            ("namines_vault", "vault_restores", "CK_vault_restores_Status", "\"Status\" IN (0, 1, 2)"),
            ("namines_vault", "vault_backups", "CK_vault_backups_Kind", "\"Kind\" IN (0, 1, 2)"),
            ("namines_vault", "vault_backups", "CK_vault_backups_Status", "\"Status\" IN (0, 1, 2)"),
            (null, "UserAIPolicies", "CK_UserAIPolicies_DbaAnalysis", "\"DbaAnalysis\" IN (0, 1, 2, 3, 4, 6, 7, 8)"),
            (null, "UserAIPolicies", "CK_UserAIPolicies_Documentation", "\"Documentation\" IN (0, 1, 2, 3, 4, 6, 7, 8)"),
            (null, "UserAIPolicies", "CK_UserAIPolicies_Migration", "\"Migration\" IN (0, 1, 2, 3, 4, 6, 7, 8)"),
            (null, "UserAIPolicies", "CK_UserAIPolicies_Scaffolding", "\"Scaffolding\" IN (0, 1, 2, 3, 4, 6, 7, 8)"),
            (null, "UserAIPolicies", "CK_UserAIPolicies_SchemaGeneration", "\"SchemaGeneration\" IN (0, 1, 2, 3, 4, 6, 7, 8)"),
            (null, "UserAIPolicies", "CK_UserAIPolicies_SchemaRevision", "\"SchemaRevision\" IN (0, 1, 2, 3, 4, 6, 7, 8)"),
            (null, "UserAIPolicies", "CK_UserAIPolicies_SmartSeed", "\"SmartSeed\" IN (0, 1, 2, 3, 4, 6, 7, 8)"),
            (null, "UserAIPolicies", "CK_UserAIPolicies_Voice", "\"Voice\" IN (0, 1, 2, 3, 4, 6, 7, 8)"),
            (null, "UsageEvents", "CK_UsageEvents_Quantity", "\"Quantity\" >= 0"),
            (null, "UsageEvents", "CK_UsageEvents_Resource", "\"Resource\" IN (0, 1, 2, 3, 4, 5)"),
            (null, "TeamInvites", "CK_TeamInvites_Role", "\"Role\" IN (0, 1, 2, 3, 4)"),
            ("desk", "sql_query_history", "CK_sql_query_history_Sql_Length", "char_length(\"Sql\") <= 8000"),
            (null, "SchemaVersions", "CK_SchemaVersions_SchemaJson_Length", "char_length(\"SchemaJson\") <= 10000000"),
            ("desk", "saved_queries", "CK_saved_queries_Sql_Length", "char_length(\"Sql\") <= 20000"),
            (null, "RepoIssues", "CK_RepoIssues_Body_Length", "char_length(\"Body\") <= 20000"),
            (null, "RepoIssueComments", "CK_RepoIssueComments_Body_Length", "char_length(\"Body\") <= 10000"),
            (null, "OrganizationMembers", "CK_OrganizationMembers_Role", "\"Role\" IN (0, 1, 2, 3, 4)"),
            ("ground", "ground_databases", "CK_ground_databases_Status", "\"Status\" IN (0, 1, 2, 3, 4, 5)"),
            (null, "GatewayAuditEntries", "CK_GatewayAuditEntries_Kind", "\"Kind\" IN (0, 1, 2, 3, 4, 5, 6)"),
            (null, "GatewayApiKeys", "CK_GatewayApiKeys_RateLimitPerMinute", "\"RateLimitPerMinute\" >= 1"),
            (null, "Feedbacks", "CK_Feedbacks_Message_Length", "char_length(\"Message\") <= 4000"),
            (null, "CloudProjects", "CK_CloudProjects_Category_Length", "char_length(\"Category\") <= 64"),
            (null, "CloudProjects", "CK_CloudProjects_Description_Length", "char_length(\"Description\") <= 1000"),
            (null, "CloudProjects", "CK_CloudProjects_ForkCount", "\"ForkCount\" >= 0"),
            (null, "CloudProjects", "CK_CloudProjects_LinkedGithubRepo_Length", "char_length(\"LinkedGithubRepo\") <= 140"),
            (null, "CloudProjects", "CK_CloudProjects_Name_Length", "char_length(\"Name\") <= 512"),
            (null, "CloudProjects", "CK_CloudProjects_SchemaJson_Length", "char_length(\"SchemaJson\") <= 10000000"),
            (null, "CloudProjects", "CK_CloudProjects_StarCount", "\"StarCount\" >= 0"),
            (null, "CloudProjects", "CK_CloudProjects_Tags_Length", "char_length(\"Tags\") <= 256"),
            (null, "CloudProjects", "CK_CloudProjects_Visibility", "\"Visibility\" IN (0, 1, 2)"),
            (null, "ChangeRequests", "CK_ChangeRequests_RiskLevel", "\"RiskLevel\" IN (0, 1, 2, 3)"),
            (null, "ChangeRequests", "CK_ChangeRequests_Status", "\"Status\" IN (0, 1, 2)"),
            (null, "ChangeRequestAuditLogs", "CK_ChangeRequestAuditLogs_Action", "\"Action\" IN (0, 1, 2, 3)"),
            (null, "ChangeRequestApprovals", "CK_ChangeRequestApprovals_Decision", "\"Decision\" IN (0, 1)"),
            (null, "AspNetUsers", "CK_AspNetUsers_Type", "\"Type\" IN (0, 1, 2)"),
        };

        private static string Qualified(string schema, string table) =>
            schema is null ? $"\"{table}\"" : $"{schema}.\"{table}\"";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (schema, table, name, sql) in Checks)
            {
                migrationBuilder.Sql(
                    $"ALTER TABLE {Qualified(schema, table)} ADD CONSTRAINT \"{name}\" CHECK ({sql}) NOT VALID;");
            }

            // Temiz olanları doğrula. VALIDATE yalnızca SHARE UPDATE EXCLUSIVE kilidi alır
            // (okuma/yazmayı engellemez).
            foreach (var (schema, table, name, sql) in Checks)
            {
                var qualified = Qualified(schema, table);
                migrationBuilder.Sql($@"
DO $MIG$
DECLARE bad bigint;
BEGIN
    SELECT count(*) INTO bad FROM {qualified} WHERE NOT ({sql});
    IF bad = 0 THEN
        ALTER TABLE {qualified} VALIDATE CONSTRAINT ""{name}"";
    ELSE
        RAISE NOTICE 'CHECK {name}: % existing row(s) violate it; left NOT VALID (enforced for new writes only). Fix the rows, then run ALTER TABLE {qualified} VALIDATE CONSTRAINT ""{name}""', bad;
    END IF;
END $MIG$;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (schema, table, name, _) in Checks)
            {
                migrationBuilder.Sql(
                    $"ALTER TABLE {Qualified(schema, table)} DROP CONSTRAINT IF EXISTS \"{name}\";");
            }
        }
    }
}
