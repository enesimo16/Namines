using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Namines.BuildingBlocks.Persistence;

#nullable disable

namespace Namines.Billing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Billing tablolarını AuthDbContext zincirinden devralır ve Stripe alanlarını <c>AspNetUsers</c>'tan çıkarır.
    ///
    /// Genişlet / doldur / doğrula / geçiş / daralt tek işlemde: yedi tablo <c>public</c>'ten <c>billing</c>'e
    /// yerinde taşınır (satır kopyalanmaz); <c>customers</c> ve <c>subscriptions</c> kullanıcı satırlarından
    /// doldurulur, <c>RAISE</c> ile sayı ve alan düzeyinde doğrulanır, ancak sonra beş kolon fiziksel olarak düşer.
    /// Doğrulama tutmazsa tüm işlem geri alınır; kullanıcı satırlarına UPDATE yapılmadığı için SecurityStamp ve
    /// oturumlar etkilenmez. <c>Down</c> kolonları geri koyar, veriyi tablolardan doldurur ve aynı doğrulamayı yapar.
    ///
    /// Aynı Stripe müşterisini taşıyan iki kullanıcı varsa (eski şemada yalnızca düz indeks vardı) migration
    /// hata verir: hangisinin gerçek sahip olduğunu otomatik seçmek yanlış kişiyi ücretli plana bağlardı.
    /// Boş dizge müşteri kimliği "yok" sayılır (anlamsız ve benzersizliği bozar); Down'da NULL döner.
    /// </summary>
    public partial class Billing_01_AdoptTables : Migration
    {
        private static readonly string[] Tables =
        {
            "UsageEvents", "UserAIQuotas", "UserBillingSettings", "GlobalAiUsages", "OrgAiUsages",
            "StripeWebhookEvents", "PendingSubscriptionCancellations",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ModuleTableHandoff.BeginMove(migrationBuilder, "billing");

            // Benzersizlik ihlalini yeni indeksten ÖNCE yakala: kullanıcıya anlaşılır hata ver.
            migrationBuilder.Sql("""
                DO $$
                DECLARE dup text;
                BEGIN
                  SELECT string_agg("StripeCustomerId", ', ') INTO dup FROM (
                    SELECT "StripeCustomerId" FROM public."AspNetUsers"
                    WHERE nullif("StripeCustomerId", '') IS NOT NULL
                    GROUP BY "StripeCustomerId" HAVING count(*) > 1 LIMIT 10) d;
                  IF dup IS NOT NULL THEN
                    RAISE EXCEPTION 'Billing_01: the same StripeCustomerId belongs to more than one user (%). Resolve the duplicates, then re-run.', dup;
                  END IF;
                END $$;
                """);

            ModuleTableHandoff.MoveTables(migrationBuilder, "public", "billing", Tables);

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "billing",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.UserId);
                    // Kullanıcı silinince müşteri eşlemesi de gider (hesap silme ayrıca açıkça temizler).
                    table.ForeignKey("FK_customers_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", principalSchema: "public", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "billing",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    StripeSubscriptionId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    PlanCode = table.Column<string>(type: "text", nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.UserId);
                    table.ForeignKey("FK_subscriptions_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", principalSchema: "public", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customers_StripeCustomerId", schema: "billing", table: "customers",
                column: "StripeCustomerId", unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_PlanCode", schema: "billing", table: "subscriptions", column: "PlanCode");
            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_StripeSubscriptionId", schema: "billing", table: "subscriptions", column: "StripeSubscriptionId");

            // Doldur.
            migrationBuilder.Sql("""
                INSERT INTO billing.customers ("UserId", "StripeCustomerId", "CreatedAt")
                SELECT "Id", "StripeCustomerId", now() FROM public."AspNetUsers"
                WHERE nullif("StripeCustomerId", '') IS NOT NULL;

                INSERT INTO billing.subscriptions ("UserId", "StripeSubscriptionId", "Status", "PlanCode", "CurrentPeriodEnd", "UpdatedAt")
                SELECT "Id", "StripeSubscriptionId", "SubscriptionStatus", "PlanCode", "CurrentPeriodEnd", now()
                FROM public."AspNetUsers"
                WHERE "StripeSubscriptionId" IS NOT NULL OR "SubscriptionStatus" IS NOT NULL
                   OR "PlanCode" IS NOT NULL OR "CurrentPeriodEnd" IS NOT NULL;
                """);

            // Doğrula: sayı ve alan düzeyinde; tutmazsa işlem geri alınır, kolonlar düşmez.
            migrationBuilder.Sql("""
                DO $$
                DECLARE expected bigint; actual bigint; bad bigint;
                BEGIN
                  SELECT count(*) INTO expected FROM public."AspNetUsers" WHERE nullif("StripeCustomerId", '') IS NOT NULL;
                  SELECT count(*) INTO actual FROM billing.customers;
                  IF expected <> actual THEN
                    RAISE EXCEPTION 'Billing_01: customers backfill count mismatch (% users, % rows)', expected, actual;
                  END IF;

                  SELECT count(*) INTO bad FROM public."AspNetUsers" u
                  LEFT JOIN billing.customers c ON c."UserId" = u."Id"
                  WHERE nullif(u."StripeCustomerId", '') IS DISTINCT FROM c."StripeCustomerId";
                  IF bad <> 0 THEN
                    RAISE EXCEPTION 'Billing_01: % users differ from billing.customers', bad;
                  END IF;

                  SELECT count(*) INTO expected FROM public."AspNetUsers"
                  WHERE "StripeSubscriptionId" IS NOT NULL OR "SubscriptionStatus" IS NOT NULL
                     OR "PlanCode" IS NOT NULL OR "CurrentPeriodEnd" IS NOT NULL;
                  SELECT count(*) INTO actual FROM billing.subscriptions;
                  IF expected <> actual THEN
                    RAISE EXCEPTION 'Billing_01: subscriptions backfill count mismatch (% users, % rows)', expected, actual;
                  END IF;

                  SELECT count(*) INTO bad FROM public."AspNetUsers" u
                  LEFT JOIN billing.subscriptions s ON s."UserId" = u."Id"
                  WHERE u."StripeSubscriptionId" IS DISTINCT FROM s."StripeSubscriptionId"
                     OR u."SubscriptionStatus" IS DISTINCT FROM s."Status"
                     OR u."PlanCode" IS DISTINCT FROM s."PlanCode"
                     OR u."CurrentPeriodEnd" IS DISTINCT FROM s."CurrentPeriodEnd";
                  IF bad <> 0 THEN
                    RAISE EXCEPTION 'Billing_01: % users differ from billing.subscriptions', bad;
                  END IF;
                END $$;
                """);

            // Daralt: kolonlar fiziksel olarak düşer (yalnızca NULL'lamak değil); indeks kolonla birlikte gider.
            migrationBuilder.Sql("""
                ALTER TABLE public."AspNetUsers"
                  DROP COLUMN "StripeCustomerId",
                  DROP COLUMN "StripeSubscriptionId",
                  DROP COLUMN "SubscriptionStatus",
                  DROP COLUMN "CurrentPeriodEnd",
                  DROP COLUMN "PlanCode";
                """);

            ModuleTableHandoff.GrantAppRole(migrationBuilder, "billing");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"SET LOCAL lock_timeout = '{ModuleTableHandoff.LockTimeout}';");

            migrationBuilder.Sql("""
                ALTER TABLE public."AspNetUsers"
                  ADD COLUMN "StripeCustomerId" text NULL,
                  ADD COLUMN "StripeSubscriptionId" text NULL,
                  ADD COLUMN "SubscriptionStatus" text NULL,
                  ADD COLUMN "CurrentPeriodEnd" timestamp with time zone NULL,
                  ADD COLUMN "PlanCode" text NULL;

                UPDATE public."AspNetUsers" u SET "StripeCustomerId" = c."StripeCustomerId"
                FROM billing.customers c WHERE c."UserId" = u."Id";

                UPDATE public."AspNetUsers" u SET
                  "StripeSubscriptionId" = s."StripeSubscriptionId",
                  "SubscriptionStatus" = s."Status",
                  "PlanCode" = s."PlanCode",
                  "CurrentPeriodEnd" = s."CurrentPeriodEnd"
                FROM billing.subscriptions s WHERE s."UserId" = u."Id";

                CREATE INDEX "IX_AspNetUsers_StripeCustomerId" ON public."AspNetUsers" ("StripeCustomerId");
                """);

            migrationBuilder.Sql("""
                DO $$
                DECLARE expected bigint; actual bigint; bad bigint;
                BEGIN
                  SELECT count(*) INTO expected FROM billing.customers;
                  SELECT count(*) INTO actual FROM public."AspNetUsers" WHERE "StripeCustomerId" IS NOT NULL;
                  IF expected <> actual THEN
                    RAISE EXCEPTION 'Billing_01 Down: customers restore count mismatch (% rows, % users)', expected, actual;
                  END IF;

                  SELECT count(*) INTO expected FROM billing.subscriptions;
                  SELECT count(*) INTO actual FROM public."AspNetUsers"
                  WHERE "StripeSubscriptionId" IS NOT NULL OR "SubscriptionStatus" IS NOT NULL
                     OR "PlanCode" IS NOT NULL OR "CurrentPeriodEnd" IS NOT NULL;
                  IF expected <> actual THEN
                    RAISE EXCEPTION 'Billing_01 Down: subscriptions restore count mismatch (% rows, % users)', expected, actual;
                  END IF;

                  SELECT count(*) INTO bad FROM public."AspNetUsers" u
                  LEFT JOIN billing.subscriptions s ON s."UserId" = u."Id"
                  WHERE u."StripeSubscriptionId" IS DISTINCT FROM s."StripeSubscriptionId"
                     OR u."SubscriptionStatus" IS DISTINCT FROM s."Status"
                     OR u."PlanCode" IS DISTINCT FROM s."PlanCode"
                     OR u."CurrentPeriodEnd" IS DISTINCT FROM s."CurrentPeriodEnd";
                  IF bad <> 0 THEN
                    RAISE EXCEPTION 'Billing_01 Down: % users differ after restore', bad;
                  END IF;
                END $$;
                """);

            migrationBuilder.DropTable(name: "customers", schema: "billing");
            migrationBuilder.DropTable(name: "subscriptions", schema: "billing");

            ModuleTableHandoff.MoveTables(migrationBuilder, "billing", "public", Tables);
        }
    }
}
