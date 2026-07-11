using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Namines.Core.Security;

/// <summary>
/// SSRF koruması: kullanıcı kaynaklı URL/host'ların iç ağa, loopback'e veya
/// cloud metadata uçlarına yönelmesini engeller. Host adları DNS ile çözülüp
/// TÜM adresleri kontrol edilir (DNS rebinding'e karşı temel önlem).
/// </summary>
public static class SsrfGuard
{
    /// <summary>Bir HTTP(S) URL'inin dışa dönük güvenli bir hedefe işaret edip etmediğini doğrular.</summary>
    public static bool IsUrlSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        return IsHostSafe(uri.Host);
    }

    /// <summary>Bir host adının (veya IP'sinin) iç/özel/loopback aralıklara düşmediğini doğrular.</summary>
    public static bool IsHostSafe(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try { addresses = Dns.GetHostAddresses(host); }
            catch { return false; }
            if (addresses.Length == 0) return false;
        }

        // Herhangi bir çözülen adres güvensizse hedefi reddet.
        return addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            // 0.0.0.0/8
            if (b[0] == 0) return false;
            // 10.0.0.0/8
            if (b[0] == 10) return false;
            // 100.64.0.0/10 (CGNAT)
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;
            // 127.0.0.0/8 (loopback — IsLoopback zaten yakalar)
            if (b[0] == 127) return false;
            // 169.254.0.0/16 (link-local + cloud metadata 169.254.169.254)
            if (b[0] == 169 && b[1] == 254) return false;
            // 172.16.0.0/12
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
            // 192.168.0.0/16
            if (b[0] == 192 && b[1] == 168) return false;
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast) return false;
            // IPv4-mapped IPv6'yı v4 kurallarıyla tekrar değerlendir.
            if (ip.IsIPv4MappedToIPv6) return IsPublicAddress(ip.MapToIPv4());
            return true;
        }

        return false;
    }
}
