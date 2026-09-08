using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Namines.Core.Analysis;
using Namines.Core.Enums;
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

    /// <remarks>
    /// <c>internal</c> -> <c>public</c>: Namines.API'deki bağlantı kaydetme ucu
    /// (GatewayKeyController) host'u SSRF politikasına sormadan saklamamalı.
    /// Saf bir yardımcı, durum tutmuyor — genişletmenin riski yok.
    /// </remarks>
    public static string ExtractHost(string cs, string dbType)
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
                CASE WHEN fk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_FK,
                c.COLUMN_DEFAULT,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE
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
                CASE WHEN fk.column_name IS NOT NULL THEN 1 ELSE 0 END AS IS_FK,
                c.column_default AS COLUMN_DEFAULT,
                c.numeric_precision AS NUMERIC_PRECISION,
                c.numeric_scale AS NUMERIC_SCALE
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

        // `information_schema.constraint_column_usage` yerine pg_catalog: BİLEŞİK
        // yabancı anahtarlarda information_schema kaynak/hedef kolon eşleşmesinin
        // SIRASINI garanti etmiyor (iki kolonlu bir FK'da kolonlar ters eşleşebilir).
        // `unnest(...) WITH ORDINALITY` ile conkey/confkey aynı sırayla eşleniyor.
        //
        // confdeltype/confupdtype tek karakter: a=NO ACTION r=RESTRICT c=CASCADE
        // n=SET NULL d=SET DEFAULT — okunabilir metne burada çevriliyor ki
        // `ParseReferentialAction` tüm motorlarda aynı sözlüğü kullansın.
        const string relationSql = """
            SELECT
                src.relname AS source_table,
                sa.attname  AS source_column,
                tgt.relname AS target_table,
                ta.attname  AS target_column,
                CASE con.confdeltype WHEN 'c' THEN 'CASCADE' WHEN 'n' THEN 'SET NULL'
                     WHEN 'd' THEN 'SET DEFAULT' WHEN 'r' THEN 'RESTRICT' ELSE 'NO ACTION' END AS on_delete,
                CASE con.confupdtype WHEN 'c' THEN 'CASCADE' WHEN 'n' THEN 'SET NULL'
                     WHEN 'd' THEN 'SET DEFAULT' WHEN 'r' THEN 'RESTRICT' ELSE 'NO ACTION' END AS on_update
            FROM pg_constraint con
            JOIN pg_class     src ON src.oid = con.conrelid
            JOIN pg_class     tgt ON tgt.oid = con.confrelid
            JOIN pg_namespace ns  ON ns.oid  = src.relnamespace
            JOIN LATERAL unnest(con.conkey)  WITH ORDINALITY AS sk(attnum, ord) ON TRUE
            JOIN LATERAL unnest(con.confkey) WITH ORDINALITY AS tk(attnum, ord) ON tk.ord = sk.ord
            JOIN pg_attribute sa ON sa.attrelid = con.conrelid  AND sa.attnum = sk.attnum
            JOIN pg_attribute ta ON ta.attrelid = con.confrelid AND ta.attnum = tk.attnum
            WHERE con.contype = 'f' AND ns.nspname = 'public'
            ORDER BY src.relname, sk.ord
            """;

        return await BuildSchemaAsync(conn, sql, conn.Database, ct, relationSql, LoadPostgresConstraintsAsync);
    }

    /// <summary>
    /// UNIQUE / CHECK kısıtlarını ve index'leri okur.
    ///
    /// <b>Neden ayrı ve neden yalnızca PostgreSQL:</b> bu bilgi kolon sorgusunda yok;
    /// üstelik her motorun katalog yapısı farklı. İlişkilerde de aynı kademeli yol
    /// izlenmişti — desteklenmeyen motorda liste boş kalır, uydurma kısıt üretilmez.
    ///
    /// Kısıtı destekleyen index'ler (PK/UNIQUE'in arkasındaki örtük index) DIŞARIDA
    /// bırakılıyor: aksi hâlde aynı kısıt hem UNIQUE hem CREATE INDEX olarak iki kez
    /// üretilir ve DDL "relation already exists" ile patlar.
    /// </summary>
    private static async Task LoadPostgresConstraintsAsync(
        DbConnection conn,
        Dictionary<string, SchemaTable> tables,
        CancellationToken ct)
    {
        const string constraintSql = """
            SELECT rel.relname   AS table_name,
                   con.contype   AS kind,
                   con.conname   AS constraint_name,
                   pg_get_constraintdef(con.oid) AS definition,
                   COALESCE(
                       (SELECT string_agg(att.attname, ',' ORDER BY k.ord)
                        FROM unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
                        JOIN pg_attribute att
                          ON att.attrelid = con.conrelid AND att.attnum = k.attnum),
                       '') AS column_names
            FROM pg_constraint con
            JOIN pg_class     rel ON rel.oid = con.conrelid
            JOIN pg_namespace ns  ON ns.oid  = rel.relnamespace
            WHERE ns.nspname = 'public' AND con.contype IN ('u', 'c')
            ORDER BY rel.relname, con.conname
            """;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = constraintSql;
            cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
            while (await reader.ReadAsync(ct))
            {
                var tableName = reader.GetString(0);
                var kind      = reader.GetValue(1)?.ToString();
                var name      = reader.GetString(2);
                var definition = reader.GetString(3);
                var columns   = reader.GetString(4);

                if (!tables.TryGetValue(tableName, out var table)) continue;

                if (kind == "u")
                {
                    var columnIds = ResolveColumnIds(table, columns);
                    if (columnIds.Count == 0) continue;

                    table.Uniques.Add(new SchemaUnique
                    {
                        Id         = Guid.NewGuid().ToString(),
                        StableUuid = SchemaIdentity.ForTable($"{tableName}.{name}"),
                        Name       = name,
                        ColumnIds  = columnIds,
                    });
                }
                else if (kind == "c")
                {
                    // "CHECK ((views >= 0))" → "(views >= 0)"; üreticiler CHECK sözcüğünü
                    // kendileri yazıyor, iki kez yazmak geçersiz DDL olurdu.
                    const string prefix = "CHECK ";
                    var expression = definition.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        ? definition[prefix.Length..].Trim()
                        : definition;

                    table.Checks.Add(new SchemaCheck
                    {
                        Id         = Guid.NewGuid().ToString(),
                        StableUuid = SchemaIdentity.ForTable($"{tableName}.{name}"),
                        Name       = name,
                        Expression = expression,
                    });
                }
            }
        }

        const string indexSql = """
            SELECT rel.relname AS table_name,
                   cls.relname AS index_name,
                   idx.indisunique AS is_unique,
                   COALESCE(
                       (SELECT string_agg(att.attname, ',' ORDER BY k.ord)
                        FROM unnest(idx.indkey::int[]) WITH ORDINALITY AS k(attnum, ord)
                        JOIN pg_attribute att
                          ON att.attrelid = idx.indrelid AND att.attnum = k.attnum),
                       '') AS column_names
            FROM pg_index idx
            JOIN pg_class     cls ON cls.oid = idx.indexrelid
            JOIN pg_class     rel ON rel.oid = idx.indrelid
            JOIN pg_namespace ns  ON ns.oid  = rel.relnamespace
            WHERE ns.nspname = 'public'
              AND NOT idx.indisprimary
              AND NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conindid = idx.indexrelid)
            ORDER BY rel.relname, cls.relname
            """;

        await using var indexCmd = conn.CreateCommand();
        indexCmd.CommandText = indexSql;
        indexCmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;

        await using var indexReader = await indexCmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
        while (await indexReader.ReadAsync(ct))
        {
            var tableName = indexReader.GetString(0);
            var indexName = indexReader.GetString(1);
            var isUnique  = indexReader.GetBoolean(2);
            var columns   = indexReader.GetString(3);

            if (!tables.TryGetValue(tableName, out var table)) continue;

            var columnIds = ResolveColumnIds(table, columns);
            if (columnIds.Count == 0) continue;

            table.Indexes.Add(new SchemaIndex
            {
                Id         = Guid.NewGuid().ToString(),
                StableUuid = SchemaIdentity.ForTable($"{tableName}.{indexName}"),
                Name       = indexName,
                IsUnique   = isUnique,
                Columns    = [.. columnIds.Select(id => new SchemaIndexColumn { ColumnId = id })],
            });
        }
    }

    /// <summary>
    /// Virgülle ayrılmış kolon adlarını tablodaki kolon Id'lerine çevirir. Bir ad
    /// eşleşmezse (ifade tabanlı index gibi) TÜM kısıt atlanır — yarım bir kısıt,
    /// hiç olmayandan daha yanıltıcıdır.
    /// </summary>
    private static List<string> ResolveColumnIds(SchemaTable table, string commaSeparatedNames)
    {
        if (string.IsNullOrWhiteSpace(commaSeparatedNames)) return [];

        var ids = new List<string>();
        foreach (var name in commaSeparatedNames.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var column = table.Columns.FirstOrDefault(
                c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (column is null) return [];
            ids.Add(column.Id);
        }
        return ids;
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
                CASE WHEN c.COLUMN_KEY = 'MUL' THEN 1 ELSE 0 END AS IS_FK,
                c.COLUMN_DEFAULT,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE
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
                CASE WHEN f.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_FK,
                -- ALL_TAB_COLUMNS.DATA_DEFAULT bir LONG kolonu ve LONG okuma
                -- sağlayıcıya göre farklı davranıyor; burada gerçek bir Oracle'a
                -- karşı doğrulanamadığı için BİLEREK boş bırakılıyor. Yanlış okuyup
                -- introspection'ın tamamını düşürmektense varsayılanı eksik
                -- bırakmak daha az zarar verir. Hassasiyet/ölçek normal kolonlar,
                -- onlar okunuyor.
                CAST(NULL AS VARCHAR2(4000)) AS COLUMN_DEFAULT,
                c.DATA_PRECISION AS NUMERIC_PRECISION,
                c.DATA_SCALE     AS NUMERIC_SCALE
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

    /// <param name="relationSql">
    /// İSTEĞE BAĞLI ikinci sorgu: yabancı anahtarların NEREYE işaret ettiğini çeker.
    /// Kolon sorgusu yalnızca "bu kolon FK mi?" (bool) bilgisini taşıyor; hedefi
    /// taşımıyor. Bu parametre verilmezse <see cref="DatabaseSchema.Relations"/>
    /// boş kalır — motor bazında kademeli açılabilsin diye null'a izin veriliyor.
    ///
    /// Beklenen kolon sırası:
    /// 0=kaynak tablo · 1=kaynak kolon · 2=hedef tablo · 3=hedef kolon
    /// 4=ON DELETE kuralı · 5=ON UPDATE kuralı (4-5 null olabilir).
    /// </param>
    /// <param name="constraintLoader">
    /// İSTEĞE BAĞLI üçüncü adım: UNIQUE/CHECK kısıtlarını ve index'leri doldurur.
    /// Verilmezse bu listeler boş kalır (motor bazında kademeli açılabilsin diye).
    /// </param>
    private static async Task<DatabaseSchema> BuildSchemaAsync(
        DbConnection conn,
        string sql,
        string schemaName,
        CancellationToken ct,
        string? relationSql = null,
        Func<DbConnection, Dictionary<string, SchemaTable>, CancellationToken, Task>? constraintLoader = null)
    {
        var tables = new Dictionary<string, SchemaTable>(StringComparer.OrdinalIgnoreCase);

        // Okuyucu BLOK İÇİNDE: ilişki sorgusu aynı bağlantıda çalışacak, açık bir
        // DataReader varken ikinci komut çalıştırmak (MARS kapalıyken) hata verir.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;

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

                // 7-9 opsiyonel: eski çağrılar (test sahteleri dahil) 7 kolon döndürebilir.
                var rawDefault = reader.FieldCount > 7 && !reader.IsDBNull(7)
                    ? reader.GetValue(7)?.ToString()
                    : null;
                var precision  = reader.FieldCount > 8 && !reader.IsDBNull(8)
                    ? Convert.ToInt32(reader.GetValue(8))
                    : (int?)null;
                var scale      = reader.FieldCount > 9 && !reader.IsDBNull(9)
                    ? Convert.ToInt32(reader.GetValue(9))
                    : (int?)null;

                var canonicalType = NormalizeType(dataType);

                // DECIMAL/NUMERIC'te uzunluk CHARACTER_MAXIMUM_LENGTH'te değil
                // NUMERIC_PRECISION'da durur. Ölçeği 0 olan bir sayı zaten tam sayıdır;
                // "(10,0)" yazmak gürültü olurdu, o yüzden yalnızca >0 taşınıyor.
                var isNumeric = canonicalType is "DECIMAL";
                var length = isNumeric ? (maxLen ?? precision) : maxLen;
                var columnScale = isNumeric && scale is > 0 ? scale : null;

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
                    Type         = canonicalType,
                    Length       = length,
                    Scale        = columnScale,
                    DefaultValue = NormalizeDefault(rawDefault),
                    IsPK       = columnKey == "PRI",
                    IsFK       = isFk,
                    IsNullable = nullable.Equals("YES", StringComparison.OrdinalIgnoreCase)
                              || nullable.Equals("Y",   StringComparison.OrdinalIgnoreCase),
                });
            }
        }

        var relations = relationSql is null
            ? []
            : await LoadRelationsAsync(conn, relationSql, tables, ct);

        if (constraintLoader is not null)
            await constraintLoader(conn, tables, ct);

        return new DatabaseSchema
        {
            Name      = schemaName,
            Tables    = [.. tables.Values],
            Relations = relations,
        };
    }

    /// <summary>
    /// FK'ları <see cref="SchemaRelation"/> listesine çevirir.
    ///
    /// <b>Neden ayrı bir sorgu:</b> kolon sorgusu satır başına BİR kolon döndürüyor,
    /// FK hedefi ise ilişki başına bir kayıt — tek sorguda birleştirmek her kolonu
    /// FK sayısı kadar çoğaltırdı.
    ///
    /// <see cref="SchemaRelation"/> ad değil <b>Id</b> referansı tutuyor, o yüzden
    /// isimden yukarıda üretilmiş Guid'lere eşleme yapılıyor. Eşleşmeyen kayıt
    /// (ör. başka şemadaki bir tabloya FK) sessizce atlanır: uydurma bir ilişki
    /// üretmek, ilişkiyi hiç göstermemekten daha kötüdür.
    /// </summary>
    private static async Task<List<SchemaRelation>> LoadRelationsAsync(
        DbConnection conn,
        string relationSql,
        Dictionary<string, SchemaTable> tables,
        CancellationToken ct)
    {
        var relations = new List<SchemaRelation>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = relationSql;
        cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
        while (await reader.ReadAsync(ct))
        {
            var srcTable = reader.GetString(0);
            var srcCol   = reader.GetString(1);
            var tgtTable = reader.GetString(2);
            var tgtCol   = reader.GetString(3);
            var onDelete = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetString(4) : null;
            var onUpdate = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : null;

            if (!tables.TryGetValue(srcTable, out var source)) continue;
            if (!tables.TryGetValue(tgtTable, out var target)) continue;

            var sourceColumn = source.Columns.FirstOrDefault(
                c => string.Equals(c.Name, srcCol, StringComparison.OrdinalIgnoreCase));
            var targetColumn = target.Columns.FirstOrDefault(
                c => string.Equals(c.Name, tgtCol, StringComparison.OrdinalIgnoreCase));
            if (sourceColumn is null || targetColumn is null) continue;

            relations.Add(new SchemaRelation
            {
                Id             = Guid.NewGuid().ToString(),
                // Canvas'ın beklediği biçim (bkz. schemaToFlow.ts): FK taşıyan taraf
                // "çok", işaret edilen taraf "bir".
                Type           = "OneToMany",
                SourceTableId  = source.Id,
                SourceColumnId = sourceColumn.Id,
                TargetTableId  = target.Id,
                TargetColumnId = targetColumn.Id,
                OnDelete       = ParseReferentialAction(onDelete),
                OnUpdate       = ParseReferentialAction(onUpdate),
            });
        }

        return relations;
    }

    /// <summary>
    /// Motorların döndürdüğü kural metnini enum'a çevirir. TANIMADIĞI DEĞER
    /// <see cref="ReferentialAction.NoAction"/>'a düşer — tahminle CASCADE üretmek
    /// veri kaybettirebilirdi (G3'teki "düşüş yönü asla CASCADE'e doğru olmaz" kuralı).
    /// </summary>
    private static ReferentialAction ParseReferentialAction(string? rule) =>
        rule?.Trim().ToUpperInvariant() switch
        {
            "CASCADE"     => ReferentialAction.Cascade,
            "SET NULL"    => ReferentialAction.SetNull,
            "SET DEFAULT" => ReferentialAction.SetDefault,
            "RESTRICT"    => ReferentialAction.Restrict,
            _             => ReferentialAction.NoAction,
        };

    // ── Varsayılan değer normalleştirme ───────────────────────────────────────

    /// <summary>
    /// Motorların döndürdüğü ham varsayılanı, yeniden derlenebilir bir ifadeye çevirir.
    ///
    /// <b>Neden gerekli:</b> ham değer motora göre süsleniyor — PostgreSQL
    /// <c>'pending'::character varying</c>, SQL Server <c>((0))</c> döndürür. Bunları
    /// olduğu gibi DDL'e yazmak başka bir motorda çalışmaz.
    ///
    /// <b>Otomatik artan atlanır:</b> PostgreSQL'de <c>nextval('..._seq'::regclass)</c>
    /// bir varsayılan değil, SERIAL'in kendisidir; taşımak <c>SERIAL DEFAULT nextval(...)</c>
    /// gibi kendini tekrar eden ve var olmayan bir diziye işaret eden DDL üretirdi.
    /// Aynı gerekçe SQL Server <c>IDENTITY</c> ve MySQL <c>AUTO_INCREMENT</c> için de geçerli.
    /// </summary>
    internal static string? NormalizeDefault(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var value = raw.Trim();

        if (value.Contains("nextval(", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("AUTO_INCREMENT", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("IDENTITY", StringComparison.OrdinalIgnoreCase))
            return null;

        // SQL Server varsayılanları parantezle sarar: ((0)) → 0, (getdate()) → getdate().
        // Fonksiyon çağrısının kendi parantezini yemesin diye yalnızca DIŞ sarmalayıcı
        // parantezler soyuluyor.
        while (value.Length > 2 && value[0] == '(' && value[^1] == ')' && IsWrappingPair(value))
            value = value[1..^1].Trim();

        // PostgreSQL tip niteleyicisi: 'pending'::character varying → 'pending'
        var castIndex = value.IndexOf("::", StringComparison.Ordinal);
        if (castIndex > 0) value = value[..castIndex].Trim();

        return value.Length == 0 ? null : value;
    }

    /// <summary>İlk '(' gerçekten son ')' ile mi eşleşiyor — "(a)+(b)" yanlışlıkla soyulmasın.</summary>
    private static bool IsWrappingPair(string value)
    {
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')')
            {
                depth--;
                if (depth == 0) return i == value.Length - 1;
            }
        }
        return false;
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
