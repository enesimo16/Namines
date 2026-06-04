using System.Threading.Tasks;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface IScaffolderService
{
    Task<byte[]> GenerateFullStackProjectAsync(DatabaseSchema schema);
    Task<byte[]> GeneratePythonFreemiumProjectAsync(DatabaseSchema schema);
}
