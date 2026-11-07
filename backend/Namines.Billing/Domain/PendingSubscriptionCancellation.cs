using System;

namespace Namines.Billing.Domain;

/// <summary>
/// Abonelik iptali outbox'ı: kullanıcı silmeyle aynı işlemde yazılır, tamamlanana kadar yeniden denenir.
/// Kullanıcıya FK yok ve kullanıcı kimliği tutulmuyor: satır hesaptan uzun yaşamak zorunda, iptal için
/// abonelik kimliği yeterli.
/// </summary>
public class PendingSubscriptionCancellation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SubscriptionId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int Attempts { get; set; }
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Geri çekilme burada: işçi "vadesi gelen" açık satırları indeksle bulur.</summary>
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }
}
