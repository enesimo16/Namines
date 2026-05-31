using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using System.Text.RegularExpressions;

namespace Namines.Infrastructure.Services;

public class DatabaseExecutorService : IDatabaseExecutor
{
    public async Task<bool> TestConnectionAsync(string connectionString, DatabaseType dbType)
    {
        try
        {
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
        
        // Oracle might have issues with trailing semicolons in ADO.NET
        if (dbType == DatabaseType.Oracle)
        {
            var statements = script.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(s => s.Trim())
                                   .Where(s => !string.IsNullOrEmpty(s))
                                   .ToArray();
            return statements;
        }

        // Default behavior (MySQL, PostgreSQL, SQLite, MariaDB)
        return script.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => s.Trim())
                     .Where(s => !string.IsNullOrEmpty(s))
                     .ToArray();
    }
}
