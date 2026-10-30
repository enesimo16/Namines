using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <summary>
    /// DB-26/27/28/29: e-posta, kişisel org, API anahtarı özeti ve sağlayıcı proje kimliği
    /// için UNIQUE indeksler.
    ///
    /// ── E-posta yinelenenleri (HESAPLAR OTOMATİK BİRLEŞTİRİLMEZ) ──
    /// Önce yinelenen NormalizedEmail aranır; varsa migration NET bir hatayla DURUR (hiçbir şey
    /// değişmeden, transaction geri alınır). Elle çözüm:
    ///   1. Yinelenenleri listele (PII'yi loga yazmamak için migration yalnızca sayıları basar):
    ///        SELECT "NormalizedEmail", count(*), array_agg("Id" ORDER BY "CreatedAt")
    ///        FROM "AspNetUsers" WHERE "NormalizedEmail" IS NOT NULL
    ///        GROUP BY 1 HAVING count(*) > 1;
    ///   2. Her grup için gerçek hesabı belirle (en son giriş yapan / projesi olan). Diğerleri
    ///      için ya hesabı sil (CASCADE/SET NULL artık güvenli) ya da e-postayı değiştir:
    ///        UPDATE "AspNetUsers" SET "Email" = '<yeni>', "NormalizedEmail" = upper('<yeni>'),
    ///               "EmailConfirmed" = false WHERE "Id" = '<id>';
    ///   3. Migration'ı yeniden çalıştır.
    ///
    /// ── Diğer yinelenenler otomatik, veri kaybetmeden çözülür ──
    ///  * Kullanıcı başına birden çok kişisel org: en çok projesi olan (sonra en eski) kişisel
    ///    kalır, diğerleri IsPersonal=false'a çevrilir (normal org olarak ve içerikleriyle durur).
    ///  * Aynı KeyHash: en eskisi kalır; diğerleri iptal edilir (RevokedAt) ve özetleri
    ///    benzersizleştirilir — zaten doğrulanamazlar.
    ///  * Canlı (silinmemiş/başarısız olmayan) iki ground kaydı aynı sağlayıcı projesine bağlıysa
    ///    hangisinin gerçek olduğu otomatik bilinemez → migration net hatayla durur.
    /// </summary>
    public partial class EnforceUniqueness : Migration
    {
        private const string PreflightSql = @"
DO $MIG$
DECLARE
    dup_emails bigint;
    dup_accounts bigint;
    n bigint;
BEGIN
    -- 1) E-posta: yinelenen varsa DUR (otomatik birleştirme yok).
    SELECT count(*), coalesce(sum(c), 0) INTO dup_emails, dup_accounts
    FROM (SELECT count(*) AS c FROM ""AspNetUsers""
          WHERE ""NormalizedEmail"" IS NOT NULL
          GROUP BY ""NormalizedEmail"" HAVING count(*) > 1) d;
    IF dup_emails > 0 THEN
        RAISE EXCEPTION 'Cannot create unique index EmailIndex: % e-mail address(es) are shared by % accounts in total. Accounts are never auto-merged. Resolve manually (see the EnforceUniqueness migration comment for the query and steps), then re-run the migration.', dup_emails, dup_accounts;
    END IF;

    -- 2) Kişisel org: fazlalıkları normal org'a çevir.
    WITH ranked AS (
        SELECT o.""Id"",
               row_number() OVER (
                   PARTITION BY o.""CreatedByUserId""
                   ORDER BY (SELECT count(*) FROM ""CloudProjects"" p WHERE p.""OrganizationId"" = o.""Id"") DESC,
                            o.""CreatedAt"", o.""Id"") AS rn
        FROM ""Organizations"" o
        WHERE o.""IsPersonal"" AND o.""CreatedByUserId"" IS NOT NULL)
    UPDATE ""Organizations"" o SET ""IsPersonal"" = false
    FROM ranked r WHERE r.""Id"" = o.""Id"" AND r.rn > 1;
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'duplicate personal organizations demoted to regular: %', n;

    -- 3) API anahtarı özeti: fazlalıkları iptal et ve özetlerini benzersizleştir.
    WITH ranked AS (
        SELECT k.""Id"", row_number() OVER (PARTITION BY k.""KeyHash"" ORDER BY k.""CreatedAt"", k.""Id"") AS rn
        FROM ""GatewayApiKeys"" k)
    UPDATE ""GatewayApiKeys"" k
    SET ""RevokedAt"" = COALESCE(k.""RevokedAt"", now()),
        ""KeyHash"" = k.""KeyHash"" || ':duplicate:' || k.""Id""
    FROM ranked r WHERE r.""Id"" = k.""Id"" AND r.rn > 1;
    GET DIAGNOSTICS n = ROW_COUNT;
    RAISE NOTICE 'duplicate API key hashes revoked and renamed: %', n;

    -- 4) Ground: canlı kayıtlar arasında yinelenen sağlayıcı projesi var mı?
    SELECT count(*) INTO n FROM (
        SELECT 1 FROM ground.ground_databases
        WHERE ""ProviderProjectId"" IS NOT NULL AND ""Status"" NOT IN (3, 4)
        GROUP BY ""Provider"", ""ProviderProjectId"" HAVING count(*) > 1) d;
    IF n > 0 THEN
        RAISE EXCEPTION 'Cannot create unique index on ground_databases(Provider, ProviderProjectId): % provider project(s) are bound to more than one live ground record. Decide which record is real (mark the others Deleted/Failed) and re-run.', n;
    END IF;
END $MIG$;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PreflightSql);

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "UX_Organizations_PersonalOwner",
                table: "Organizations",
                column: "CreatedByUserId",
                unique: true,
                filter: "\"IsPersonal\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_ground_databases_Provider_ProviderProjectId",
                schema: "ground",
                table: "ground_databases",
                columns: new[] { "Provider", "ProviderProjectId" },
                unique: true,
                filter: "\"ProviderProjectId\" IS NOT NULL AND \"Status\" NOT IN (3, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayApiKeys_KeyHash",
                table: "GatewayApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Organizations_PersonalOwner",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_ground_databases_Provider_ProviderProjectId",
                schema: "ground",
                table: "ground_databases");

            migrationBuilder.DropIndex(
                name: "IX_GatewayApiKeys_KeyHash",
                table: "GatewayApiKeys");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
