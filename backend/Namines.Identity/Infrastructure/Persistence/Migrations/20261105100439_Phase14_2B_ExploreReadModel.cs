using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hub okuma modeli: Explore'un q ve motor filtresi tek SQL'de çalışsın diye proje adı ve motoru
    /// hub.repositories'e kopyalanır (OwnerUserId deseni, modüller arası join yok).
    /// </summary>
    public partial class Phase14_2B_ExploreReadModel : Migration
    {
        private const string Npgsql = "Npgsql.EntityFrameworkCore.PostgreSQL";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != Npgsql) return;

            migrationBuilder.Sql("""
                LOCK TABLE studio.projects, hub.repositories IN ACCESS EXCLUSIVE MODE;

                ALTER TABLE hub.repositories
                    ADD COLUMN "ProjectName" text NULL,
                    ADD COLUMN "ProjectDbType" text NULL;

                -- NOT VALID CHECK'ler UPDATE'te yeniden denetlenir: tavanı aşan eski bir satır (Visibility=99 gibi)
                -- doldurmayı düşürürdü. Bu kolonlarla ilgileri yok; aynı işlemde kaldırılıp aynen geri eklenir.
                DO $backfill$
                DECLARE
                    c record;
                    saved text[] := ARRAY[]::text[];
                    def text;
                BEGIN
                    FOR c IN
                        SELECT conname, pg_get_constraintdef(oid) AS definition FROM pg_constraint
                        WHERE conrelid = 'hub.repositories'::regclass AND contype = 'c' AND NOT convalidated
                    LOOP
                        saved := saved || format('ADD CONSTRAINT %I %s', c.conname, c.definition);
                        EXECUTE format('ALTER TABLE hub.repositories DROP CONSTRAINT %I', c.conname);
                    END LOOP;

                    UPDATE hub.repositories r
                    SET "ProjectName" = p."Name", "ProjectDbType" = p."DbType"
                    FROM studio.projects p
                    WHERE p."Id" = r."ProjectId";

                    FOREACH def IN ARRAY saved LOOP
                        EXECUTE 'ALTER TABLE hub.repositories ' || def;
                    END LOOP;
                END $backfill$;

                DO $verify$
                DECLARE diff bigint;
                BEGIN
                    SELECT count(*) INTO diff FROM hub.repositories r
                    LEFT JOIN studio.projects p ON p."Id" = r."ProjectId"
                    WHERE p."Id" IS NULL
                       OR r."ProjectName" IS DISTINCT FROM p."Name"
                       OR r."ProjectDbType" IS DISTINCT FROM p."DbType";
                    IF diff > 0 THEN
                        RAISE EXCEPTION 'Phase14_2B read model verify: field_diff=%', diff;
                    END IF;
                END $verify$;

                ALTER TABLE hub.repositories
                    ALTER COLUMN "ProjectName" SET NOT NULL,
                    ALTER COLUMN "ProjectDbType" SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != Npgsql) return;

            migrationBuilder.Sql("""
                ALTER TABLE hub.repositories
                    DROP COLUMN "ProjectName",
                    DROP COLUMN "ProjectDbType";
                """);
        }
    }
}
