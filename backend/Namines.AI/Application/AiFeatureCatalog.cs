using Namines.AI.Domain;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Models.Auth;

namespace Namines.AI.Application;

/// <summary>
/// "Hangi uç bir AI özelliğidir ve ne kadar tutar?" sorusunun TEK cevabı.
///
/// Önceden bu tablo API'deki middleware'in içinde, yol önekleriyle iç içe if-else olarak duruyordu: maliyet
/// kuralı HTTP katmanına gömülüydü ve AI'nın kendi modülünde değildi. Middleware artık yalnızca HTTP'yi bu
/// kataloğa ve Billing kota sözleşmesine bağlayan bir adaptör.
/// </summary>
public static class AiFeatureCatalog
{
    private static readonly (string Prefix, AiFeature Feature)[] Features =
    {
        ("/api/aidba", new("dba-analysis", 3500, p => p.DbaAnalysis)),
        ("/api/schema/revise", new("schema-revision", 2500, p => p.SchemaRevision)),
        ("/api/smartseed", new("smart-seed", 2000, p => p.SmartSeed, Deterministic: true)),
        ("/api/schema/mockdata", new("mock-data", 2000, p => p.SmartSeed)),
        ("/api/schema/generate", new("schema-generation", 2500, p => p.SchemaGeneration, SelfReserving: true)),
        ("/api/reverseengineer", new("reverse-engineer", 4500, p => p.Scaffolding)),
        ("/api/coderai", new("coder-ai", 6000, p => p.Scaffolding, Deterministic: true)),
        ("/api/documentation/pdf", new("documentation", 3000, p => p.Documentation)),
        ("/api/migration/parse", new("migration", 3000, p => p.Migration)),
        ("/api/migration/generate", new("migration", 3000, p => p.Migration)),
        ("/api/voice", new("voice", 1500, p => p.Voice)),
    };

    /// <summary>Politika kaydı yoksa kullanılan mod (önceki davranış).</summary>
    public const AIMode DefaultMode = AIMode.Medium;

    /// <summary>Yol bir AI özelliğine aitse onu döndürür; değilse null. Karşılaştırma büyük/küçük harf duyarsız.</summary>
    public static AiFeature? Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var (prefix, feature) in Features)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return feature;
        return null;
    }

    /// <summary>Kullanıcının bu özellik için seçtiği mod; politika yoksa <see cref="DefaultMode"/>.</summary>
    public static AIMode ModeFor(AiFeature feature, AiPolicySnapshot? policy) =>
        policy is null ? DefaultMode : feature.ModeOf(policy);
}
