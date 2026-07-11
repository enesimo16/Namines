using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
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
    // SSRF koruması: varsayılan olarak private/loopback hedefler reddedilir.
    // Yerel geliştirmede appsettings.Development.json → "Executor:AllowPrivateHosts": true ile açılabilir.
    private readonly bool _allowPrivateHosts;

    public DatabaseExecutorService(IConfiguration configuration)
    {
        _allowPrivateHosts = configuration.GetValue<bool>("Executor:AllowPrivateHosts");
    }

    private void ValidateConnectionTarget(string connectionString, DatabaseType dbType)
    {
        if (_allowPrivateHosts) return;
        foreach (var host in ExtractHosts(connectionString, dbType))
        {
            if (!SsrfGuard.IsHostSafe(host))
                throw new InvalidOperationException("Connection target host is not allowed.");
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

    public async Task<bool> TestConnectionAsync(string connectionString, DatabaseType dbType)
    {
        try
        {
            ValidateConnectionTarget(connectionString, dbType);
            await using var connection = CreateConnection(connectionString, dbType);
            await connection.OpenAsync();
            return connection.State == ConnectionState.Open;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ExecutionResult> ExecuteScriptAsync(string connectionString, string ddlScript, DatabaseType dbType)
    {
        int statementsExecuted = 0;
        try
        {
            ValidateConnectionTarget(connectionString, dbType);
            await using var connection = CreateConnection(connectionString, dbType);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

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

                    await command.ExecuteNonQueryAsync();
                    statementsExecuted++;
                }

                await transaction.CommitAsync();
                return new ExecutionResult(true, null, statementsExecuted);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new ExecutionResult(false, $"Error executing script at statement {statementsExecuted + 1}: {ex.Message}", statementsExecuted);
            }
        }
        catch (Exception ex)
        {
            return new ExecutionResult(false, $"Connection error: {ex.Message}", 0);
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
