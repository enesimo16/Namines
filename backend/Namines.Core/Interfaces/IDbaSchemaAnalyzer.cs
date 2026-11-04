using System.Collections.Generic;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <summary>
/// Şema üzerinde AI tabanlı DBA analizi yapan sözleşme.
/// </summary>
public interface IDbaSchemaAnalyzer
{
    Task<List<DbaIssue>> AnalyzeSchemaDbaAsync(DatabaseSchema schema, DatabaseType dbType);
}
