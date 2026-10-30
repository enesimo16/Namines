using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Security;
using Xunit;

namespace Namines.Tests.Security;

/// <summary>DB-35: TOTP anahtarı ve kurtarma kodları AspNetUserTokens'ta şifreli.</summary>
public sealed class EncryptedUserStoreTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:ConnectionEncryptionKey"] = new string('k', 40),
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddSingleton<IConnectionSecretProtector, AesGcmConnectionSecretProtector>();
        services.AddDbContext<AuthDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddUserStore<EncryptedUserStore>()
            .AddDefaultTokenProviders();
        _sp = services.BuildServiceProvider();
        using var scope = _sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<(IServiceScope scope, UserManager<ApplicationUser> um, ApplicationUser user)> NewUserAsync()
    {
        var scope = _sp.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "totp@example.com", Email = "totp@example.com" };
        var r = await um.CreateAsync(user);
        Assert.True(r.Succeeded, string.Join(",", r.Errors.Select(e => e.Description)));
        return (scope, um, user);
    }

    private static async Task<string?> RawAsync(IServiceScope scope, string userId, string name) =>
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().UserTokens
            .Where(t => t.UserId == userId && t.Name == name).Select(t => t.Value).SingleOrDefaultAsync();

    private static string Totp(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0; var acc = 0; var bytes = new List<byte>();
        foreach (var ch in base32.TrimEnd('=').ToUpperInvariant())
        {
            acc = (acc << 5) | alphabet.IndexOf(ch); bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)((acc >> bits) & 0xFF)); }
        }
        var step = BitConverter.GetBytes((long)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30));
        if (BitConverter.IsLittleEndian) Array.Reverse(step);
        var h = new HMACSHA1(bytes.ToArray()).ComputeHash(step);
        var o = h[^1] & 0xF;
        var code = ((h[o] & 0x7F) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        return (code % 1_000_000).ToString("D6");
    }

    [Fact]
    public async Task Authenticator_key_is_encrypted_at_rest_and_roundtrips_and_totp_verifies()
    {
        var (scope, um, user) = await NewUserAsync();
        using var _ = scope;
        await um.ResetAuthenticatorKeyAsync(user);

        var key = await um.GetAuthenticatorKeyAsync(user);
        Assert.False(string.IsNullOrEmpty(key));
        var raw = await RawAsync(scope, user.Id, "AuthenticatorKey");
        Assert.StartsWith(EncryptedUserStore.Prefix, raw);
        Assert.DoesNotContain(key!, raw);

        Assert.True(await um.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, Totp(key!)));
        Assert.False(await um.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, "000000"
            == Totp(key!) ? "111111" : "000000"));
    }

    [Fact]
    public async Task Recovery_codes_are_encrypted_and_redeemable_once()
    {
        var (scope, um, user) = await NewUserAsync();
        using var _ = scope;
        var codes = (await um.GenerateNewTwoFactorRecoveryCodesAsync(user, 5))!.ToList();

        var raw = await RawAsync(scope, user.Id, "RecoveryCodes");
        Assert.StartsWith(EncryptedUserStore.Prefix, raw);
        Assert.DoesNotContain(codes[0], raw);
        Assert.Equal(5, await um.CountRecoveryCodesAsync(user));

        Assert.True((await um.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded);
        Assert.False((await um.RedeemTwoFactorRecoveryCodeAsync(user, codes[0])).Succeeded);
        Assert.Equal(4, await um.CountRecoveryCodesAsync(user));
        Assert.StartsWith(EncryptedUserStore.Prefix, await RawAsync(scope, user.Id, "RecoveryCodes"));
    }

    [Fact]
    public async Task Legacy_plaintext_values_are_readable_and_reencrypted_on_next_write()
    {
        var (scope, um, user) = await NewUserAsync();
        using var _ = scope;
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        db.UserTokens.Add(new IdentityUserToken<string>
        {
            UserId = user.Id, LoginProvider = "[AspNetUserStore]", Name = "RecoveryCodes", Value = "aaaaa-bbbbb;ccccc-ddddd",
        });
        db.UserTokens.Add(new IdentityUserToken<string>
        {
            UserId = user.Id, LoginProvider = "[AspNetUserStore]", Name = "AuthenticatorKey", Value = "JBSWY3DPEHPK3PXP",
        });
        await db.SaveChangesAsync();

        Assert.Equal("JBSWY3DPEHPK3PXP", await um.GetAuthenticatorKeyAsync(user));
        Assert.Equal(2, await um.CountRecoveryCodesAsync(user));
        Assert.True((await um.RedeemTwoFactorRecoveryCodeAsync(user, "aaaaa-bbbbb")).Succeeded);

        var raw = await RawAsync(scope, user.Id, "RecoveryCodes");
        Assert.StartsWith(EncryptedUserStore.Prefix, raw);
        Assert.Equal(1, await um.CountRecoveryCodesAsync(user));
        // Anahtara dokunulmadı: hâlâ düz metin okunuyor.
        Assert.Equal("JBSWY3DPEHPK3PXP", await um.GetAuthenticatorKeyAsync(user));
    }
}
