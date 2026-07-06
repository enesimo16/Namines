using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface ISmartSeedService
{
    Task<SmartSeedResult> GenerateSmartSeedAsync(DatabaseSchema schema, DatabaseType dbType, string? domainHint, int rowCount = 50, bool forceDeterministic = false);
}
