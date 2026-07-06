using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Namines.Core.Models.Auth
{
    public enum AIMode
    {
        DefaultNamines = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Ultra = 4,
        BYOK = 5,
        HighMixtral = 6,
        GeminiFlash = 7,  // Gemini 1.5 Flash — High+ tier (5x)
        GeminiPro = 8     // Gemini 1.5 Pro   — Ultra tier   (6x)
    }

    public class UserAIPolicy
    {
        [Key]
        public string UserId { get; set; } = null!;

        [ForeignKey("UserId")]
        public virtual ApplicationUser User { get; set; } = null!;

        public AIMode SmartSeed { get; set; } = AIMode.HighMixtral;
        public AIMode Documentation { get; set; } = AIMode.HighMixtral;
        public AIMode Scaffolding { get; set; } = AIMode.HighMixtral;
        public AIMode SchemaGeneration { get; set; } = AIMode.HighMixtral;
        public AIMode SchemaRevision { get; set; } = AIMode.HighMixtral;
        public AIMode DbaAnalysis { get; set; } = AIMode.HighMixtral;
        public AIMode Migration { get; set; } = AIMode.HighMixtral;
        public AIMode Voice { get; set; } = AIMode.HighMixtral;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
