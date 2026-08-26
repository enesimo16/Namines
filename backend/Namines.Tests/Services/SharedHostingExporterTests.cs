using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>second-phase/13-DAGITIM-HEDEFLERI.md — Plesk/cPanel/mobil paketleri.</summary>
public class SharedHostingExporterTests
{
    private static readonly IDdlGeneratorFactory Ddl = new DdlGeneratorFactory();

    private static DatabaseSchema SampleSchema() => new()
    {
        Name = "shop",
        Tables =
        {
            new SchemaTable
            {
                Id = "t1", Name = "users",
                Columns =
                {
                    new SchemaColumn { Id = "c1", Name = "id", Type = "INT", IsPK = true },
                    new SchemaColumn { Id = "c2", Name = "name", Type = "VARCHAR", Length = 100 },
                },
            },
        },
    };

    [Fact]
    public async Task Mysql_export_produces_a_sql_file_and_a_readme()
    {
        var files = await SharedHostingExporter.ExportAsync(SampleSchema(), DatabaseType.MySQL, Ddl);

        Assert.Contains(files, f => f.Name == "schema.sql");
        Assert.Contains(files, f => f.Name == "README.txt");
    }

    [Fact]
    public async Task Mysql_export_explicitly_sets_utf8mb4_even_though_the_base_generator_does_not()
    {
        var files = await SharedHostingExporter.ExportAsync(SampleSchema(), DatabaseType.MySQL, Ddl);

        var sql = Encoding.UTF8.GetString(files.Single(f => f.Name == "schema.sql").Content);
        Assert.Contains("utf8mb4", sql);

        // Karşılaştırma: temel üretici bunu YAZMIYOR — düzeltme yalnızca bu ihracat
        // yolunda, temel üretici (ve golden-file testleri) değişmedi.
        var baseDdl = Ddl.GetGenerator(DatabaseType.MySQL).Generate(SampleSchema());
        Assert.DoesNotContain("utf8mb4", baseDdl);
    }

    [Fact]
    public async Task Mysql_export_wraps_foreign_key_checks()
    {
        var files = await SharedHostingExporter.ExportAsync(SampleSchema(), DatabaseType.MySQL, Ddl);

        var sql = Encoding.UTF8.GetString(files.Single(f => f.Name == "schema.sql").Content);
        Assert.Contains("SET FOREIGN_KEY_CHECKS=0;", sql);
        Assert.Contains("SET FOREIGN_KEY_CHECKS=1;", sql);
        Assert.True(sql.IndexOf("FOREIGN_KEY_CHECKS=0") < sql.IndexOf("CREATE TABLE"));
    }

    [Fact]
    public async Task Mysql_export_never_emits_create_database()
    {
        // Paylaşımlı barındırmada kullanıcı çoğu zaman CREATE DATABASE yapamaz.
        // Not: dosyanın kendi yorum satırları bu ifadeden AÇIKLAMA olarak bahsediyor
        // ("CREATE DATABASE içermez") — o yüzden yorum olmayan satırlara bakılıyor.
        var files = await SharedHostingExporter.ExportAsync(SampleSchema(), DatabaseType.MySQL, Ddl);

        var sql = Encoding.UTF8.GetString(files.Single(f => f.Name == "schema.sql").Content);
        var executableLines = sql.Split('\n').Where(l => !l.TrimStart().StartsWith("--"));
        Assert.DoesNotContain("CREATE DATABASE", string.Join('\n', executableLines), System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mysql57_check_constraint_warning_only_appears_when_the_schema_has_a_check()
    {
        var withCheck = SampleSchema();
        withCheck.Tables[0].Checks.Add(new SchemaCheck { Id = "chk1", Expression = "id > 0" });

        var filesWithCheck = await SharedHostingExporter.ExportAsync(withCheck, DatabaseType.MySQL, Ddl);
        var sqlWithCheck = Encoding.UTF8.GetString(filesWithCheck.Single(f => f.Name == "schema.sql").Content);
        Assert.Contains("MySQL 5.7", sqlWithCheck);

        var filesWithoutCheck = await SharedHostingExporter.ExportAsync(SampleSchema(), DatabaseType.MySQL, Ddl);
        var sqlWithoutCheck = Encoding.UTF8.GetString(filesWithoutCheck.Single(f => f.Name == "schema.sql").Content);
        Assert.DoesNotContain("MySQL 5.7", sqlWithoutCheck);
    }

    [Fact]
    public async Task Mariadb_export_reuses_the_generator_that_already_sets_utf8mb4()
    {
        var files = await SharedHostingExporter.ExportAsync(SampleSchema(), DatabaseType.MariaDB, Ddl);

        var sql = Encoding.UTF8.GetString(files.Single(f => f.Name == "schema.sql").Content);
        Assert.Contains("utf8mb4", sql);
    }

    [Fact]
    public async Task Sqlite_export_produces_a_real_openable_database_file_plus_sql_and_readme()
    {
        var files = await SharedHostingExporter.ExportAsync(SampleSchema(), DatabaseType.SQLite, Ddl);

        Assert.Contains(files, f => f.Name == "schema.db");
        Assert.Contains(files, f => f.Name == "schema.sql");
        Assert.Contains(files, f => f.Name == "README.txt");

        var dbBytes = files.Single(f => f.Name == "schema.db").Content;
        // SQLite dosya başlığı — gerçekten geçerli bir SQLite veritabanı, boş bir dosya değil.
        Assert.Equal("SQLite format 3\0", Encoding.ASCII.GetString(dbBytes, 0, 16));
    }

    [Fact]
    public async Task Postgres_target_is_rejected_with_a_clear_message()
    {
        var ex = await Assert.ThrowsAsync<System.NotSupportedException>(
            () => SharedHostingExporter.ExportAsync(SampleSchema(), DatabaseType.PostgreSQL, Ddl));

        Assert.Contains("MySQL, MariaDB, and SQLite", ex.Message);
    }
}
