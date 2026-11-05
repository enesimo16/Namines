using Microsoft.EntityFrameworkCore.Migrations;
using Namines.BuildingBlocks.Persistence;

#nullable disable

namespace Namines.Gateway.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gateway tablolarını Auth zincirinden devralır. Çocuk listeler (origin, IP, maske) 14.2C'den beri
    /// gateway şemasında; anahtar, izin ve denetim tabloları yerinde taşınır. Satır kopyalanmaz.
    /// </summary>
    public partial class Gateway_01_AdoptTables : Migration
    {
        private static readonly string[] Tables = { "GatewayApiKeys", "GatewayTablePermissions", "GatewayAuditEntries" };

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ModuleTableHandoff.BeginMove(migrationBuilder, "gateway");
            ModuleTableHandoff.MoveTables(migrationBuilder, "public", "gateway", Tables);
            ModuleTableHandoff.GrantAppRole(migrationBuilder, "gateway");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"SET LOCAL lock_timeout = '{ModuleTableHandoff.LockTimeout}';");
            ModuleTableHandoff.MoveTables(migrationBuilder, "gateway", "public", Tables);
        }
    }
}
