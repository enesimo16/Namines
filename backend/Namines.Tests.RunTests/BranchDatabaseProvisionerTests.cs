using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;
using Npgsql;

namespace Namines.Tests.RunTests;

/// <summary>
/// Branch veritabanı sağlama — GERÇEK Docker'a karşı (Faz B / 06 §4).
///
/// Bu özelliğin tek anlamlı testi budur: vaat "branch'in gerçek, BAĞLANILABİLİR bir
/// veritabanı var". Container'ın oluştuğunu doğrulamak yetmez — asıl soru host'tan
/// gerçekten bağlanılıp bağlanılamadığı ve şemanın gerçekten uygulanıp
/// uygulanmadığı. İkisi de yalnızca gerçek bir bağlantıyla kanıtlanır.
///
/// Testler yalnızca PostgreSQL'i kullanıyor: MSSQL/MySQL aynı kod yolundan geçiyor
/// ve CLAUDE.md'nin uyarısı gereği bu makinede ağır container'ları çoğaltmak
/// Docker'ı boğabiliyor.
///
/// NEDEN BU PROJEDE: Namines.Tests, Testcontainers'a bağımlı ve o paket ailesi kendi
/// Docker.DotNet forkunu AYNI dosya adıyla getiriyor. Bu testler ilk olarak orada
/// yazıldı ve TypeLoadException ile düştü — csproj yorumundaki çakışmanın tam olarak
/// kendisi. Ham Docker.DotNet kullanan her test buraya ait.
/// </summary>
public class BranchDatabaseProvisionerTests
{
    private static BranchDatabaseProvisioner Provisioner() =>
        new(new DdlGeneratorFactory(), NullLogger<BranchDatabaseProvisioner>.Instance);

    private static DatabaseSchema UsersSchema() => new()
    {
        Name = "branchdb_test",
        Tables =
        {
            new SchemaTable
            {
                Id = "t1", Name = "users",
                Columns =
                {
                    new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "c2", Name = "email", Type = "VARCHAR", Length = 255 },
                },
            },
        },
    };

    [RequiresDockerFact]
    public async Task Provisioned_database_is_reachable_and_carries_the_schema()
    {
        var branchId = "test-" + Guid.NewGuid().ToString("N")[..8];
        var provisioner = Provisioner();

        try
        {
            var db = await provisioner.ProvisionAsync(branchId, UsersSchema(), DatabaseType.PostgreSQL);

            // Loopback'e bağlanmak sözleşmenin bir parçası — dışarı açılmamalı.
            Assert.Equal("127.0.0.1", db.Host);
            Assert.True(db.Port > 0);

            await using var conn = new NpgsqlConnection(db.ConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            // Şema uygulanmadıysa bu sorgu "relation does not exist" ile patlar —
            // yani asıl vaadi (yaşayan, şemalı veritabanı) doğrudan sınıyor.
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(0, count);

            // Gerçekten yazılabilir olmalı: "kullanıcı sorgu çalıştırabilir" vaadi bu.
            cmd.CommandText = "INSERT INTO users (email) VALUES ('a@b.c')";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "SELECT COUNT(*) FROM users";
            Assert.Equal(1, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
        }
        finally
        {
            await provisioner.DestroyAsync(branchId);
        }
    }

    [RequiresDockerFact]
    public async Task Provisioning_twice_reuses_the_same_database()
    {
        // Aksi hâlde her sayfa yenilemesi host'ta bir veritabanı daha bırakırdı.
        var branchId = "test-" + Guid.NewGuid().ToString("N")[..8];
        var provisioner = Provisioner();

        try
        {
            var first = await provisioner.ProvisionAsync(branchId, UsersSchema(), DatabaseType.PostgreSQL);
            var second = await provisioner.ProvisionAsync(branchId, UsersSchema(), DatabaseType.PostgreSQL);

            Assert.Equal(first.Port, second.Port);
            Assert.Equal(first.Password, second.Password);
        }
        finally
        {
            await provisioner.DestroyAsync(branchId);
        }
    }

    [RequiresDockerFact]
    public async Task State_survives_a_new_provisioner_instance()
    {
        // Durum bellekte tutulsaydı sunucu yeniden başladığında container'lar
        // sahipsiz kalırdı — bulunamaz ama çalışmaya devam eder, yani sızıntı.
        var branchId = "test-" + Guid.NewGuid().ToString("N")[..8];
        var first = Provisioner();

        try
        {
            var created = await first.ProvisionAsync(branchId, UsersSchema(), DatabaseType.PostgreSQL);

            var fresh = Provisioner();
            var found = await fresh.GetAsync(branchId);

            Assert.NotNull(found);
            Assert.Equal(created.Port, found!.Port);
            Assert.Equal(created.Password, found.Password);

            // Yeniden bulunan bilgiyle GERÇEKTEN bağlanılabilmeli; eşleşen alanlar
            // yetmez, parola yanlış okunmuşsa bağlantı burada patlar.
            await using var conn = new NpgsqlConnection(found.ConnectionString);
            await conn.OpenAsync();
        }
        finally
        {
            await first.DestroyAsync(branchId);
        }
    }

    [RequiresDockerFact]
    public async Task Destroy_removes_the_database_and_is_safe_to_repeat()
    {
        var branchId = "test-" + Guid.NewGuid().ToString("N")[..8];
        var provisioner = Provisioner();

        await provisioner.ProvisionAsync(branchId, UsersSchema(), DatabaseType.PostgreSQL);
        await provisioner.DestroyAsync(branchId);

        Assert.Null(await provisioner.GetAsync(branchId));

        // "Zaten yok" bir hata değil — çağıran için sonuç aynı.
        await provisioner.DestroyAsync(branchId);
    }

    [RequiresDockerFact]
    public async Task Unsupported_engine_is_rejected_before_anything_is_created()
    {
        var provisioner = Provisioner();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            provisioner.ProvisionAsync("test-oracle", UsersSchema(), DatabaseType.Oracle));

        Assert.Contains("Oracle", ex.Message);
    }

    [Fact]
    public void Generated_passwords_satisfy_sql_server_complexity()
    {
        // Karmaşıklık kuralı karşılanmazsa SQL Server container'ı sessizce başlamaz
        // ve hata "veritabanı hazır olmadı" gibi görünür — yanlış yere baktırır.
        for (var i = 0; i < 50; i++)
        {
            var password = BranchDatabaseProvisioner.GeneratePassword();

            Assert.True(password.Length >= 12);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public void Generated_passwords_are_not_repeated()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++)
            Assert.True(seen.Add(BranchDatabaseProvisioner.GeneratePassword()));
    }

    [Theory]
    [InlineData("branch/with slash", "branch-with-slash")]
    [InlineData("ok_name-1.2", "ok_name-1.2")]
    public void Container_names_are_sanitised(string input, string expected)
    {
        // Docker container adları sınırlı bir karakter kümesi kabul eder; branch
        // adı doğrudan geçseydi "feature/x" gibi tamamen normal bir ad oluşturmayı
        // bozardı.
        Assert.Equal(expected, BranchDatabaseProvisioner.Sanitize(input));
    }
}
