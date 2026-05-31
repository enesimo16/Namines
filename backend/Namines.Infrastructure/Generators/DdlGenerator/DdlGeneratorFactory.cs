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
            DatabaseType.Db2        => new OracleDdlGenerator(),
            DatabaseType.Firebird   => new SqliteDdlGenerator(),
            DatabaseType.Spanner    => new PostgresDdlGenerator(),
            DatabaseType.Redshift   => new PostgresDdlGenerator(),
            _ => throw new NotImplementedException($"DDL generator for {dbType} is not implemented.")
        };
    }
}
