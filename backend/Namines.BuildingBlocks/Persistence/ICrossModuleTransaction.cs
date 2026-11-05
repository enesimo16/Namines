using System;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.BuildingBlocks.Persistence;

/// <summary>
/// Proje çekirdeği (AuthDbContext), Studio ve Hub yazımlarını TEK veritabanı işleminde birleştirir:
/// bir proje repo satırı olmadan var olamaz, fork sayacı fork'tan ayrı artamaz.
///
/// <paramref name="operation"/> geçici hatada baştan çalışabilir. Bu yüzden içinde
/// <c>SaveChangesAsync(acceptAllChangesOnSuccess: false)</c> kullanılmalı ve değişiklikler dönüşten
/// sonra <c>AcceptAllChanges</c> ile kabul edilmeli; aksi hâlde ikinci turda EF kayıt yapacak bir
/// şey bulamaz ve geri alınmış satır sessizce kaybolur.
/// </summary>
public interface ICrossModuleTransaction
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);
}
