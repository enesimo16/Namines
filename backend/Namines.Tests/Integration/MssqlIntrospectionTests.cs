using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Enums;
using Namines.Infrastructure.Services;
using Testcontainers.MsSql;
using Xunit;

namespace Namines.Tests.Integration;

/// <summary>
/// <see cref="DbIntrospectionService"/>'in SQL Server katalog sorgularını GERÇEK
/// bir SQL Server'a karşı doğrular (backlog B-12'nin MSSQL yarısı).
///
/// <b>Neden yazıldı:</b> MSSQL yolu yazıldığından beri hiç gerçek bir sunucuya
/// karşı koşmadı — Docker VM'i motorun istediği 2000 MB'ın altındaydı ve
/// kodun kendisi "⚠️ CANLI DOĞRULANMADI" diyordu. MySQL karşılığı canlı
/// denenmişti; bu dosya onun aynası.
/// </summary>
[Collection("Docker")]
public class MssqlIntrospectionTests : IAsyncLifetime
{
    private const string DatabaseName = "naminesdb";

    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() =>
        EngineAvailable.For(DatabaseType.MSSQL) ? _container.StartAsync() : Task.CompletedTask;

    public Task DisposeAsync() =>
        EngineAvailable.For(DatabaseType.MSSQL) ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

    // Ayrı veritabanı ŞART: container'ın varsayılanı `master`, orada dbo altında
    // SQL Server'ın kendi tabloları (spt_monitor vb.) duruyor ve tablo sayısını bozardı.
    private static readonly string[] Schema =
    [
        """
        CREATE TABLE musteri (
            id     INT IDENTITY(1,1) PRIMARY KEY,
            eposta NVARCHAR(200) NOT NULL CONSTRAINT uq_musteri_eposta UNIQUE,
            ad     NVARCHAR(100) NOT NULL,
            bakiye DECIMAL(12,2) NOT NULL CONSTRAINT df_bakiye DEFAULT 0.00,
            CONSTRAINT ck_bakiye CHECK (bakiye >= 0)
        )
        """,
        """
        CREATE TABLE siparis (
            id         INT IDENTITY(1,1) PRIMARY KEY,
            musteri_id INT NOT NULL,
            tutar      DECIMAL(10,2) NOT NULL,
            CONSTRAINT fk_siparis_musteri FOREIGN KEY (musteri_id)
                REFERENCES musteri(id) ON DELETE CASCADE
        )
        """,
        "CREATE INDEX ix_siparis_musteri ON siparis (musteri_id)",
        // Dışarıdan atanan kimlik: IDENTITY OLMAYAN tamsayı PK. Identity yanlışlıkla
        // her int PK'ya yapıştırılırsa bu tabloya satır eklemek imkânsız olurdu.
        "CREATE TABLE harici (siparis_no INT PRIMARY KEY, notu NVARCHAR(50) NULL)",
    ];

    private async Task<Core.Models.DatabaseSchema> IntrospectAsync()
    {
        await using (var master = new SqlConnection(_container.GetConnectionString()))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE {DatabaseName}", master);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = DatabaseName,
        }.ConnectionString;

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            foreach (var statement in Schema)
            {
                await using var command = new SqlCommand(statement, connection);
                await command.ExecuteNonQueryAsync();
            }
        }

        var service = new DbIntrospectionService(
            NullLogger<DbIntrospectionService>.Instance, new AlwaysAllowHostPolicy());
        return await service.IntrospectAsync(connectionString, "MSSQL", CancellationToken.None);
    }

    [RequiresEngineFact(DatabaseType.MSSQL)]
    public async Task Reads_tables_columns_types_and_identity()
    {
        var schema = await IntrospectAsync();

        Assert.Equal(3, schema.Tables.Count);

        var musteri = schema.Tables.Single(t => t.Name == "musteri");

        // IDENTITY, Identity olarak GÖRÜLMELİ: görülmezse Desk onu ekleme
        // formunda zorunlu alan sanır, kullanıcıdan kimliği elle ister ve
        // SQL Server "IDENTITY kolona açık değer eklenemez" diye reddeder.
        var id = musteri.Columns.Single(c => c.Name == "id");
        Assert.True(id.IsPK);
        Assert.True(id.Identity);

        var eposta = musteri.Columns.Single(c => c.Name == "eposta");
        Assert.Equal(200, eposta.Length);
        Assert.False(eposta.IsNullable);

        var bakiye = musteri.Columns.Single(c => c.Name == "bakiye");
        Assert.Equal(12, bakiye.Length);
        Assert.Equal(2, bakiye.Scale);
        // SQL Server varsayılanı ((0.00)) diye sarmalar; soyulmuş hâli gelmeli.
        Assert.Equal("0.00", bakiye.DefaultValue);
    }

    [RequiresEngineFact(DatabaseType.MSSQL)]
    public async Task An_integer_primary_key_without_identity_is_not_marked_identity()
    {
        var schema = await IntrospectAsync();
        var harici = schema.Tables.Single(t => t.Name == "harici");

        var pk = harici.Columns.Single(c => c.Name == "siparis_no");
        Assert.True(pk.IsPK);
        // null = "söylenmedi"; true olursa Desk bu kolonu formdan düşürür ve
        // dışarıdan atanan sipariş numarasını girmek imkânsızlaşır.
        Assert.Null(pk.Identity);
    }

    [RequiresEngineFact(DatabaseType.MSSQL)]
    public async Task Reads_unique_and_check_constraints()
    {
        var schema = await IntrospectAsync();
        var musteri = schema.Tables.Single(t => t.Name == "musteri");
        var eposta = musteri.Columns.Single(c => c.Name == "eposta");

        Assert.Contains(musteri.Uniques, u => u.ColumnIds.Contains(eposta.Id));
        Assert.Contains(musteri.Checks, c => c.Name == "ck_bakiye");
    }

    [RequiresEngineFact(DatabaseType.MSSQL)]
    public async Task Reads_foreign_keys_with_referential_action_and_indexes()
    {
        var schema = await IntrospectAsync();
        var siparis = schema.Tables.Single(t => t.Name == "siparis");
        var musteri = schema.Tables.Single(t => t.Name == "musteri");

        var fk = siparis.Columns.Single(c => c.Name == "musteri_id");
        Assert.True(fk.IsFK);

        // sys.foreign_keys yolu: ON DELETE davranışı yalnızca burada var.
        var relation = Assert.Single(schema.Relations, r =>
            r.SourceTableId == siparis.Id && r.SourceColumnId == fk.Id && r.TargetTableId == musteri.Id);
        Assert.Equal(ReferentialAction.Cascade, relation.OnDelete);
        Assert.Equal(ReferentialAction.NoAction, relation.OnUpdate);

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
