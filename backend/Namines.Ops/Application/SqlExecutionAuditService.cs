using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Namines.Ops.Domain;
using Namines.Ops.Infrastructure.Persistence;

namespace Namines.Ops.Application;

/// <summary>Denetim kaydının girdisi. Bağlantı dizesi BİLEREK yok: yalnızca host ve veritabanı adı.</summary>
public sealed record SqlExecutionRecord(
    string UserId,
    string? ProjectId,
    string? TargetHost,
    string? TargetDatabase,
    string DbType,
    string Script,
    bool Success,
    int StatementsExecuted,
    bool PartialApplyPossible,
    string? Error);

public interface ISqlExecutionAuditService
{
    /// <summary>
    /// Betik saklanmaz; özet, kısaltılmış ve maskelenmiş ön ek yazılır. Yazım hatası çağırana gider:
    /// SQL hedefte çoktan uygulandığı için geri alınacak bir şey yok, çağıran hatayı loglar.
    /// </summary>
    Task RecordAsync(SqlExecutionRecord record, CancellationToken ct = default);
}

internal sealed class SqlExecutionAuditService : ISqlExecutionAuditService
{
    public const int ScriptPreviewLength = 500;

    private static readonly string[] DestructiveKeywords = { "DROP ", "TRUNCATE ", "DELETE ", "ALTER ", "REVOKE ", "GRANT " };

    // Ön ek denetim kaydında uzun yaşar ve daha çok kişi okur; betikteki parola oraya kopyalanmamalı.
    private static readonly Regex SecretLiteral = new(
        @"\b(PASSWORD|IDENTIFIED\s+BY|SECRET|TOKEN|API[_ ]?KEY)(\s*(?:=|WITH)?\s*)('(?:[^']|'')*'|""[^""]*"")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    private readonly OpsDbContext _db;

    public SqlExecutionAuditService(OpsDbContext db) => _db = db;

    public async Task RecordAsync(SqlExecutionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.UserId);

        var script = record.Script ?? string.Empty;
        _db.SqlExecutionAudits.Add(new SqlExecutionAudit
        {
            UserId = record.UserId,
            ProjectId = string.IsNullOrWhiteSpace(record.ProjectId) ? null : record.ProjectId,
            TargetHost = record.TargetHost,
            TargetDatabase = record.TargetDatabase,
            DbType = record.DbType,
            ScriptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script))),
            ScriptPreview = BuildPreview(script),
            ScriptLength = script.Length,
            ContainsDestructiveKeyword = DestructiveKeywords.Any(k => script.Contains(k, StringComparison.OrdinalIgnoreCase)),
            Success = record.Success,
            StatementsExecuted = record.StatementsExecuted,
            PartialApplyPossible = record.PartialApplyPossible,
            Error = record.Error,
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Önce maskele, sonra kes: tersi, kesilme sınırına denk gelen tırnaksız kalmış bir parolayı
    /// desenin dışında bırakırdı.
    /// </summary>
    internal static string BuildPreview(string script)
    {
        string masked;
        try
        {
            masked = SecretLiteral.Replace(script, m => $"{m.Groups[1].Value}{m.Groups[2].Value}'***'");
        }
        catch (RegexMatchTimeoutException)
        {
            // Maskelenemeyen metni yazmaktansa ön eki feda et: özet ve uzunluk yine kayıtta.
            return "[preview omitted]";
        }

        return masked.Length <= ScriptPreviewLength ? masked : masked[..ScriptPreviewLength];
    }
}
