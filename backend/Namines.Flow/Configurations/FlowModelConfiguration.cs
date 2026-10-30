using Microsoft.EntityFrameworkCore;
using Namines.Core.Models;
using Namines.Core.Models.Auth;

namespace Namines.Flow.Configurations
{
    /// <summary>
    /// Namines Flow modülü için izole veritabanı şema ve tablo konfigürasyonları.
    /// Modular Monolith mimarisinde "flow" şeması altında yaşar.
    /// </summary>
    public static class FlowModelConfiguration
    {
        public const string DefaultSchema = "flow";

        public static void Configure(ModelBuilder builder, string? schema = DefaultSchema)
        {
            // ── AutomationRule (Tetikleyici kural tanımları) ──
            builder.Entity<AutomationRule>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("automation_rules", schema: schema);
                else
                    entity.ToTable("automation_rules");

                entity.HasOne<CloudProject>()
                      .WithMany()
                      .HasForeignKey(r => r.ProjectId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(r => new { r.ProjectId, r.Enabled });

                // Aksiyonlar kuralın parçası — kural silinince onlarla birlikte gider.
                entity.HasMany(r => r.Actions)
                      .WithOne()
                      .HasForeignKey(a => a.RuleId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ── AutomationAction (Kural aksiyon adımları) ──
            builder.Entity<AutomationAction>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("automation_actions", schema: schema);
                else
                    entity.ToTable("automation_actions");

                entity.HasIndex(a => new { a.RuleId, a.SortOrder });
            });

            // ── AutomationRunLog (Otomasyon çalıştırma geçmişi ve denetim kaydı) ──
            builder.Entity<AutomationRunLog>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("automation_run_logs", schema: schema);
                else
                    entity.ToTable("automation_run_logs");

                entity.HasOne<AutomationRule>()
                      .WithMany()
                      .HasForeignKey(l => l.RuleId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Kural bazında "son çalıştırmalar" (RuleId, TriggeredAt DESC) ve saklama temizliği
                // (WHERE TriggeredAt < cutoff) için (TriggeredAt) — DB-17. Tek başına RuleId
                // indeksi bileşiğin öneki olduğundan kaldırıldı.
                entity.HasIndex(l => new { l.RuleId, l.TriggeredAt })
                      .IsDescending(false, true);
                entity.HasIndex(l => l.TriggeredAt);
            });
        }
    }
}
