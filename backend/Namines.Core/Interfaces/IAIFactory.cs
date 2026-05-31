using Namines.Core.Interfaces;

namespace Namines.Core.Interfaces;

public interface IAIFactory
{
    IAIService GetService(string provider);
}
