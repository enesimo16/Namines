using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <summary>
/// AI araçlarının hangi LLM sağlayıcısına yönlendirileceğini yöneten dinamik yönlendirici.
/// </summary>
public interface IAiToolRouter
{
    Task<AiToolRoute> GetRouteAsync(string toolKey, CancellationToken ct = default);

    Task<IReadOnlyList<AiToolRoute>> GetAllRoutesAsync(CancellationToken ct = default);

    Task UpdateRouteAsync(
        string toolKey,
        string provider,
        string modelName,
        double temperature,
        string? fallbackProvider = null,
        CancellationToken ct = default);

    IChatCompletionProvider GetProvider(string providerName);

    IReadOnlyList<string> GetAvailableProviders();
}
