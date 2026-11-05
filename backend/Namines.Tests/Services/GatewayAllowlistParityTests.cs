using System.Net;
using System.Net.Sockets;
using Namines.Core.Models.Auth;
using Namines.Core.Models.Hub;
using Namines.Infrastructure.Data;

namespace Namines.Tests.Services;

/// <summary>
/// Eski virgüllü metin eşleşmesinin DONMUŞ kopyası. Kesme öncesi GatewayKeyRestrictions'ta ne
/// yazıyorsa bu; yeni davranış buna karşı sınanır ve kopya kod silindikten sonra da burada kalır.
/// </summary>
public static class LegacyGatewayMatching
{
    public static string[] Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsOriginAllowed(string? list, string? origin)
    {
        var allowed = Split(list);
        if (allowed.Length == 0) return true;
        if (string.IsNullOrWhiteSpace(origin)) return false;
        return allowed.Any(a => string.Equals(a, origin, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsIpAllowed(string? list, IPAddress? client, bool trustworthy)
    {
        var allowed = Split(list);
        if (allowed.Length == 0) return true;
        if (!trustworthy || client is null) return false;
        var address = client.IsIPv4MappedToIPv6 ? client.MapToIPv4() : client;
        return allowed.Any(rule => Matches(rule, address));
    }

    private static bool Matches(string rule, IPAddress address)
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

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (networkBytes[fullBytes] & mask) == (addressBytes[fullBytes] & mask);
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6
            ? address.MapToIPv4()
            : address;
}

/// <summary>Migration testinin de kullandığı dağınık eski değerler ve deneme girdileri.</summary>
public static class GatewayAllowlistCases
{
    public static readonly string?[] OriginLists =
    {
        null, "", "   ", ",", " , ,",
        "https://app.example.com", "HTTPS://App.Example.COM",
        "https://a.com, https://b.com", " https://a.com ,https://a.com,HTTPS://A.COM",
        "http://localhost:3000", "https://a.com:443", "capacitor://localhost", "https://[::1]:8443", "ftp://a.com",
        "\thttps://tab.com\n", "https://nbsp.com ",
        // Geçersizler
        "*", "https://*.example.com", "https://a.com/", "https://a.com/path", "null", "not-an-origin",
        "https://a.com:99999", "https://a.com:0", "https://a.com:080", "https://münchen.de", "https://user@a.com",
        "https://[not-v6]", "https://[1.2.3.4]",
        // Karışık: geçerli olan yaşar, geçersiz olan karantinaya gider
        "https://ok.com,*", "https://ok.com,not valid,https://ok2.com",
    };

    public static readonly string?[] OriginProbes =
    {
        null, "", " ", "*", "null", "https://a.com", "https://a.com/", "https://A.COM", "https://a.com:443", "https://b.com",
        "https://app.example.com", "HTTPS://APP.EXAMPLE.COM", "https://evil.example", "http://localhost:3000", "https://localhost:3000",
        "https://ok.com", "https://ok2.com", "not-an-origin", "not valid", "https://foo.example.com", "https://*.example.com",
        "capacitor://localhost", "https://[::1]:8443", "https://tab.com", "https://nbsp.com", "ftp://a.com",
    };

    public static readonly string?[] IpLists =
    {
        null, "", " ", ",", " , ",
        "1.2.3.4", "1.2.3.4/32", " 1.2.3.4 , 1.2.3.4/32 ,1.2.3.4", "10.0.0.0/8", "10.1.2.3/8", "192.168.1.0/24, 192.168.1.0/24 ,192.168.1.77/24",
        "172.16.0.0/20", "0.0.0.0/0", "::/0", "2001:db8::/32", "2001:DB8::1", "2001:0db8:0:0:0:0:0:1", "::1", "::ffff:1.2.3.4", "::ffff:102:304",
        "2001:db8::1/128", "2001:db8:1::5/48", "::1.2.3.4", "1:0:2:3:4:5:6:7",
        // Geçersizler
        "not-an-ip", "10.0.0.0/99", "10.0.0.0/x", "10.0.0.0/", "10.0.0.0/08", "10.0.0.0/+8", "1.2", "0x7f.0.0.1", "010.0.0.1",
        "[::1]", "fe80::1%3", "1.2.3.4:80", "::ffff:1.2.3.4/128", "::ffff:10.0.0.0/8", "256.1.1.1", "2001:db8::/129", "1:2:3:4:5:6:7:8:9",
        // PostgreSQL inet kabul eder, .NET etmez: eski kodda kimseyle eşleşmiyordu (denetim B-1)
        "::ffff:1.2.3", "::ffff:1.23", "::.2.3.4/0", "::ffff:62:.253.88.177/46", "::ffff:1.2.3.07",
        // Karışık
        "1.2.3.4,bad", "bad,2001:db8::1",
    };

    public static readonly string[] IpProbes =
    {
        "1.2.3.4", "1.2.3.5", "10.1.2.3", "11.0.0.1", "192.168.1.5", "192.168.2.1", "172.16.15.1", "172.16.16.1", "127.0.0.1", "8.8.8.8",
        "1.0.0.2", "8.0.0.1", "0.0.0.1", "2001:db8::1", "2001:db8:1::1", "2001:db8:1:ffff::1", "2001:db9::1", "::1", "::2", "::ffff:1.2.3.4",
        "::ffff:10.0.0.1", "fe80::1", "::102:304",
    };

    public static GatewayApiKey Key(string? origins, string? ips)
    {
        var key = new GatewayApiKey { Id = "k", ProjectId = "p", Name = "k", Prefix = "nmn_x", KeyHash = "h", CreatedByUserId = "u" };
        var parsedOrigins = GatewayAllowlistRules.ParseOrigins(origins);
        var parsedIps = GatewayAllowlistRules.ParseIps(ips);
        GatewayAllowlistRules.ApplyOrigins(key, parsedOrigins.Entries);
        GatewayAllowlistRules.ApplyIps(key, parsedIps.Entries);
        // Migration ile aynı kural: girdi vardıysa kısıt vardır, geçerli kalmasa bile.
        key.RestrictOrigins = parsedOrigins.HasAnyEntry;
        key.RestrictIps = parsedIps.HasAnyEntry;
        return key;
    }
}

/// <summary>Yeni davranış, eski metin eşleşmesiyle tablo üzerinde karşılaştırılır.</summary>
public class GatewayAllowlistParityTests
{
    [Fact]
    public void Origin_table_matches_legacy_when_valid_and_never_widens()
    {
        foreach (var list in GatewayAllowlistCases.OriginLists)
        {
            var parsed = GatewayAllowlistRules.ParseOrigins(list);
            var key = GatewayAllowlistCases.Key(list, null);
            foreach (var probe in GatewayAllowlistCases.OriginProbes)
            {
                var legacy = LegacyGatewayMatching.IsOriginAllowed(list, probe);
                var current = GatewayKeyRestrictions.IsOriginAllowed(key, probe);

                if (parsed.Invalid.Count == 0) Assert.True(legacy == current, $"'{list}' vs '{probe}': legacy={legacy}, current={current}");
                else if (current) Assert.True(legacy, $"'{list}' widened access for '{probe}'");
            }
        }
    }

    [Fact]
    public void Ip_table_matches_legacy_when_valid_and_never_widens()
    {
        foreach (var list in GatewayAllowlistCases.IpLists)
        {
            var parsed = GatewayAllowlistRules.ParseIps(list);
            var key = GatewayAllowlistCases.Key(null, list);
            foreach (var probe in GatewayAllowlistCases.IpProbes)
            {
                var address = IPAddress.Parse(probe);
                foreach (var trustworthy in new[] { true, false })
                {
                    var legacy = LegacyGatewayMatching.IsIpAllowed(list, address, trustworthy);
                    var current = GatewayKeyRestrictions.IsIpAllowed(key, address, trustworthy, out _);

                    if (parsed.Invalid.Count == 0) Assert.True(legacy == current, $"'{list}' vs {probe}: legacy={legacy}, current={current}");
                    else if (current) Assert.True(legacy, $"'{list}' widened access for {probe}");
                }
            }
        }
    }

    [Fact]
    public void A_scoped_client_matches_a_plain_rule_by_design()
    {
        // Bilinen tek fark (rapor bölüm 5): eski IPAddress.Equals kapsam kimliğini de karşılaştırıyordu, CIDR yolu
        // karşılaştırmıyordu. Artık her kural CIDR: aynı bağlantı-yerel adres, kapsam ne olursa olsun eşleşir.
        // Kural kapsam kimliği taşıyamaz (reddedilir), başka adrese izin verilmez.
        Assert.Null(GatewayAllowlistRules.NormalizeIp("fe80::1%3"));
        var key = GatewayAllowlistCases.Key(null, "fe80::1");
        Assert.True(GatewayKeyRestrictions.IsIpAllowed(key, IPAddress.Parse("fe80::1%3"), true, out _));
        Assert.False(GatewayKeyRestrictions.IsIpAllowed(key, IPAddress.Parse("fe80::2%3"), true, out _));
    }

    [Theory]
    [InlineData("::ffff:1.2.3")]
    [InlineData("::ffff:1.23")]
    [InlineData("::.2.3.4/0")]
    [InlineData("::ffff:62:.253.88.177/46")]
    [InlineData("::ffff:1.2.3.07")]
    public void Truncated_embedded_ipv4_is_invalid(string rule) =>
        Assert.Null(GatewayAllowlistRules.NormalizeIp(rule));

    [Theory]
    [InlineData("*")]
    [InlineData("https://a.com/")]
    [InlineData("not-an-origin")]
    [InlineData("null, *")]
    public void An_origin_list_with_only_invalid_entries_denies_everything(string list)
    {
        var key = GatewayAllowlistCases.Key(list, null);

        Assert.Empty(key.OriginRules);
        Assert.True(key.RestrictOrigins);
        foreach (var probe in GatewayAllowlistCases.OriginProbes)
            Assert.False(GatewayKeyRestrictions.IsOriginAllowed(key, probe), $"probe '{probe}'");
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.0/99")]
    [InlineData("1.2")]
    [InlineData("bad, worse")]
    public void An_ip_list_with_only_invalid_entries_denies_everything(string list)
    {
        var key = GatewayAllowlistCases.Key(null, list);

        Assert.Empty(key.IpRules);
        Assert.True(key.RestrictIps);
        foreach (var probe in GatewayAllowlistCases.IpProbes)
            Assert.False(GatewayKeyRestrictions.IsIpAllowed(key, IPAddress.Parse(probe), true, out _), probe);
    }

    [Fact]
    public void Rows_without_the_flag_are_still_enforced()
    {
        // Elle yazılmış satır, bayrak unutulmuş: kapalıya düşmeli, "serbest"e değil.
        var key = new GatewayApiKey { Id = "k", ProjectId = "p", Name = "k", Prefix = "p", KeyHash = "h" };
        key.OriginRules.Add(new GatewayApiKeyAllowedOrigin { Origin = "https://a.com" });
        key.IpRules.Add(new GatewayApiKeyAllowedIp { Cidr = "1.2.3.4/32" });

        Assert.False(GatewayKeyRestrictions.IsOriginAllowed(key, "https://evil.example"));
        Assert.True(GatewayKeyRestrictions.IsOriginAllowed(key, "https://a.com"));
        Assert.False(GatewayKeyRestrictions.IsIpAllowed(key, IPAddress.Parse("9.9.9.9"), true, out _));
    }

    [Fact]
    public void The_flag_without_loaded_rows_denies()
    {
        // Navigasyonu yüklemeyi unutan yeni bir kod yolu kapıyı açmamalı.
        var key = new GatewayApiKey { Id = "k", ProjectId = "p", Name = "k", Prefix = "p", KeyHash = "h", RestrictOrigins = true, RestrictIps = true };

        Assert.False(GatewayKeyRestrictions.IsOriginAllowed(key, "https://a.com"));
        Assert.False(GatewayKeyRestrictions.IsIpAllowed(key, IPAddress.Parse("1.2.3.4"), true, out _));
    }

    [Theory]
    [InlineData("HTTPS://App.Example.COM", "https://app.example.com")]
    [InlineData(" https://a.com:443 ", "https://a.com:443")]
    [InlineData("https://[::1]:8443", "https://[::1]:8443")]
    [InlineData("capacitor://localhost", "capacitor://localhost")]
    public void Origin_normalization_is_explicit(string input, string expected) =>
        Assert.Equal(expected, GatewayAllowlistRules.NormalizeOrigin(input));

    [Theory]
    [InlineData("1.2.3.4", "1.2.3.4/32")]
    [InlineData("10.1.2.3/8", "10.0.0.0/8")]
    [InlineData("172.16.15.1/20", "172.16.0.0/20")]
    [InlineData("::ffff:1.2.3.4", "1.2.3.4/32")]
    [InlineData("2001:DB8::1", "2001:db8::1/128")]
    [InlineData("2001:db8:1::5/48", "2001:db8:1::/48")]
    public void Ip_normalization_masks_host_bits(string input, string expected) =>
        Assert.Equal(expected, GatewayAllowlistRules.NormalizeIp(input));

    [Fact]
    public void Display_never_hides_a_restriction()
    {
        Assert.Null(GatewayAllowlistRules.DisplayOrigins(false, Array.Empty<string>()));
        Assert.Equal(GatewayAllowlistRules.DenyAllDisplay, GatewayAllowlistRules.DisplayOrigins(true, Array.Empty<string>()));
        Assert.Equal(GatewayAllowlistRules.DenyAllDisplay, GatewayAllowlistRules.DisplayIps(true, Array.Empty<string>()));
        Assert.Equal("1.2.3.4,10.0.0.0/8,2001:db8::1", GatewayAllowlistRules.DisplayIps(true, new[] { "1.2.3.4/32", "10.0.0.0/8", "2001:db8::1/128" }));
    }

    [Theory]
    [InlineData("email, phone", new[] { "email", "phone" })]
    [InlineData("email,email, email ", new[] { "email" })]
    [InlineData("Email,email", new[] { "Email", "email" })]
    [InlineData(" , ,", new string[0])]
    [InlineData("\tİsim\n,isim", new[] { "İsim", "isim" })]
    public void Masked_columns_only_collapse_exact_duplicates(string csv, string[] expected) =>
        Assert.Equal(expected, GatewayAllowlistRules.ParseMaskedColumns(csv));

    [Theory]
    [InlineData("email, phone")]
    [InlineData("Email,email,EMAIL")]
    [InlineData("  a ,, b,a, c ")]
    [InlineData("İsim,isim,ISIM")]
    public void Masking_lookup_equals_the_legacy_lookup(string csv)
    {
        // Maskeleme kararı bu kümeyle veriliyor: yeni liste aynı adları örtmek zorunda.
        var legacy = new HashSet<string>(LegacyGatewayMatching.Split(csv), StringComparer.OrdinalIgnoreCase);
        var current = new HashSet<string>(GatewayAllowlistRules.ParseMaskedColumns(csv), StringComparer.OrdinalIgnoreCase);
        Assert.True(legacy.SetEquals(current));
    }

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData(" , ", new string[0])]
    [InlineData("bug", new[] { "bug" })]
    [InlineData(" Bug, bug ,BUG, perf", new[] { "Bug", "perf" })]
    [InlineData("İstanbul,istanbul", new[] { "İstanbul", "istanbul" })]
    public void Issue_labels_collapse_case_duplicates_and_keep_first_spelling(string? csv, string[] expected) =>
        Assert.Equal(expected, IssueLabelRules.Parse(csv).Select(l => l.Display));
}
