using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Faz 14.2B: CloudProjects ayrıştırması. SQL'ler bilerek elle yazılmış (bkz.
    /// <see cref="Phase14_2BDecompositionScripts"/>): EF iskelesi veriyi taşımadan kolonları
    /// düşürürdü. Üretilen Designer/Snapshot modele eşit, yani bir sonraki migration temiz başlar.
    /// </summary>
    public partial class Phase14_2B_DecomposeCloudProjects : Migration
    {
        private const string Npgsql = "Npgsql.EntityFrameworkCore.PostgreSQL";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Yalnızca PostgreSQL: SQLite tabanlı testler EnsureCreated ile modelden kuruyor.
            if (migrationBuilder.ActiveProvider != Npgsql) return;
            migrationBuilder.Sql(Phase14_2BDecompositionScripts.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != Npgsql) return;
            migrationBuilder.Sql(Phase14_2BDecompositionScripts.Down);
        }
    }
}
