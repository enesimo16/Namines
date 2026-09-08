using System;
using Namines.Core.Enums;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.Generators.DdlGenerator;

public interface IDdlGeneratorFactory
{
    IDdlGenerator GetGenerator(DatabaseType dbType);
}

public class DdlGeneratorFactory : IDdlGeneratorFactory
{
    public IDdlGenerator GetGenerator(DatabaseType dbType)
    {
        return dbType switch
        {
            DatabaseType.MSSQL      => new MssqlDdlGenerator(),
            DatabaseType.PostgreSQL => new PostgresDdlGenerator(),
            DatabaseType.MySQL      => new MySqlDdlGenerator(),
            DatabaseType.SQLite     => new SqliteDdlGenerator(),
            DatabaseType.Oracle     => new OracleDdlGenerator(),
            DatabaseType.MariaDB    => new MariaDbDdlGenerator(),

            // Yeni bir motor eklerken kendi üreticisini yaz — var olan bir üreticiye
            // takma ad vermek sessizce yanlış DDL üretir (Db2/Firebird/Spanner/Redshift
            // tam olarak böyle kaldırıldı, bkz. DatabaseType).
            _ => throw new NotImplementedException($"DDL generator for {dbType} is not implemented.")
        };
    }
}
