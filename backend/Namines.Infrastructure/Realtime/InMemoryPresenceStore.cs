using System.Collections.Concurrent;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.Realtime;

/// <summary>
/// Tek instance için presence deposu. Redis yapılandırılmamışsa (yerel geliştirme,
/// tek-instance dağıtım) kullanılır — Faz 1'deki <c>static ConcurrentDictionary</c>
/// davranışının aynısı, sadece artık DI singleton'ı (test edilebilir, static değil).
/// </summary>
public sealed class InMemoryPresenceStore : IPresenceStore
{
    private readonly ConcurrentDictionary<string, string> _membership = new();

    public Task SetRoomAsync(string connectionId, string roomId)
    {
        _membership[connectionId] = roomId;
        return Task.CompletedTask;
    }

    public Task<string?> TryGetRoomAsync(string connectionId) =>
        Task.FromResult(_membership.TryGetValue(connectionId, out var roomId) ? roomId : null);

    public Task<string?> RemoveAsync(string connectionId) =>
        Task.FromResult(_membership.TryRemove(connectionId, out var roomId) ? roomId : null);
}
