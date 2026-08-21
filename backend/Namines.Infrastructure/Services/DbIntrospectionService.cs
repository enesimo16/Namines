using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Namines.Core.Analysis;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Security;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Canlı bir veritabanına bağlanır, INFORMATION_SCHEMA'yı sorgular ve
/// Namines şemasına dönüştürür.
///
/// GÜVENLİK: Her bağlantı kurulmadan önce host SSRF guard'dan geçirilir.
/// Credentials asla loglanmaz; connection açıkken timeout uygulanır.
/// </summary>
public sealed class DbIntrospectionService : IDbIntrospectionService
{
    private const int ConnectTimeoutSeconds = 10;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger<DbIntrospectionService> _logger;
    private readonly IDbHostAccessPolicy _hostPolicy;

    public DbIntrospectionService(ILogger<DbIntrospectionService> logger, IDbHostAccessPolicy hostPolicy)
    {
        _logger = logger;
        _hostPolicy = hostPolicy;
    }

    public async Task<DatabaseSchema> IntrospectAsync(
        string connectionString,
        string dbType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbType);

        var host = ExtractHost(connectionString, dbType);
        if (!_hostPolicy.IsHostAllowed(host, out var denyReason))
            throw new InvalidOperationException(denyReason);

        return dbType.ToUpperInvariant() switch
        {
            "MSSQL" or "SQLSERVER" => await IntrospectMssqlAsync(connectionString, cancellationToken),
            "POSTGRESQL" or "POSTGRES" => await IntrospectPostgresAsync(connectionString, cancellationToken),
            "MYSQL" => await IntrospectMySqlAsync(connectionString, cancellationToken),
            "MARIADB" => await IntrospectMySqlAsync(connectionString, cancellationToken),
            "ORACLE" => await IntrospectOracleAsync(connectionString, cancellationToken),
            _ => throw new NotSupportedException($"Database type '{dbType}' is not supported for live introspection."),
        };
    }

    // ── SSRF: host extraction ─────────────────────────────────────────────────
    // internal: GatewayService de SSRF guard'ından önce aynı host-çıkarma mantığına
    // ihtiyaç duyuyor — kopyalamak yerine burayı paylaşıyor, tek bir yerde düzeltilsin.

    internal static string ExtractHost(string cs, string dbType)
    {
        // Anahtar-değer çiftlerinden host/server/data source değerini çıkar.
        // Her sağlayıcının farklı anahtar isimleri olduğu için regex ile eşleştir.
        var patterns = new[]
        {
            @"(?:^|;)\s*(?:server|host|data\s*source|datasource)\s*=\s*([^;,]+)",
        };

        foreach (var pattern in patterns)
        {
            var m = Regex.Match(cs, pattern, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var raw = m.Groups[1].Value.Trim();
                // MSSQL: "host,port" → "host"
                // PostgreSQL: "host:port" → "host"
                // Oracle EZConnect: "host:port/SID" → "host"
                return raw.Split(',', ':', '/')[0].Trim();
            }
        }

        return string.Empty;
    }

    // ── SQL Server ────────────────────────────────────────────────────────────

    private async Task<DatabaseSchema> IntrospectMssqlAsync(string cs, CancellationToken ct)
    {
        // Sertleştirme (zaman aşımı, TLS, salt-okunur oturum) tek kapıdan —
        // introspection ASLA yazmaz, o yüzden readOnly her zaman true.
        await using var conn = await UserDbConnection.OpenAsync(cs, "MSSQL", readOnly: true, ct);

        const string sql = """
            SELECT
                t.TABLE_NAME,
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.IS_NULLABLE,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 'PRI' ELSE '' END AS COLUMN_KEY,
                CASE WHEN fk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_FK
            FROM INFORMATION_SCHEMA.TABLES t
            JOIN INFORMATION_SCHEMA.COLUMNS c
                ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME
            LEFT JOIN (
                SELECT ku.TABLE_NAME, ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ) pk ON pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
            LEFT JOIN (
                SELECT ku.TABLE_NAME, ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY'
            ) fk ON fk.TABLE_NAME = c.TABLE_NAME AND fk.COLUMN_NAME = c.COLUMN_NAME
            WHERE t.TABLE_TYPE = 'BASE TABLE' AND t.TABLE_SCHEMA = SCHEMA_NAME()
            ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION
            """;

        return await BuildSchemaAsync(conn, sql, conn.Database, ct);
    }

    // ── PostgreSQL ────────────────────────────────────────────────────────────

    private async Task<DatabaseSchema> IntrospectPostgresAsync(string cs, CancellationToken ct)
    {
        await using var conn = await UserDbConnection.OpenAsync(cs, "PostgreSQL", readOnly: true, ct);

        const string sql = """
            SELECT
                c.table_name   AS TABLE_NAME,
                c.column_name  AS COLUMN_NAME,
                c.udt_name     AS DATA_TYPE,
                c.character_maximum_length AS CHARACTER_MAXIMUM_LENGTH,
                c.is_nullable  AS IS_NULLABLE,
                CASE WHEN pk.column_name IS NOT NULL THEN 'PRI' ELSE '' END AS COLUMN_KEY,
                CASE WHEN fk.column_name IS NOT NULL THEN 1 ELSE 0 END AS IS_FK
            FROM information_schema.columns c
            LEFT JOIN (
                SELECT ku.table_name, ku.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage ku
                    ON tc.constraint_name = ku.constraint_name AND tc.table_schema = ku.table_schema
                WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = 'public'
            ) pk ON pk.table_name = c.table_name AND pk.column_name = c.column_name
            LEFT JOIN (
                SELECT ku.table_name, ku.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage ku
                    ON tc.constraint_name = ku.constraint_name AND tc.table_schema = ku.table_schema
                WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = 'public'
            ) fk ON fk.table_name = c.table_name AND fk.column_name = c.column_name
            WHERE c.table_schema = 'public'
            ORDER BY c.table_name, c.ordinal_position
            """;

        return await BuildSchemaAsync(conn, sql, conn.Database, ct);
    }

    // ── MySQL / MariaDB ───────────────────────────────────────────────────────

    private async Task<DatabaseSchema> IntrospectMySqlAsync(string cs, CancellationToken ct)
    {
        await using var conn = await UserDbConnection.OpenAsync(cs, "MySQL", readOnly: true, ct);

        const string sql = """
            SELECT
                c.TABLE_NAME,
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.IS_NULLABLE,
                CASE WHEN c.COLUMN_KEY = 'PRI' THEN 'PRI' ELSE '' END AS COLUMN_KEY,
                CASE WHEN c.COLUMN_KEY = 'MUL' THEN 1 ELSE 0 END AS IS_FK
            FROM information_schema.COLUMNS c
            JOIN information_schema.TABLES t
                ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
            WHERE c.TABLE_SCHEMA = DATABASE()
              AND t.TABLE_TYPE = 'BASE TABLE'
            ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION
            """;

        return await BuildSchemaAsync(conn, sql, conn.Database, ct);
    }

    // ── Oracle ────────────────────────────────────────────────────────────────

    private async Task<DatabaseSchema> IntrospectOracleAsync(string cs, CancellationToken ct)
    {
        await using var conn = await UserDbConnection.OpenAsync(cs, "Oracle", readOnly: true, ct);

        const string sql = """
            SELECT
                c.TABLE_NAME,
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.CHAR_LENGTH  AS CHARACTER_MAXIMUM_LENGTH,
                c.NULLABLE     AS IS_NULLABLE,
                CASE WHEN p.COLUMN_NAME IS NOT NULL THEN 'PRI' ELSE '' END AS COLUMN_KEY,
                CASE WHEN f.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_FK
            FROM ALL_TAB_COLUMNS c
            JOIN ALL_OBJECTS o
                ON o.OBJECT_NAME = c.TABLE_NAME AND o.OWNER = c.OWNER AND o.OBJECT_TYPE = 'TABLE'
            LEFT JOIN (
                SELECT ac.TABLE_NAME, acc.COLUMN_NAME
                FROM ALL_CONSTRAINTS ac
                JOIN ALL_CONS_COLUMNS acc
                    ON ac.CONSTRAINT_NAME = acc.CONSTRAINT_NAME AND ac.OWNER = acc.OWNER
                WHERE ac.CONSTRAINT_TYPE = 'P' AND ac.OWNER = SYS_CONTEXT('USERENV','CURRENT_SCHEMA')
            ) p ON p.TABLE_NAME = c.TABLE_NAME AND p.COLUMN_NAME = c.COLUMN_NAME
            LEFT JOIN (
                SELECT ac.TABLE_NAME, acc.COLUMN_NAME
                FROM ALL_CONSTRAINTS ac
                JOIN ALL_CONS_COLUMNS acc
                    ON ac.CONSTRAINT_NAME = acc.CONSTRAINT_NAME AND ac.OWNER = acc.OWNER
                WHERE ac.CONSTRAINT_TYPE = 'R' AND ac.OWNER = SYS_CONTEXT('USERENV','CURRENT_SCHEMA')
            ) f ON f.TABLE_NAME = c.TABLE_NAME AND f.COLUMN_NAME = c.COLUMN_NAME
            WHERE c.OWNER = SYS_CONTEXT('USERENV','CURRENT_SCHEMA')
            ORDER BY c.TABLE_NAME, c.COLUMN_ID
            """;

        return await BuildSchemaAsync(conn, sql, conn.DataSource, ct);
    }

    // ── Shared result builder ─────────────────────────────────────────────────

    private static async Task<DatabaseSchema> BuildSchemaAsync(
        DbConnection conn,
        string sql,
        string schemaName,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;

        var tables = new Dictionary<string, SchemaTable>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
        while (await reader.ReadAsync(ct))
        {
            var tableName  = reader.GetString(0);
            var columnName = reader.GetString(1);
            var dataType   = reader.GetString(2);
            var maxLen     = reader.IsDBNull(3) ? (int?)null : Convert.ToInt32(reader.GetValue(3));
            var nullable   = reader.GetString(4);
            var columnKey  = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
            var isFk       = !reader.IsDBNull(6) && Convert.ToInt32(reader.GetValue(6)) == 1;

            if (!tables.TryGetValue(tableName, out var table))
            {
                table = new SchemaTable
                {
                    Id         = Guid.NewGuid().ToString(),
                    Name       = tableName,
                    StableUuid = SchemaIdentity.ForTable(tableName),
                };
                tables[tableName] = table;
            }

            table.Columns.Add(new SchemaColumn
            {
                Id         = Guid.NewGuid().ToString(),
                Name       = columnName,
                StableUuid = SchemaIdentity.ForColumn(tableName, columnName),
                Type       = NormalizeType(dataType),
                Length     = maxLen,
                IsPK       = columnKey == "PRI",
                IsFK       = isFk,
                IsNullable = nullable.Equals("YES", StringComparison.OrdinalIgnoreCase)
                          || nullable.Equals("Y",   StringComparison.OrdinalIgnoreCase),
            });
        }

        return new DatabaseSchema
        {
            Name   = schemaName,
            Tables = [.. tables.Values],
        };
    }

    // ── Tip normalleştirme ────────────────────────────────────────────────────
    // Ham DB tipini (varchar, int4, NUMBER vb.) frontend'in gösterdiği kısa
    // canonical forma dönüştürür.

    private static string NormalizeType(string raw) => raw.ToLowerInvariant() switch
    {
        "int" or "int4" or "integer" or "number" => "INT",
        "bigint" or "int8" => "BIGINT",
        "smallint" or "int2" => "SMALLINT",
        "tinyint" => "TINYINT",
        "float" or "float4" or "float8" or "double" or "double precision" => "FLOAT",
        "numeric" or "decimal" => "DECIMAL",
        "bool" or "boolean" or "bit" => "BOOLEAN",
        "char" or "bpchar" => "CHAR",
        "varchar" or "varchar2" or "nvarchar" or "character varying" => "VARCHAR",
        "text" or "clob" or "ntext" => "TEXT",
        "date" => "DATE",
        "time" or "timetz" => "TIME",
        "timestamp" or "timestamptz" or "datetime" or "datetime2" or "smalldatetime" => "TIMESTAMP",
        "json" or "jsonb" => "JSON",
        "uuid" or "uniqueidentifier" => "UUID",
        "blob" or "bytea" or "varbinary" or "binary" or "image" => "BLOB",
        _ => raw.ToUpperInvariant(),
    };
}
