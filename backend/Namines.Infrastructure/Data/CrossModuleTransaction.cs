using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.BuildingBlocks.Persistence;

namespace Namines.Infrastructure.Data;

/// <inheritdoc cref="ICrossModuleTransaction"/>
public sealed class CrossModuleTransaction : ICrossModuleTransaction
{
    private readonly AuthDbContext _auth;
    private readonly ISharedTransactionManager _transactions;
    private readonly Microsoft.EntityFrameworkCore.DbContext[] _modules;

    public CrossModuleTransaction(
        AuthDbContext auth,
        IEnumerable<ITransactionParticipant> participants,
        ISharedTransactionManager transactions)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _modules = (participants ?? throw new ArgumentNullException(nameof(participants))).Select(p => p.Context).ToArray();
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
        _transactions.ExecuteInTransactionAsync(_auth, _modules, operation, ct);
}
