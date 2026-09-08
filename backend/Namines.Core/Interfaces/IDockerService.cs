using System;
using System.Threading.Tasks;
using Namines.Core.Enums;

using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface IDockerService
{
    Task RunSandboxAndBackupAsync(string jobId, string sqlContent, DatabaseType dbType, Action<string> onProgress);
}
