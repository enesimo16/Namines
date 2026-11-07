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
    private readonly ISharedTransactionManager _transactions;
    private readonly Microsoft.EntityFrameworkCore.DbContext[] _contexts;

    /// <summary>
    /// Katılımcıların ilki işlemi başlatır, kalanlar aynı bağlantı ve işleme katılır. Hepsi aynı veritabanında;
    /// hangisinin ilk olduğu önemsiz, yeter ki her modülün bağlamı listede olsun.
    /// </summary>
    public CrossModuleTransaction(IEnumerable<ITransactionParticipant> participants, ISharedTransactionManager transactions)
    {
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _contexts = (participants ?? throw new ArgumentNullException(nameof(participants))).Select(p => p.Context).ToArray();
        if (_contexts.Length == 0) throw new ArgumentException("En az bir modül bağlamı gerekir.", nameof(participants));
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
        _transactions.ExecuteInTransactionAsync(_contexts[0], _contexts.Skip(1).ToList(), operation, ct);
}
