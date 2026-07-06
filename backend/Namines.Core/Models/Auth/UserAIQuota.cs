using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Namines.Core.Models.Auth
{
    public class UserAIQuota
    {
        [Key]
        public string UserId { get; set; } = null!;

        [ForeignKey("UserId")]
        public virtual ApplicationUser User { get; set; } = null!;

        public int DailyLimit { get; set; } = 100;          // Freemium default (100% points)
        public int DailyUsageCount { get; set; } = 0;
        public DateTime LastResetDate { get; set; } = DateTime.UtcNow;
    }
}
