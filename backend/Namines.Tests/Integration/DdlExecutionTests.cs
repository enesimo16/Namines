using Microsoft.Data.SqlClient;
using MySqlConnector;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Tests.Fixtures;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Namines.Tests.Integration;

/// <summary>
/// GERÇEK veritabanlarında DDL çalıştırma.
///
/// Golden-file testleri üretilen SQL'in METNİNİ doğrular — doğru görünmesini.
/// Bu testler ÇALIŞTIĞINI doğrular. İkisi farklı şeydir: sözdizimi hatasız görünen
/// bir DDL, motor tarafından pekâlâ reddedilebilir (bkz. çoklu cascade yolu).
///
/// Ürünün ana satış iddiası "6 motorda çalışan DDL üretiyoruz" olacaksa,
/// bunu kanıtlayan tek şey budur.
///
/// Docker gerekir. Yoksa testler atlanır (bkz. RequiresDockerFact).
/// </summary>
[Collection("Docker")]
public class DdlExecutionTests
{
    private static string Ddl(DatabaseSchema schema, DatabaseType engine) =>
        new DdlGeneratorFactory().GetGenerator(engine).Generate(schema);

    private static string[] SplitStatements(string ddl) =>
        ddl.Split(';', StringSplitOptions.RemoveEmptyEntries)
           .Select(s => s.Trim())
           .Where(s => s.Length > 0 && !s.StartsWith("--"))
           .ToArray();

    // ══════════════════════════════════════════════════════════════════════
    //  PostgreSQL
    // ══════════════════════════════════════════════════════════════════════

    public class PostgresTests : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container =
            new PostgreSqlBuilder("postgres:17-alpine").Build();

        public Task InitializeAsync() =>
            DockerAvailable.Value ? _container.StartAsync() : Task.CompletedTask;

        public Task DisposeAsync() =>
            DockerAvailable.Value ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

        public static TheoryData<string> Fixtures()
        {
            var data = new TheoryData<string>();
            foreach (var (name, _) in SchemaFixtures.All()) data.Add(name);
            return data;
        }

        [RequiresDockerTheory]
        [MemberData(nameof(Fixtures))]
        public async Task Generated_ddl_executes(string fixtureName)
        {
            var ddl = Ddl(SchemaFixtures.ByName(fixtureName), DatabaseType.PostgreSQL);

            await using var conn = new NpgsqlConnection(_container.GetConnectionString());
            await conn.OpenAsync();

            // Her fixture kendi şemasında çalışsın — testler birbirini kirletmesin.
            var schemaName = $"t_{fixtureName.Replace("-", "_")}";
            await Exec(conn, $"CREATE SCHEMA {schemaName}; SET search_path TO {schemaName};");

            foreach (var statement in SplitStatements(ddl))
            {
                try
                {
                    await Exec(conn, statement);
                }
                catch (PostgresException ex)
                {
                    Assert.Fail(
                        $"PostgreSQL üretilen DDL'i reddetti.{Environment.NewLine}" +
                        $"Hata: {ex.SqlState} {ex.MessageText}{Environment.NewLine}" +
                        $"İfade:{Environment.NewLine}{statement};");
                }
            }
        }

