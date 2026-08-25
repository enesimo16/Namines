using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Namines.Core.Models.Auth
{
    /// <summary>
    /// Ozellik basina model tercihi.
    ///
    /// <b>Bu enum ARTIK saglayici modellerini temsil etmiyor.</b> Kullaniciya uc
    /// NAI modeli gosteriliyor ve sunucuda su sekilde eslesiyor:
    /// <c>Low</c> → NAI v1 Flash, <c>Medium</c> → NAI v1, <c>Ultra</c> → NAI v1 Pro.
    ///
    /// Eski degerler (HighMixtral, GeminiFlash, GeminiPro...) SILINMEDI: veritabaninda
    /// int olarak duruyorlar ve silinirse o satirlar cozumlenemez hale gelirdi.
    /// Okunurken uc modele indirgeniyorlar (bkz. GroqAIService.ResolveModelNameAsync).
    /// </summary>
    public enum AIMode
    {
        DefaultNamines = 0,
        Low = 1,          // → NAI v1 Flash
        Medium = 2,       // → NAI v1
        High = 3,         // legacy → NAI v1 Pro
        Ultra = 4,        // → NAI v1 Pro
        BYOK = 5,
        HighMixtral = 6,  // legacy → NAI v1 Pro
        GeminiFlash = 7,  // legacy → NAI v1
        GeminiPro = 8     // legacy → NAI v1 Pro
    }

    public class UserAIPolicy
    {
        [Key]
        public string UserId { get; set; } = null!;

        [ForeignKey("UserId")]
        public virtual ApplicationUser User { get; set; } = null!;

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
