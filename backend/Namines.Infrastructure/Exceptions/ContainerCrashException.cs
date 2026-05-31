using System;

namespace Namines.Infrastructure.Exceptions;

/// <summary>
/// Exception thrown when a Docker container crashes during deployment.
/// Carries exit code and error logs for AI self-healing mechanism.
/// </summary>
public class ContainerCrashException : Exception
{
    /// <summary>
    /// Container exit code (typically > 0 for crashes)
    /// </summary>
    public int ExitCode { get; }
    
    /// <summary>
    /// Container error logs extracted from stdout/stderr
    /// </summary>
    public string ErrorLogs { get; }
    
    public ContainerCrashException(int exitCode, string errorLogs) 
        : base($"Container crashed with exit code {exitCode}")
    {
        ExitCode = exitCode;
        ErrorLogs = errorLogs;
    }
}
