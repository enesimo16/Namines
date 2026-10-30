using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <summary>
    /// DB-41 (düşük riskli kısım): SchemaJson sütunları büyük metindir ve her şema sürümü tam
    /// kopyadır. TOAST sıkıştırmasını pglz'den lz4'e çevirmek (PostgreSQL 14+) sıkıştırma/
    /// açma maliyetini düşürür.
    ///
    /// Yalnızca YENİ yazılan değerleri etkiler; mevcut satırlar yeniden yazılmaz (tablo kilidi
    /// veya şişme yok). Sunucu &lt; 14 ya da lz4 desteği olmadan derlenmişse NOTICE ile
    /// atlanır — migration asla patlamaz. Model/şema değişikliği yok (EF'in bilmediği bir
    /// depolama özelliği).
    /// </summary>
    public partial class SetSchemaJsonCompressionLz4 : Migration
    {
        private const string Template = @"
DO $MIG$
BEGIN
    IF current_setting('server_version_num')::int < 140000 THEN
        RAISE NOTICE 'SET COMPRESSION skipped: PostgreSQL < 14';
        RETURN;
    END IF;
    BEGIN
        ALTER TABLE ""CloudProjects"" ALTER COLUMN ""SchemaJson"" SET COMPRESSION {0};
        ALTER TABLE ""SchemaVersions"" ALTER COLUMN ""SchemaJson"" SET COMPRESSION {0};
    EXCEPTION WHEN feature_not_supported THEN
        RAISE NOTICE 'SET COMPRESSION {0} skipped: not supported by this server build';
    END;
END $MIG$;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(string.Format(Template, "lz4"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(string.Format(Template, "DEFAULT"));
    }
}
