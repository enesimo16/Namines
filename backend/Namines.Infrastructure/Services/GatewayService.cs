using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Security;
using Npgsql;
using NpgsqlTypes;
using Oracle.ManagedDataAccess.Client;

namespace Namines.Infrastructure.Services;

/// <summary>
/// G14 — Minimal Gateway. Bkz. Namines.Core/Models/GatewayModels.cs sınıf yorumu.
///
/// Faz B/08 ile yazma yolu eklendi (INSERT/UPDATE/DELETE). Tablo/kolon adları asla ham metin olarak
/// SQL'e eklenmez: önce <see cref="IsValidIdentifier"/> ile katı bir regex'ten geçer
/// (yalnızca harf/rakam/alt çizgi, ilk karakter harf/alt çizgi), sonra motora özgü
/// quote karakterleriyle sarılır (DDL üreticilerindeki aynı Quote() deseni — bkz.
/// MssqlDdlGenerator/PostgresDdlGenerator vb. private Quote metotları). Değer tarafı
/// (WHERE) her zaman parametreli — asla string interpolation değil.
///
/// SSRF: DbIntrospectionService ile AYNI <see cref="SsrfGuard"/> kullanılır — bu
/// yüzden localhost/private IP aralıklarına bağlanamaz (bilinçli, gevşetilmez).
/// Bu da bu servisin canlı-bağlantı yolunun yerel Docker'a karşı test edilemediği
/// anlamına gelir — DbIntrospectionService'in de aynı, önceden var olan sınırı
/// (bkz. GatewayServiceTests.cs sınıf yorumu, testler yalnızca SQL-üretim mantığını
/// kanıtlıyor, canlı bağlantıyı değil).
/// </summary>
public sealed class GatewayService : IGatewayService
{
    private readonly IDbHostAccessPolicy _hostPolicy;

    public GatewayService(IDbHostAccessPolicy hostPolicy)
    {
        _hostPolicy = hostPolicy;
    }

