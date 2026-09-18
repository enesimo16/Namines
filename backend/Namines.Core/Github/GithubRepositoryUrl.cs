using System;

namespace Namines.Core.Github;

/// <summary>
/// Kullanıcının yapıştırdığı adresten depo kimliğini çıkarır.
///
/// <b>Host beyaz listesi bir güvenlik sınırı</b>, kolaylık değil: bu adres
/// sunucunun dışarı çıkacağı yeri belirliyor. <c>Contains("github.com")</c>
/// gibi bir kontrol <c>github.com.evil.example</c>'ı kabul ederdi, o yüzden
/// host TAM eşleşmeyle karşılaştırılıyor (aynı çizgi: DbIntrospect'in SSRF
/// modeli, AGENTS.md).
/// </summary>
public static class GithubRepositoryUrl
{
    public static bool TryParse(string? url, out GithubRepository? repository)
    {
        repository = null;
        if (string.IsNullOrWhiteSpace(url)) return false;

        var text = url.Trim();

        // Şemasız yapıştırmak yaygın ("github.com/acme/shop"); kullanıcıyı
        // "https://" yazmaya zorlamanın bir karşılığı yok.
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;

        var host = uri.Host.ToLowerInvariant();
        if (host != "github.com" && host != "www.github.com") return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return false;

        var owner = segments[0];
        var name = segments[1];

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        if (owner.Length == 0 || name.Length == 0) return false;

        repository = new GithubRepository(owner, name);
        return true;
    }
}
