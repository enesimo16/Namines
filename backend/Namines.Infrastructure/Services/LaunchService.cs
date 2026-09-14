using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Namines.Core;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;

namespace Namines.Infrastructure.Services;

public enum LaunchStatus { Ready, NeedsReview, ProvisionFailed, DdlFailed }

/// <param name="DdlApplied">Testler ve arayüz için: DDL gerçekten çalıştı mı,
/// yoksa hedefte zaten tablo olduğu için mi atlandı.</param>
/// <param name="BackupWarning">İlk Vault yedeği başarısız olduysa BURADA taşınır
/// — Ready durumunu ENGELLEMEZ (panel yine kullanılabilir), ama sessizce
/// yutulmaz: arayüz bunu görünür bir uyarı olarak göstermek ZORUNDA.</param>
public sealed record LaunchResult(
    LaunchStatus Status,
    string? Error = null,
    bool DdlApplied = false,
    string? BackupWarning = null);

/// <summary>
/// "/compile"'da onaylanmış bir şemayı saniyeler içinde çalışan bir Desk
/// paneline dönüştüren omurga. Ground (provizyon), var olan DDL yürütücü
/// (uygula) ve Vault'u (ilk yedek) TEK bir sırayla birbirine bağlar —
/// üçü de zaten ayrı ayrı canlı kanıtlanmış, burada yeni olan yalnızca sıra.
///
/// <b>"İlk kurulum mu / güncelleme mi" bir istemci bayrağı DEĞİL</b> — hedefin
/// canlı introspection'ı karar veriyor (adım 3). Böylece Desk'ten ya da elle
/// tablo eklenmiş bir projede bile doğru dala düşer.
///
/// <b>Hedef doluysa DDL'e hiç dokunulmaz.</b> Var olan Change Review akışına
/// (ChangeRequestController.CreateQuick) devretmek çağıranın işi — bu servis
/// yalnızca <see cref="LaunchStatus.NeedsReview"/> döner, hiçbir şey yazmaz.
/// </summary>
public class LaunchService
{
    /// <summary>Denetim kaydında saklanan betik ön ekinin uzunluğu — bkz.
    /// <see cref="DatabaseExecutorController"/>'daki aynı sabit; TAMAMI bilerek
    /// saklanmıyor.</summary>
    private const int ScriptPreviewLength = 500;

    private static readonly string[] DestructiveKeywords =
        { "DROP ", "TRUNCATE ", "DELETE ", "ALTER ", "REVOKE ", "GRANT " };

    private readonly GroundService _ground;
    private readonly IDbIntrospectionService _introspection;
    private readonly IDatabaseExecutor _executor;
    private readonly VaultService _vault;
    private readonly IConnectionSecretProtector _protector;
    private readonly AuthDbContext _context;
    private readonly ILogger<LaunchService> _logger;

    public LaunchService(
        GroundService ground,
        IDbIntrospectionService introspection,
        IDatabaseExecutor executor,
        VaultService vault,
        IConnectionSecretProtector protector,
        AuthDbContext context,
        ILogger<LaunchService> logger)
    {
        _ground = ground;
        _introspection = introspection;
        _executor = executor;
        _vault = vault;
        _protector = protector;
        _context = context;
        _logger = logger;
    }

