using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>
/// Birden fazla modül `DbContext`'i üzerinde tek bir atomik veritabanı işlemi (transaction)
/// yürüten koordinatör (Kritik Kontrol #4: Transaction Boundaries).
/// </summary>
public interface ISharedTransactionManager
{
    Task ExecuteInTransactionAsync(
        DbContext primaryContext,
        IReadOnlyList<DbContext> secondaryContexts,
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default);
}

public sealed class SharedTransactionManager : ISharedTransactionManager
{
    public async Task ExecuteInTransactionAsync(
        DbContext primaryContext,
        IReadOnlyList<DbContext> secondaryContexts,
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default)
    {
        if (primaryContext is null) throw new ArgumentNullException(nameof(primaryContext));
        if (operation is null) throw new ArgumentNullException(nameof(operation));

        var validSecondaries = (secondaryContexts ?? Array.Empty<DbContext>())
            .Where(c => c is not null && !ReferenceEquals(c, primaryContext))
            .Distinct()
            .ToList();

        var strategy = primaryContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async cancellationToken =>
        {
            await using var transaction = await primaryContext.Database.BeginTransactionAsync(cancellationToken);
            var dbTransaction = transaction.GetDbTransaction();
            var sharedConnection = primaryContext.Database.GetDbConnection();
            var originalConnections = new Dictionary<DbContext, DbConnection>(validSecondaries.Count);

            try
            {
                foreach (var secondary in validSecondaries)
                {
                    var currentConn = secondary.Database.GetDbConnection();
                    if (!ReferenceEquals(currentConn, sharedConnection))
                    {
                        originalConnections[secondary] = currentConn;
                        if (currentConn.State != ConnectionState.Closed)
                        {
                            await secondary.Database.CloseConnectionAsync();
                        }
                        secondary.Database.SetDbConnection(sharedConnection, contextOwnsConnection: false);
                    }

                    await secondary.Database.UseTransactionAsync(dbTransaction, cancellationToken);
                }

                await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                foreach (var secondary in validSecondaries)
                {
                    try
                    {
                        await secondary.Database.UseTransactionAsync(null, CancellationToken.None);
                    }
                    catch
                    {
                        // Bir ikincil context'in transaction ayrıştırma hatası diğerlerinin temizliğini
                        // veya asıl işlem istisnasını maskelememeli.
                    }

                    if (originalConnections.TryGetValue(secondary, out var originalConn))
                    {
                        try
                        {
                            secondary.Database.SetDbConnection(originalConn, contextOwnsConnection: true);
                        }
                        catch
                        {
                            // Bağlantı geri yükleme hatası asıl istisnayı yutmamalı.
                        }
                    }
                }
            }
        }, ct);
    }
}
