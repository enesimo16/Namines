using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Hub.Domain
{
    public class RepoIssueComment
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string IssueId { get; set; } = null!;

        public string Body { get; set; } = null!;

        public string? AuthorUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
