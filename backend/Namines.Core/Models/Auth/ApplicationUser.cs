using Microsoft.AspNetCore.Identity;
using System;

namespace Namines.Core.Models.Auth
{
    public class ApplicationUser : IdentityUser
    {
        public UserType Type { get; set; } = UserType.Individual;
        public string? CompanyName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
