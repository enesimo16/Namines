using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;

namespace Namines.Vault.Configurations
{
    /// <summary>
    /// Namines Vault modülü için izole veritabanı şema ve tablo konfigürasyonları.
    /// Modular Monolith mimarisinde "vault" şeması altında yaşar.
    /// </summary>
    public static class VaultModelConfiguration
    {
        public const string DefaultSchema = "namines_vault";

        public static void Configure(ModelBuilder builder, string? schema = DefaultSchema)
        {
            builder.Entity<VaultBackup>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("vault_backups", schema: schema);
                else
                    entity.ToTable("vault_backups");

                entity.HasIndex(b => new { b.ProjectId, b.CreatedAt });
            });

            builder.Entity<VaultRestore>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("vault_restores", schema: schema);
                else
                    entity.ToTable("vault_restores");

                entity.HasIndex(r => new { r.ProjectId, r.StartedAt });
            });

            builder.Entity<VaultSchedule>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("vault_schedules", schema: schema);
                else
                    entity.ToTable("vault_schedules");

                entity.HasIndex(s => s.ProjectId).IsUnique();
            });
        }
    }
}
