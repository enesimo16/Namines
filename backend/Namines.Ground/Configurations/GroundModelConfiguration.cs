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

                entity.ToTable(t =>
                    t.HasCheckConstraint(CheckConstraintSql.Name("ground_databases", "Status"), CheckConstraintSql.EnumIn<GroundStatus>("Status")));

                // Aynı sağlayıcı projesi iki kayda bağlanamaz (DB-29). Silinmiş (3) ve başarısız (4)
                // kayıtlar tarihçedir ve kimliği yeniden kullanabilir → kapsam dışı. GroundStatus
                // değerleri değişirse bu filtre için migration gerekir.
                entity.HasIndex(g => new { g.Provider, g.ProviderProjectId })
                      .IsUnique()
                      .HasFilter("\"ProviderProjectId\" IS NOT NULL AND \"Status\" NOT IN (3, 4)");

                // Proje başına EN FAZLA bir yönetilen veritabanı.
                entity.HasIndex(g => g.ProjectId).IsUnique();

                // Referans bütünlüğü (DB-02). Sağlayıcıda açılmış gerçek bir veritabanı
                // faturalanır: projeyi silmek onu SESSİZCE yetim bırakmamalı → RESTRICT.
                // Silme akışı önce sağlayıcı DB'yi kapatıp kaydı işaretlemek zorunda.
                entity.HasOne<CloudProject>().WithMany()
                      .HasForeignKey(g => g.ProjectId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Organization>().WithMany()
                      .HasForeignKey(g => g.OrganizationId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<ApplicationUser>().WithMany()
                      .HasForeignKey(g => g.CreatedByUserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
