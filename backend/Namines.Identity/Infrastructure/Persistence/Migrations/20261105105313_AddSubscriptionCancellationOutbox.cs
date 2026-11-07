using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionCancellationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingSubscriptionCancellations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false, defaultValueSql: "gen_random_uuid()::text"),
                    SubscriptionId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingSubscriptionCancellations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingSubscriptionCancellations_Open",
                table: "PendingSubscriptionCancellations",
                column: "NextAttemptAt",
                filter: "\"CompletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // BL-2: açık satır, henüz iptal edilmemiş bir abonelik demek; tabloyu düşürmek müşteriyi sessizce
            // ücretlendirmeye devam ettirirdi. Önce işçinin bitirmesi ya da elle iptal gerekir.
            migrationBuilder.Sql("""
            DO $$
            BEGIN
              IF to_regclass('public."PendingSubscriptionCancellations"') IS NOT NULL
                 AND EXISTS (SELECT 1 FROM public."PendingSubscriptionCancellations" WHERE "CompletedAt" IS NULL) THEN
                RAISE EXCEPTION 'PendingSubscriptionCancellations has open rows (unfinished Stripe cancellations); finish or cancel them manually before rolling back.';
              END IF;
            END $$;
            """);

            migrationBuilder.DropTable(
                name: "PendingSubscriptionCancellations");
        }
    }
}
