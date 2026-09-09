using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.Vault.Abstractions;
using Namines.Vault.DependencyInjection;
using Namines.Vault.Storage;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Vault'un yedek deposunu YAPILANDIRMADAN seçmesi.
///
/// <b>Neden test ediliyor:</b> seçim sessiz. Yanlış tarafa düşerse kullanıcı
/// yedeklerinin nesne deposunda olduğunu sanarken onlar sunucu diskinde durur
/// (ve sunucu yeniden kurulduğunda gider) — hatanın anlaşıldığı an, yedeğe
/// gerçekten ihtiyaç duyulan andır.
/// </summary>
public class VaultStoreSelectionTests
{
    private static IBackupStore Resolve(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            // Cipher yapılandırması her durumda gerekli: eksikse Vault
            // AÇIKÇA duruyor ve testin ölçtüğü şey o olmuyor.
            ["Vault:BackupEncryptionKey"] = new string('k', 40),
        };
        foreach (var (key, value) in settings) values[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddNaminesVault();

        return services.BuildServiceProvider().GetRequiredService<IBackupStore>();
    }

    [Fact]
    public void Falls_back_to_local_disk_when_no_object_store_is_configured()
    {
        var store = Resolve(("Vault:StoragePath", AppContext.BaseDirectory));

        Assert.IsType<FileSystemBackupStore>(store);
        // Açıklama kullanıcıya gösteriliyor: "nerede duruyor" sorusunun cevabı
        // gizlenmemeli, çünkü diskte durmak kabul edilebilir ama bilinmesi şart.
        Assert.Contains("Local disk", store.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Uses_the_object_store_when_a_bucket_is_configured()
    {
        var store = Resolve(
            ("Vault:S3:Bucket", "namines-backups"),
            ("Vault:S3:AccessKey", "key"),
            ("Vault:S3:SecretKey", "secret"),
            ("Vault:S3:ServiceUrl", "http://localhost:19000"));

        Assert.IsType<S3BackupStore>(store);
        // Bucket ve adres açıklamada görünüyor ki kullanıcı YANLIŞ bir kovaya
        // yazdığını fark edebilsin.
        Assert.Contains("namines-backups", store.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void An_object_store_without_credentials_fails_loudly()
    {
        // Sessizce diske düşmek EN KÖTÜ davranış olurdu: kullanıcı nesne
        // deposu istediğini söylemiş, biz de "tamam" deyip başka yere yazmış
        // olurduk. Eksik yapılandırma açıkça hata vermeli.
        var error = Assert.Throws<InvalidOperationException>(
            () => Resolve(("Vault:S3:Bucket", "namines-backups")));

        Assert.Contains("Vault:S3:AccessKey", error.Message, StringComparison.Ordinal);
    }
}
