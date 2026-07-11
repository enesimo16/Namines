using System;

namespace Namines.Core.Models.Auth;

/// <summary>
/// Günlük paylaşımlı AI token havuzunun tüketim sayacı (tüm kullanıcılar ortak).
/// Her gün için tek satır tutulur; gün değişince yeni satır/atomik reset.
/// </summary>
public class GlobalAiUsage
{
    public int Id { get; set; }
    /// <summary>UTC gün (yyyy-MM-dd, saat 00:00).</summary>
    public DateTime Date { get; set; }
    public long TokensUsed { get; set; }
}
