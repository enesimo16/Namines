using Namines.Core.Enums;

namespace Namines.Core.Models;

public class ParseDbContextRequest
{
    public string DbContextCode { get; set; } = string.Empty;
    public DatabaseType DbType { get; set; }
}
