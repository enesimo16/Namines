using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Interfaces;
using Namines.Core.Security;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Bkz. <see cref="IDbPrivilegeInspector"/>.
///
/// Salt-okunur bir oturumda çalışır ve yalnızca katalog sorar; incelemenin kendisi
/// veritabanına hiçbir şey yapmaz. Motorun desteklenmediği durumda "yetki yok" DEMEZ —
/// bilmediğini söyler, çünkü "kontrol edilemedi" ile "risk yok" birbirine karıştığında
/// kullanıcı sahte bir güvence kazanır.
/// </summary>
public sealed class DbPrivilegeInspector : IDbPrivilegeInspector
{
    private readonly IDbHostAccessPolicy _hostPolicy;

    public DbPrivilegeInspector(IDbHostAccessPolicy hostPolicy) => _hostPolicy = hostPolicy;

    public async Task<DbPrivilegeReport> InspectAsync(
        string connectionString, string dbType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbType);

        var host = DbIntrospectionService.ExtractHost(connectionString, dbType);
        if (!_hostPolicy.IsHostAllowed(host, out var denyReason))
            throw new InvalidOperationException(denyReason);

        await using var conn = await UserDbConnection.OpenAsync(connectionString, dbType, readOnly: true, cancellationToken);

        return dbType.ToUpperInvariant() switch
        {
            "POSTGRESQL" or "POSTGRES" => await InspectPostgresAsync(conn, cancellationToken),
            "MYSQL" or "MARIADB" => await InspectMySqlAsync(conn, cancellationToken),
            "MSSQL" or "SQLSERVER" => await InspectMssqlAsync(conn, cancellationToken),
            _ => Unknown(dbType),
        };
    }

    private static DbPrivilegeReport Unknown(string dbType) => new(
        Username: null,
        IsSuperuser: false,
        CanWrite: false,
        CanDropObjects: false,
        Findings: new[]
        {
            new DbPrivilegeFinding("info",
                $"Privileges could not be checked for {dbType}. This is not a statement that the " +
                "connection is safe — it means we did not look."),
        },
        Recommendation: null);

    // ── PostgreSQL ───────────────────────────────────────────────────────────

    private static async Task<DbPrivilegeReport> InspectPostgresAsync(DbConnection conn, CancellationToken ct)
    {
        var username = await ScalarAsync<string>(conn, "SELECT current_user", ct);
        var isSuper = await ScalarAsync<bool>(conn, "SELECT usesuper FROM pg_user WHERE usename = current_user", ct);
        var canCreateDb = await ScalarAsync<bool>(conn, "SELECT usecreatedb FROM pg_user WHERE usename = current_user", ct);

        // Süper kullanıcı zaten her şeyi yapabilir; tek tek sormak yanıltıcı olurdu.
        var canWrite = isSuper;
        var canDrop = isSuper;

        if (!isSuper)
        {
            // Sahiplik = DROP/ALTER yetkisi. Postgres'te DROP için ayrı bir
            // "has_table_privilege" yok, sahiplik üzerinden bakılır.
            canDrop = await ScalarAsync<bool>(conn, """
                SELECT EXISTS (
                    SELECT 1 FROM pg_tables
                    WHERE schemaname NOT IN ('pg_catalog','information_schema')
                      AND tableowner = current_user)
                """, ct);

            canWrite = await ScalarAsync<bool>(conn, """
                SELECT EXISTS (
                    SELECT 1 FROM pg_tables
                    WHERE schemaname NOT IN ('pg_catalog','information_schema')
                      AND (has_table_privilege(quote_ident(schemaname)||'.'||quote_ident(tablename), 'INSERT')
                        OR has_table_privilege(quote_ident(schemaname)||'.'||quote_ident(tablename), 'UPDATE')
                        OR has_table_privilege(quote_ident(schemaname)||'.'||quote_ident(tablename), 'DELETE')))
                """, ct);
        }

        return Build(username, isSuper, canWrite, canDrop, extra: canCreateDb && !isSuper
            ? new DbPrivilegeFinding("medium", "This user can create new databases.")
            : null);
    }

    // ── MySQL / MariaDB ──────────────────────────────────────────────────────

    private static async Task<DbPrivilegeReport> InspectMySqlAsync(DbConnection conn, CancellationToken ct)
    {
        var username = await ScalarAsync<string>(conn, "SELECT CURRENT_USER()", ct);

        var grants = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SHOW GRANTS FOR CURRENT_USER()";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                grants.Add(reader.GetString(0).ToUpperInvariant());
        }

        // "ALL PRIVILEGES ... WITH GRANT OPTION" fiilen yöneticidir.
        var isSuper = grants.Any(g => g.Contains("ALL PRIVILEGES") || g.Contains("SUPER"));
        var canWrite = isSuper || grants.Any(g =>
            g.Contains("INSERT") || g.Contains("UPDATE") || g.Contains("DELETE"));
        var canDrop = isSuper || grants.Any(g => g.Contains("DROP"));

        return Build(username, isSuper, canWrite, canDrop);
    }

    // ── SQL Server ───────────────────────────────────────────────────────────

    private static async Task<DbPrivilegeReport> InspectMssqlAsync(DbConnection conn, CancellationToken ct)
    {
        var username = await ScalarAsync<string>(conn, "SELECT SUSER_SNAME()", ct);
        var isSuper = await ScalarAsync<bool>(conn, "SELECT IS_SRVROLEMEMBER('sysadmin')", ct);

        var canWrite = isSuper || await ScalarAsync<bool>(conn, """
            SELECT CASE WHEN IS_MEMBER('db_owner') = 1
                          OR IS_MEMBER('db_datawriter') = 1 THEN 1 ELSE 0 END
            """, ct);

        var canDrop = isSuper || await ScalarAsync<bool>(conn, """
            SELECT CASE WHEN IS_MEMBER('db_owner') = 1
                          OR IS_MEMBER('db_ddladmin') = 1 THEN 1 ELSE 0 END
            """, ct);

        return Build(username, isSuper, canWrite, canDrop);
    }

    // ── Rapor ────────────────────────────────────────────────────────────────

    private static DbPrivilegeReport Build(
        string? username, bool isSuper, bool canWrite, bool canDrop, DbPrivilegeFinding? extra = null)
    {
        var findings = new List<DbPrivilegeFinding>();

        if (isSuper)
            findings.Add(new DbPrivilegeFinding("high",
                "This connection uses a superuser/administrator account. It can read, change and " +
                "destroy anything in the database, including data Namines never touches."));
        else if (canDrop)
            findings.Add(new DbPrivilegeFinding("high",
                "This user can DROP or ALTER tables. A mistaken migration run with this " +
                "connection could destroy data."));
        else if (canWrite)
            findings.Add(new DbPrivilegeFinding("medium",
                "This user can INSERT, UPDATE and DELETE rows."));
        else
            findings.Add(new DbPrivilegeFinding("info",
                "This user appears to be read-only. That is the safest way to connect."));

        if (extra is not null) findings.Add(extra);

        // Öneri yalnızca yapılacak bir şey varken verilir. Salt-okunur bir bağlantıya
        // "daha dar bir rol kullanın" demek, uyarıyı gürültüye çevirirdi.
        var recommendation = (isSuper || canDrop)
            ? "Namines only needs SELECT to read your schema and browse data. Consider connecting " +
              "with a read-only role, and using a separate, narrower account when you actually " +
              "apply a migration."
            : null;

        return new DbPrivilegeReport(username, isSuper, canWrite, canDrop, findings, recommendation);
    }

    private static async Task<T> ScalarAsync<T>(DbConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync(ct);

        if (value is null || value is DBNull) return default!;
        if (typeof(T) == typeof(bool)) return (T)(object)Convert.ToBoolean(value);
        if (typeof(T) == typeof(string)) return (T)(object)Convert.ToString(value)!;
        return (T)Convert.ChangeType(value, typeof(T));
    }
}
