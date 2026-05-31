using System.Collections.Generic;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface IEfCoreGenerator
{
    // Returns a dictionary where key is the filename (e.g., "User.cs") and value is the content
    Dictionary<string, string> Generate(DatabaseSchema schema);
}
