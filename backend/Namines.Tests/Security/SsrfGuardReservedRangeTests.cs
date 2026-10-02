using Namines.Core.Security;

namespace Namines.Tests.Security;

/// <summary>
/// <see cref="SsrfGuard"/>'ın ilk sürümünün kaçırdığı adres aralıkları. <c>SsrfGuardTests</c> ile
/// aynı kural: yalnızca IP değişmezleri (DNS'e çıkmıyor).
/// </summary>
public class SsrfGuardReservedRangeTests
{
    [Theory]
    [InlineData("::")]                      // belirtilmemiş adres: işletim sistemi yerel makineye yönlendirir
    [InlineData("fec0::1")]                 // IPv6 site-local (fc00::/7 kapsamında DEĞİL)
    [InlineData("64:ff9b::a00:1")]          // NAT64 içinde 10.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe")]      // NAT64 içinde 169.254.169.254 (bulut metadata)
    [InlineData("2002:0a00:0001::1")]       // 6to4 içinde 10.0.0.1
    [InlineData("::ffff:10.0.0.1")]         // IPv4-mapped (önceden de reddediliyordu; gerilemeyi yakalar)
    public void Ipv6_forms_that_reach_internal_hosts_are_rejected(string host)
    {
        Assert.False(SsrfGuard.IsHostSafe(host));
    }

    [Theory]
    [InlineData("224.0.0.1")]               // multicast
    [InlineData("239.255.255.250")]         // multicast (SSDP)
    [InlineData("240.0.0.1")]               // ayrılmış
    [InlineData("255.255.255.255")]         // yayın
    [InlineData("198.18.0.1")]              // benchmark aralığı
    [InlineData("192.0.0.8")]               // IETF protokol atamaları
    public void Non_unicast_and_reserved_ipv4_is_rejected(string host)
    {
        Assert.False(SsrfGuard.IsHostSafe(host));
    }

    [Theory]
    [InlineData("93.184.216.34")]           // gerçek bir herkese açık adres
    [InlineData("8.8.8.8")]
    [InlineData("64:ff9b::808:808")]        // NAT64 içinde 8.8.8.8: herkese açık, reddedilmemeli
    [InlineData("2001:4860:4860::8888")]    // herkese açık IPv6
    public void Public_addresses_are_still_allowed(string host)
    {
        Assert.True(SsrfGuard.IsHostSafe(host));
    }
}
