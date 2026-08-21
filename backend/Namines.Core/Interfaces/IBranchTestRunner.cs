using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <summary>
/// "Run Tests" — G5'in Testcontainers altyapısının runtime (ürün içi) versiyonu.
/// new-phase/30-SERVER-SIDE-BRANCHING.md §3 Adım 3 ve §5'teki güvenlik notuna tabidir:
/// implementasyon worker'ın KENDİ host'unda (container İÇİNDEN değil) Testcontainers
/// çalıştırmalı — docker.sock hiçbir container'a mount edilmez (G1 kuralı, bkz. CLAUDE.md).
/// </summary>
public interface IBranchTestRunner
{
    Task<TestRunResult> RunAsync(DatabaseSchema schema, DatabaseType engine, CancellationToken cancellationToken = default);
}
