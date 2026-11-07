using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Namines.Core.Enums;
using Namines.Core.Models.Auth;

namespace Namines.Identity.Application;

public enum RegistrationStatus { Created, ReservedEmail, EmailTaken, Failed }

public sealed record RegistrationResult(RegistrationStatus Status, ApplicationUser? User, string? Errors);

public enum PasswordSignInStatus { Succeeded, UnknownUser, LockedOut, BadPassword, TwoFactorRequired, BadTwoFactorCode }

public sealed record PasswordSignInResult(PasswordSignInStatus Status, ApplicationUser? User);

public enum ReauthStatus { Verified, LockedOut, BadPassword, BadTwoFactorCode, NoPassword }

public enum ExternalSignInStatus { Existing, Created, CreateFailed, LockedOut, TwoFactorRequired, SecureFailed, LinkFailed }

public sealed record ExternalSignInResult(ExternalSignInStatus Status, ApplicationUser? User, string? Errors);

public sealed record AccountUpdateResult(bool Succeeded, IReadOnlyList<string> Errors);

/// <summary>
/// Hesap ve oturum güvenliği: kayıt, parola/kilit/MFA doğrulaması, MFA kurulumu, profil, oturum iptali, OAuth kullanıcı çözümü.
/// UserManager yalnızca Identity modülünün içindedir; denetleyiciler bunu bilmez, yalnızca sonucu HTTP'ye eşler.
/// </summary>
public interface IIdentityAccounts
{
    Task<ApplicationUser?> FindByIdAsync(string userId);

    Task<RegistrationResult> RegisterAsync(string? username, string email, string password, string? companyName);

    /// <summary>E-posta + parola (+ MFA) kapıları; başarılıysa sayaç sıfırlanır ve yapılandırılmış geliştirici hesabı yükseltilir.</summary>
    Task<PasswordSignInResult> PasswordSignInAsync(string email, string password, string? twoFactorCode);

    /// <summary>Hesap silme gibi hassas işlemlerde yeniden kimlik doğrulama (kilit, parola, MFA).</summary>
    Task<ReauthStatus> ReauthenticateAsync(ApplicationUser user, string? password, string? twoFactorCode);

    Task<AccountUpdateResult> UpdateProfileAsync(ApplicationUser user, string? companyName, string profileJson);

    /// <summary>Güvenlik damgasını döndürür: tüm açık oturumlar geçersizleşir.</summary>
    Task<bool> RevokeAllSessionsAsync(ApplicationUser user);

    Task<string?> BeginMfaSetupAsync(ApplicationUser user);

    /// <summary>Kod geçerliyse MFA açılır ve kurtarma kodları döner; değilse null.</summary>
    Task<IReadOnlyList<string>?> EnableMfaAsync(ApplicationUser user, string? code);

    /// <summary>Kod geçersizse sayaç artar ve false döner.</summary>
    Task<bool> DisableMfaAsync(ApplicationUser user, string? code);

    Task<int> CountRecoveryCodesAsync(ApplicationUser user);

    Task<ExternalSignInResult> ResolveExternalUserAsync(string email, string? name, string? avatarUrl);
}

/// <summary>Yönetim paneli için yetki çözümü ve hedef kullanıcı okuması. Okuma yolu hiçbir şey yazmaz.</summary>
public interface IAdminUserService
{
    /// <summary>Çağıran platform yöneticisiyse kullanıcıyı, değilse null döner. Yan etkisi yoktur.</summary>
    Task<ApplicationUser?> ResolveAdminAsync(string? userId, string? email);

    Task<ApplicationUser?> FindUserAsync(string userId);
}

public sealed class IdentityAccountService : IIdentityAccounts, IAdminUserService
{
    private const int RecoveryCodeCount = 8;

    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration _configuration;

    public IdentityAccountService(UserManager<ApplicationUser> users, IConfiguration configuration)
    {
        _users = users;
        _configuration = configuration;
    }

    public static string NormalizeCode(string? code) =>
        code?.Replace(" ", string.Empty).Replace("-", string.Empty).Trim() ?? string.Empty;

    private bool IsConfiguredDevEmail(string? email)
    {
        var dev = _configuration["Dev:Email"];
        return !string.IsNullOrWhiteSpace(dev) && string.Equals(email, dev, StringComparison.OrdinalIgnoreCase);
    }

    public Task<ApplicationUser?> FindByIdAsync(string userId) => _users.FindByIdAsync(userId);

