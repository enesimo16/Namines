using System;

namespace Namines.Hub.Domain
{
    /// <summary>
    /// Repo yıldızlama kaydı. Bileşik birincil anahtar (ProjectId, UserId).
    /// </summary>
    public class RepoStar
    {
        public string ProjectId { get; set; } = null!;

        public string UserId { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
