using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShareTokenHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShareTokenHash",
                table: "CloudProjects",
                type: "text",
                nullable: true);

            // Mevcut token'ların özetini doldur (C# ShareTokenHasher ile birebir aynı çıktı:
            // SHA-256, UTF-8, küçük harf hex). Düz metin sütun BİLİNÇLİ olarak kalıyor —
            // bkz. CloudProject.ShareToken TODO'su.
            migrationBuilder.Sql(@"
DO $MIG$
DECLARE n bigint;
BEGIN
    UPDATE ""CloudProjects""
    SET ""ShareTokenHash"" = encode(sha256(convert_to(""ShareToken"", 'UTF8')), 'hex')
    WHERE ""ShareToken"" IS NOT NULL AND ""ShareTokenHash"" IS NULL;
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'share tokens hashed: %', n;

    -- 192-bit rastgele token'larda çakışma beklenmez; yine de olursa UNIQUE indeks migration'ı
    -- patlatmasın: en eski projenin bağlantısı kalır, diğerlerinin bağlantısı iptal edilir
    -- (sahibi yeniden oluşturabilir).
    WITH ranked AS (
        SELECT ""Id"", row_number() OVER (PARTITION BY ""ShareTokenHash"" ORDER BY ""CreatedAt"", ""Id"") AS rn
        FROM ""CloudProjects"" WHERE ""ShareTokenHash"" IS NOT NULL)
    UPDATE ""CloudProjects"" c SET ""ShareToken"" = NULL, ""ShareTokenHash"" = NULL
    FROM ranked r WHERE r.""Id"" = c.""Id"" AND r.rn > 1;
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'duplicate share tokens revoked: %', n;
END $MIG$;");

            migrationBuilder.CreateIndex(
                name: "IX_CloudProjects_ShareTokenHash",
                table: "CloudProjects",
                column: "ShareTokenHash",
                unique: true,
                filter: "\"ShareTokenHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CloudProjects_ShareTokenHash",
                table: "CloudProjects");

            migrationBuilder.DropColumn(
                name: "ShareTokenHash",
                table: "CloudProjects");
        }
    }
}
