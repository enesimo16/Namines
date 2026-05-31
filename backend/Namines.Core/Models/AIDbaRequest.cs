using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Models;

public class AIDbaRequest
{
    public DatabaseSchema Schema { get; set; } = null!;
    public DatabaseType DbType { get; set; }
}
