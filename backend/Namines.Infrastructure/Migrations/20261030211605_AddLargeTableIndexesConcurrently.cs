using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <summary>
    /// DB-12/DB-16/DB-33: büyük tablolarda (GatewayAuditEntries, UsageEvents) indeks eklemek
    /// yazmaları bloklamasın diye CREATE INDEX CONCURRENTLY. CONCURRENTLY transaction içinde
    /// çalışamaz, o yüzden her komut <c>suppressTransaction: true</c>; bu migration BİLEREK
    /// yalnızca bu komutları içerir (karışık transaction/transaction-dışı migration, yarım
    /// uygulanmış durumu zorlaştırır).
    ///
    /// Yarım kalan bir CONCURRENTLY inşa geçersiz (indisvalid=false) bir indeks bırakır ve
    /// IF NOT EXISTS onu "var" sayardı: o yüzden önce geçersiz olanlar düşürülüyor.
    /// Model (AuthDbContext) aynı indeksleri tanımlar, yani EF açısından drift yoktur.
    /// </summary>
    public partial class AddLargeTableIndexesConcurrently : Migration
    {
        private static readonly string[] IndexNames =
        {
            "IX_GatewayAuditEntries_ProjectId_CreatedAt",
            "IX_UsageEvents_Resource_OccurredAt",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Geçersiz (yarım kalmış) indeksleri temizle. Düz DROP INDEX burada yeterli:
            // geçersiz indeks sorgu planlarında kullanılmaz.
            migrationBuilder.Sql(@"
DO $MIG$
DECLARE r record;
BEGIN
    FOR r IN
        SELECT n.nspname, c.relname
        FROM pg_index i
        JOIN pg_class c ON c.oid = i.indexrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE NOT i.indisvalid
          AND c.relname IN ('IX_GatewayAuditEntries_ProjectId_CreatedAt', 'IX_UsageEvents_Resource_OccurredAt')
    LOOP
        RAISE NOTICE 'Dropping invalid index %.%', r.nspname, r.relname;
        EXECUTE format('DROP INDEX %I.%I', r.nspname, r.relname);
    END LOOP;
END $MIG$;", suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ""IX_GatewayAuditEntries_ProjectId_CreatedAt"" ON ""GatewayAuditEntries"" (""ProjectId"", ""CreatedAt"" DESC);",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ""IX_UsageEvents_Resource_OccurredAt"" ON ""UsageEvents"" (""Resource"", ""OccurredAt"");",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var name in IndexNames)
            {
                migrationBuilder.Sql(
                    $@"DROP INDEX CONCURRENTLY IF EXISTS ""{name}"";",
                    suppressTransaction: true);
            }
        }
    }
}
