using Namines.Core.Enums;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Tests.Fixtures;

namespace Namines.Tests.Ddl;

/// <summary>
/// REGRESYON KORUMASI — Testcontainers ile gerçek SQL Server'a karşı çalıştırılan
/// bir entegrasyon testinin bulduğu hatanın geri gelmesini engeller.
///
/// Dört üreticinin (MSSQL, MySQL, MariaDB, Oracle) dördü de bir kolonun otomatik
/// artan olup olmayacağına yalnızca "bu kolon PK mi ve sayısal mı" diye bakıyordu —
/// bileşik PK'nın KAÇ kolonu olduğuna bakmıyordu. Sonuç: 03-composite-key fixture'ında
/// hem OrderId hem ProductId INT olduğu için ikisi de identity/auto-increment aldı.
///
/// Gerçek veritabanları bunu reddediyor:
///   SQL Server: Msg 2744 "Multiple identity columns specified"
///   MySQL/MariaDB: yalnızca bir AUTO_INCREMENT kolonuna izin verir
///   Oracle: ORA-30673 (birden fazla IDENTITY kolonu)
///
/// SQLite bu hatadan muaftı — kendi üreticisinde zaten `pkColumns.Count == 1`
/// kontrolü vardı. Diğer dördü ona uydurulmalıydı.
/// </summary>
public class CompositeKeyIdentityTests
{
    [Theory]
    [InlineData(DatabaseType.MSSQL, "IDENTITY")]
    [InlineData(DatabaseType.MySQL, "AUTO_INCREMENT")]
    [InlineData(DatabaseType.MariaDB, "AUTO_INCREMENT")]
    [InlineData(DatabaseType.Oracle, "GENERATED ALWAYS AS IDENTITY")]
    public void Composite_pk_never_gets_auto_increment_on_any_column(DatabaseType engine, string marker)
    {
        var ddl = new DdlGeneratorFactory().GetGenerator(engine).Generate(SchemaFixtures.CompositeKey());

        // OrderProducts bileşik PK'lı tek tablo; onun gövdesini izole et.
        var tableStart = ddl.IndexOf("OrderProducts", StringComparison.Ordinal);
        Assert.True(tableStart >= 0, "OrderProducts tablosu DDL'de bulunamadı.");
        var tableEnd = ddl.IndexOf(");", tableStart, StringComparison.Ordinal);
        var tableBody = ddl[tableStart..tableEnd];

        Assert.DoesNotContain(marker, tableBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DatabaseType.MSSQL, "IDENTITY")]
    [InlineData(DatabaseType.MySQL, "AUTO_INCREMENT")]
    [InlineData(DatabaseType.MariaDB, "AUTO_INCREMENT")]
    [InlineData(DatabaseType.Oracle, "GENERATED ALWAYS AS IDENTITY")]
    public void Single_column_pk_still_gets_auto_increment(DatabaseType engine, string marker)
    {
        // Düzeltme özelliği kaldırmadı — tek kolonlu PK hâlâ otomatik artan olmalı.
        var ddl = new DdlGeneratorFactory().GetGenerator(engine).Generate(SchemaFixtures.CompositeKey());

        var tableStart = ddl.IndexOf("\"Orders\"", StringComparison.Ordinal);
        if (tableStart < 0) tableStart = ddl.IndexOf("[Orders]", StringComparison.Ordinal);
        if (tableStart < 0) tableStart = ddl.IndexOf("`Orders`", StringComparison.Ordinal);
        Assert.True(tableStart >= 0, "Orders tablosu DDL'de bulunamadı.");

        var tableEnd = ddl.IndexOf(");", tableStart, StringComparison.Ordinal);
        var tableBody = ddl[tableStart..tableEnd];

        Assert.Contains(marker, tableBody, StringComparison.OrdinalIgnoreCase);
    }
}
