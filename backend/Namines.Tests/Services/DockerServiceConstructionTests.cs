using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// Docker'a bağlı servisler, Docker OLMADAN kurulabilmeli.
///
/// <b>Yakaladığı gerçek arıza (TD-005 / BACK-006):</b> Test projesi
/// Testcontainers üzerinden <c>Docker.DotNet.Enhanced</c> paketini çekiyor.
/// O paket, adı <c>Docker.DotNet</c> olan bir derleme üretiyor — yani üretimde
/// kullanılan <c>Docker.DotNet</c> 3.125.15 paketiyle <b>aynı dosya adını</b>
/// taşıyor. NuGet bunu bir çatışma olarak göremiyor (paket kimlikleri farklı),
/// çıktı klasöründe yüksek sürümlü olan kazanıyor ve 3.x sessizce kayboluyor.
///
/// Yeni sürümde <c>DockerClientConfiguration</c> türü <c>DockerConfiguration</c>
/// olarak yeniden adlandırılmış. Sonuç: bu servislerden birini testte kurmak
/// <c>TypeLoadException: Could not load type 'Docker.DotNet.DockerClientConfiguration'</c>
/// fırlatıyordu — istemci kurucuda oluşturulduğu için, Docker'a hiç dokunmayan
/// bir testte bile.
///
/// <b>Denetim raporundaki teşhis eksikti</b> ("Testcontainers Docker.DotNet 4.x
/// getiriyor"); asıl neden bir sürüm aralığı çatışması değil, iki farklı paketin
/// aynı derleme adını üretmesi. Bu ayrım önemli: sürüm sabitlemek sorunu
/// çözmezdi.
///
/// <b>Çözüm:</b> İstemci artık <c>Lazy&lt;T&gt;</c> ile ilk kullanımda kuruluyor
/// (<c>Namines.Vault.ContainerBackupProvider</c>'daki desenle aynı). Bu testler
/// o davranışı kilitliyor. <c>Dispose</c> de istemciyi ZORLA KURMAMALI —
/// aksi hâlde temizlik, kaçındığımız istisnayı geri getirirdi.
///
/// <b>Not — kalıcı çözüm ertelendi:</b> Üretim kodunu 4.x forkuna taşımak
/// (<c>DockerConfiguration</c>) yeniden adlandırma yüzünden 5 dosyayı
/// etkiliyor ve doğruluğu ancak CANLI Docker ile kanıtlanabilir. Bu oturumda
/// Docker kullanılamadığı için yapılmadı; kanıtlanamayan bir geçiş,
/// kanıtlanmış bir arızadan daha kötüdür.
/// </summary>
public class DockerServiceConstructionTests
{
    [Fact]
    public void DockerBackupService_Docker_olmadan_kurulabilir()
    {
        using var service = new DockerBackupService();

        Assert.NotNull(service);
    }

    [Fact]
    public void BranchTestRunnerService_Docker_olmadan_kurulabilir()
    {
        using var service = new BranchTestRunnerService(ddlGeneratorFactory: null!);

        Assert.NotNull(service);
    }

    [Fact]
    public void BranchDatabaseProvisioner_Docker_olmadan_kurulabilir()
    {
        using var service = new BranchDatabaseProvisioner(
            ddlFactory: null!,
            scopeFactory: null!,
            logger: NullLogger<BranchDatabaseProvisioner>.Instance);

        Assert.NotNull(service);
    }

    [Fact]
    public void DockerSweeperBackgroundService_Docker_olmadan_kurulabilir()
    {
        using var service = new DockerSweeperBackgroundService(
            NullLogger<DockerSweeperBackgroundService>.Instance,
            jobRegistry: null!,
            branchDatabases: null!,
            new ConfigurationBuilder().Build());

        Assert.NotNull(service);
    }

    /// <summary>
    /// <c>Dispose</c>, hiç kullanılmamış bir istemciyi kurmaya ÇALIŞMAMALI.
    /// <c>_clientLazy.Value.Dispose()</c> yazılsaydı temizlik, kaçındığımız
    /// <c>TypeLoadException</c>'ı tam da nesne atılırken geri getirirdi.
    /// </summary>
    [Fact]
    public void Kullanilmamis_istemciyi_Dispose_kurmaya_calismaz()
    {
        var service = new DockerBackupService();

        var ex = Record.Exception(service.Dispose);

        Assert.Null(ex);
    }
}
