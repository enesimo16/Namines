using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <summary>
/// Branch başına GERÇEK, yaşayan bir veritabanı ayağa kaldırır
/// (new-phase/06-DATA-PLANE.md §4).
///
/// <see cref="IBranchTestRunner"/>'dan farkı ömür: test koşucusu DDL'i çalıştırıp
/// container'ı hemen atar ("kabul edildi mi?" sorusuna cevap verir). Buradaki
/// veritabanı branch kapanana ya da süresi dolana kadar YAŞAR ve bağlanılabilir —
/// geliştirici gerçekten sorgu çalıştırabilir.
///
/// <b>docker.sock ASLA bir container'a mount EDİLMEZ</b> (CLAUDE.md kesin kuralı:
/// host'ta root eşdeğeri yetki verir). <see cref="IBranchTestRunner"/> ile aynı
/// model geçerli: bu servis host süreci içinde çalışır ve Docker API'sine oradan
/// konuşur, container içinden değil (bkz. 30 §5).
/// </summary>
public interface IBranchDatabaseProvisioner
{
    /// <summary>
    /// Branch için veritabanını oluşturur ve şemayı uygular. Aynı branch için zaten
    /// çalışan bir veritabanı varsa YENİSİ AÇILMAZ, mevcut olan döner — aksi hâlde
    /// her sayfa yenilemesi host'ta bir container daha bırakırdı.
    /// </summary>
    Task<BranchDatabase> ProvisionAsync(
        string branchId, DatabaseSchema schema, DatabaseType engine,
        CancellationToken cancellationToken = default);

    /// <summary>Branch'in veritabanı varsa döner; yoksa null. Container oluşturmaz.</summary>
    Task<BranchDatabase?> GetAsync(string branchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Veritabanını yok eder. Yoksa sessizce başarılı sayılır — "zaten yok" ile
    /// "silinemedi" farklı şeyler, ama çağıran için ikisi de "artık yok" demek.
    /// </summary>
    Task DestroyAsync(string branchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Süresi dolmuş branch veritabanlarını temizler ve kaç tane kapatıldığını döner.
    /// Bir zaman aşımı olmadan bu container'lar host'u doldurur.
    /// </summary>
    Task<int> SweepExpiredAsync(CancellationToken cancellationToken = default);
}
