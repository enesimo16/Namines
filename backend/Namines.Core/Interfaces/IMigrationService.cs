using System.Threading.Tasks;
using Namines.Core.Models;
using Namines.Core.Enums;

namespace Namines.Core.Interfaces;

public interface IMigrationService
{
    Task<DatabaseSchema> ParseDbContextAsync(string dbContextCode, DatabaseType dbType);
    Task<SchemaDiffResult> CalculateDiffAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema);
    Task<MigrationResult> GenerateMigrationAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType dbType);
}
