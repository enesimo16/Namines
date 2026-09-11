using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectRowVersion : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// REL-003a / B-32 -- optimistic concurrency belirteci.
        ///
        /// <b>`AddColumn(name: "xmin")` YANILTICI ama DOGRU.</b> `xmin`
        /// PostgreSQL'de ayrilmis bir SISTEM kolonu adi ve normalde
        /// eklenemez -- bu satiri okuyup "uretimde patlar" diye dusunmek
        /// dogal. Olculdu: patlamiyor.
        ///
        /// Npgsql saglayicisi `rowVersion: true` + `xid` tipi + `xmin` adi
        /// birlesimini sistem kolonu olarak taniyor ve HIC DDL uretmiyor.
        /// Migration yalnizca model anlik goruntusunu kaydediyor; fiziksel
        /// bir kolon EKLENMIYOR. Uygulandiktan sonra `GET /auth/projects`
        /// calismaya devam etti ve belirtec okundu (canli dogrulama).
        ///
        /// Ileriye uyumluluk (deploy/MIGRATION-VE-GERI-ALMA.md §2): kolon
        /// eklenmedigi icin eski kod etkilenmiyor. `Down()` da bir sey
        /// dusurmuyor.
        ///
        /// ELLE DEGISTIRME: bu blogu "duzeltmek" (or. AddColumn'u silmek)
        /// model anlik goruntusuyle migration'i ayristirir ve bir sonraki
        /// `migrations add` cagrisi ayni kolonu tekrar uretir.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "CloudProjects",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "CloudProjects");
        }
    }
}