    public async Task<LaunchResult> LaunchAsync(
        CloudProject project, string userId, string providerName, string ddlScript, CancellationToken ct)
    {
        var provider = _ground.FindProvider(providerName);
        if (provider is null)
            return new LaunchResult(LaunchStatus.ProvisionFailed, Error: $"No such provider: '{providerName}'.");

        if (await provider.ProbeAsync(ct) is { } probeProblem)
            return new LaunchResult(LaunchStatus.ProvisionFailed, Error: probeProblem);

        var provisionResult = await _ground.ProvisionAsync(project, userId, provider, ct);
        if (!provisionResult.Ok)
            return new LaunchResult(LaunchStatus.ProvisionFailed, Error: provisionResult.Error);

        // ProvisionAsync, CloudProject.EncryptedConnectionString'i BAŞARILI olduğunda
        // günceller (bkz. GroundService.ProvisionAsync) — 'project' aynı EF izlenen
        // örnek olduğu için burada zaten güncel.
        if (string.IsNullOrWhiteSpace(project.EncryptedConnectionString))
            return new LaunchResult(LaunchStatus.ProvisionFailed,
                Error: "Provisioning reported success but left no connection string.");

        var connectionString = _protector.Unprotect(project.EncryptedConnectionString);

        DatabaseSchema liveSchema;
        try
        {
            liveSchema = await _introspection.IntrospectAsync(connectionString, "PostgreSQL", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Launch: {ProjectId} icin hedef okunamadi.", project.Id);
            return new LaunchResult(LaunchStatus.ProvisionFailed,
                Error: $"The new database could not be read back: {ex.Message}");
        }

        if (liveSchema.Tables.Count > 0)
        {
            // Hedefte zaten veri var — DDL'e HİÇ DOKUNMUYORUZ. Çağıran (LaunchController)
            // bunu Change Review akışına devreder.
            return new LaunchResult(LaunchStatus.NeedsReview);
        }

        var execution = await _executor.ExecuteScriptAsync(connectionString, ddlScript, DatabaseType.PostgreSQL, ct);
        await WriteAuditAsync(userId, project.Id, connectionString, ddlScript, execution, ct);

        if (!execution.Success)
        {
            _logger.LogError(
                "Launch: {ProjectId} icin DDL basarisiz ({Statements} ifade calisti, kismi uygulama {Partial}): {Error}",
                project.Id, execution.StatementsExecuted, execution.PartialApplyPossible, execution.ErrorMessage);
            return new LaunchResult(LaunchStatus.DdlFailed, Error: execution.ErrorMessage);
        }

        string? backupWarning = null;
        try
        {
            var backup = await _vault.BackupAsync(project, userId, VaultBackupKind.Manual, ct);
            if (!backup.Ok)
                backupWarning = $"Initial backup failed: {backup.Error}. Back it up manually from the Vault tab.";
        }
        catch (Exception ex)
        {
            // İlk yedek Ready'yi ENGELLEMİYOR — ama sessizce yutulmuyor da.
            _logger.LogError(ex, "Launch: {ProjectId} icin ilk yedek basarisiz.", project.Id);
            backupWarning = "Initial backup failed unexpectedly. Back it up manually from the Vault tab.";
        }

        return new LaunchResult(LaunchStatus.Ready, DdlApplied: true, BackupWarning: backupWarning);
    }

    /// <summary>
    /// DatabaseExecutorController.WriteAuditAsync ile AYNI desen — Launch'ın
    /// uyguladığı DDL de denetim kaydının dışında kalmamalı. Başarısızlar da
    /// kaydedilir: bir deneme yanlış giderse "kim, ne zaman, nerede" cevapsız
    /// kalmamalı.
    /// </summary>
    private async Task WriteAuditAsync(
        string userId, string projectId, string connectionString, string ddlScript,
        ExecutionResult execution, CancellationToken ct)
    {
        var (host, database) = ConnectionTargetDescriber.Describe(connectionString, DatabaseType.PostgreSQL);

        var entry = new SqlExecutionAudit
        {
            UserId = userId,
            ProjectId = projectId,
            TargetHost = host,
            TargetDatabase = database,
            DbType = DatabaseType.PostgreSQL.ToString(),
            ScriptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ddlScript))),
            ScriptPreview = ddlScript.Length <= ScriptPreviewLength ? ddlScript : ddlScript[..ScriptPreviewLength],
            ScriptLength = ddlScript.Length,
            ContainsDestructiveKeyword = DestructiveKeywords.Any(
                k => ddlScript.Contains(k, StringComparison.OrdinalIgnoreCase)),
            Success = execution.Success,
            StatementsExecuted = execution.StatementsExecuted,
            PartialApplyPossible = execution.PartialApplyPossible,
            Error = execution.ErrorMessage,
        };

        try
        {
            _context.SqlExecutionAudits.Add(entry);
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Launch: SQL calistirma denetim kaydi YAZILAMADI. Kullanici={UserId} Hedef={Host}/{Database} Hash={Hash}",
                userId, host, database, entry.ScriptHash);
        }
    }
}
