using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Namines.Core.Models.Auth;

/// <summary>
/// <c>gateway.api_key_allowed_origins</c>: anahtarın kabul ettiği tek bir <c>Origin</c>.
/// <see cref="Origin"/> kanonik biçimdedir (küçük harf); eşleşme tam dizge karşılaştırmasıdır.
/// </summary>
public sealed class GatewayApiKeyAllowedOrigin
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ApiKeyId { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;

    /// <summary>API listeyi kullanıcının yazdığı sırayla döndürür; satır sırası bunu garanti etmez.</summary>
    public int Position { get; set; }
}

/// <summary>
/// <c>gateway.api_key_allowed_ips</c>: tek bir ağ. Düz IP de <c>/32</c> ya da <c>/128</c> olarak
/// durur, host bitleri sıfırlıdır: "10.1.2.3/8" ile "10.0.0.0/8" aynı ağ ve UNIQUE ikisini yakalar.
/// </summary>
public sealed class GatewayApiKeyAllowedIp
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ApiKeyId { get; set; } = string.Empty;
    public string Cidr { get; set; } = string.Empty;
    public int Position { get; set; }
}

/// <summary>
/// <c>gateway.table_masked_columns</c>. Yineleme yalnızca BAYT BAYT aynı adda atılır: "Email" ile
/// "email" PostgreSQL'de iki ayrı kolon olabilir ve maskelemede yanlış birleştirme veri sızdırır.
/// Eşleşme maskeleme anında zaten büyük/küçük harf duyarsızdır.
/// </summary>
public sealed class GatewayTableMaskedColumn
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TablePermissionId { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public int Position { get; set; }
}

/// <summary>Ayrıştırma sonucu: geçerli kanonik girdiler ve geçersiz (ham) girdiler.</summary>
public sealed record AllowlistParseResult(IReadOnlyList<string> Entries, IReadOnlyList<string> Invalid)
{
    /// <summary>Boş olmayan en az bir girdi var mıydı (geçerli ya da değil).</summary>
    public bool HasAnyEntry => Entries.Count > 0 || Invalid.Count > 0;
}

/// <summary>
/// Kaynak kısıtı ve maskeleme listelerinin tek kural kaynağı. Migration'daki SQL karşılığı aynı
/// sonucu üretmek zorunda (<c>Phase14_2CMigrationTests</c> tablo ile sınar).
///
/// <b>Origin politikası:</b> <c>scheme://host[:port]</c>, başka bir şey değil. Joker karakter
/// DESTEKLENMEZ: eski kod da "*" girdisini joker olarak yorumlamıyordu, yalnızca harfi harfine
/// "*" başlığıyla eşleşiyordu; onu joker yapmak erişimi genişletirdi. Aynı gerekçeyle sondaki "/"
/// silinmez ve varsayılan port düşürülmez: eski "https://a.com/" girdisi tarayıcının gönderdiği
/// "https://a.com" ile eşleşmiyordu, temizlemek kapıyı açardı. "null" origin kabul edilmez:
/// korumalı her iframe onu gönderebilir. CORS kararı bu kuralların dışında, Program.cs'te.
/// </summary>
public static partial class GatewayAllowlistRules
{
    public const int MaxEntriesPerList = 64;
    public const int MaxOriginLength = 512;
    public const int MaxMaskedColumnLength = 512;

    /// <summary>
    /// Listesi kısıtlı ama geçerli girdisi kalmamış anahtarda API'nin döndürdüğü metin. Null dönseydi
    /// arayüz "kısıt yok" gösterirdi; oysa anahtar her isteği reddediyor.
    /// </summary>
    public const string DenyAllDisplay = "(no valid entries: all requests are denied)";

    /// <summary>Küçük harfli origin kalıbı. DB CHECK ve migration aynı metni kullanır.</summary>
    public const string OriginPattern =
        @"^[a-z][a-z0-9+.-]*://([a-z0-9_]([a-z0-9_-]*[a-z0-9_])?(\.[a-z0-9_]([a-z0-9_-]*[a-z0-9_])?)*|\[[0-9a-f:.]+\])(:[1-9][0-9]{0,4})?$";

    public const string Ipv4Pattern =
        @"^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}$";

    public const string Ipv6CharsPattern = @"^[0-9a-fA-F:.]*:[0-9a-fA-F:.]*$";

    public const string PrefixPattern = @"^(0|[1-9][0-9]{0,2})$";

    [GeneratedRegex(OriginPattern, RegexOptions.CultureInvariant)]
    private static partial Regex OriginRegex();

