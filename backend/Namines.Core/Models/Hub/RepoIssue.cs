using System;
using System.ComponentModel.DataAnnotations;
using Namines.Core.Models.Auth;

namespace Namines.Core.Models.Hub
{
    public class RepoIssue
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ProjectId { get; set; } = null!;
        public CloudProject Project { get; set; } = null!;

        /// <summary>Proje bazında artan issue numarası (#1, #2, #3...)</summary>
        public int IssueNumber { get; set; }

        public string Title { get; set; } = null!;
        public string Body { get; set; } = string.Empty;

        public bool IsClosed { get; set; } = false;

        /// <summary>İlişkili tablo adı (ör: "orders")</summary>
        public string? RelatedTableName { get; set; }

        /// <summary>Linter kuralından üretildiyse kural kodu (ör: "NSL006")</summary>
        public string? NslRuleCode { get; set; }

        /// <summary>Virgülle ayrılmış etiketler (ör: "dba-risk,performance")</summary>
        public string? Labels { get; set; }

        public string? CreatedByUserId { get; set; }
        public ApplicationUser? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ClosedAt { get; set; }
    }
}
