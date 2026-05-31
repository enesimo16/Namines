using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface IDdlGenerator
{
    string Generate(DatabaseSchema schema);
}
