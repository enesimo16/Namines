using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface IAIDbaService
{
    Task<DbaAnalysisResult> AnalyzeSchemaAsync(DatabaseSchema schema, DatabaseType dbType);
}
