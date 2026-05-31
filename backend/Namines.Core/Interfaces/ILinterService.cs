using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface ILinterService
{
    LintResult Lint(DatabaseSchema schema);
}
