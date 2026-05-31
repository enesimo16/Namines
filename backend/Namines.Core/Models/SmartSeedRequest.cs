using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Models;

public class SmartSeedRequest
{
    public DatabaseSchema Schema { get; set; } = null!;
    public DatabaseType DbType { get; set; }
    public string? DomainHint { get; set; }
    public int RowCount { get; set; } = 50;
}
