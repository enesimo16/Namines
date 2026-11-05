using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Namines.Gateway.Domain;

/// <summary>
/// API anahtarının kaynak kısıtları (new-phase/08-GATEWAY-API.md §4.3:
/// <c>allowedIps</c>, <c>allowedOrigins</c>).
///
/// Kısıt işaretli değilse ve liste boşsa kısıt yoktur — anahtarın kendisi zaten bir kimlik.
/// Aksi hâlde kural katı: eşleşmeyen reddedilir ve <b>kararsız kalınan durumda da
/// reddedilir</b>. Gerekçe aşağıda, <see cref="IsIpAllowed"/>'da.
///
/// <b>"Kısıt var" ile "satır var" ayrı sorular:</b> geçersiz girdileri karantinaya alınmış ya da
/// navigasyonu yüklenmemiş bir anahtarda liste boş görünür ve bu asla "serbest" anlamına gelmemeli.
/// İki taraf da kapalıya düşer: bayrak VEYA satır varsa kısıt uygulanır, satır yoksa reddedilir.
/// </summary>
internal static class GatewayKeyRestrictions
{
    /// <summary>
    /// Origin kontrolü. Tarayıcıdan gelmeyen isteklerde <c>Origin</c> başlığı
    /// bulunmaz; liste doluysa bu da REDDEDİLİR — "origin kısıtla" diyen biri,
    /// başlığı hiç göndermeyen bir istemciye kapıyı açık bırakmayı kastetmez.
    /// </summary>
    public static bool IsOriginAllowed(GatewayApiKey key, string? origin)
    {
        if (!key.RestrictOrigins && key.OriginRules.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(origin)) return false;

        return key.OriginRules.Any(a => string.Equals(a.Origin, origin, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// IP kontrolü.
    ///
    /// <paramref name="clientAddressIsTrustworthy"/> KRİTİK: uygulama bir proxy
    /// arkasındaysa ve güvenilen proxy ağları tanımlanmamışsa, istemci
    /// <c>X-Forwarded-For</c>'ı istediği gibi yazabilir. Böyle bir ortamda IP
    /// beyaz listesi hiçbir şey doğrulamaz — üstelik doğruluyormuş gibi görünerek
    /// yanlış güven verir. Bu durumda liste doluysa istek REDDEDİLİR: kısıtı sessizce
    /// uygulanamaz hâle getirmek, onu hiç istememekten kötüdür.
    /// </summary>
    public static bool IsIpAllowed(
        GatewayApiKey key, IPAddress? clientAddress, bool clientAddressIsTrustworthy, out string reason)
    {
        reason = string.Empty;

        if (!key.RestrictIps && key.IpRules.Count == 0) return true;

        if (!clientAddressIsTrustworthy)
        {
            reason =
                "This key restricts source IPs, but the server cannot determine the caller's " +
                "address reliably (no trusted proxy networks are configured). Refusing rather " +
                "than pretending the restriction is enforced.";
            return false;
        }

        if (clientAddress is null)
        {
            reason = "This key restricts source IPs and the caller's address is unknown.";
            return false;
        }

        // IPv4-mapped IPv6 (::ffff:1.2.3.4) kural yazarken beklenmeyen bir biçim;
        // v4'e indirgemek "1.2.3.4/32" kuralının çalışmasını sağlar.
        var address = clientAddress.IsIPv4MappedToIPv6 ? clientAddress.MapToIPv4() : clientAddress;

        if (key.IpRules.Any(rule => Matches(rule.Cidr, address))) return true;

        // Adres mesaja KOYULMUYOR: hata gövdesi çağırana hangi IP olarak
        // görüldüğünü söylerse, kuralı atlatmayı deneyen birine geri bildirim verir.
        reason = "This key is not allowed from your network.";
        return false;
    }

    /// <summary>Tek bir kural: düz IP ya da CIDR. Ayrıştırılamayan kural EŞLEŞMEZ.</summary>
    internal static bool Matches(string rule, IPAddress address)
    {
        var slash = rule.IndexOf('/');
        if (slash < 0)
            return IPAddress.TryParse(rule, out var single) && Normalize(single).Equals(Normalize(address));

        if (!IPAddress.TryParse(rule[..slash], out var network)) return false;
        if (!int.TryParse(rule[(slash + 1)..], out var prefixLength)) return false;

        network = Normalize(network);
        address = Normalize(address);
        if (network.AddressFamily != address.AddressFamily) return false;

        var networkBytes = network.GetAddressBytes();
        var addressBytes = address.GetAddressBytes();
        var maxBits = networkBytes.Length * 8;
        if (prefixLength < 0 || prefixLength > maxBits) return false;

        var fullBytes = prefixLength / 8;
        for (var i = 0; i < fullBytes; i++)
            if (networkBytes[i] != addressBytes[i]) return false;

        var remainingBits = prefixLength % 8;
        if (remainingBits == 0) return true;

        // Kalan bitler için maske: örn. 3 bit → 11100000.
        var mask = (byte)(0xFF << (8 - remainingBits));
        return (networkBytes[fullBytes] & mask) == (addressBytes[fullBytes] & mask);
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6
            ? address.MapToIPv4()
            : address;
}
