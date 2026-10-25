using System;
using Namines.Core.Models.Auth;

namespace Namines.Core.Models.Hub
{
    /// <summary>
    /// Repo yıldızlama kaydı. Bileşik birincil anahtar (ProjectId, UserId).
    /// </summary>
    public class RepoStar
    {
        public string ProjectId { get; set; } = null!;
        public CloudProject Project { get; set; } = null!;

        public string UserId { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