    public async Task<RegistrationResult> RegisterAsync(string? username, string email, string password, string? companyName)
    {
        var reserved = !string.IsNullOrWhiteSpace(email) && (
            string.Equals(email, "namines@namines.com", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(email, "admin@namines.com", StringComparison.OrdinalIgnoreCase) ||
            IsConfiguredDevEmail(email));
        if (reserved) return new RegistrationResult(RegistrationStatus.ReservedEmail, null, null);

        if (await _users.FindByEmailAsync(email) != null)
            return new RegistrationResult(RegistrationStatus.EmailTaken, null, null);

        // Herkes Free (Individual) başlar; Pro yalnızca Stripe webhook'u ile verilir, kayıtta kendi kendine atanamaz.
        var user = new ApplicationUser
        {
            UserName = username ?? email,
            Email = email,
            Type = UserType.Individual,
            CompanyName = companyName,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _users.CreateAsync(user, password);
        return result.Succeeded
            ? new RegistrationResult(RegistrationStatus.Created, user, null)
            : new RegistrationResult(RegistrationStatus.Failed, null, string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    public async Task<PasswordSignInResult> PasswordSignInAsync(string email, string password, string? twoFactorCode)
    {
        var user = await _users.FindByEmailAsync(email);
        // Kullanıcı yoksa da çağıran aynı mesajı verir: farklı cevap e-posta numaralandırmasına yarardı.
        if (user is null) return new(PasswordSignInStatus.UnknownUser, null);
        if (await _users.IsLockedOutAsync(user)) return new(PasswordSignInStatus.LockedOut, user);

        if (!await _users.CheckPasswordAsync(user, password))
        {
            // CheckPasswordAsync başarısızlığı saymaz; kilit buna dayandığı için kaydediyoruz.
            await _users.AccessFailedAsync(user);
            return new(PasswordSignInStatus.BadPassword, user);
        }

        // MFA kapısı parola doğrulandıktan SONRA: aksi halde parolayı bilmeyen de hesapta MFA olup olmadığını öğrenirdi.
        if (user.TwoFactorEnabled)
        {
            // Sayaç sıfırlanmaz: parola doğru ama giriş tamamlanmadı.
            if (string.IsNullOrWhiteSpace(twoFactorCode)) return new(PasswordSignInStatus.TwoFactorRequired, user);
            if (!await VerifyTwoFactorAsync(user, twoFactorCode))
            {
                // Yanlış kod da kilit sayacına yazılır; yoksa altı hane sınırsız denenebilirdi.
                await _users.AccessFailedAsync(user);
                return new(PasswordSignInStatus.BadTwoFactorCode, user);
            }
        }

        await _users.ResetAccessFailedCountAsync(user);

        // Yapılandırılmış geliştirici hesabının yükseltilmesi YALNIZCA burada, parola doğrulanmış girişte yapılır.
        // Daha önce GET isteği olan yönetim kontrolü de yazıyordu; okuma yolunun yazması ve hatayı yutması kaldırıldı.
        if (!user.IsDev && IsConfiguredDevEmail(user.Email))
        {
            user.IsDev = true;
            user.EmailConfirmed = true;
            user.Type = UserType.Corporate;
            await _users.UpdateAsync(user);
        }

        return new(PasswordSignInStatus.Succeeded, user);
    }

    private async Task<bool> VerifyTwoFactorAsync(ApplicationUser user, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var totp = NormalizeCode(code);
        if (totp.Length > 0 && await _users.VerifyTwoFactorTokenAsync(user, _users.Options.Tokens.AuthenticatorTokenProvider, totp))
            return true;
        var recovery = await _users.RedeemTwoFactorRecoveryCodeAsync(user, code.Trim());
        return recovery.Succeeded;
    }

    public async Task<ReauthStatus> ReauthenticateAsync(ApplicationUser user, string? password, string? twoFactorCode)
    {
        if (await _users.IsLockedOutAsync(user)) return ReauthStatus.LockedOut;
        if (!await _users.HasPasswordAsync(user)) return ReauthStatus.NoPassword;

        if (string.IsNullOrEmpty(password) || !await _users.CheckPasswordAsync(user, password))
        {
            await _users.AccessFailedAsync(user);
            return ReauthStatus.BadPassword;
        }
        if (user.TwoFactorEnabled && !await VerifyTwoFactorAsync(user, twoFactorCode))
        {
            await _users.AccessFailedAsync(user);
            return ReauthStatus.BadTwoFactorCode;
        }
        return ReauthStatus.Verified;
    }

    public async Task<AccountUpdateResult> UpdateProfileAsync(ApplicationUser user, string? companyName, string profileJson)
    {
        if (!string.IsNullOrWhiteSpace(companyName)) user.CompanyName = companyName;
        user.ProfileJson = profileJson;
        var result = await _users.UpdateAsync(user);
        return new AccountUpdateResult(result.Succeeded, result.Errors.Select(e => e.Description).ToList());
    }

    public async Task<bool> RevokeAllSessionsAsync(ApplicationUser user) => (await _users.UpdateSecurityStampAsync(user)).Succeeded;

    public async Task<string?> BeginMfaSetupAsync(ApplicationUser user)
    {
        await _users.ResetAuthenticatorKeyAsync(user);
        var key = await _users.GetAuthenticatorKeyAsync(user);
        return string.IsNullOrEmpty(key) ? null : key;
    }

    public async Task<IReadOnlyList<string>?> EnableMfaAsync(ApplicationUser user, string? code)
    {
        var valid = await _users.VerifyTwoFactorTokenAsync(user, _users.Options.Tokens.AuthenticatorTokenProvider, NormalizeCode(code));
        if (!valid) return null;
        await _users.SetTwoFactorEnabledAsync(user, true);
        return (await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToList() ?? new List<string>();
    }

    public async Task<bool> DisableMfaAsync(ApplicationUser user, string? code)
    {
        if (!await VerifyTwoFactorAsync(user, code))
        {
            await _users.AccessFailedAsync(user);
            return false;
        }
        await _users.SetTwoFactorEnabledAsync(user, false);
        await _users.ResetAuthenticatorKeyAsync(user);
        return true;
    }

    public Task<int> CountRecoveryCodesAsync(ApplicationUser user) => _users.CountRecoveryCodesAsync(user);

    public async Task<ExternalSignInResult> ResolveExternalUserAsync(string email, string? name, string? avatarUrl)
    {
        var user = await _users.FindByEmailAsync(email);
        if (user == null)
        {
            var baseUsername = !string.IsNullOrWhiteSpace(name)
                ? System.Text.RegularExpressions.Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9\-]", "")
                : email.Split('@')[0];
            if (string.IsNullOrWhiteSpace(baseUsername)) baseUsername = "user";
            var unique = baseUsername;
            var suffix = 1;
            while (await _users.FindByNameAsync(unique) != null) unique = $"{baseUsername}{suffix++}";

            user = new ApplicationUser
            {
                UserName = unique,
                Email = email,
                EmailConfirmed = true,
                Type = UserType.Individual,
                CreatedAt = DateTime.UtcNow,
                ProfileJson = !string.IsNullOrEmpty(avatarUrl) ? System.Text.Json.JsonSerializer.Serialize(new { avatarUrl }) : null
            };
            var created = await _users.CreateAsync(user);
            return created.Succeeded
                ? new(ExternalSignInStatus.Created, user, null)
                : new(ExternalSignInStatus.CreateFailed, null, string.Join(", ", created.Errors.Select(e => e.Description)));
        }

        // Mevcut hesaba e-posta eşleşmesiyle bağlanılıyor: parola girişindeki kapılar (kilit, MFA) OAuth'tan atlanamaz.
        if (await _users.IsLockedOutAsync(user)) return new(ExternalSignInStatus.LockedOut, user, null);
        if (user.TwoFactorEnabled) return new(ExternalSignInStatus.TwoFactorRequired, user, null);

        // Doğrulanmamış hesap: saldırgan bu adresle önceden parola belirlemiş olabilir; sağlayıcı e-postayı doğruladığı
        // için hesap gerçek sahibine geçer, parola ve açık oturumlar ölür.
        if (!user.EmailConfirmed)
        {
            user.PasswordHash = null;
            if (!(await _users.UpdateSecurityStampAsync(user)).Succeeded) return new(ExternalSignInStatus.SecureFailed, user, null);
            user.EmailConfirmed = true;
            if (!(await _users.UpdateAsync(user)).Succeeded) return new(ExternalSignInStatus.LinkFailed, user, null);
        }
        return new(ExternalSignInStatus.Existing, user, null);
    }

    public async Task<ApplicationUser?> ResolveAdminAsync(string? userId, string? email)
    {
        if (string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(email)) return null;

        ApplicationUser? user = null;
        if (!string.IsNullOrEmpty(userId)) user = await _users.FindByIdAsync(userId);
        if (user == null && !string.IsNullOrEmpty(email)) user = await _users.FindByEmailAsync(email);
        if (user == null) return null;

        // Yetki kuralı: IsDev, yapılandırılmış geliştirici e-postası ya da Admin rolü. Hiçbiri kalıcı bir değişiklik yapmaz;
        // yapılandırılmış hesabın IsDev bayrağı girişte (PasswordSignInAsync) yazılır, yetki ise zaten e-postadan gelir.
        if (user.IsDev || IsConfiguredDevEmail(user.Email)) return user;
        if (_users.SupportsUserRole && await _users.IsInRoleAsync(user, "Admin")) return user;
        return null;
    }

    public Task<ApplicationUser?> FindUserAsync(string userId) => _users.FindByIdAsync(userId);
}
