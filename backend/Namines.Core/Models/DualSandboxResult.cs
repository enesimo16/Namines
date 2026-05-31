using System;

namespace Namines.Core.Models;

public record DualSandboxResult
{
    public string DbContainerId { get; init; } = string.Empty;
    public string AppContainerId { get; init; } = string.Empty;
    public string NetworkId { get; init; } = string.Empty;
    public int StreamlitPort { get; init; }
    public string StreamlitUrl { get; init; } = string.Empty;
    public string FinalAppPyContent { get; init; } = string.Empty;
}
