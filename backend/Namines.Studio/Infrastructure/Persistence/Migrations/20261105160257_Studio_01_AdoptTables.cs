using Microsoft.EntityFrameworkCore.Migrations;
using Namines.BuildingBlocks.Persistence;

#nullable disable

namespace Namines.Studio.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Studio tablolarını Auth zincirinden devralır. projects, project_connections ve project_shares zaten
    /// studio şemasında (14.2B); burada yalnızca sahiplik değişir. Branches, SchemaVersions ve
    /// CrossDatabaseRelations yerinde taşınır: satır kopyalanmaz, FK ve indeksler tabloyla gider.
    /// </summary>
    public partial class Studio_01_AdoptTables : Migration
    {
        private static readonly string[] Tables = { "Branches", "SchemaVersions", "CrossDatabaseRelations" };

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ModuleTableHandoff.BeginMove(migrationBuilder, "studio");
            ModuleTableHandoff.MoveTables(migrationBuilder, "public", "studio", Tables);
            ModuleTableHandoff.GrantAppRole(migrationBuilder, "studio");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"SET LOCAL lock_timeout = '{ModuleTableHandoff.LockTimeout}';");
            ModuleTableHandoff.MoveTables(migrationBuilder, "studio", "public", Tables);
        }
    }
}
