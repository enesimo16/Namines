using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Namines.Ops.Domain;

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

    // Bağlantı dizesi biçimi bir literal'in içinde de olur: dblink_connect('host=h password=hunter2'). Değer
    // boşluğa, noktalı virgüle ya da tırnağa kadar; tırnakla başlayan değer yukarıdaki desenin işi.
    private static readonly Regex SecretPair = new(
        @"\b(PASSWORD|PASSWD|PWD|SSLPASSWORD|SECRET|TOKEN|API[_-]?KEY|ACCESS[_-]?KEY)(\s*=\s*)([^\s;'""]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    // Oturum ayarı değeri (SET app.key = '...'): adı ne olursa olsun değer gizlenir. Yalnızca deyim başındaki
    // SET; UPDATE ... SET kolon atamasıdır, ayar değil.
    private static readonly Regex SessionSet = new(
        @"(^|;)(\s*SET\s+(?:SESSION\s+|LOCAL\s+)?[\w.""]+\s*(?:=|\bTO\b)\s*)('(?:[^']|'')*'|[^\s;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    private readonly IOpsData _db;

    public SqlExecutionAuditService(IOpsData db) => _db = db;

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
            // Motor hatası çoğu zaman deyimi geri yansıtır: hata metni de aynı maskeden geçer.
            Error = record.Error is null ? null : MaskOrOmit(record.Error, "[error omitted]"),
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Önce maskele, sonra kes: tersi, kesilme sınırına denk gelen tırnaksız kalmış bir parolayı
    /// desenin dışında bırakırdı.
    /// </summary>
    internal static string BuildPreview(string script)
    {
        // Maskelenemeyen metni yazmaktansa ön eki feda et: özet ve uzunluk yine kayıtta.
        var masked = MaskOrOmit(script, "[preview omitted]");
        return masked.Length <= ScriptPreviewLength ? masked : masked[..ScriptPreviewLength];
    }

    internal static string MaskSecrets(string text)
    {
        var masked = SecretLiteral.Replace(text, m => $"{m.Groups[1].Value}{m.Groups[2].Value}'***'");
        masked = SecretPair.Replace(masked, m => $"{m.Groups[1].Value}{m.Groups[2].Value}***");
        return SessionSet.Replace(masked, m => $"{m.Groups[1].Value}{m.Groups[2].Value}'***'");
    }

    private static string MaskOrOmit(string text, string omitted)
    {
        try { return MaskSecrets(text); }
        catch (RegexMatchTimeoutException) { return omitted; }
    }
}
