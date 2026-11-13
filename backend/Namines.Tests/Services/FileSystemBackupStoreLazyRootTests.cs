using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Namines.Vault.Storage;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Yerel Vault deposu singleton ve hesap silme yolu üzerinden girişin bağımlılık zincirinde. Yazılamayan bir kök
/// (konteynerde root'a ait /app) kurucuda atınca Vault'u değil girişi düşürüyordu: imaj duman testinde login 403.
/// Kök artık ilk yazımda oluşturuluyor.
/// </summary>
public sealed class FileSystemBackupStoreLazyRootTests : IDisposable
{
    private readonly string _blocker = Path.GetTempFileName(); // aynı adda dosya: altında dizin oluşturulamaz

    public void Dispose() => File.Delete(_blocker);

    private FileSystemBackupStore NewStore() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Vault:StoragePath"] = Path.Combine(_blocker, "vault") })
        .Build());

    [Fact]
    public async Task An_unusable_root_does_not_fail_construction_only_the_write()
    {
        var store = NewStore(); // önceden burada IOException

        Assert.Null(await store.OpenAsync("p1/b1.bak", CancellationToken.None));
        await Assert.ThrowsAnyAsync<IOException>(
            () => store.PutAsync("p1/b1.bak", new MemoryStream(new byte[] { 1, 2, 3 }), CancellationToken.None));
    }
}
