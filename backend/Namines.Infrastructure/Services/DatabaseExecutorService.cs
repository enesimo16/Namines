using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Security;
using System.Text.RegularExpressions;

namespace Namines.Infrastructure.Services;

public class DatabaseExecutorService : IDatabaseExecutor
{
    /// <summary>
    /// Tek bir SQL ifadesinin calisabilecegi en uzun sure.
    ///
    /// Buyuk bir tabloda ALTER dakikalar surebilir, bu yuzden Gateway'in 15
    /// saniyelik sorgu zaman asimindan uzun; ama sinirsiz DEGIL -- sinirsiz
    /// olsaydi tek bir betik baglanti havuzundaki bir yuvayi sureleyebilirdi.
    /// </summary>
    private const int StatementTimeoutSeconds = 120;

    /// <summary>
    /// SSRF ve egress kontrolu -- ORTAK politika.
    ///
    /// <b>Onceden bu servis kendi bayragini kullaniyordu</b>
    /// (`Executor:AllowPrivateHosts`) ve o bayrak TEK KAPILIYDI: yalnizca
    /// config'e bakiyordu, ortama bakmiyordu. Oysa ayni kararin diger tarafi
    /// (`DbHostAccessPolicy`) CIFT KAPILI ve gerekcesi kendi yorumunda yazili:
    /// "tek basina config bayragi, prod'a yanlislikla kopyalanan bir env
    /// degiskeniyle SSRF korumasini kapatabilirdi".
    ///
    /// Yani en tehlikeli uc (keyfi SQL calistirma) en zayif kapiyi kullaniyordu.
    /// `.env.example` da `Executor__AllowPrivateHosts=true` diyip "production'da
    /// MUTLAKA false olmali" notuyla operatorun hatirlamasina guveniyordu.
    ///
    /// Artik iki taraf ayni politikadan geciyor: cift kapi + egress allowlist.
    /// </summary>
    private readonly IDbHostAccessPolicy _hostPolicy;

    public DatabaseExecutorService(IDbHostAccessPolicy hostPolicy)
    {
        _hostPolicy = hostPolicy;
    }

    private void ValidateConnectionTarget(string connectionString, DatabaseType dbType)
    {
        foreach (var host in ExtractHosts(connectionString, dbType))
        {
            if (!_hostPolicy.IsHostAllowed(host, out var denyReason))
                throw new InvalidOperationException(denyReason);
        }
    }

    private static IEnumerable<string> ExtractHosts(string connectionString, DatabaseType dbType)
    {
        string? raw = null;
        try
        {
            switch (dbType)
            {
                case DatabaseType.MSSQL:
                    raw = new SqlConnectionStringBuilder(connectionString).DataSource;
                    break;
                case DatabaseType.PostgreSQL:
                    raw = new NpgsqlConnectionStringBuilder(connectionString).Host;
                    break;
                case DatabaseType.MySQL:
                case DatabaseType.MariaDB:
                    raw = new MySqlConnectionStringBuilder(connectionString).Server;
                    break;
                case DatabaseType.Oracle:
                    raw = new OracleConnectionStringBuilder(connectionString).DataSource;
                    break;
                case DatabaseType.SQLite:
                    yield break; // yerel dosya — ağ hedefi yok
            }
        }
        catch { yield break; } // parse edilemiyorsa host çıkarımı yapılamaz

        if (string.IsNullOrWhiteSpace(raw)) yield break;

        // Çoklu host (Postgres/MySQL virgülle) + MSSQL "tcp:host,1433\\instance" gibi biçimleri normalize et.
        foreach (var part in raw.Split(','))
        {
            var h = part.Trim();
            var protoIdx = h.IndexOf(':');
            // "tcp:host" / "host:port" — protokol veya port ayır
            if (protoIdx >= 0)
            {
                var left = h.Substring(0, protoIdx);
                if (left.Equals("tcp", StringComparison.OrdinalIgnoreCase) || left.Equals("np", StringComparison.OrdinalIgnoreCase))
                    h = h.Substring(protoIdx + 1);
                else
                    h = left; // host:port
            }
            var slashIdx = h.IndexOfAny(new[] { '\\', '/' });
            if (slashIdx >= 0) h = h.Substring(0, slashIdx); // named instance / service
            h = h.Trim();
            if (!string.IsNullOrEmpty(h)) yield return h;
        }
    }

