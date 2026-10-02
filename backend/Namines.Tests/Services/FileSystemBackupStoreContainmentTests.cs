using Microsoft.Extensions.Configuration;
using Namines.Vault.Storage;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// <see cref="FileSystemBackupStore"/> kök kapsama kontrolü.
///
/// <b>Bulunan açık:</b> kontrol çıplak <c>StartsWith(_root)</c> idi ve kökün sonunda ayırıcı
/// yoktu; <c>/depo/vault-backups-evil/x</c> kök olan <c>/depo/vault-backups</c> ile başladığı için
/// geçiyordu. Anahtarın ilk parçası (proje kimliği) istemci seçimliydi.
/// </summary>
public sealed class FileSystemBackupStoreContainmentTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "namines-fsstore-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public FileSystemBackupStoreContainmentTests()
    {
        _root = Path.Combine(_parent, "vault-backups");
        Directory.CreateDirectory(_parent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_parent)) Directory.Delete(_parent, recursive: true);
    }

    private FileSystemBackupStore NewStore() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Vault:StoragePath"] = _root })
        .Build());

    private static MemoryStream Content() => new(new byte[] { 1, 2, 3 });

    [Fact]
    public async Task A_normal_key_is_stored_under_the_root()
    {
        var store = NewStore();

        await store.PutAsync("project-1/backup.nvlt", Content(), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_root, "project-1", "backup.nvlt")));
    }

    [Fact]
    public async Task A_sibling_directory_sharing_the_roots_prefix_is_rejected()
    {
        var store = NewStore();

        // `..` ile köke kardeş, adı kökün adıyla başlayan bir dizine çıkış.
        var key = Path.Combine("..", "vault-backups-evil", "stolen.nvlt");

        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(key, Content(), CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(_parent, "vault-backups-evil")));
    }

    [Theory]
    [InlineData("../outside.nvlt")]
    [InlineData("a/../../outside.nvlt")]
    public async Task Parent_traversal_is_rejected(string key)
    {
        var store = NewStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(key, Content(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_cannot_reach_a_sibling_directory_either()
    {
        var store = NewStore();
        var victimDir = Path.Combine(_parent, "vault-backups-evil");
        Directory.CreateDirectory(victimDir);
        var victim = Path.Combine(victimDir, "keep.nvlt");
        await File.WriteAllBytesAsync(victim, new byte[] { 9 });

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.DeleteAsync(Path.Combine("..", "vault-backups-evil", "keep.nvlt"), CancellationToken.None));

        Assert.True(File.Exists(victim));
    }
}
