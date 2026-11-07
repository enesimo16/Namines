namespace Namines.Core.Models.Auth;

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
    // BYOK ("kendi anahtarını getir") kaldırıldı — 5 numara BOŞ BIRAKILDI,
    // yeniden KULLANILMADI: veritabanında hâlâ bu değere sahip eski satırlar
    // olabilir, GroqAIService.ResolveModelNameAsync onları "_" dalına
    // düşürüp NAI Standard'a çözümlüyor (bkz. dosya başındaki not).
    HighMixtral = 6,  // legacy → NAI v1 Pro
    GeminiFlash = 7,  // legacy → NAI v1
    GeminiPro = 8     // legacy → NAI v1 Pro
}
