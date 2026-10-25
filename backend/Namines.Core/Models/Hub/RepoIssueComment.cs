using System;
using System.ComponentModel.DataAnnotations;
using Namines.Core.Models.Auth;

namespace Namines.Core.Models.Hub
{
    public class RepoIssueComment
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string IssueId { get; set; } = null!;
        public RepoIssue Issue { get; set; } = null!;

        public string Body { get; set; } = null!;

        public string AuthorUserId { get; set; } = null!;
        public ApplicationUser AuthorUser { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
