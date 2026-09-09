using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Namines.Core.Enums;
using Namines.Infrastructure.Services;
using Testcontainers.MySql;
using Xunit;

namespace Namines.Tests.Integration;

/// <summary>
/// <see cref="DbIntrospectionService"/>'in MySQL katalog sorgularını GERÇEK bir
/// MySQL'e karşı doğrular.
///
/// <b>Neden yazıldı:</b> bu SQL yazıldığından beri hiçbir zaman gerçek bir
/// MySQL'e karşı çalıştırılmamıştı — yalnızca PostgreSQL yolu canlı
/// denenmişti. INFORMATION_SCHEMA motorlar arasında adı aynı ama davranışı
/// aynı DEĞİL (MySQL'de CHECK_CONSTRAINTS 8.0.16'da geldi, GROUP_CONCAT'ın
/// varsayılan uzunluk sınırı var, UNIQUE bir INDEX olarak da görünür).
/// "Derleniyor" ile "doğru veriyi okuyor" arasındaki farkı ancak bu kapatır.
/// </summary>
[Collection("Docker")]
public class MySqlIntrospectionTests : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.0")
        .WithDatabase("naminesdb")
        .Build();

    public Task InitializeAsync() =>
        EngineAvailable.For(DatabaseType.MySQL) ? _container.StartAsync() : Task.CompletedTask;

    public Task DisposeAsync() =>
        EngineAvailable.For(DatabaseType.MySQL) ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

    private const string Schema = """
        CREATE TABLE musteri (
            id     INT AUTO_INCREMENT PRIMARY KEY,
            eposta VARCHAR(200) NOT NULL UNIQUE,
            ad     VARCHAR(100) NOT NULL,
            bakiye DECIMAL(12,2) NOT NULL DEFAULT 0.00,
            aktif  TINYINT(1) NOT NULL DEFAULT 1,
            CONSTRAINT ck_bakiye CHECK (bakiye >= 0)
        );

        CREATE TABLE siparis (
            id         INT AUTO_INCREMENT PRIMARY KEY,
            musteri_id INT NOT NULL,
            tutar      DECIMAL(10,2) NOT NULL,
            CONSTRAINT fk_siparis_musteri FOREIGN KEY (musteri_id) REFERENCES musteri(id) ON DELETE CASCADE
        );

        CREATE INDEX ix_siparis_musteri ON siparis (musteri_id);
        """;

    private async Task<Core.Models.DatabaseSchema> IntrospectAsync()
    {
        await using var connection = new MySqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        foreach (var statement in Schema.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            await using var command = new MySqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }

        var service = new DbIntrospectionService(
            NullLogger<DbIntrospectionService>.Instance, new AlwaysAllowHostPolicy());
        return await service.IntrospectAsync(_container.GetConnectionString(), "MySQL", CancellationToken.None);
    }

    [RequiresEngineFact(DatabaseType.MySQL)]
    public async Task Reads_tables_columns_types_and_identity()
    {
        var schema = await IntrospectAsync();

        Assert.Equal(2, schema.Tables.Count);

        var musteri = schema.Tables.Single(t => t.Name == "musteri");

        // AUTO_INCREMENT, Identity olarak GÖRÜLMELİ: görülmezse Desk onu
        // ekleme formunda zorunlu bir alan sanır ve kullanıcıdan birincil
        // anahtarı elle ister.
        var id = musteri.Columns.Single(c => c.Name == "id");
        Assert.True(id.IsPK);
        Assert.True(id.Identity);

        // Uzunluk ve ölçek okunmalı: DDL'i yeniden üretirken VARCHAR(200)
        // yerine VARCHAR(255) yazmak sessiz bir veri kaybı riski olurdu.
        var eposta = musteri.Columns.Single(c => c.Name == "eposta");
        Assert.Equal(200, eposta.Length);
        Assert.False(eposta.IsNullable);

        var bakiye = musteri.Columns.Single(c => c.Name == "bakiye");
        Assert.Equal(12, bakiye.Length);
        Assert.Equal(2, bakiye.Scale);
        // Varsayılan değeri olan bir kolon, formda ZORUNLU gösterilmemeli.
        Assert.False(string.IsNullOrWhiteSpace(bakiye.DefaultValue));
    }

    [RequiresEngineFact(DatabaseType.MySQL)]
    public async Task Reads_unique_and_check_constraints()
    {
        var schema = await IntrospectAsync();
        var musteri = schema.Tables.Single(t => t.Name == "musteri");

        // Kısıtlar kolon KİMLİĞİ taşıyor, adı değil: eşleştirmeyi kimlik
        // üzerinden yapmak, kolon adı değiştiğinde kısıtın kopmamasını sağlıyor.
        var eposta = musteri.Columns.Single(c => c.Name == "eposta");

        // UNIQUE: MySQL'de hem KEY_COLUMN_USAGE'da hem STATISTICS'te görünür;
        // ikisini birden okumak onu ÇİFT saymaya yol açabilirdi.
        Assert.Contains(musteri.Uniques, u => u.ColumnIds.Contains(eposta.Id));

        // CHECK: MySQL'de CHECK_CONSTRAINTS görünümü 8.0.16'da geldi; sorgu
        // onu bulamadığında sessizce boş dönmeli, patlamamalı.
        Assert.Contains(musteri.Checks, c => c.Name == "ck_bakiye");
    }

    [RequiresEngineFact(DatabaseType.MySQL)]
    public async Task Reads_foreign_keys_and_indexes()
    {
        var schema = await IntrospectAsync();
        var siparis = schema.Tables.Single(t => t.Name == "siparis");
        var musteri = schema.Tables.Single(t => t.Name == "musteri");

        var fk = siparis.Columns.Single(c => c.Name == "musteri_id");
        Assert.True(fk.IsFK);

        // İlişkiler tablo değil ŞEMA seviyesinde tutuluyor: bir yabancı anahtar
        // iki tabloya ait, birine yazmak diğerinden görünmez kılardı.
        Assert.Contains(schema.Relations, r =>
            r.SourceTableId == siparis.Id && r.SourceColumnId == fk.Id &&
            r.TargetTableId == musteri.Id);

        Assert.Contains(siparis.Indexes, i => i.Name == "ix_siparis_musteri");
    }

    /// <summary>
    /// Testte SSRF politikası devre dışı: konteynerin adresi özel bir adres ve
    /// üretim politikası onu haklı olarak reddediyor. Test edilen şey katalog
    /// SQL'i, politikanın kendisi değil (o ayrıca test ediliyor).
    /// </summary>
    private sealed class AlwaysAllowHostPolicy : Core.Security.IDbHostAccessPolicy
    {
        public bool IsHostAllowed(string? host, out string denyReason)
        {
            denyReason = string.Empty;
            return true;
        }
    }
}
