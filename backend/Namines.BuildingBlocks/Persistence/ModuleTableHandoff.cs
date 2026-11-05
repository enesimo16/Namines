using Microsoft.EntityFrameworkCore.Migrations;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>
/// Tabloların AuthDbContext'ten bir modüle devri (bkz. docs/architecture/MIGRATION_PLAN.md §D-1):
/// satır kopyalanmaz, <c>ALTER TABLE ... SET SCHEMA</c> yalnızca katalogu değiştirir; FK, indeks, kimlik
/// dizisi ve yetkiler tabloyla birlikte gider. Auth tarafındaki eş migration bilerek boştur: EF'in
/// ürettiği DROP TABLE orada olsaydı veri giderdi.
/// </summary>
public static class ModuleTableHandoff
{
    /// <summary>
    /// Kilit kuyruğunda süresiz beklemesin: uzun bir sorgu bitene kadar sıraya giren ACCESS EXCLUSIVE,
    /// arkasındaki her isteği de bekletirdi. Süre dolarsa migration düşer, yeniden çalıştırılabilir.
    /// </summary>
    public const string LockTimeout = "30s";

    public static void BeginMove(MigrationBuilder migrationBuilder, string targetSchema)
    {
        migrationBuilder.EnsureSchema(targetSchema);
        migrationBuilder.Sql($"SET LOCAL lock_timeout = '{LockTimeout}';");
    }

    public static void MoveTables(MigrationBuilder migrationBuilder, string fromSchema, string toSchema, params string[] tables)
    {
        foreach (var table in tables)
            migrationBuilder.Sql($"ALTER TABLE \"{fromSchema}\".\"{table}\" SET SCHEMA \"{toSchema}\";");
    }

    /// <summary>
    /// roles.sql yeniden çalıştırılmamış kurulumda uygulama rolü yeni şemaya erişemez ve açılışta
    /// geçmiş tablosunu okuyamazdı. Rol yoksa (tek rollü kurulum) hiçbir şey yapmaz.
    /// </summary>
    public static void GrantAppRole(MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql($"""
            DO $$
            BEGIN
              IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'namines_app') THEN
                GRANT USAGE ON SCHEMA "{schema}" TO namines_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA "{schema}" TO namines_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA "{schema}" TO namines_app;
              END IF;
            END $$;
            """);
}
