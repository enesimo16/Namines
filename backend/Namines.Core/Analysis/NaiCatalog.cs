using System;
using System.Collections.Generic;
using System.Linq;

namespace Namines.Core.Analysis;

/// <summary>
/// Kullanıcıya gösterilen Namines AI modelleri.
///
/// <b>Sağlayıcı adları kullanıcıya HİÇ gösterilmiyor</b> — ne "llama", ne "gpt",
/// ne "qwen". Üç sebep:
/// <list type="number">
/// <item>Kullanıcı hangisini seçeceğini bilmiyor; "llama-3.3-70b-versatile" ile
/// "llama-3.1-8b-instant" arasında seçim yapmak ürünün işi değil, bizim işimiz.</item>
/// <item>Sağlayıcı modelleri <b>ölüyor.</b> Bu kod tabanında tam olarak bu oldu:
/// yapılandırmadaki model bir gün "does not exist" demeye başladı ve şema üretimi
/// tamamen durdu. Ad bizim olursa üstteki değişiklik tek satırda kapanır.</item>
/// <item>Kota ancak modelin maliyeti biliniyorsa doğru işler; kullanıcı serbestçe
/// model seçebiliyorsa bütçe tahmin edilemez.</item>
/// </list>
/// </summary>
public enum NaiModel
{
    /// <summary>Hızlı ve ucuz. Kısa işler, öneriler, sınıflandırma.</summary>
    Flash = 0,

    /// <summary>Dengeli varsayılan.</summary>
    Standard = 1,

    /// <summary>En yetenekli. Şema tasarımı, karmaşık analiz.</summary>
    Pro = 2,
}

/// <param name="Id">Kullanıcıya ve API'ye görünen ad.</param>
/// <param name="DisplayName">Arayüzde yazan ad.</param>
/// <param name="Description">Kullanıcının hangisini seçeceğini anlaması için tek cümle.</param>
/// <param name="TokenMultiplier">
/// Kota maliyeti çarpanı.
///
/// Büyük model daha pahalı. Hepsini aynı saymak, kullanıcının her işi Pro'da
/// yapmasını teşvik eder ve bütçe bir günde biter.
/// </param>
public sealed record NaiModelInfo(
    string Id,
    string DisplayName,
    string Description,
    double TokenMultiplier);

/// <summary>
/// Namines AI modellerinin <b>TEK</b> tanımı.
///
/// Sağlayıcı model kimlikleri burada DEĞİL, sağlayıcının kendi kataloğunda
/// (<see cref="IModelCatalog"/>). Burada kalan her şey ürünün kendi kararı:
/// kademe adları, fiyat çarpanı, plan kısıtı — sağlayıcı değişse de değişmezler.
/// </summary>
public static class NaiCatalog
{
    /// <summary>
    /// Kademe tanımları: kullanıcıya görünen ad, açıklama ve kota çarpanı.
    ///
    /// Hangi kademenin hangi SAĞLAYICI MODELİNE karşılık geldiği burada değil
    /// (bkz. <see cref="IModelCatalog"/>); o eşleme yapılandırmadan override
    /// edilebilir (<c>Nai:Flash</c>, <c>Nai:Standard</c>, <c>Nai:Pro</c>).
    /// </summary>
    private static readonly Dictionary<NaiModel, NaiModelInfo> Models = new()
    {
        [NaiModel.Flash] = new NaiModelInfo(
            Id: "nai-v1-flash",
            DisplayName: "NAI v1 Flash",
            Description: "Fastest. Best for short edits, suggestions and quick answers.",
            TokenMultiplier: 0.5),

        [NaiModel.Standard] = new NaiModelInfo(
            Id: "nai-v1",
            DisplayName: "NAI v1",
            Description: "Balanced. The default for everyday work.",
            // Standard'ın Flash ile aynı MODELE düşmesi bilinçli bir taviz;
            // gerekçesi ve onu doğuran üç canlı hata GroqModelCatalog'da yazılı.
            // Buradaki çarpan ise sağlayıcıdan bağımsız: Standard, Flash'ın iki
            // katı kotaya mal olmaya devam ediyor.
            TokenMultiplier: 1.0),

        [NaiModel.Pro] = new NaiModelInfo(
            Id: "nai-v1-pro",
            DisplayName: "NAI v1 Pro",
            Description: "Most capable. Best for designing a schema from scratch or deep analysis.",
            TokenMultiplier: 2.0),
    };

    public static IReadOnlyList<NaiModelInfo> All => Models.Values.ToList();

    public static NaiModelInfo Get(NaiModel model) => Models[model];

    /// <summary>
    /// Kullanıcıdan gelen ada göre modeli bulur.
    ///
    /// <b>Tanınmayan ad varsayılana düşer, hata vermez.</b> Eski bir istemcinin
    /// "mixtral-8x7b" göndermesi isteği tamamen reddettirmemeli — kullanıcı
    /// açısından bu, ürünün çalışmayı bırakması demek olurdu.
    /// </summary>
    public static NaiModel Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return NaiModel.Standard;

        var normalized = id.Trim().ToLowerInvariant();

        // Sürümsüz eski kimlikler (nai-flash / nai / nai-pro) hâlâ tanınıyor.
        // Kullanıcıların kayıtlı tercihi tarayıcıda duruyor; v1'e geçerken onları
        // atmak, Pro seçmiş bir kullanıcıyı sessizce Standard'a düşürürdü ve bunu
        // ancak sonuç kalitesi düşünce fark ederdi.
        return normalized switch
        {
            "nai-v1-flash" or "nai-flash" or "flash" => NaiModel.Flash,
            "nai-v1-pro" or "nai-pro" or "pro" => NaiModel.Pro,
            "nai-v1" or "nai" or "standard" => NaiModel.Standard,
            _ => NaiModel.Standard,
        };
    }

    /// <summary>
    /// Bir planın kullanabileceği en yetenekli model.
    ///
    /// <b>Free planda Pro yok.</b> En pahalı modeli ücretsiz vermek, paylaşılan
    /// havuzu birkaç kullanıcının tüketmesi demek — ve o noktada ödeme yapan
    /// müşteri de hizmet alamaz.
    /// </summary>
    public static NaiModel MaxFor(PlanTier tier) => tier switch
    {
        PlanTier.Free => NaiModel.Standard,
        _ => NaiModel.Pro,
    };

    /// <summary>
    /// Planın izin verdiği en yetenekli modele indirger.
    ///
    /// İsteği REDDETMEK yerine indirgemek bilinçli: kullanıcı bir şema üretmek
    /// istiyor, model seçimi onun asıl derdi değil. "Pro'ya geçmelisin" diye
    /// hata vermek, işi bitirmesini engellemek olurdu.
    /// </summary>
    public static NaiModel ClampToPlan(NaiModel requested, PlanTier tier)
    {
        var max = MaxFor(tier);
        return requested > max ? max : requested;
    }

    /// <summary>Bir işin bu modelde kaça mal olacağı.</summary>
    public static int CostOf(NaiModel model, int baseTokens) =>
        (int)Math.Ceiling(baseTokens * Get(model).TokenMultiplier);
}
