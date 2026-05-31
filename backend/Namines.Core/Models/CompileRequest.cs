using Namines.Core.Enums;

namespace Namines.Core.Models;

public class CompileRequest
{
    public DatabaseSchema Schema { get; set; } = new();
    public DatabaseType DbType { get; set; } = DatabaseType.MSSQL;
}
