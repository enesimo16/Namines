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

                        -- Clean up empty legacy vault schema if it exists and has no remaining tables
                        IF EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'vault') THEN
                            IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'vault') THEN
                                DROP SCHEMA IF EXISTS vault CASCADE;
                            END IF;
                        END IF;
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
