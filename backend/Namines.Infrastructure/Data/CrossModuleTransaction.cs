using System;
using System.Threading;
using System.Threading.Tasks;
using Namines.BuildingBlocks.Modules;
using Namines.BuildingBlocks.Persistence;

namespace Namines.Infrastructure.Data;

/// <inheritdoc cref="ICrossModuleTransaction"/>
public sealed class CrossModuleTransaction : ICrossModuleTransaction
{
    private readonly AuthDbContext _auth;
    private readonly StudioDbContext _studio;
    private readonly HubDbContext _hub;
    private readonly ISharedTransactionManager _transactions;

    public CrossModuleTransaction(
        AuthDbContext auth,
        StudioDbContext studio,
        HubDbContext hub,
        ISharedTransactionManager transactions)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _studio = studio ?? throw new ArgumentNullException(nameof(studio));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
        _transactions.ExecuteInTransactionAsync(_auth, new Microsoft.EntityFrameworkCore.DbContext[] { _studio, _hub }, operation, ct);
}
