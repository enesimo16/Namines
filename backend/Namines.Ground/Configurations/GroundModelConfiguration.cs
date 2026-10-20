using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;

namespace Namines.Ground.Configurations
{
    /// <summary>
    /// Namines Ground modülü için izole veritabanı şema ve tablo konfigürasyonları.
    /// Modular Monolith mimarisinde "ground" şeması altında yaşar.
    /// </summary>
    public static class GroundModelConfiguration
    {
        public const string DefaultSchema = "ground";

        public static void Configure(ModelBuilder builder, string? schema = DefaultSchema)
        {
            builder.Entity<GroundDatabase>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("ground_databases", schema: schema);
                else
                    entity.ToTable("ground_databases");

                // Proje başına EN FAZLA bir yönetilen veritabanı.
                entity.HasIndex(g => g.ProjectId).IsUnique();
            });
        }
    }
}
