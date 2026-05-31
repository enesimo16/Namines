using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface ICoderAIPackager
{
    Task<string> PackageAsZipAsync(string appPyContent, string sqlContent, DatabaseType dbType, string projectName);
    Task<byte[]> PackageNextJsZipAsync(DatabaseSchema schema, string projectName);
}
