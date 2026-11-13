using Namines.BuildingBlocks.Contracts;
using Namines.Core.Models.Auth;

namespace Namines.AI.Domain;

/// <summary>
/// Bir AI özelliğinin kota açısından kimliği: ön rezervasyon tahmini ve kullanıcının o özellik için seçtiği mod.
/// </summary>
/// <param name="EstimatedTokens">Çağrıdan ÖNCE atomik olarak rezerve edilen tahmin; sonra gerçek ölçümle uzlaştırılır.</param>
/// <param name="SelfReserving">Uç kendi bütçesini kendi içinde rezerve edip uzlaştırıyor (middleware ikinci kez düşmez).</param>
/// <param name="Deterministic">İstek gövdesi <c>enhanceWithAI:false</c> diyorsa AI çağrısı yok, kota da yok.</param>
public sealed record AiFeature(
    string Name,
    int EstimatedTokens,
    Func<AiPolicySnapshot, AIMode> ModeOf,
    bool SelfReserving = false,
    bool Deterministic = false);
