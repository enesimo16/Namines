using Microsoft.EntityFrameworkCore;
using Namines.Core.Models.Auth;

namespace Namines.Desk.Configurations
{
    /// <summary>
    /// Namines Desk modülü için izole veritabanı şema ve tablo konfigürasyonları.
    /// Modular Monolith mimarisinde "desk" şeması altında yaşar.
    /// </summary>
    public static class DeskModelConfiguration
    {
        public const string DefaultSchema = "desk";

        public static void Configure(ModelBuilder builder, string? schema = DefaultSchema)
        {
            // ── DeskHandoffToken (Desk v2 SSO devir jetonları) ──
            builder.Entity<DeskHandoffToken>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("desk_handoff_tokens", schema: schema);
                else
                    entity.ToTable("desk_handoff_tokens");

                entity.HasIndex(t => t.TokenHash).IsUnique();
            });

            // ── SqlQueryHistoryEntry (Desk SQL konsolunun sorgu geçmişi) ──
            builder.Entity<SqlQueryHistoryEntry>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("sql_query_history", schema: schema);
                else
                    entity.ToTable("sql_query_history");

                entity.HasIndex(h => new { h.UserId, h.ProjectId, h.CreatedAt });
            });

            // ── SavedQuery (Kullanıcının ada kaydettiği sorgular) ──
            builder.Entity<SavedQuery>(entity =>
            {
                if (!string.IsNullOrEmpty(schema))
                    entity.ToTable("saved_queries", schema: schema);
                else
                    entity.ToTable("saved_queries");

                entity.HasIndex(q => new { q.UserId, q.ProjectId, q.Name }).IsUnique();
            });
        }
    }
}
