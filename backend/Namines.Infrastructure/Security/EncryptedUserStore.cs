using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Security;

/// <summary>
/// Identity <c>UserStore</c>'u; TOTP gizli anahtarını ve kurtarma kodlarını
/// <c>AspNetUserTokens</c>'a ŞİFRELİ yazar (DB-35). Bu iki değer veritabanı
/// dökümü sızarsa doğrudan ikinci faktörü kıran sırlardır; düz metin tutmak
/// 2FA'nın anlamını azaltır. Şema değişmiyor: değer <c>nenc1:</c> ön ekiyle
/// <see cref="IConnectionSecretProtector"/> çıktısını taşır.
///
/// Geriye uyumluluk: ön eksiz değer ESKİ düz metin sayılır ve olduğu gibi
/// okunur; bir sonraki yazmada (kod kullanımı, anahtar sıfırlama) şifrelenir.
/// Kalan her şey (başka sağlayıcı jetonları) dokunulmadan geçer.
/// </summary>
public class EncryptedUserStore : UserStore<ApplicationUser, IdentityRole, AuthDbContext>
{
    public const string Prefix = "nenc1:";

    private const string InternalProvider = "[AspNetUserStore]";
    private static readonly HashSet<string> SensitiveNames =
        new(StringComparer.Ordinal) { "AuthenticatorKey", "RecoveryCodes" };

    private readonly IConnectionSecretProtector _protector;

    public EncryptedUserStore(
        AuthDbContext context,
        IConnectionSecretProtector protector,
        IdentityErrorDescriber? describer = null)
        : base(context, describer)
    {
        _protector = protector;
    }

    private static bool IsSensitive(string loginProvider, string name) =>
        loginProvider == InternalProvider && SensitiveNames.Contains(name);

    public override Task SetTokenAsync(ApplicationUser user, string loginProvider, string name, string? value,
        CancellationToken cancellationToken)
    {
        if (value is not null && IsSensitive(loginProvider, name) && !value.StartsWith(Prefix, StringComparison.Ordinal))
            value = Prefix + _protector.Protect(value);
        return base.SetTokenAsync(user, loginProvider, name, value, cancellationToken);
    }

    public override async Task<string?> GetTokenAsync(ApplicationUser user, string loginProvider, string name,
        CancellationToken cancellationToken)
    {
        var stored = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        if (stored is not null && IsSensitive(loginProvider, name) && stored.StartsWith(Prefix, StringComparison.Ordinal))
            return _protector.Unprotect(stored[Prefix.Length..]);
        return stored;
    }
}
