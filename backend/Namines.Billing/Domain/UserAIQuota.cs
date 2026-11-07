using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Billing.Domain
{
    public class UserAIQuota
    {
        [Key]
        public string UserId { get; set; } = null!;
        public int DailyLimit { get; set; } = 100;          // Freemium default (100% points)
        public int DailyUsageCount { get; set; } = 0;
        public DateTime LastResetDate { get; set; } = DateTime.UtcNow;
    }
}