    public async Task<bool> TestConnectionAsync(
        string connectionString, DatabaseType dbType, CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateConnectionTarget(connectionString, dbType);
            await using var connection = CreateConnection(connectionString, dbType);
            await connection.OpenAsync(cancellationToken);
            return connection.State == ConnectionState.Open;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Betigi hedef veritabaninda calistirir.
    ///
    /// <b>Zaman asimi ACIKCA veriliyor:</b> varsayilan CommandTimeout surucuden
    /// suruc uye degisiyor ve bazilarinda cok uzun. Buyuk bir tabloya ALTER
    /// calistiran bir betik, sinirsiz zaman asimiyla istegi ve baglanti
    /// havuzundaki bir yuvayi dakikalarca tutabilirdi.
    ///
    /// <b>Iptal edilebilir:</b> istemci vazgectiginde is sunucuda devam etmesin.
    /// </summary>
    public async Task<ExecutionResult> ExecuteScriptAsync(
        string connectionString, string ddlScript, DatabaseType dbType,
        CancellationToken cancellationToken = default)
    {
        // DDL her motorda transaction'a girmiyor: MySQL/MariaDB/Oracle ortuk
        // commit yapar. Basarisizlikta cagirana "kismi uygulama mumkun" demek
        // zorundayiz, aksi halde arayuz verilmeyen bir garanti gosterir.
        var ddlIsTransactional = dbType is DatabaseType.PostgreSQL or DatabaseType.MSSQL or DatabaseType.SQLite;

        int statementsExecuted = 0;
        try
        {
            ValidateConnectionTarget(connectionString, dbType);
            await using var connection = CreateConnection(connectionString, dbType);
            await connection.OpenAsync(cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                var commands = SplitScript(ddlScript, dbType);

                foreach (var cmdText in commands)
                {
                    if (string.IsNullOrWhiteSpace(cmdText)) continue;

                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = cmdText;

                    // Oracle requires CommandType.Text
                    command.CommandType = CommandType.Text;
                    command.CommandTimeout = StatementTimeoutSeconds;

                    await command.ExecuteNonQueryAsync(cancellationToken);
                    statementsExecuted++;
                }

                await transaction.CommitAsync(cancellationToken);
                return new ExecutionResult(true, null, statementsExecuted);
            }
            catch (Exception ex)
            {
                // Geri alma en iyi caba: motor DDL'i zaten commit'lediyse bu
                // cagri sessizce hicbir seyi geri almaz -- PartialApplyPossible
                // tam olarak bunu cagirana bildiriyor.
                //
                // CancellationToken.None ile: istek iptal edildigi icin buraya
                // dustuysek, iptal edilmis bir token'la geri alma denemek
                // geri almayi da iptal ederdi.
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { /* baglanti dusmus olabilir */ }

                // Iptal disariya birakiliyor: asagidaki handler onu dogru
                // sebeple raporluyor.
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;

                return new ExecutionResult(
                    false,
                    // Baglanti KURULDUKTAN sonraki hata: surucu mesaji korunuyor,
                    // cunku cagiran hedefe erisebildigini zaten biliyor ve
                    // "42. ifadede sozdizimi hatasi" bilgisi olmadan betigini
                    // duzeltemez. Kesif degeri yok, tanisal degeri yuksek.
                    DbConnectionFailure.DescribeStatementFailure(ex, statementsExecuted + 1),
                    statementsExecuted,
                    PartialApplyPossible: !ddlIsTransactional && statementsExecuted > 0);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Iptal bir BAGLANTI HATASI DEGIL; oyle raporlamak kullaniciya
            // yanlis sebep gosterirdi. Istisna yeniden firlatilmiyor cunku
            // cagiran (controller) bu sonucu denetim kaydina yaziyor: yarida
            // kesilmis bir DDL kismen uygulanmis olabilir ve tam da o durumun
            // iz birakmasi gerekiyor.
            return new ExecutionResult(
                false,
                "The request was cancelled before the script finished. Part of it may already be applied.",
                statementsExecuted,
                PartialApplyPossible: statementsExecuted > 0);
        }
        catch (Exception ex)
        {
            // Baglanti KURULAMADI: surucunun ham mesaji hedef host/port bilgisi
            // tasidigi icin ISTEMCIYE DONMEZ. Ayni karar GatewayKeyController'da
            // da veriliyor; siniflandirici ortak.
            return new ExecutionResult(false, DbConnectionFailure.Classify(ex), 0);
        }
    }

    private DbConnection CreateConnection(string connectionString, DatabaseType dbType)
    {
        return dbType switch
        {
            DatabaseType.MSSQL      => new SqlConnection(connectionString),
            DatabaseType.PostgreSQL => new NpgsqlConnection(connectionString),
            DatabaseType.MySQL      => new MySqlConnection(connectionString),
            DatabaseType.MariaDB    => new MySqlConnection(connectionString),
            DatabaseType.SQLite     => new SqliteConnection(connectionString),
            DatabaseType.Oracle     => new OracleConnection(connectionString),
            _ => throw new NotSupportedException($"Database type {dbType} is not supported for execution.")
        };
    }

    private string[] SplitScript(string script, DatabaseType dbType)
    {
        if (string.IsNullOrWhiteSpace(script))
            return Array.Empty<string>();

        // MSSQL uses GO
        if (dbType == DatabaseType.MSSQL)
        {
            return Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray();
        }
        
        // Oracle, MySQL, PostgreSQL, SQLite, MariaDB: string literali ('...'), tanımlayıcı ("...")
        // ve $$...$$ dollar-quoting içindeki ';' karakterlerini yok sayan tokenizer ile böl.
        return SplitOnStatementSeparators(script);
    }

    // ';' üzerinde böler ama tırnaklı/dollar-quoted bloklar içindeki ';'leri korur.
    private static string[] SplitOnStatementSeparators(string script)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        char? stringDelim = null; // aktif ' veya " literali
        string? dollarTag = null; // aktif $tag$ bloğu

        for (int i = 0; i < script.Length; i++)
        {
            char c = script[i];

            if (stringDelim != null)
            {
                current.Append(c);
                if (c == stringDelim)
                {
                    // '' veya "" kaçışı → literal devam eder
                    if (i + 1 < script.Length && script[i + 1] == stringDelim)
                    {
                        current.Append(script[i + 1]);
                        i++;
                    }
                    else stringDelim = null;
                }
                continue;
            }

            if (dollarTag != null)
            {
                if (c == '$' && MatchesAt(script, i, dollarTag))
                {
                    current.Append(dollarTag);
                    i += dollarTag.Length - 1;
                    dollarTag = null;
                }
                else current.Append(c);
                continue;
            }

            if (c == '\'' || c == '"')
            {
                stringDelim = c;
                current.Append(c);
                continue;
            }

            if (c == '$')
            {
                var tag = ReadDollarTag(script, i);
                if (tag != null)
                {
                    dollarTag = tag;
                    current.Append(tag);
                    i += tag.Length - 1;
                    continue;
                }
            }

            if (c == ';')
            {
                statements.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) statements.Add(current.ToString());

        return statements.Select(s => s.Trim())
                         .Where(s => !string.IsNullOrEmpty(s))
                         .ToArray();
    }

    // Konumdaki $tag$ etiketini okur ($$ veya $name$); değilse null.
    private static string? ReadDollarTag(string s, int start)
    {
        int j = start + 1;
        while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
        if (j < s.Length && s[j] == '$')
            return s.Substring(start, j - start + 1);
        return null;
    }

    private static bool MatchesAt(string s, int index, string token) =>
        index + token.Length <= s.Length && string.CompareOrdinal(s, index, token, 0, token.Length) == 0;
}
