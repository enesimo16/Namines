using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubSlugUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Benzersiz indeks mevcut veriyle kurulamazsa migration yarıda patlar; önce
            // yinelenenler temizleniyor. Hiçbir satır silinmiyor, yalnızca yeniden adlandırılıyor.

            // Boş slug "slug yok" demek; NULL'a çekilince filtreli indeksin dışında kalır.
            migrationBuilder.Sql("""
                UPDATE "CloudProjects" SET "Slug" = NULL WHERE "Slug" = '';
                """);

            // (UserId, Slug) başına en eski satır adını korur; diğerlerine Id'nin ilk 6 karakteri eklenir.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (PARTITION BY "UserId", "Slug" ORDER BY "CreatedAt", "Id") AS rn
                    FROM "CloudProjects"
                    WHERE "Slug" IS NOT NULL
                )
                UPDATE "CloudProjects" p
                SET "Slug" = p."Slug" || '-' || LEFT(p."Id", 6)
                FROM ranked r
                WHERE p."Id" = r."Id" AND r.rn > 1;
                """);

            // Üretilen ad da başka bir satırla çakışırsa (çok düşük ihtimal) tam Id ile ayırt et.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (PARTITION BY "UserId", "Slug" ORDER BY "CreatedAt", "Id") AS rn
                    FROM "CloudProjects"
                    WHERE "Slug" IS NOT NULL
                )
                UPDATE "CloudProjects" p
                SET "Slug" = p."Slug" || '-' || p."Id"
                FROM ranked r
                WHERE p."Id" = r."Id" AND r.rn > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_CloudProjects_UserId",
                table: "CloudProjects");

            migrationBuilder.CreateIndex(
                name: "IX_CloudProjects_UserId_Slug",
                table: "CloudProjects",
                columns: new[] { "UserId", "Slug" },
                unique: true,
                filter: "\"Slug\" IS NOT NULL AND \"Slug\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CloudProjects_UserId_Slug",
                table: "CloudProjects");

            migrationBuilder.CreateIndex(
                name: "IX_CloudProjects_UserId",
                table: "CloudProjects",
                column: "UserId");
        }
    }
}
