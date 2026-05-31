using Namines.Core.Enums;

namespace Namines.Core.Models;

public class MigrationGenerateRequest
{
    public DatabaseSchema OldSchema { get; set; } = new();
    public DatabaseSchema NewSchema { get; set; } = new();
    public DatabaseType DbType { get; set; }
}
