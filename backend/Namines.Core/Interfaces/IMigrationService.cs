using System.Threading.Tasks;
using Namines.Core.Models;
using Namines.Core.Enums;

namespace Namines.Core.Interfaces;

public interface IMigrationService
{
    Task<DatabaseSchema> ParseDbContextAsync(string dbContextCode, DatabaseType dbType);

    /// <summary>
    /// <paramref name="engine"/>, risk sınıflandırmasının motora özgü mesajlarını belirler
    /// (ör. MSSQL'de FK cascade "Msg 1785"). Belirtilmezse PostgreSQL varsayılır.
    /// </summary>
    Task<SchemaDiffResult> CalculateDiffAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType engine = DatabaseType.PostgreSQL);
    Task<MigrationResult> GenerateMigrationAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType dbType);
}
