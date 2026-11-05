using System;
using Microsoft.EntityFrameworkCore;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>
/// Bir modülün <see cref="ICrossModuleTransaction"/>'a kattığı bağlam. Koordinatör modül tiplerini
/// tanımaz; her modül kendi internal bağlamını bu kayıtla katar.
/// </summary>
public interface ITransactionParticipant
{
    DbContext Context { get; }
}

public sealed class TransactionParticipant : ITransactionParticipant
{
    public TransactionParticipant(DbContext context) =>
        Context = context ?? throw new ArgumentNullException(nameof(context));

    public DbContext Context { get; }
}
