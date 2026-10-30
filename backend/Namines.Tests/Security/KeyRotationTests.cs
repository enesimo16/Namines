using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using Namines.Infrastructure.Security;
using Xunit;

namespace Namines.Tests.Security;

/// <summary>DB-36: çoklu anahtar + yeniden şifreleme.</summary>
public class KeyRotationTests
{
    private static readonly string K1 = new('a', 40);
    private static readonly string K2 = new('b', 40);

    private static AesGcmConnectionSecretProtector Make(string? legacy, params (string id, string key)[] keys)
    {
        var d = new Dictionary<string, string?>();
        if (legacy != null) d["Security:ConnectionEncryptionKey"] = legacy;
        for (var i = 0; i < keys.Length; i++)
        {
            d[$"Security:ConnectionEncryptionKeys:{i}:Id"] = keys[i].id;
            d[$"Security:ConnectionEncryptionKeys:{i}:Key"] = keys[i].key;
        }
        return new AesGcmConnectionSecretProtector(new ConfigurationBuilder().AddInMemoryCollection(d).Build());
    }

    [Fact]
    public void Legacy_only_config_still_writes_and_reads_v1()
    {
        var p = Make(K1);
        var c = p.Protect("secret");
        Assert.StartsWith("v1:", c);
        Assert.Equal("secret", p.Unprotect(c));
        Assert.True(p.IsEncryptedWithCurrentKey(c));
    }

    [Fact]
    public void Newest_key_encrypts_and_old_ciphertexts_remain_readable()
    {
        var oldV1 = Make(K1).Protect("old");
        var p = Make(K1, ("k1", K1), ("k2", K2));
        var fresh = p.Protect("new");
        Assert.StartsWith("k.k2:", fresh);
        Assert.Equal("new", p.Unprotect(fresh));
        Assert.Equal("old", p.Unprotect(oldV1));
        Assert.False(p.IsEncryptedWithCurrentKey(oldV1));
        Assert.True(p.IsEncryptedWithCurrentKey(fresh));
        // Eski anahtar listeden çıkarılırsa kimlik bulunamaz
        Assert.Throws<CryptographicException>(() => Make(null, ("k2", K2)).Unprotect(Make(null, ("k1", K1)).Protect("x")));
    }

    [Fact]
    public async Task Reencrypt_moves_all_rows_to_current_key_and_is_idempotent()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var opts = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(conn).Options;
        await using var db = new AuthDbContext(opts);
        await db.Database.EnsureCreatedAsync();
        await using (var pragma = conn.CreateCommand()) { pragma.CommandText = "PRAGMA foreign_keys=OFF;"; await pragma.ExecuteNonQueryAsync(); }

        var legacy = Make(K1);
        db.CloudProjects.Add(new CloudProject { Id = "p1", Name = "a", DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u", EncryptedConnectionString = legacy.Protect("conn-1") });
        db.CloudProjects.Add(new CloudProject { Id = "p2", Name = "b", DbType = "PostgreSQL", SchemaJson = "{}", NodePositionsJson = "{}", UserId = "u" });
        await db.SaveChangesAsync();

        var rotated = Make(K1, ("k2", K2));
        var r = await ConnectionSecretReencryptor.ReencryptAsync(db, rotated);
        Assert.Equal(new ConnectionSecretReencryptor.Result(1, 1, 0), r);

        var raw = await db.CloudProjects.AsNoTracking().Where(p => p.Id == "p1").Select(p => p.EncryptedConnectionString).SingleAsync();
        Assert.StartsWith("k.k2:", raw);
        Assert.Equal("conn-1", Make(null, ("k2", K2)).Unprotect(raw!)); // artık eski anahtar gerekmiyor

        var again = await ConnectionSecretReencryptor.ReencryptAsync(db, rotated);
        Assert.Equal(0, again.Reencrypted);
    }
}
