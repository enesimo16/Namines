using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namines.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MigrateLegacyVaultSchemaToNaminesVault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
                    DO $$
                    BEGIN
                        -- Ensure target schema exists
                        CREATE SCHEMA IF NOT EXISTS namines_vault;

                        -- Migrate tables from legacy 'vault' schema if they exist there
                        IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'vault' AND table_name = 'vault_backups') THEN
                            ALTER TABLE vault.vault_backups SET SCHEMA namines_vault;
                        END IF;

                        IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'vault' AND table_name = 'vault_restores') THEN
                            ALTER TABLE vault.vault_restores SET SCHEMA namines_vault;
                        END IF;

                        IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'vault' AND table_name = 'vault_schedules') THEN
                            ALTER TABLE vault.vault_schedules SET SCHEMA namines_vault;
                        END IF;

                        -- Eski 'vault' sema BILEREK silinmiyor: Supabase'de 'vault' bir SISTEM semasi
                        -- (pgsodium/secrets) ve DROP ... CASCADE onu ve bagimlilarini yok eder.
                    END $$;
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