    private const int ConnectTimeoutSeconds = 10;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);
    private static readonly Regex IdentifierPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public async Task<GatewayListResult> ListAsync(
        string connectionString, string dbType, string tableName,
        int page, int pageSize, string? orderByColumn = null,
        bool includeTotalCount = true,
        GatewaySortDirection sortDirection = GatewaySortDirection.Asc,
        IReadOnlyList<GatewayFilter>? filters = null,
        IReadOnlyList<GatewayFilterGroup>? orGroups = null,
        IReadOnlyList<string>? selectColumns = null,
        IReadOnlyList<GatewayExpand>? expands = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifierOrThrow(tableName, nameof(tableName));
        if (orderByColumn is not null) ValidateIdentifierOrThrow(orderByColumn, nameof(orderByColumn));
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200); // sınırsız sayfa boyutu = kaza ile tüm tabloyu dökme riski

        await using var conn = await OpenGuardedConnectionAsync(connectionString, dbType, readOnly: true, cancellationToken);

        // COUNT(*) büyük tabloda tam tarama — her sayfa gezinmesinde tekrarlamak yerine
        // yalnızca istendiğinde (ilk yükleme) hesaplanır. -1 = "önceki değeri koru".
        // Filtreler COUNT'a da uygulanır: aksi hâlde sayfalama çubuğu, filtrelenmiş
        // listeyle çelişen bir toplam gösterirdi.
        var totalCount = includeTotalCount
            ? await CountAsync(conn, dbType, tableName, filters, orGroups, cancellationToken)
            : -1L;

        var (sql, bind) = BuildListSql(
            dbType, tableName, page, pageSize, orderByColumn, sortDirection,
            filters, orGroups, selectColumns);
        await using var cmd = CreateCommand(conn, dbType);
        cmd.CommandText = sql;
        cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;
        bind(cmd);

        var rows = new List<GatewayRow>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(ReadRow(reader));
        }

        await ApplyExpandsAsync(conn, dbType, rows, expands, cancellationToken);

        return new GatewayListResult(rows, page, pageSize, totalCount);
    }

    /// <summary>08 §5: sorgu maliyeti tavanı. Aşan istek kırpılmaz, REDDEDİLİR —
    /// sessizce kırpılmış bir dışa aktarım, eksik olduğunu söylemeyen bir dosyadır.</summary>
    internal const int MaxExportRows = 10_000;

    public async Task<IReadOnlyList<GatewayRow>> ExportAsync(
        string connectionString, string dbType, string tableName,
        int maxRows,
        string? orderByColumn = null,
        GatewaySortDirection sortDirection = GatewaySortDirection.Asc,
        IReadOnlyList<GatewayFilter>? filters = null,
        IReadOnlyList<GatewayFilterGroup>? orGroups = null,
        IReadOnlyList<string>? selectColumns = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifierOrThrow(tableName, nameof(tableName));
        if (orderByColumn is not null) ValidateIdentifierOrThrow(orderByColumn, nameof(orderByColumn));

        if (maxRows <= 0) maxRows = MaxExportRows;
        if (maxRows > MaxExportRows)
            throw new ArgumentException(
                $"Export is capped at {MaxExportRows} rows. Narrow the filter, or page through /list instead.");

        // Dışa aktarım OKUMA yoludur: salt-okunur oturum (06 §5).
        await using var conn = await OpenGuardedConnectionAsync(connectionString, dbType, readOnly: true, cancellationToken);

        // Tek sayfa olarak, tavan + 1 satır iste: fazladan gelen satır "sonuç
        // kesildi" demektir ve bunu sessizce yutmak yerine hata veriyoruz.
        var (sql, bind) = BuildListSql(
            dbType, tableName, page: 1, pageSize: maxRows + 1, orderByColumn, sortDirection,
            filters, orGroups, selectColumns);

        await using var cmd = CreateCommand(conn, dbType);
        cmd.CommandText = sql;
        cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;
        bind(cmd);

        var rows = new List<GatewayRow>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(ReadRow(reader));
        }

        if (rows.Count > maxRows)
            throw new ArgumentException(
                $"This query matches more than {maxRows} rows. Narrow the filter, or page through /list instead.");

        return rows;
    }

    public async Task<GatewayRow?> DetailAsync(
        string connectionString, string dbType, string tableName,
        string pkColumn, string pkValue, CancellationToken cancellationToken = default)
    {
        ValidateIdentifierOrThrow(tableName, nameof(tableName));
        ValidateIdentifierOrThrow(pkColumn, nameof(pkColumn));

        await using var conn = await OpenGuardedConnectionAsync(connectionString, dbType, readOnly: true, cancellationToken);

        var sql = BuildDetailSql(dbType, tableName, pkColumn);
        await using var cmd = CreateCommand(conn, dbType);
        cmd.CommandText = sql;
        cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;
        AddParameter(cmd, dbType, "pkvalue", pkValue);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRow(reader) : null;
    }

    // ── Yazma yolu (Faz B/08) ────────────────────────────────────────────────

    public async Task<GatewayWriteResult> CreateAsync(
        string connectionString, string dbType, string tableName,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifierOrThrow(tableName, nameof(tableName));
        var columns = ValidateColumns(values);

        await using var conn = await OpenGuardedConnectionAsync(connectionString, dbType, readOnly: false, cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await using var cmd = CreateCommand(conn, dbType);
        cmd.Transaction = tx;
        cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;
        cmd.CommandText = BuildInsertSql(dbType, tableName, columns);
        foreach (var column in columns) AddParameter(cmd, dbType, ParameterNameFor(column), values[column]);

        GatewayRow? row = null;
        var affected = 0;

        if (SupportsReturning(dbType))
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                row = ReadRow(reader);
                affected = 1;
            }
        }
        else
        {
            affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return new GatewayWriteResult(affected, row);
    }

    public async Task<GatewayWriteResult> UpdateAsync(
        string connectionString, string dbType, string tableName,
        string pkColumn, string pkValue,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifierOrThrow(tableName, nameof(tableName));
        ValidateIdentifierOrThrow(pkColumn, nameof(pkColumn));
        var columns = ValidateColumns(values);

        await using var conn = await OpenGuardedConnectionAsync(connectionString, dbType, readOnly: false, cancellationToken);

        return await ExecuteGuardedWriteAsync(conn, dbType, cancellationToken, cmd =>
        {
            cmd.CommandText = BuildUpdateSql(dbType, tableName, pkColumn, columns);
            foreach (var column in columns) AddParameter(cmd, dbType, ParameterNameFor(column), values[column]);
            AddParameter(cmd, dbType, "pkvalue", pkValue);
        });
    }

    public async Task<GatewayWriteResult> DeleteAsync(
        string connectionString, string dbType, string tableName,
        string pkColumn, string pkValue,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifierOrThrow(tableName, nameof(tableName));
        ValidateIdentifierOrThrow(pkColumn, nameof(pkColumn));

        await using var conn = await OpenGuardedConnectionAsync(connectionString, dbType, readOnly: false, cancellationToken);

        return await ExecuteGuardedWriteAsync(conn, dbType, cancellationToken, cmd =>
        {
            cmd.CommandText = BuildDeleteSql(dbType, tableName, pkColumn);
            AddParameter(cmd, dbType, "pkvalue", pkValue);
        });
    }

    /// <summary>
    /// Tekil anahtarla hedeflenen yazmaların ORTAK KORUMASI.
    ///
    /// İşlem içinde çalıştırır ve etkilenen satır sayısını doğrular: 1'den fazlaysa
    /// GERİ ALIR. Sebebi somut — çağıran "birincil anahtar" dediği kolonun gerçekten
    /// benzersiz olduğunu varsayıyor; değilse tek bir istek sessizce onlarca satırı
    /// değiştirir ya da siler. Bu durumu fark etmenin tek anı, işlem hâlâ açıkken
    /// burasıdır; commit'ten sonra geri dönüş yoktur.
    ///
    /// 0 satır bir hata DEĞİLDİR (kayıt yok) — commit edilir, çağıran 404 döndürür.
    /// </summary>
    // ── Toplu yazma, fonksiyon çağrısı ve ham sorgu (08 §2) ──────────────────

    internal const int MaxImportRows = 10_000;
    internal const int MaxQueryRows = 10_000;
    internal const int MaxRpcArguments = 32;

    public async Task<GatewayImportResult> ImportAsync(
        string connectionString, string dbType, string tableName,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifierOrThrow(tableName, nameof(tableName));

        if (rows is null || rows.Count == 0)
            throw new ArgumentException("Import needs at least one row.");

        if (rows.Count > MaxImportRows)
            throw new ArgumentException(
                $"Import is capped at {MaxImportRows} rows per request. Split the file and send it in batches.");

        // Kolon kümesi İLK satırdan alınıp diğerlerinde AYNISI aranıyor. Satır başına
        // farklı kolonlara izin vermek, eksik kolonu olan satıra sessizce varsayılan
        // (ya da NULL) yazardı — içe aktarımın en sık gözden kaçan hatası budur.
        var columns = ValidateColumns(rows[0]);

        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i].Count != columns.Count || !columns.All(rows[i].ContainsKey))
                throw new ArgumentException(
                    $"Row {i + 1} has a different set of columns than the first row. " +
                    "Every row in one import must describe the same columns.");
        }

        await using var conn = await OpenGuardedConnectionAsync(connectionString, dbType, readOnly: false, cancellationToken);

        // TEK işlem: yarım kalan bir içe aktarım, çağıranın hangi satırların
        // yazıldığını bilememesi ve aynı dosyayı tekrar denediğinde yinelenen kayıt
        // üretmesi demektir.
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        var inserted = 0;
        try
        {
            var sql = BuildInsertSql(dbType, tableName, columns, includeReturning: false);

            foreach (var row in rows)
            {
                await using var cmd = CreateCommand(conn, dbType);
                cmd.Transaction = tx;
                cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;
                cmd.CommandText = sql;
                foreach (var column in columns)
                    AddParameter(cmd, dbType, ParameterNameFor(column), row[column]);

                inserted += await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        await tx.CommitAsync(cancellationToken);
        return new GatewayImportResult(inserted);
    }

    public async Task<GatewayQueryResult> RpcAsync(
        string connectionString, string dbType, string functionName,
        IReadOnlyList<string?> arguments,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifierOrThrow(functionName, nameof(functionName));

        arguments ??= Array.Empty<string?>();
        if (arguments.Count > MaxRpcArguments)
            throw new ArgumentException($"At most {MaxRpcArguments} arguments are supported.");

        var sql = BuildRpcSql(dbType, functionName, arguments.Count);

        // Fonksiyon YAZABİLİR; salt-okunur oturum, geçerli bir çağrıyı anlaşılmaz bir
        // hatayla düşürürdü. Yetki kontrolü çağıran katmanda (anahtarın yazma izni).
        await using var conn = await OpenGuardedConnectionAsync(connectionString, dbType, readOnly: false, cancellationToken);

        await using var cmd = CreateCommand(conn, dbType);
        cmd.CommandText = sql;
        cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;
        for (var i = 0; i < arguments.Count; i++)
            AddParameter(cmd, dbType, "a" + i.ToString(CultureInfo.InvariantCulture), arguments[i]);

        return await ReadResultAsync(cmd, cancellationToken);
    }

    public async Task<GatewayQueryResult> QueryAsync(
        string connectionString, string dbType, string sql,
        bool readOnly = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL is required.");

        EnsureSingleStatement(sql);

        await using var conn = await OpenGuardedConnectionAsync(connectionString, dbType, readOnly, cancellationToken);

        await using var cmd = CreateCommand(conn, dbType);
        cmd.CommandText = sql;
        cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;

        return await ReadResultAsync(cmd, cancellationToken);
    }

    /// <summary>
    /// Zincirlenmiş ifadeleri reddeder.
    ///
    /// <c>SELECT 1; DROP TABLE users</c> tek bir istekte iki iş yapar ve incelenen
    /// sorgunun yanına ikinci bir sorgu iliştirmenin klasik yoludur. Dize sonundaki
    /// tek bir noktalı virgül zararsız, o yüzden yalnızca ARDINDAN kod gelen
    /// noktalı virgül reddediliyor.
    /// </summary>
    internal static void EnsureSingleStatement(string sql)
    {
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];

            // Dize ve tanımlayıcı sınırlayıcıları atlanmalı: 'a;b' geçerli bir
            // değerdir ve içindeki noktalı virgül ifade sonu DEĞİLDİR.
            if (c == '\'' && !inDouble) inSingle = !inSingle;
            else if (c == '"' && !inSingle) inDouble = !inDouble;
            else if (c == ';' && !inSingle && !inDouble)
            {
                if (!string.IsNullOrWhiteSpace(sql[(i + 1)..]))
                    throw new ArgumentException(
                        "Only one statement is allowed per request. Send chained statements separately.");
            }
        }
    }

    /// <summary>
    /// Satır döndüren ve döndürmeyen ifadeleri tek yerde okur.
    ///
    /// <see cref="GatewayQueryResult.AffectedRows"/> satır döndüren bir sorguda -1:
    /// "0 satır döndü" ile "0 satır etkilendi" farklı şeylerdir ve tek bir alanda
    /// birleştirmek bu farkı yok ederdi.
    /// </summary>
    private static async Task<GatewayQueryResult> ReadResultAsync(DbCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (reader.FieldCount == 0)
            return new GatewayQueryResult(Array.Empty<GatewayRow>(), reader.RecordsAffected, false);

        var rows = new List<GatewayRow>();
        var truncated = false;

        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= MaxQueryRows)
            {
                // Sessizce kesilen bir sonuç, çağıranın eksik veriyi tam sanması
                // demektir; kesildiği açıkça bildiriliyor.
                truncated = true;
                break;
            }
            rows.Add(ReadRow(reader));
        }

        return new GatewayQueryResult(rows, -1, truncated);
    }

    /// <summary>
    /// Motorun fonksiyon çağırma sözdizimi.
    ///
    /// Desteklenmeyen motorda <b>reddediliyor</b>: yanlış sözdizimi üretip
    /// veritabanının hata vermesini beklemek, çağırana "Namines bozuk" dedirtir.
    /// </summary>
    internal static string BuildRpcSql(string dbType, string functionName, int argumentCount)
    {
        var name = Quote(dbType, functionName);
        var engine = dbType.ToUpperInvariant();

        var placeholders = string.Join(", ",
            Enumerable.Range(0, argumentCount).Select(i => ParameterToken(engine, "a" + i.ToString(CultureInfo.InvariantCulture))));

        return engine switch
        {
            "POSTGRESQL" or "POSTGRES" => $"SELECT * FROM {name}({placeholders})",
            "MYSQL" or "MARIADB" => $"CALL {name}({placeholders})",
            "MSSQL" or "SQLSERVER" => $"EXEC {name} {placeholders}".TrimEnd(),
            _ => throw new NotSupportedException(
                $"Calling database functions is not supported for {dbType}. " +
                "Supported engines: PostgreSQL, MySQL, MariaDB, SQL Server."),
        };
    }

    private static string ParameterToken(string engine, string name) => engine switch
    {
        "ORACLE" => ":" + name,
        _ => "@" + name,
    };

    private static async Task<GatewayWriteResult> ExecuteGuardedWriteAsync(
        DbConnection conn, string dbType, CancellationToken ct, Action<DbCommand> configure)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = CreateCommand(conn, dbType);
        cmd.Transaction = tx;
        cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;
        configure(cmd);

        var affected = await cmd.ExecuteNonQueryAsync(ct);

        if (affected > 1)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"Refusing to modify {affected} rows: the key column is not unique for the given value. " +
                "The transaction was rolled back and nothing changed.");
        }

        await tx.CommitAsync(ct);
        return new GatewayWriteResult(affected, null);
    }

    /// <summary>
    /// Kolon adlarını doğrular ve KARARLI bir sıraya sokar.
    ///
    /// Sıra önemlidir: Oracle sürücüsünde parametreler koleksiyona eklenme sırasına
    /// göre de bağlanabiliyor (bkz. CreateCommand yorumu). Sözlük sırası garanti
    /// değildir; SQL metni ile bağlama sırasının aynı listeden üretilmesi, ikisinin
    /// ayrışmasını yapısal olarak imkânsız kılar.
    /// </summary>
    private static List<string> ValidateColumns(IReadOnlyDictionary<string, string?> values)
    {
        if (values is null || values.Count == 0)
            throw new ArgumentException("At least one column value is required.", nameof(values));

        var columns = new List<string>(values.Keys);
        columns.Sort(StringComparer.Ordinal);
        foreach (var column in columns) ValidateIdentifierOrThrow(column, nameof(values));
        return columns;
    }

    /// <summary>Kolon adı parametre adına dönüşür; çakışmasın diye önek taşır.</summary>
    private static string ParameterNameFor(string column) => "v_" + column;

    /// <summary>
    /// INSERT sonrası satırı TEK ifadede güvenle döndürebilen motorlar.
    ///
    /// SQL Server'ın <c>OUTPUT INSERTED.*</c>'ı bilinçli olarak KULLANILMIYOR: hedef
    /// tabloda bir trigger varsa Msg 334 ile başarısız olur, yani yazma tamamen
    /// çalışmaz hâle gelirdi. Satırı geri okuyamamak, yazmayı kıramaktan iyidir.
    /// MySQL/Oracle'da da tek ifadeli bir karşılığı yok.
    /// </summary>
    private static bool SupportsReturning(string dbType) =>
        dbType.ToUpperInvariant() is "POSTGRESQL" or "POSTGRES" or "SQLITE";

    internal static string BuildInsertSql(string dbType, string tableName, IReadOnlyList<string> columns) =>
        BuildInsertSql(dbType, tableName, columns, includeReturning: true);

    /// <param name="includeReturning">
    /// Toplu içe aktarımda false: 10.000 satırın her biri için eklenen satırı geri
    /// okumak, hiç kullanılmayacak veriyi ağdan geçirmek olurdu.
    /// </param>
    internal static string BuildInsertSql(string dbType, string tableName, IReadOnlyList<string> columns, bool includeReturning)
    {
        var prefix = dbType.Equals("ORACLE", StringComparison.OrdinalIgnoreCase) ? ":" : "@";
        var table = Quote(dbType, tableName);
        var columnList = string.Join(", ", columns.Select(c => Quote(dbType, c)));
        var valueList = string.Join(", ", columns.Select(c => prefix + ParameterNameFor(c)));
        var returning = includeReturning && SupportsReturning(dbType) ? " RETURNING *" : string.Empty;

        return $"INSERT INTO {table} ({columnList}) VALUES ({valueList}){returning}";
    }

    internal static string BuildUpdateSql(
        string dbType, string tableName, string pkColumn, IReadOnlyList<string> columns)
    {
        var prefix = dbType.Equals("ORACLE", StringComparison.OrdinalIgnoreCase) ? ":" : "@";
        var table = Quote(dbType, tableName);
        var assignments = string.Join(", ", columns.Select(c => $"{Quote(dbType, c)} = {prefix}{ParameterNameFor(c)}"));

        // WHERE kaldırılamaz: imza pkColumn/pkValue'yu zorunlu kılıyor ve burada
        // koşulsuz bir UPDATE üretmenin yolu yok. Filtresiz UPDATE tüm tabloyu ezer.
        return $"UPDATE {table} SET {assignments} WHERE {Quote(dbType, pkColumn)} = {prefix}pkvalue";
    }

    internal static string BuildDeleteSql(string dbType, string tableName, string pkColumn)
    {
        var prefix = dbType.Equals("ORACLE", StringComparison.OrdinalIgnoreCase) ? ":" : "@";
        return $"DELETE FROM {Quote(dbType, tableName)} WHERE {Quote(dbType, pkColumn)} = {prefix}pkvalue";
    }

    // ── expand: ilişki gömme (08 §2.1) ───────────────────────────────────────

    /// <summary>
    /// Her ilişki için TEK ek sorgu çalıştırır ve sonucu satırlara gömer.
    ///
    /// Satır başına sorgu (N+1) bilinçli olarak yapılmıyor: 50 satırlık bir sayfa
    /// 51 sorguya dönüşür ve gecikme sayfa boyutuyla doğrusal artar. Bunun yerine
    /// ana sorgudan gelen yabancı anahtar değerleri toplanıp tek bir <c>IN (...)</c>
    /// ile çekiliyor.
    /// </summary>
    private static async Task ApplyExpandsAsync(
        DbConnection conn, string dbType, List<GatewayRow> rows,
        IReadOnlyList<GatewayExpand>? expands, CancellationToken ct)
    {
        if (expands is null || expands.Count == 0 || rows.Count == 0) return;

        foreach (var expand in expands)
        {
            ValidateIdentifierOrThrow(expand.FromColumn, nameof(expand.FromColumn));
            ValidateIdentifierOrThrow(expand.Table, nameof(expand.Table));
            ValidateIdentifierOrThrow(expand.ToColumn, nameof(expand.ToColumn));

            var alias = string.IsNullOrWhiteSpace(expand.As) ? expand.Table : expand.As!;
            ValidateIdentifierOrThrow(alias, nameof(expand.As));

            // Null yabancı anahtarlar sorguya girmez: NULL hiçbir şeye eşit değildir,
            // IN listesine koymak yalnızca sorguyu büyütür.
            var keys = rows
                .Select(r => r.Values.TryGetValue(expand.FromColumn, out var v) ? v : null)
                .Where(v => v is not null)
                .Distinct()
                .ToList();

            if (keys.Count == 0) continue;

            var prefix = dbType.Equals("ORACLE", StringComparison.OrdinalIgnoreCase) ? ":" : "@";
            var names = keys.Select((_, i) => $"{prefix}e{i}").ToList();

            await using var cmd = CreateCommand(conn, dbType);
            cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;
            cmd.CommandText =
                $"SELECT * FROM {Quote(dbType, expand.Table)} " +
                $"WHERE {Quote(dbType, expand.ToColumn)} IN ({string.Join(", ", names)})";

            for (var i = 0; i < keys.Count; i++)
                AddParameter(cmd, dbType, $"e{i}", keys[i]);

            var related = new Dictionary<string, GatewayRow>(StringComparer.Ordinal);
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var row = ReadRow(reader);
                    if (row.Values.TryGetValue(expand.ToColumn, out var key) && key is not null)
                        // Anahtar metne çevrilerek eşleştiriliyor: motorlar aynı değeri
                        // farklı CLR tipleriyle döndürebiliyor (int vs long vs decimal)
                        // ve tip üzerinden eşleştirmek sessizce eşleşmeme üretirdi.
                        related[Convert.ToString(key, CultureInfo.InvariantCulture)!] = row;
                }
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var values = new Dictionary<string, object?>(rows[i].Values);

                if (values.TryGetValue(expand.FromColumn, out var fk) && fk is not null)
                {
                    var lookup = Convert.ToString(fk, CultureInfo.InvariantCulture)!;
                    // Eşleşme yoksa alan NULL olarak eklenir; hiç eklememek, istemcinin
                    // "alan var mı yok mu" diye ayrı bir kontrol yazmasını gerektirirdi.
                    values[alias] = related.TryGetValue(lookup, out var match) ? match.Values : null;
                }
                else
                {
                    // Yabancı anahtarı NULL olan satırda da alan bulunmalı: aynı
                    // sorgunun bazı satırlarında olup bazılarında olmayan bir alan,
                    // istemci tarafında tip belirsizliği yaratır.
                    values[alias] = null;
                }

                // GatewayRow'un sözlüğünü yerinde değiştirmek yerine satır yeniden
                // kuruluyor: IReadOnlyDictionary'yi IDictionary'ye cast etmek,
                // ReadRow'un somut tipine gizli bir bağımlılık olurdu.
                rows[i] = new GatewayRow(values);
            }
        }
    }

    // ── Kimlik doğrulama + quote (SQL injection'a karşı tek savunma hattı) ─────

    internal static void ValidateIdentifierOrThrow(string identifier, string paramName)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !IdentifierPattern.IsMatch(identifier))
            throw new ArgumentException($"'{identifier}' is not a valid SQL identifier.", paramName);
    }

    internal static string Quote(string dbType, string identifier) => dbType.ToUpperInvariant() switch
    {
        "MSSQL" or "SQLSERVER" => $"[{identifier}]",
        "MYSQL" or "MARIADB" => $"`{identifier}`",
        "POSTGRESQL" or "POSTGRES" or "ORACLE" or "SQLITE" => $"\"{identifier}\"",
        _ => throw new NotSupportedException($"Database type '{dbType}' is not supported by the Gateway."),
    };

    // ── Sayfalama SQL'i — motora göre farklı sözdizimi ──────────────────────────

    /// <summary>
    /// ORDER BY olmadan LIMIT/OFFSET sayfalaması motorlar tarafından KARARLI kabul edilmez:
    /// Postgres UPDATE/VACUUM sonrası veya paralel taramada satır sırasını değiştirebilir,
    /// bu da "sonraki sayfa"da aynı satırı tekrar gösterip başka birini hiç göstermemeye
    /// yol açar. Bu yüzden bir sıralama kolonu (genelde PK) verildiğinde ORDER BY eklenir.
    /// Verilmediğinde eski davranış korunur — çağıran (UI) kullanıcıyı uyarır.
    /// </summary>
    internal static (string Sql, Action<DbCommand> Bind) BuildListSql(
        string dbType, string tableName, int page, int pageSize, string? orderByColumn = null,
        GatewaySortDirection sortDirection = GatewaySortDirection.Asc,
        IReadOnlyList<GatewayFilter>? filters = null,
        IReadOnlyList<GatewayFilterGroup>? orGroups = null,
        IReadOnlyList<string>? selectColumns = null)
    {
        var table = Quote(dbType, tableName);
        var skip = (page - 1) * pageSize;
        var engine = dbType.ToUpperInvariant();

        var where = BuildWhere(dbType, filters, orGroups, out var bindFilters);
        var projection = BuildProjection(dbType, selectColumns);

        // Yön SQL'e ENUM'dan yazılır, kullanıcı metninden değil.
        var direction = sortDirection == GatewaySortDirection.Desc ? " DESC" : " ASC";

        // MSSQL/Oracle OFFSET-FETCH için ORDER BY zorunlu; kolon yoksa eski yer tutucu kalır.
        var order = orderByColumn is not null
            ? $" ORDER BY {Quote(dbType, orderByColumn)}{direction}"
            : (engine is "MSSQL" or "SQLSERVER" ? " ORDER BY (SELECT NULL)" : string.Empty);

        return engine switch
        {
            "MSSQL" or "SQLSERVER" => (
                $"SELECT {projection} FROM {table}{where}{order} OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY",
                (DbCommand cmd) => { bindFilters(cmd); AddParameter(cmd, dbType, "skip", skip); AddParameter(cmd, dbType, "take", pageSize); }),
            "POSTGRESQL" or "POSTGRES" or "SQLITE" => (
                $"SELECT {projection} FROM {table}{where}{order} LIMIT @take OFFSET @skip",
                (DbCommand cmd) => { bindFilters(cmd); AddParameter(cmd, dbType, "take", pageSize); AddParameter(cmd, dbType, "skip", skip); }),
            "MYSQL" or "MARIADB" => (
                $"SELECT {projection} FROM {table}{where}{order} LIMIT @skip, @take",
                (DbCommand cmd) => { bindFilters(cmd); AddParameter(cmd, dbType, "skip", skip); AddParameter(cmd, dbType, "take", pageSize); }),
            "ORACLE" => (
                $"SELECT {projection} FROM {table}{where}{order} OFFSET :skip ROWS FETCH NEXT :take ROWS ONLY",
                (DbCommand cmd) => { bindFilters(cmd); AddParameter(cmd, dbType, "skip", skip); AddParameter(cmd, dbType, "take", pageSize); }),
            _ => throw new NotSupportedException($"Database type '{dbType}' is not supported by the Gateway."),
        };
    }

    // ── Filtreleme (08 §2.1'in alt kümesi) ────────────────────────────────────

    /// <summary>
    /// WHERE cümlesi. Kolon adları doğrulanıp quote'lanır, DEĞERLER her zaman
    /// parametreye gider. Operatör bir enum olduğu için SQL'e yazılan karşılaştırma
    /// parçası da kullanıcı girdisi değildir — serbest metin bir operatör alanı,
    /// injection yüzeyini geri açardı.
    /// </summary>
    internal static string BuildWhere(
        string dbType, IReadOnlyList<GatewayFilter>? filters, out Action<DbCommand> bind) =>
        BuildWhere(dbType, filters, orGroups: null, out bind);

    internal static string BuildWhere(
        string dbType, IReadOnlyList<GatewayFilter>? filters,
        IReadOnlyList<GatewayFilterGroup>? orGroups, out Action<DbCommand> bind)
    {
        var hasFilters = filters is { Count: > 0 };
        var hasGroups = orGroups is not null && orGroups.Any(g => g.Any.Count > 0);

        if (!hasFilters && !hasGroups)
        {
            bind = _ => { };
            return string.Empty;
        }

        filters ??= Array.Empty<GatewayFilter>();

        var prefix = dbType.Equals("ORACLE", StringComparison.OrdinalIgnoreCase) ? ":" : "@";
        var clauses = new List<string>();
        var parameters = new List<(string Name, object? Value)>();
        var index = 0;

        foreach (var filter in filters)
        {
            // Boş IN listesi motorlarda sözdizimi hatasıdır; RenderFilter bunu
            // anlaşılır bir doğrulama hatasına çevirir.
            ValidateIdentifierOrThrow(filter.Column, "filter.Column");
            clauses.Add(RenderFilter(dbType, filter, prefix, parameters, ref index));
        }

        foreach (var group in orGroups ?? Array.Empty<GatewayFilterGroup>())
        {
            if (group.Any.Count == 0) continue;

            var alternatives = new List<string>();
            foreach (var filter in group.Any)
            {
                ValidateIdentifierOrThrow(filter.Column, "filter.Column");
                alternatives.Add(RenderFilter(dbType, filter, prefix, parameters, ref index));
            }

            // Parantez ŞART: OR'lu bir grup parantezsiz yazılırsa, AND'in önceliği
            // yüzünden anlam sessizce değişir ve filtre beklenenden fazla satır döndürür.
            clauses.Add("(" + string.Join(" OR ", alternatives) + ")");
        }

        bind = cmd =>
        {
            foreach (var (name, value) in parameters) AddParameter(cmd, dbType, name, value);
        };

        return clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
    }

    /// <summary>Tek bir filtrenin SQL parçası. AND listesi ve OR grupları aynı gövdeyi kullanır.</summary>
    private static string RenderFilter(
        string dbType, GatewayFilter filter, string prefix,
        List<(string Name, object? Value)> parameters, ref int index)
    {
        var column = Quote(dbType, filter.Column);

        switch (filter.Operator)
        {
            case GatewayOperator.IsNull:
                return $"{column} IS NULL";

            case GatewayOperator.IsNotNull:
                return $"{column} IS NOT NULL";

            case GatewayOperator.In:
            {
                if (filter.Values.Count == 0)
                    throw new ArgumentException($"Filter on '{filter.Column}' uses IN with no values.");

                var names = new List<string>();
                foreach (var value in filter.Values)
                {
                    var name = $"f{index++}";
                    names.Add(prefix + name);
                    parameters.Add((name, value));
                }
                return $"{column} IN ({string.Join(", ", names)})";
            }

            default:
            {
                if (filter.Values.Count == 0)
                    throw new ArgumentException($"Filter on '{filter.Column}' has no value.");

                var name = $"f{index++}";
                parameters.Add((name, filter.Values[0]));
                return $"{column} {ComparisonSql(filter.Operator)} {prefix}{name}";
            }
        }
    }

    /// <summary>
    /// Kolon projeksiyonu (08 §2.1 <c>select=</c>).
    ///
    /// Kolon adları tanımlayıcı doğrulamasından geçer ve quote'lanır; liste boşsa
    /// <c>*</c> döner. Projeksiyon yalnızca ağ trafiğini azaltmaz — istemcinin
    /// ihtiyaç duymadığı kolonları hiç okumaması, kaza ile hassas bir alanı
    /// loglara taşımasını da engeller.
    /// </summary>
    internal static string BuildProjection(string dbType, IReadOnlyList<string>? columns)
    {
        if (columns is null || columns.Count == 0) return "*";

        var quoted = new List<string>();
        foreach (var column in columns)
        {
            ValidateIdentifierOrThrow(column, "select");
            quoted.Add(Quote(dbType, column));
        }
        return string.Join(", ", quoted);
    }

    private static string ComparisonSql(GatewayOperator op) => op switch
    {
        GatewayOperator.Eq => "=",
        GatewayOperator.Neq => "<>",
        GatewayOperator.Gt => ">",
        GatewayOperator.Gte => ">=",
        GatewayOperator.Lt => "<",
        GatewayOperator.Lte => "<=",
        GatewayOperator.Like => "LIKE",
        _ => throw new NotSupportedException($"Operator '{op}' has no comparison form."),
    };

    internal static string BuildDetailSql(string dbType, string tableName, string pkColumn)
    {
        var table = Quote(dbType, tableName);
        var column = Quote(dbType, pkColumn);
        var placeholder = dbType.ToUpperInvariant() == "ORACLE" ? ":pkvalue" : "@pkvalue";
        return $"SELECT * FROM {table} WHERE {column} = {placeholder}";
    }

    internal static string BuildCountSql(
        string dbType, string tableName,
        IReadOnlyList<GatewayFilter>? filters, out Action<DbCommand> bind) =>
        BuildCountSql(dbType, tableName, filters, orGroups: null, out bind);

    internal static string BuildCountSql(
        string dbType, string tableName,
        IReadOnlyList<GatewayFilter>? filters, IReadOnlyList<GatewayFilterGroup>? orGroups,
        out Action<DbCommand> bind) =>
        $"SELECT COUNT(*) FROM {Quote(dbType, tableName)}{BuildWhere(dbType, filters, orGroups, out bind)}";

    private static void AddParameter(DbCommand cmd, string dbType, string name, object? value)
    {
        var p = cmd.CreateParameter();
        // Oracle ':name' bekler ama parametre adı yine de kolon adı gibi (iki nokta üstü olmadan) verilir.
        p.ParameterName = name;
        // null → DBNull. Doğrudan null atamak ADO.NET'te "parametre değeri atanmadı"
        // sayılır ve sürücüye göre ya hata verir ya da parametreyi düşürür; ikisi de
        // "bu kolona NULL yaz" niyetini sessizce başka bir şeye çevirirdi.
        p.Value = value ?? DBNull.Value;

        // ── PostgreSQL: metin parametreleri TİPSİZ gönderilir ────────────────
        //
        // BULUNMA YERİ: gerçek Postgres'e karşı yazılan ilk entegrasyon testi,
        // `WHERE id = @pkvalue` için "42883: operator does not exist: integer = text"
        // verdi. Gateway'in değerleri (PK'lar, filtre değerleri) HTTP'den string
        // olarak geliyor; Npgsql bunları `text` diye bildirince Postgres — diğer
        // motorların aksine — örtük dönüşüm YAPMAZ ve sorguyu reddeder.
        //
        // Bu YENİ bir hata değil: mevcut DetailAsync de aynı yoldan geçiyordu, yani
        // tamsayı birincil anahtarlı bir tabloda gateway detay ucu hiç çalışmıyordu.
        // Testler yalnızca üretilen SQL METNİNİ doğruladığı için görünmemişti.
        //
        // Çözüm ::text cast'i DEĞİL — o, kolon üzerindeki index'i kullanılamaz hâle
        // getirip her sorguyu tam taramaya çevirirdi. Bunun yerine parametre "tipsiz"
        // bildiriliyor; Postgres o zaman değeri, tıpkı sorguya yazılmış bir sabit gibi,
        // KOLONUN tipine göre çözümlüyor.
        if (p is NpgsqlParameter npgsqlParameter && value is string)
            npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Unknown;

        cmd.Parameters.Add(p);
    }

    private static async Task<long> CountAsync(
        DbConnection conn, string dbType, string tableName,
        IReadOnlyList<GatewayFilter>? filters, IReadOnlyList<GatewayFilterGroup>? orGroups,
        CancellationToken ct)
    {
        await using var cmd = CreateCommand(conn, dbType);
        cmd.CommandText = BuildCountSql(dbType, tableName, filters, orGroups, out var bind);
        cmd.CommandTimeout = (int)QueryTimeout.TotalSeconds;
        bind(cmd);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    // ODP.NET'in OracleCommand.BindByName'i VARSAYILAN OLARAK false — yani parametreler
    // isme göre değil, Parameters koleksiyonuna EKLENME SIRASINA göre bağlanır. Bugün
    // BuildListSql/BuildDetailSql'deki AddParameter çağrı sırası SQL metnindeki :skip/:take/
    // :pkvalue sırasıyla eşleştiği için bu "isabetle" doğru çalışıyor — ama bu örtük varsayımı
    // burada tek bir yerde açıkça doğru davranışa (isme göre bağlama) sabitliyoruz ki
    // ileride bir parametre eklenir/sırası değişirse sessizce yanlış değere bağlanmasın.
    private static DbCommand CreateCommand(DbConnection conn, string dbType)
    {
        var cmd = conn.CreateCommand();
        if (dbType.Equals("ORACLE", StringComparison.OrdinalIgnoreCase) && cmd is OracleCommand oracleCmd)
            oracleCmd.BindByName = true;
        return cmd;
    }

    private static GatewayRow ReadRow(DbDataReader reader)
    {
        var values = new Dictionary<string, object?>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var value = reader.GetValue(i);
            values[reader.GetName(i)] = value is DBNull ? null : value;
        }
        return new GatewayRow(values);
    }

    // ── Bağlantı açma — DbIntrospectionService ile aynı SSRF/timeout deseni ────

    /// <param name="readOnly">
    /// Okuma uçları için true. PostgreSQL ve MySQL'de oturum salt-okunura çekilir —
    /// yani liste/detay yolundaki bir SQL üretim hatası bile veri YAZAMAZ. 06 §5'in
    /// "salt-okunur mod varsayılan, yazma için açık onay" kuralı budur; yazma uçları
    /// bunu bilinçli olarak false geçer.
    /// </param>
    private async Task<DbConnection> OpenGuardedConnectionAsync(
        string connectionString, string dbType, bool readOnly, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbType);

        // DbIntrospectionService ile PAYLAŞILAN host-çıkarma mantığı — bkz. o dosyadaki
        // ExtractHost yorumu (kopyalanırsa biri güncellenip diğeri unutulabilir).
        var host = DbIntrospectionService.ExtractHost(connectionString, dbType);
        if (!_hostPolicy.IsHostAllowed(host, out var denyReason))
            throw new InvalidOperationException(denyReason);

        return await UserDbConnection.OpenAsync(connectionString, dbType, readOnly, ct);
    }
}
