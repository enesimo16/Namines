using Namines.AI.Application;
using Namines.AI.Domain;
using Namines.BuildingBlocks.Contracts;
using Namines.Core.Models.Auth;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Maliyet tablosu API middleware'inden AI modülüne taşındı. Davranış eşliği: her uç önceki tahmin, mod alanı,
/// deterministik ve kendi-rezervasyon bayraklarıyla aynı kalmalı.
/// </summary>
public sealed class AiFeatureCatalogTests
{
    // Her alana farklı mod: hangi alanın okunduğu ayırt edilebilsin.
    private static readonly AiPolicySnapshot Policy = new(
        SmartSeed: AIMode.Low, Documentation: AIMode.High, Scaffolding: AIMode.DefaultNamines,
        SchemaGeneration: AIMode.Medium, SchemaRevision: AIMode.High, DbaAnalysis: AIMode.Medium,
        Migration: AIMode.Low, Voice: AIMode.High, AdvancedJson: null);

    [Theory]
    [InlineData("/api/aidba/analyze", 3500, AIMode.Medium, false, false)]
    [InlineData("/api/schema/revise", 2500, AIMode.High, false, false)]
    [InlineData("/api/smartseed/generate", 2000, AIMode.Low, false, true)]
    [InlineData("/api/schema/mockdata", 2000, AIMode.Low, false, false)]
    [InlineData("/api/schema/generate", 2500, AIMode.Medium, true, false)]
    [InlineData("/api/reverseengineer/parse", 4500, AIMode.DefaultNamines, false, false)]
    [InlineData("/api/coderai/scaffold", 6000, AIMode.DefaultNamines, false, true)]
    [InlineData("/api/documentation/pdf", 3000, AIMode.High, false, false)]
    [InlineData("/api/migration/parse", 3000, AIMode.Low, false, false)]
    [InlineData("/api/migration/generate", 3000, AIMode.Low, false, false)]
    [InlineData("/api/voice/command", 1500, AIMode.High, false, false)]
    [InlineData("/API/AIDBA/analyze", 3500, AIMode.Medium, false, false)] // büyük/küçük harf duyarsız
    public void Ai_endpoints_keep_their_estimate_mode_and_flags(string path, int estimate, AIMode mode, bool selfReserving, bool deterministic)
    {
        var feature = AiFeatureCatalog.Resolve(path);

        Assert.NotNull(feature);
        Assert.Equal(estimate, feature!.EstimatedTokens);
        Assert.Equal(mode, AiFeatureCatalog.ModeFor(feature, Policy));
        Assert.Equal(selfReserving, feature.SelfReserving);
        Assert.Equal(deterministic, feature.Deterministic);
    }

    [Theory]
    [InlineData("/api/schema")]
    [InlineData("/api/documentation/markdown")] // yalnız PDF AI kullanıyor
    [InlineData("/api/migration/history")]
    [InlineData("/api/auth/login")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_ai_paths_are_not_metered(string? path) =>
        Assert.Null(AiFeatureCatalog.Resolve(path));

    [Fact]
    public void Without_a_policy_the_default_mode_applies() =>
        Assert.Equal(AIMode.Medium, AiFeatureCatalog.ModeFor(AiFeatureCatalog.Resolve("/api/aidba")!, null));
}