        [RequiresDockerFact]
        public async Task Indexes_actually_exist_after_execution()
        {
            var ddl = Ddl(SchemaFixtures.IndexesAndConstraints(), DatabaseType.PostgreSQL);

            await using var conn = new NpgsqlConnection(_container.GetConnectionString());
            await conn.OpenAsync();
            await Exec(conn, "CREATE SCHEMA ix_check; SET search_path TO ix_check;");

            foreach (var statement in SplitStatements(ddl))
                await Exec(conn, statement);

            // Üretmek yetmez — motor gerçekten oluşturmuş mu?
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_indexes WHERE schemaname = 'ix_check' AND indexname LIKE 'IX_%' OR indexname LIKE 'UX_%'",
                conn);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            Assert.True(count >= 4, $"En az 4 index bekleniyordu, {count} bulundu.");
        }

        [RequiresDockerFact]
        public async Task Check_constraint_actually_rejects_bad_data()
        {
            // CHECK üretmek bir şey ifade etmez — veritabanı onu ZORLUYOR mu?
            var ddl = Ddl(SchemaFixtures.IndexesAndConstraints(), DatabaseType.PostgreSQL);

            await using var conn = new NpgsqlConnection(_container.GetConnectionString());
            await conn.OpenAsync();
            await Exec(conn, "CREATE SCHEMA ck_check; SET search_path TO ck_check;");

            foreach (var statement in SplitStatements(ddl))
                await Exec(conn, statement);

            var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
                await Exec(conn, @"INSERT INTO ""Orders"" (""UserId"", ""Total"") VALUES (1, -5)"));

            // 23514 = check_violation
            Assert.Equal("23514", ex.SqlState);
        }

        private static async Task Exec(NpgsqlConnection conn, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SQL Server — Msg 1785 iddiasının ampirik kanıtı
    // ══════════════════════════════════════════════════════════════════════

    public class SqlServerTests : IAsyncLifetime
    {
        private readonly MsSqlContainer _container =
            new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        public Task InitializeAsync() =>
            DockerAvailable.Value ? _container.StartAsync() : Task.CompletedTask;

        public Task DisposeAsync() =>
            DockerAvailable.Value ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

        public static TheoryData<string> Fixtures()
        {
            var data = new TheoryData<string>();
            foreach (var (name, _) in SchemaFixtures.All()) data.Add(name);
            return data;
        }

        [RequiresDockerTheory]
        [MemberData(nameof(Fixtures))]
        public async Task Generated_ddl_executes(string fixtureName)
        {
            var ddl = Ddl(SchemaFixtures.ByName(fixtureName), DatabaseType.MSSQL);
            var dbName = $"t_{fixtureName.Replace("-", "_")}";

            await using var conn = new SqlConnection(_container.GetConnectionString());
            await conn.OpenAsync();
            await Exec(conn, $"CREATE DATABASE [{dbName}]");
            await Exec(conn, $"USE [{dbName}]");

            foreach (var statement in SplitStatements(ddl))
            {
                try
                {
                    await Exec(conn, statement);
                }
                catch (SqlException ex)
                {
                    Assert.Fail(
                        $"SQL Server üretilen DDL'i reddetti.{Environment.NewLine}" +
                        $"Msg {ex.Number}: {ex.Message}{Environment.NewLine}" +
                        $"İfade:{Environment.NewLine}{statement};");
                }
            }
        }

        /// <summary>
        /// G2/G3 boyunca "SQL Server çoklu cascade yolunu Msg 1785 ile reddeder" dedik
        /// ama bunu hiç ÇALIŞTIRMADIK — dokümantasyona dayanıyorduk.
        ///
        /// Bu test iddiayı kanıta çevirir: eski davranışı (her FK'ya CASCADE) yeniden
        /// üretip gerçek SQL Server'a gönderir ve Msg 1785 aldığını doğrular.
        /// </summary>
        [RequiresDockerFact]
        public async Task Cascade_on_every_fk_is_rejected_with_msg_1785()
        {
            var schema = SchemaFixtures.MultiCascadePath();
            foreach (var rel in schema.Relations)
                rel.OnDelete = ReferentialAction.Cascade; // Faz 1'in davranışı

            var ddl = Ddl(schema, DatabaseType.MSSQL);

            await using var conn = new SqlConnection(_container.GetConnectionString());
            await conn.OpenAsync();
            await Exec(conn, "CREATE DATABASE [msg1785_proof]");
            await Exec(conn, "USE [msg1785_proof]");

            SqlException? caught = null;
            foreach (var statement in SplitStatements(ddl))
            {
                try { await Exec(conn, statement); }
                catch (SqlException ex) { caught = ex; break; }
            }

            Assert.NotNull(caught);
            Assert.Equal(1785, caught!.Number);
        }

        /// <summary>Aynı şema, YENİ varsayılanla (NO ACTION) sorunsuz çalışmalı.</summary>
        [RequiresDockerFact]
        public async Task Same_schema_with_default_no_action_succeeds()
        {
            var ddl = Ddl(SchemaFixtures.MultiCascadePath(), DatabaseType.MSSQL);

            await using var conn = new SqlConnection(_container.GetConnectionString());
            await conn.OpenAsync();
            await Exec(conn, "CREATE DATABASE [no_action_proof]");
            await Exec(conn, "USE [no_action_proof]");

            foreach (var statement in SplitStatements(ddl))
                await Exec(conn, statement); // hata fırlatırsa test kırılır
        }

        private static async Task Exec(SqlConnection conn, string sql)
        {
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  MySQL
    // ══════════════════════════════════════════════════════════════════════

    public class MySqlTests : IAsyncLifetime
    {
        private readonly MySqlContainer _container =
            new MySqlBuilder("mysql:8.4").Build();

        public Task InitializeAsync() =>
            DockerAvailable.Value ? _container.StartAsync() : Task.CompletedTask;

        public Task DisposeAsync() =>
            DockerAvailable.Value ? _container.DisposeAsync().AsTask() : Task.CompletedTask;

        public static TheoryData<string> Fixtures()
        {
            var data = new TheoryData<string>();
            foreach (var (name, _) in SchemaFixtures.All()) data.Add(name);
            return data;
        }

        [RequiresDockerTheory]
        [MemberData(nameof(Fixtures))]
        public async Task Generated_ddl_executes(string fixtureName)
        {
            var ddl = Ddl(SchemaFixtures.ByName(fixtureName), DatabaseType.MySQL);

            // Testcontainers'ın MySQL kullanıcısı yalnızca container oluşturulurken
            // atanan veritabanına erişebilir — CREATE DATABASE + farklı isimle GRANT
            // olmadan yeni bir veritabanına giremez. Bu yüzden tek (varsayılan)
            // veritabanı kullanılır; her fixture kendi tabloları tekrar
            // oluşturabilsin diye önce DROP DATABASE/CREATE ile temizlenir.
            await using var conn = new MySqlConnection(_container.GetConnectionString());
            await conn.OpenAsync();
            var dbName = conn.Database;
            await Exec(conn, $"DROP DATABASE IF EXISTS `{dbName}`");
            await Exec(conn, $"CREATE DATABASE `{dbName}`");
            await Exec(conn, $"USE `{dbName}`");

            foreach (var statement in SplitStatements(ddl))
            {
                try
                {
                    await Exec(conn, statement);
                }
                catch (MySqlException ex)
                {
                    Assert.Fail(
                        $"MySQL üretilen DDL'i reddetti.{Environment.NewLine}" +
                        $"Hata {ex.Number}: {ex.Message}{Environment.NewLine}" +
                        $"İfade:{Environment.NewLine}{statement};");
                }
            }
        }

        private static async Task Exec(MySqlConnection conn, string sql)
        {
            await using var cmd = new MySqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
