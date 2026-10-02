using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Security;
using Namines.Infrastructure.Security;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// SSRF host çıkarımı: sürücünün <c>server</c> ile EŞANLAMLI saydığı anahtarlar.
///
/// <b>Bulunan açık:</b> <see cref="DbIntrospectionService.ExtractHosts"/> yalnızca
/// <c>server|host|data source|datasource</c> tanıyordu. SqlClient ve MySqlConnector ayrıca
/// <c>Address</c>, <c>Addr</c> ve <c>Network Address</c>'i kabul ediyor ve tekrar eden anahtarda
/// SON değer kazanıyor: <c>Server=public;Addr=10.0.0.15</c> dizesinde guard ilk (masum) host'u
/// onaylıyor, sürücü ikincisine bağlanıyordu.
///
/// IP değişmezleri kasıtlı (bkz. <see cref="DbIntrospectionHostExtractionTests"/>): ağ/DNS'e çıkılmıyor.
/// </summary>
public class DbIntrospectionAddressAliasTests
{
    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private const string PublicIp = "93.184.216.34";
    private const string InternalIp = "10.0.0.15";

    private static IDbHostAccessPolicy StrictPolicy() =>
        new DbHostAccessPolicy(new FakeEnv(), new ConfigurationBuilder().Build(), NullLogger<DbHostAccessPolicy>.Instance);

    [Theory]
    [InlineData("MSSQL", "Addr")]
    [InlineData("MSSQL", "Address")]
    [InlineData("MSSQL", "Network Address")]
    [InlineData("MySQL", "Addr")]
    [InlineData("MySQL", "Address")]
    [InlineData("MariaDB", "Network Address")]
    public void A_hidden_synonym_host_is_extracted_and_denied(string dbType, string synonym)
    {
        var cs = $"Server={PublicIp};{synonym}={InternalIp};User ID=sa;Password=x;";

        var hosts = DbIntrospectionService.ExtractHosts(cs, dbType);

        Assert.Contains(PublicIp, hosts);
        Assert.Contains(InternalIp, hosts);
        Assert.NotNull(DbIntrospectionService.FindDisallowedHost(StrictPolicy(), cs, dbType));
    }

    [Fact]
    public void The_host_the_driver_will_actually_use_is_always_validated()
    {
        // Regex bir takma adı kaçırsa bile sürücünün builder'ının çözdüğü host aday olmalı.
        var cs = $"Data Source={PublicIp};Addr={InternalIp};User ID=sa;Password=x;";

        var hosts = DbIntrospectionService.ExtractHosts(cs, "MSSQL");

        Assert.Contains(InternalIp, hosts);
    }

    [Fact]
    public void An_ordinary_public_target_is_still_allowed()
    {
        var cs = $"Server={PublicIp},1433;Database=app;User ID=sa;Password=x;";

        Assert.Null(DbIntrospectionService.FindDisallowedHost(StrictPolicy(), cs, "MSSQL"));
    }

    [Fact]
    public void A_tcp_prefixed_server_is_validated_by_its_real_host()
    {
        var cs = $"Server=tcp:{InternalIp},1433;Database=app;User ID=sa;Password=x;";

        var hosts = DbIntrospectionService.ExtractHosts(cs, "MSSQL");

        Assert.Contains(InternalIp, hosts);
        Assert.NotNull(DbIntrospectionService.FindDisallowedHost(StrictPolicy(), cs, "MSSQL"));
    }
}
