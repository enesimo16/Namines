using Namines.Core.Enums;

namespace Namines.Core.Models;

public record CoderAIRequest
{
    public DatabaseSchema Schema { get; init; } = new();
    public DatabaseType DbType { get; init; }
    public bool EnhanceWithAI { get; init; } = false;
}
