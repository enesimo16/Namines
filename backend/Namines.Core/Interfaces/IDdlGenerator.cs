using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface IDdlGenerator
{
    string Generate(DatabaseSchema schema);
}

public interface IDdlGeneratorFactory
{
    IDdlGenerator GetGenerator(DatabaseType dbType);
}
