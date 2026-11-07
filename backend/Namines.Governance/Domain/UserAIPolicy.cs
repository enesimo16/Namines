using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Namines.Core.Models.Auth;

namespace Namines.Governance.Domain
{
    public class UserAIPolicy
    {
        [Key]
        public string UserId { get; set; } = null!;

        // Varsayilan FLASH. Onceden hepsi HighMixtral (en pahali karsilik) idi:
        // hicbir ayara dokunmamis bir kullanici, en ucuz isi bile en pahali
        // modelde calistiriyor ve gunluk butcesini gereksiz yere iki kat hizli
        // tuketiyordu. Flash cogu is icin yeterli; kullanici isterse yukseltir.
        public AIMode SmartSeed { get; set; } = AIMode.Low;
        public AIMode Documentation { get; set; } = AIMode.Low;
        public AIMode Scaffolding { get; set; } = AIMode.Low;
        // Sema uretimi tek istisna: kullanicinin urunle ilk temasi ve en kritik
        // cikti burasi. Dengeli model varsayilan, Flash degil.
        public AIMode SchemaGeneration { get; set; } = AIMode.Medium;
        public AIMode SchemaRevision { get; set; } = AIMode.Low;
        public AIMode DbaAnalysis { get; set; } = AIMode.Low;
        public AIMode Migration { get; set; } = AIMode.Low;
        public AIMode Voice { get; set; } = AIMode.Low;

        /// <summary>
        /// "Advanced AI Tuning" tercihleri, JSON olarak (bkz. AiAdvancedSettings).
        ///
        /// Tek kolonda tutuluyor cunku bunlar uzerinde sorgu calistirilmiyor;
        /// her yeni tercih icin migration gerektirmesi, ayar eklemeyi gereksiz
        /// pahali yapardi. Null ise varsayilanlar gecerli.
        /// </summary>
        public string? AdvancedJson { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
