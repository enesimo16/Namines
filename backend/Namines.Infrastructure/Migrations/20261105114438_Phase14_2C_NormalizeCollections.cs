using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <summary>
    /// Faz 14.2C: CSV alanları ilişkisel tablolara. SQL'ler bilerek elle yazılmış (bkz.
    /// <see cref="Phase14_2CNormalizationScripts"/>): EF iskelesi veriyi taşımadan kolonları
    /// düşürürdü. Üretilen Designer/Snapshot modele eşit.
    /// </summary>
    public partial class Phase14_2C_NormalizeCollections : Migration
    {
        private const string Npgsql = "Npgsql.EntityFrameworkCore.PostgreSQL";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Yalnızca PostgreSQL: SQLite tabanlı testler EnsureCreated ile modelden kuruyor.
            if (migrationBuilder.ActiveProvider != Npgsql) return;
            migrationBuilder.Sql(Phase14_2CNormalizationScripts.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != Npgsql) return;
            migrationBuilder.Sql(Phase14_2CNormalizationScripts.Down);
        }
    }
}
