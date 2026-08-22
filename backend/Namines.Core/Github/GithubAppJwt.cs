using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Namines.Core.Github;

/// <summary>
/// GitHub App'in kendini tanıtmak için imzaladığı JWT (11 §7).
///
/// <b>Neden elle üretiliyor:</b> GitHub'ın istediği şey RS256 ile imzalanmış,
/// üç alanlı (<c>iat</c>, <c>exp</c>, <c>iss</c>) minik bir token. Bunun için
/// bir JWT kütüphanesi eklemek, projeye tek bir imza için yeni bir bağımlılık ve
/// yeni bir güvenlik yüzeyi katardı; .NET'in kendi <see cref="RSA"/>'sı yeterli.
///
/// Token yalnızca App'i tanıtır — depoya erişim için bununla bir <b>kurulum
/// (installation) token'ı</b> alınır. Ayrım önemli: App token'ı hiçbir müşterinin
/// verisine erişemez, kurulum token'ı yalnızca o kurulumun depolarına erişir ve
/// bir saat sonra ölür.
/// </summary>
public static class GithubAppJwt
{
    /// <summary>
    /// <paramref name="lifetime"/> GitHub tarafından <b>10 dakikayla sınırlı</b>;
    /// daha uzunu reddedilir. Varsayılan 9 dakika — saat kaymasına pay bırakıyor.
    /// </summary>
    public static string Create(string appId, string pemPrivateKey, TimeSpan? lifetime = null, DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pemPrivateKey);

        var issued = now ?? DateTimeOffset.UtcNow;
        var span = lifetime ?? TimeSpan.FromMinutes(9);

        if (span > TimeSpan.FromMinutes(10))
            throw new ArgumentException("GitHub rejects an app JWT that lives longer than 10 minutes.", nameof(lifetime));

        // 'iat' 60 saniye GERİYE alınıyor: sunucu saati GitHub'ınkinden birkaç
        // saniye ileriyse token "gelecekte üretilmiş" sayılır ve reddedilir. Bu,
        // yalnızca bazı makinelerde görülen ve sebebi anlaşılmayan bir arıza olurdu.
        var header = new { alg = "RS256", typ = "JWT" };
        var payload = new
        {
            iat = issued.AddSeconds(-60).ToUnixTimeSeconds(),
            exp = issued.Add(span).ToUnixTimeSeconds(),
            iss = appId,
        };

        var unsigned = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header)) + "." +
                       Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pemPrivateKey);

        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return unsigned + "." + Base64Url(signature);
    }

    /// <summary>
    /// JWT base64url kullanır: dolgu yok, <c>+</c> ve <c>/</c> yerine <c>-</c> ve
    /// <c>_</c>. Düz base64 göndermek token'ı geçersiz kılar.
    /// </summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
