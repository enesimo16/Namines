using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCanExportPermission : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// F-08 / B-44 -- disa aktarim ayri bir izin oldu.
        ///
        /// IKI YENI KOLON DA `false` VARSAYILANIYLA GELIYOR, yani MEVCUT
        /// anahtarlar toplu indirme yetkisini KAYBEDIYOR. Bu bilincli bir
        /// kirilma:
        ///
        /// Varsayilani `true` yapmak (mevcut davranisi korumak) izni
        /// dekoratif hale getirirdi -- bugun okuma izni olan her anahtar
        /// indirmeye devam eder ve bulgu hic kapanmazdi. Guvenlik izinlerinde
        /// varsayilan KAPALI olmalidir; bu, deponun kendi ilkesi
        /// (08 §1 "hicbir tablo varsayilan olarak public degil").
        ///
        /// Ileriye uyumluluk (deploy/MIGRATION-VE-GERI-ALMA.md §2) KORUNUYOR:
        /// varsayilani olan kolon EKLEMEK guvenli -- eski kod bu kolonlari
        /// bilmiyor, INSERT'lerinde yer vermiyor, veritabani varsayilani
        /// yaziyor. Geri alinabilir.
        ///
        /// Operatorun yapmasi gereken: indirmesi gereken anahtarlara
        /// `CanExport` ve ilgili tablolara tablo seviyesinde `CanExport`
        /// vermek. 403 mesaji bunu aciklikla soyluyor.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanExport",
                table: "GatewayTablePermissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanExport",
                table: "GatewayApiKeys",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanExport",
                table: "GatewayTablePermissions");

            migrationBuilder.DropColumn(
                name: "CanExport",
                table: "GatewayApiKeys");
        }
    }
}
