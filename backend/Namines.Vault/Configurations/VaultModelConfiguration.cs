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

                // Referans bütünlüğü (DB-01). Yedek = şifreli blob'a işaret eden veri:
                // projeyi SESSİZCE silip blob'u yetim bırakmak yerine RESTRICT — silme
                // akışı önce yedekleri (blob dahil) açıkça temizlemek zorunda.
                entity.HasOne<CloudProject>().WithMany()
                      .HasForeignKey(b => b.ProjectId)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Organization>().WithMany()
                      .HasForeignKey(b => b.OrganizationId)
                      .OnDelete(DeleteBehavior.Restrict);
                // Atıf kolonu: kullanıcı silinince boşalır, yedek kalır.
                entity.HasOne<ApplicationUser>().WithMany()
                      .HasForeignKey(b => b.CreatedByUserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<VaultRestore>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("vault_restores", schema: schema);
                else
                    entity.ToTable("vault_restores");

                entity.HasIndex(r => new { r.ProjectId, r.StartedAt });

                // Geri yükleme geçmişi: proje gidince geçmişi de gider (yedekler RESTRICT
                // ile zaten önce temizlenmiş olmak zorunda). Yedek tek tek silinebildiği
                // için (elle / saklama) yedek FK'ları SET NULL: RESTRICT olsaydı bir kez
                // geri yüklenmiş her yedek silinemezdi.
                entity.HasOne<CloudProject>().WithMany()
                      .HasForeignKey(r => r.ProjectId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Organization>().WithMany()
                      .HasForeignKey(r => r.OrganizationId)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<VaultBackup>().WithMany()
                      .HasForeignKey(r => r.BackupId)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<VaultBackup>().WithMany()
                      .HasForeignKey(r => r.PreRestoreBackupId)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<ApplicationUser>().WithMany()
                      .HasForeignKey(r => r.PerformedByUserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<VaultSchedule>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("vault_schedules", schema: schema);
                else
                    entity.ToTable("vault_schedules");

                entity.HasIndex(s => s.ProjectId).IsUnique();

                // Zamanlayıcı yapılandırmadır, veri değil: proje ile birlikte gider.
                entity.HasOne<CloudProject>().WithMany()
                      .HasForeignKey(s => s.ProjectId)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Organization>().WithMany()
                      .HasForeignKey(s => s.OrganizationId)
                      .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
