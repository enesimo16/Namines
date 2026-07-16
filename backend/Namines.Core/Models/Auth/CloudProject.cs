using System;
using System.ComponentModel.DataAnnotations;

namespace Namines.Core.Models.Auth
{
    public class CloudProject
    {
        [Key]
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string DbType { get; set; } = null!;
        public string SchemaJson { get; set; } = null!;       // Serialized DatabaseSchema JSON
        public string NodePositionsJson { get; set; } = null!;  // Serialized positions JSON
        
        public string UserId { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Null ise proje özel, dolu ise bu token aracılığıyla herkese salt-okunur paylaşılmış.
        /// </summary>
        public string? ShareToken { get; set; }
    }
}
