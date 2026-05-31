using System;
using System.Threading.Tasks;
using Namines.Core.Enums;

using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface IDockerService
{
    Task RunSandboxAndBackupAsync(string jobId, string sqlContent, DatabaseType dbType, Action<string> onProgress);
    
    /// <summary>
    /// Runs dual sandbox (database + Streamlit containers) with AI self-healing retry mechanism.
    /// </summary>
    /// <param name="schema">Database schema for AI fix service context</param>
    Task<DualSandboxResult> RunDualSandboxAsync(string jobId, string sqlContent, string appPyContent, DatabaseType dbType, Action<string> onProgress, DatabaseSchema schema);
    
    Task CleanupSandboxAsync(string jobId);
}
