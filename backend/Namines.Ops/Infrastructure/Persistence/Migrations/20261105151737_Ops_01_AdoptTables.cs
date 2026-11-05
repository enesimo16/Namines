using Microsoft.EntityFrameworkCore.Migrations;
using Namines.BuildingBlocks.Persistence;

#nullable disable

namespace Namines.Ops.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Ops tablolarını AuthDbContext zincirinden devralır. Boş ve canlı veritabanı burada aynı durumda:
    /// orkestratör Auth'u önce çalıştırır, tablolar <c>public</c>'tedir. Satır kopyalanmaz.
    /// </summary>
    public partial class Ops_01_AdoptTables : Migration
    {
        private static readonly string[] Tables = { "Feedbacks", "SqlExecutionAudits", "FeatureFlags", "AiToolRoutes" };

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ModuleTableHandoff.BeginMove(migrationBuilder, "ops");
            ModuleTableHandoff.MoveTables(migrationBuilder, "public", "ops", Tables);
            ModuleTableHandoff.GrantAppRole(migrationBuilder, "ops");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"SET LOCAL lock_timeout = '{ModuleTableHandoff.LockTimeout}';");
            ModuleTableHandoff.MoveTables(migrationBuilder, "ops", "public", Tables);
        }
    }
}
