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

    /// <summary>
    /// Host'un KESİN OLARAK özel/loopback olduğunu doğrular.
    ///
    /// <see cref="IsHostSafe"/>'in tersi DEĞİLDİR: orası "hepsi public mi?" diye
    /// sorar ve çözülemeyen bir ada da false der. Çağıranın "özel mi?" sorusunu
    /// onun olumsuzuyla cevaplamak, BİLİNMEYEN bir host'u özel saymak olurdu —
    /// TLS zorunluluğu gibi kararlarda bu, varsayılanı güvensiz tarafa düşürür.
    /// Burada bilinmeyen host özel SAYILMAZ.
    /// </summary>
    public static bool IsHostPrivate(string? host)
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

        return addresses.All(ip => !IsPublicAddress(ip));
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
            // 192.0.0.0/24 (IETF protokol atamaları)
            if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;
            // 198.18.0.0/15 (ağ benchmark testi)
            if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) return false;
            // 224.0.0.0/4 multicast + 240.0.0.0/4 ayrılmış (255.255.255.255 yayın dahil):
            // bunlar HİÇBİR ZAMAN geçerli bir tekil veritabanı hedefi değil.
            if (b[0] >= 224) return false;
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // `::` (belirtilmemiş adres) loopback DEĞİL ama işletim sistemi bağlanırken
            // yerel makineye yönlendiriyor; IsLoopback yalnızca `::1`i yakalıyor.
            // IsIPv6SiteLocal = fec0::/10 (kullanımdan kalkmış ama hâlâ özel ağ adresi;
            // IsIPv6UniqueLocal yalnızca fc00::/7'yi kapsıyor).
            if (ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6SiteLocal) return false;
            if (ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast) return false;
            // IPv4-mapped IPv6'yı v4 kurallarıyla tekrar değerlendir.
            if (ip.IsIPv4MappedToIPv6) return IsPublicAddress(ip.MapToIPv4());

            var v6 = ip.GetAddressBytes();
            // NAT64 (64:ff9b::/96): son 4 bayt gömülü IPv4 — 64:ff9b::a00:1 aslında 10.0.0.1.
            var nat64 = v6[0] == 0x00 && v6[1] == 0x64 && v6[2] == 0xff && v6[3] == 0x9b;
            for (var i = 4; nat64 && i < 12; i++) nat64 = v6[i] == 0;
            if (nat64) return IsPublicAddress(new IPAddress(new[] { v6[12], v6[13], v6[14], v6[15] }));
            // 6to4 (2002::/16): 2..5. baytlar gömülü IPv4.
            if (v6[0] == 0x20 && v6[1] == 0x02)
                return IsPublicAddress(new IPAddress(new[] { v6[2], v6[3], v6[4], v6[5] }));
            return true;
        }

        return false;
    }
}
