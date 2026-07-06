using Microsoft.AspNetCore.Identity;
using System;

namespace Namines.Core.Models.Auth
{
    public class ApplicationUser : IdentityUser
    {
        public UserType Type { get; set; } = UserType.Individual;
        public string? CompanyName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Stripe Subscription Information
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
        public string? SubscriptionStatus { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }

        // Extended profile data stored as JSON (bio, social links, location, etc.)
        public string? ProfileJson { get; set; }
    }
}
