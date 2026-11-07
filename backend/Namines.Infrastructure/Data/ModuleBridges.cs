using Namines.BuildingBlocks.Contracts;
using Namines.Core.Security;
using Namines.Infrastructure.Services;

namespace Namines.Infrastructure.Data;

/// <summary>SSRF politikası Infrastructure'da; Studio bağlantı dizesini yalnızca bu dar kapıdan sordurur.</summary>
public sealed class ConnectionHostInspector : IConnectionHostInspector
{
    private readonly IDbHostAccessPolicy _policy;

    public ConnectionHostInspector(IDbHostAccessPolicy policy) => _policy = policy;

    public string? FindDisallowedHost(string connectionString, string connectionDbType) =>
        DbIntrospectionService.FindDisallowedHost(_policy, connectionString, connectionDbType);
}