    [GeneratedRegex(Ipv4Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(Ipv6CharsPattern, RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6CharsRegex();

    [GeneratedRegex(PrefixPattern, RegexOptions.CultureInvariant)]
    private static partial Regex PrefixRegex();

    /// <summary>Geçerliyse kanonik origin, değilse null.</summary>
    public static string? NormalizeOrigin(string? entry)
    {
        var value = entry?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > MaxOriginLength) return null;

        // Tarayıcı Origin'i daima ASCII (punycode). Küçültmeyi ASCII'ye sınırlamak, eski
        // OrdinalIgnoreCase karşılaştırmasıyla farklı katlanan harfleri (Kelvin K) dışarıda tutar.
        if (value.Any(c => c is < '!' or > '~')) return null;

        var lower = value.ToLowerInvariant();
        var match = OriginRegex().Match(lower);
        if (!match.Success) return null;

        // Köşeli parantezli IPv6 ve port aralığı kalıpla ifade edilemiyor.
        var host = match.Groups[1].Value;
        if (host.StartsWith('[') &&
            (!IPAddress.TryParse(host[1..^1], out var parsed) || parsed.AddressFamily != AddressFamily.InterNetworkV6))
            return null;

        var authorityEnd = match.Groups[1].Index + match.Groups[1].Length;
        if (authorityEnd < lower.Length &&
            int.Parse(lower[(authorityEnd + 1)..], CultureInfo.InvariantCulture) > 65535)
            return null;

        return lower;
    }

    /// <summary>
    /// Geçerliyse kanonik CIDR (host bitleri sıfır), değilse null. Kalıp .NET ayrıştırıcısından
    /// bilerek DAR: <c>IPAddress.TryParse</c> "1.2", "0x7f.1" ve "010.0.0.1" kabul eder, bunların
    /// ne anlama geldiğini yazan kişi bile bilmez. Reddetmek erişimi yalnızca daraltır.
    /// </summary>
    public static string? NormalizeIp(string? entry)
    {
        var value = entry?.Trim();
        if (string.IsNullOrEmpty(value)) return null;

        var slash = value.IndexOf('/');
        var addressText = slash < 0 ? value : value[..slash];
        if (!Ipv4Regex().IsMatch(addressText) && !Ipv6CharsRegex().IsMatch(addressText)) return null;
        if (!IPAddress.TryParse(addressText, out var address)) return null;

        if (address.IsIPv4MappedToIPv6)
        {
            // Öneki olan eşlenmiş adresin eski davranışı tutarsızdı (v4'e iniyor, önek v6 sayılıyordu).
            if (slash >= 0) return null;
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        var maxBits = bytes.Length * 8;
        var prefix = maxBits;
        if (slash >= 0)
        {
            var prefixText = value[(slash + 1)..];
            if (!PrefixRegex().IsMatch(prefixText)) return null;
            prefix = int.Parse(prefixText, CultureInfo.InvariantCulture);
            if (prefix > maxBits) return null;
        }

        for (var i = 0; i < bytes.Length; i++)
        {
            var keep = Math.Clamp(prefix - i * 8, 0, 8);
            bytes[i] &= (byte)(0xFF << (8 - keep));
        }

        return new IPAddress(bytes) + "/" + prefix.ToString(CultureInfo.InvariantCulture);
    }

    public static AllowlistParseResult ParseOrigins(string? csv) => Parse(csv, NormalizeOrigin);

    public static AllowlistParseResult ParseIps(string? csv) => Parse(csv, NormalizeIp);

    private static AllowlistParseResult Parse(string? csv, Func<string, string?> normalize)
    {
        var entries = new List<string>();
        var invalid = new List<string>();
        foreach (var item in SplitCsv(csv))
        {
            var canonical = normalize(item);
            if (canonical is null) invalid.Add(item);
            else if (!entries.Contains(canonical, StringComparer.Ordinal)) entries.Add(canonical);
        }

        return new AllowlistParseResult(entries, invalid);
    }

    /// <summary>Eski <c>Split(',', RemoveEmpty | Trim)</c> ile birebir aynı.</summary>
    public static string[] SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Girdi sırasını korur, yalnızca bayt bayt aynı adları atar.</summary>
    public static IReadOnlyList<string> ParseMaskedColumns(string? csv)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return SplitCsv(csv).Where(seen.Add).ToList();
    }

    public static void ApplyOrigins(GatewayApiKey key, IReadOnlyList<string> origins)
    {
        key.RestrictOrigins = origins.Count > 0;
        key.OriginRules = origins
            .Select((o, i) => new GatewayApiKeyAllowedOrigin { ApiKeyId = key.Id, Origin = o, Position = i })
            .ToList();
    }

    public static void ApplyIps(GatewayApiKey key, IReadOnlyList<string> cidrs)
    {
        key.RestrictIps = cidrs.Count > 0;
        key.IpRules = cidrs
            .Select((c, i) => new GatewayApiKeyAllowedIp { ApiKeyId = key.Id, Cidr = c, Position = i })
            .ToList();
    }

    /// <summary>API'nin tarihsel metin alanı: kısıt yoksa null, geçerli girdi yoksa açık bir ifade.</summary>
    public static string? DisplayOrigins(bool restricted, IEnumerable<string> ordered) =>
        Display(restricted, ordered);

    public static string? DisplayIps(bool restricted, IEnumerable<string> orderedCidrs) =>
        Display(restricted, orderedCidrs.Select(DisplayCidr));

    public static string? DisplayMaskedColumns(IEnumerable<string> ordered)
    {
        var list = ordered.ToList();
        return list.Count == 0 ? null : string.Join(",", list);
    }

    /// <summary>Tek adres eskisi gibi öneksiz görünür: kullanıcı "1.2.3.4" yazdı.</summary>
    public static string DisplayCidr(string cidr)
    {
        if (cidr.EndsWith("/128", StringComparison.Ordinal)) return cidr[..^4];
        if (cidr.EndsWith("/32", StringComparison.Ordinal) && !cidr.Contains(':')) return cidr[..^3];
        return cidr;
    }

    private static string? Display(bool restricted, IEnumerable<string> ordered)
    {
        var list = ordered.ToList();
        if (list.Count > 0) return string.Join(",", list);
        return restricted ? DenyAllDisplay : null;
    }
}
