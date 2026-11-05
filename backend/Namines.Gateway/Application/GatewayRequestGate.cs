using System.Net;
using Microsoft.Extensions.Configuration;
using Namines.BuildingBlocks.Application;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Security;
using Namines.Core.Analysis;
using Namines.Core.Models;
using Namines.Core.Models.Auth;
using Namines.Gateway.Domain;

namespace Namines.Gateway.Application;

/// <summary>
/// Gateway isteğini yapan taraf. Ham anahtar başlığı, oturum kimliği ve ağ bilgisi; HTTP'den bağımsız.
/// <paramref name="RemoteIpTrustworthy"/>: ters vekil yapılandırılmışsa uzak adres gerçek istemcidir.
/// </summary>
public sealed record GatewayCaller(
    string? RawKey,
    bool SessionAuthenticated,
    string? SessionUserId,
    string? Origin,
    IPAddress? RemoteIp,
    bool RemoteIpTrustworthy)
{
    /// <summary>Başlık hiç yoksa ya da boşsa oturum yolu.</summary>
    public bool HasKey => !string.IsNullOrWhiteSpace(RawKey);
}

public enum GatewayDenial
{
    None,
    /// <summary>401, gövdesiz: ne anahtar ne oturum.</summary>
    Unauthenticated,
    /// <summary>401, mesajlı: geçersiz, süresi dolmuş ya da iptal anahtar.</summary>
    InvalidCredentials,
    Forbidden,
    NotFound,
    RateLimited,
    BadRequest,
}

/// <summary>Kapının kararı. Reddedilince <see cref="Message"/> istemciye gider; kabulde <see cref="Key"/> anahtar yolunda dolu.</summary>
public sealed record GatewayDecision(GatewayDenial Denial, string? Message, GatewayApiKey? Key)
{
    public bool Allowed => Denial == GatewayDenial.None;

    internal static GatewayDecision Allow(GatewayApiKey? key) => new(GatewayDenial.None, null, key);
    internal static GatewayDecision Deny(GatewayDenial denial, string? message = null) => new(denial, message, null);
}

/// <summary>Anahtarın bir tablodaki etkin hakkı: anahtarın genel yazma yetkisi tablo iznini daraltır.</summary>
public sealed record GatewayTableGrant(string TableName, bool CanRead, bool CanWrite);

/// <summary>Kapının ölçüm çıkışı; sayaç Infrastructure'da, modül ona bağlanmasın diye arayüz.</summary>
public interface IGatewayMetrics
{
    void Request(string operation, string outcome);
}

internal sealed class NoGatewayMetrics : IGatewayMetrics
{
    public void Request(string operation, string outcome) { }
}

/// <summary>
/// Veri düzleminin tek kapısı (denetim M-1): anahtar doğrulama, köken ve IP listesi, hız sınırı, tablo izni,
/// dışa aktarım ve ham SQL hakkı, maskeleme. Controller kararı yalnızca HTTP'ye çevirir; aynı kontrolü üç
/// ayrı kopyada yazmak, birine eklenen kuralın diğerinde unutulması demekti.
/// </summary>
public interface IGatewayRequestGate
{
    /// <summary>
    /// Tablo isteği. Anahtar yolunda başarı kullanım ve son kullanım kaydı yazar. <paramref name="expands"/>
    /// ayrı tablo okur, ayrı izin ister; hız sınırı ve faturalama bir kez sayılır. <paramref name="export"/>
    /// toplu indirme hakkını da ister.
    /// </summary>
    Task<GatewayDecision> AuthorizeTableAsync(
        GatewayCaller caller, string tableName, bool forWrite,
        IReadOnlyList<GatewayExpand>? expands = null, bool export = false, CancellationToken ct = default);

    /// <summary>Ham SQL. <paramref name="readOnly"/> false ise anahtarın yazma hakkı da gerekir; null denetlemez.</summary>
    Task<GatewayDecision> AuthorizeSqlAsync(GatewayCaller caller, bool? readOnly, CancellationToken ct = default);

    /// <summary>
    /// Projeye kayıtlı bağlantıda ham SQL: anahtar yolunda proje izni, oturum yolunda Owner ve proje izni.
    /// Çağıran kendi bağlantı dizesini verdiyse kapı uygulanmaz: zaten kendi veritabanına erişiyor.
    /// </summary>
    Task<GatewayDecision> AuthorizeStoredConnectionSqlAsync(
        GatewayCaller caller, GatewayApiKey? key, string? requestConnectionString, string? requestProjectId, CancellationToken ct = default);

    /// <summary>Oturum yolunda projeye erişim (Desk): okuma için görme, yazma için düzenleme.</summary>
    Task<GatewayDecision> AuthorizeSessionProjectAsync(GatewayCaller caller, string projectId, bool forWrite, CancellationToken ct = default);

    /// <summary>
    /// <c>schema</c>, <c>tables</c>, <c>openapi.json</c>: anahtar varsa köken, IP ve hız sınırı uygulanır.
    /// Geçersiz anahtar oturum yolu sayılır (bugünkü davranış), anahtar null döner.
    /// </summary>
    Task<GatewayDecision> ResolveMetadataKeyAsync(GatewayCaller caller, CancellationToken ct = default);

    /// <summary>Anahtarın okuyabildiği tablolar; <paramref name="readableOnly"/> false ise tüm izin satırları.</summary>
    Task<IReadOnlyList<GatewayTableGrant>> TableGrantsAsync(GatewayApiKey key, bool readableOnly, CancellationToken ct = default);

    /// <summary>
    /// PII maskeleme, yalnızca anahtar yolunda: oturumda kullanıcı bağlantı dizesini kendisi giriyor, maskelemek
    /// güvenlik değil. Gizli anahtar proje başına: iki projenin çıktısı eşleştirilip kimlik çözülemesin.
    /// Hassas kolon okunduğunda denetim kaydı yazılır.
    /// </summary>
    Task<IReadOnlyList<GatewayRow>> MaskAsync(GatewayApiKey? key, string? actorUserId, string tableName, IReadOnlyList<GatewayRow> rows, CancellationToken ct = default);

    /// <summary><c>expand</c> ile gömülen satırlar KENDİ tablolarının kuralıyla: aksi hâlde maske ilişkinin öbür ucundan aşılırdı.</summary>
    Task<IReadOnlyList<GatewayRow>> MaskExpandedAsync(GatewayApiKey? key, string? actorUserId, IReadOnlyList<GatewayExpand>? expands, IReadOnlyList<GatewayRow> rows, CancellationToken ct = default);
}

internal sealed class GatewayRequestGate : IGatewayRequestGate
{
    private const string InvalidKeyMessage = "Invalid, expired or revoked API key.";

    private readonly IGatewayAccess _access;
    private readonly IGatewayAuditLog _audit;
    private readonly IUsageMeter _usage;
    private readonly ITenantProjectAuthority _authority;
    private readonly IStudioApplicationService _studio;
    private readonly IGatewayMetrics _metrics;
    private readonly string _maskingRoot;

    public GatewayRequestGate(
        IGatewayAccess access, IGatewayAuditLog audit, IUsageMeter usage, ITenantProjectAuthority authority,
        IStudioApplicationService studio, IGatewayMetrics metrics, IConfiguration configuration)
    {
        _access = access;
        _audit = audit;
        _usage = usage;
        _authority = authority;
        _studio = studio;
        _metrics = metrics;
        // Maskeleme anahtarı sunucu gizlisinden türetilir: ayrı sır yönetimi gerekmesin, projeler arasında da ortak olmasın.
        _maskingRoot = configuration["Jwt:Key"] ?? "namines-masking-fallback";
    }

    /// <summary>Anahtar doğrulama, sonra kaynak kısıtları ve hız sınırı. Kısıtı geçemeyene hangi tabloyu istediği bilgisi verilmez.</summary>
    private async Task<GatewayDecision> AuthenticateKeyAsync(GatewayCaller caller, CancellationToken ct)
    {
        var key = await _access.AuthenticateAsync(caller.RawKey, ct);
        if (key is null) return GatewayDecision.Deny(GatewayDenial.InvalidCredentials, InvalidKeyMessage);
        return await RestrictAsync(key, caller);
    }

    private static async Task<GatewayDecision> RestrictAsync(GatewayApiKey key, GatewayCaller caller)
    {
        if (!GatewayKeyRestrictions.IsOriginAllowed(key, caller.Origin ?? string.Empty))
            return GatewayDecision.Deny(GatewayDenial.Forbidden, "This key is not allowed from this origin.");
        if (!GatewayKeyRestrictions.IsIpAllowed(key, caller.RemoteIp, caller.RemoteIpTrustworthy, out var ipReason))
            return GatewayDecision.Deny(GatewayDenial.Forbidden, ipReason);
        if (!await GatewayRateLimiter.TryAcquireAsync(key))
            return GatewayDecision.Deny(GatewayDenial.RateLimited, $"This key is limited to {key.RateLimitPerMinute} requests per minute.");
        return GatewayDecision.Allow(key);
    }

    private static GatewayDecision Session(GatewayCaller caller) =>
        caller.SessionAuthenticated ? GatewayDecision.Allow(null) : GatewayDecision.Deny(GatewayDenial.Unauthenticated);

    public async Task<GatewayDecision> AuthorizeTableAsync(
        GatewayCaller caller, string tableName, bool forWrite,
        IReadOnlyList<GatewayExpand>? expands = null, bool export = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var decision = caller.HasKey ? await AuthenticateKeyAsync(caller, ct) : Session(caller);
        if (!decision.Allowed) return decision;
        var key = decision.Key;

        if (key is not null)
        {
            // 403 ve 404 arasında bilinçli tercih: "tablo yok" demek hangi tabloların var olduğunu sızdırırdı.
            if (!await _access.IsTableAllowedAsync(key, tableName, forWrite, ct))
            {
                // Reddedilenler hatadan ayrı sayılır: eksik izin yapılandırması "sistem bozuk" gibi görünmesin.
                _metrics.Request(forWrite ? "write" : "read", "denied");
                return GatewayDecision.Deny(GatewayDenial.Forbidden, forWrite
                    ? $"This API key is not allowed to write to '{tableName}'."
                    : $"This API key is not allowed to read '{tableName}'.");
            }

            await _access.TouchLastUsedAsync(key, ct);
            _metrics.Request(forWrite ? "write" : "read", "ok");
            // İsteği yapan anonim bir uygulama, faturayı ödeyen anahtarı üreten hesap.
            await _usage.RecordAsync(key.CreatedByUserId!, UsageResource.ApiRequest, 1, tableName, ct);
        }

        // expand ayrı tablo okur, ayrı izin ister (docs/last-review H-2). Hız sınırı ve fatura bir kez: bir isteği
        // expand sayısı kadar çarpmak güvenlik düzeltmesini sessiz bir fiyat artışına çevirirdi.
        foreach (var expand in expands ?? Array.Empty<GatewayExpand>())
        {
            if (string.IsNullOrWhiteSpace(expand.Table))
                return GatewayDecision.Deny(GatewayDenial.BadRequest, "Each expand needs a table name.");
            if (key is not null && !await _access.IsTableAllowedAsync(key, expand.Table, forWrite: false, ct))
            {
                _metrics.Request("read", "denied");
                return GatewayDecision.Deny(GatewayDenial.Forbidden, $"This API key is not allowed to read '{expand.Table}'.");
            }
        }

        // Dışa aktarım ayrı izin (F-08): sayfa sayfa okumak ile hepsini dosya olarak almak aynı yetki değil.
        // Oturum yolu muaf: proje üyesi zaten tüm veriye erişiyor.
        if (export && key is not null && !await _access.IsTableExportAllowedAsync(key, tableName, ct))
        {
            _metrics.Request("export", "denied");
            return GatewayDecision.Deny(GatewayDenial.Forbidden,
                $"This API key is not allowed to export '{tableName}'. " +
                "Bulk export is a separate grant from read access: reading a table " +
                "page by page and downloading all of it are different permissions.");
        }

        return decision;
    }

    public async Task<GatewayDecision> AuthorizeSqlAsync(GatewayCaller caller, bool? readOnly, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!caller.HasKey) return Session(caller);

        var decision = await AuthenticateKeyAsync(caller, ct);
        if (!decision.Allowed) return decision;
        var key = decision.Key!;

        if (!key.CanExecuteSql)
        {
            _metrics.Request("sql", "denied");
            return GatewayDecision.Deny(GatewayDenial.Forbidden,
                "This API key is not allowed to run raw SQL. Raw SQL bypasses table " +
                "permissions, so it is a separate grant from write access.");
        }

        _metrics.Request("sql", "ok");
        await _usage.RecordAsync(key.CreatedByUserId!, UsageResource.ApiRequest, 1, "sql", ct);

        // Ham SQL hakkı "her şeyi oku" demek, "her şeyi değiştir" değil.
        if (readOnly == false && !key.CanWrite)
            return GatewayDecision.Deny(GatewayDenial.Forbidden, "This API key may run read-only SQL only.");

        return decision;
    }

    public async Task<GatewayDecision> AuthorizeStoredConnectionSqlAsync(
        GatewayCaller caller, GatewayApiKey? key, string? requestConnectionString, string? requestProjectId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!string.IsNullOrWhiteSpace(requestConnectionString)) return GatewayDecision.Allow(key);

        if (key is null)
        {
            if (string.IsNullOrWhiteSpace(requestProjectId)) return GatewayDecision.Allow(null);

            // Oturum + kayıtlı bağlantı: "kullanıcının zaten DB erişimi var" öncülü geçersiz, erişimi bu istek veriyor.
            // Desk SQL konsoluyla aynı eşik: yalnızca Owner, yalnızca proje açıkça izin verdiyse.
            if (string.IsNullOrEmpty(caller.SessionUserId)) return GatewayDecision.Deny(GatewayDenial.Unauthenticated);
            var role = await _authority.GetRoleAsync(requestProjectId, caller.SessionUserId, ct);
            // Proje hiç görünmüyorsa 404; görünüyor ama Owner değilse 403 ve açık sebep.
            if (role is null) return GatewayDecision.Deny(GatewayDenial.NotFound, "Project not found.");
            if (role != OrgRole.Owner)
                return GatewayDecision.Deny(GatewayDenial.Forbidden,
                    "Only the project Owner can run raw SQL against the stored connection. " +
                    "Send your own connection string, or use the Desk SQL console.");
            if (!(await _studio.GetConnectionMetadataAsync(requestProjectId, ct)).AllowDeskSql)
                return GatewayDecision.Deny(GatewayDenial.Forbidden, "The SQL console is not enabled for this project yet.");
            return GatewayDecision.Allow(null);
        }

        // Proje ya da bağlantı satırı yoksa da reddedilir: "kayıt yok"u izin saymak fail-open olurdu.
        if ((await _studio.GetConnectionMetadataAsync(key.ProjectId, ct)).AllowDeskSql) return GatewayDecision.Allow(key);

        _metrics.Request("sql", "denied");
        return GatewayDecision.Deny(GatewayDenial.Forbidden,
            "Raw SQL against this project's stored connection is turned off. " +
            "The project Owner can enable it in the project settings.");
    }

    public async Task<GatewayDecision> AuthorizeSessionProjectAsync(GatewayCaller caller, string projectId, bool forWrite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (string.IsNullOrEmpty(caller.SessionUserId)) return GatewayDecision.Deny(GatewayDenial.Unauthenticated);

        var hasAccess = forWrite
            ? await _authority.CanEditAsync(projectId, caller.SessionUserId, ct)
            : await _authority.CanViewAsync(projectId, caller.SessionUserId, ct);
        // 403 ile "proje yok" ayrılmaz: hangi kimliklerin var olduğu sızmasın.
        return hasAccess ? GatewayDecision.Allow(null) : GatewayDecision.Deny(GatewayDenial.Forbidden, "You do not have access to this project.");
    }

    public async Task<GatewayDecision> ResolveMetadataKeyAsync(GatewayCaller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.RawKey is null) return GatewayDecision.Allow(null);
        var key = await _access.AuthenticateAsync(caller.RawKey, ct);
        if (key is null) return GatewayDecision.Allow(null);
        // Yalnız kimlik doğrulamak yetmez: IP'ye kilitli bir anahtar sızarsa canlı şema her adresten okunurdu.
        return await RestrictAsync(key, caller);
    }

    public async Task<IReadOnlyList<GatewayTableGrant>> TableGrantsAsync(GatewayApiKey key, bool readableOnly, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var permissions = readableOnly
            ? await _access.ReadableTablesAsync(key.ProjectId, ct)
            : await _access.ListPermissionsAsync(key.ProjectId, ct);
        // İzinli görünüp 403 alan uç olmasın: anahtarın yazma yetkisi yoksa tabloda da yazma kapalı gösterilir.
        return permissions.Select(p => new GatewayTableGrant(p.TableName, p.CanRead, p.CanWrite && key.CanWrite)).ToList();
    }

    public async Task<IReadOnlyList<GatewayRow>> MaskAsync(GatewayApiKey? key, string? actorUserId, string tableName, IReadOnlyList<GatewayRow> rows, CancellationToken ct = default)
    {
        if (key is null || rows.Count == 0) return rows;

        var masked = await _access.MaskedColumnsAsync(key.ProjectId, tableName, ct);
        if (masked.Count == 0) return rows;

        // Maskeli kolonu kimin çektiği görünmeliydi: maskeyi koymanın sebebi kolonun hassas olması.
        await _audit.RecordAsync(key, actorUserId, GatewayWriteKind.Read, tableName, rowKey: null, columns: masked,
            affectedRows: rows.Count, succeeded: true, ct);

        var secret = MaskingSecret(key.ProjectId);
        var lookup = new HashSet<string>(masked, StringComparer.OrdinalIgnoreCase);
        return rows.Select(row => new GatewayRow(MaskValues(row.Values, lookup, secret))).ToList();
    }

    public async Task<IReadOnlyList<GatewayRow>> MaskExpandedAsync(GatewayApiKey? key, string? actorUserId, IReadOnlyList<GatewayExpand>? expands, IReadOnlyList<GatewayRow> rows, CancellationToken ct = default)
    {
        if (key is null || expands is null || expands.Count == 0 || rows.Count == 0) return rows;

        var secret = MaskingSecret(key.ProjectId);
        var result = rows;
        foreach (var expand in expands)
        {
            var masked = await _access.MaskedColumnsAsync(key.ProjectId, expand.Table, ct);
            if (masked.Count == 0) continue;

            var alias = string.IsNullOrWhiteSpace(expand.As) ? expand.Table : expand.As!;
            var lookup = new HashSet<string>(masked, StringComparer.OrdinalIgnoreCase);

            await _audit.RecordAsync(key, actorUserId, GatewayWriteKind.Read, expand.Table, rowKey: null, columns: masked,
                affectedRows: result.Count, succeeded: true, ct);

            result = result.Select(row =>
            {
                if (!row.Values.TryGetValue(alias, out var nested) || nested is not IReadOnlyDictionary<string, object?> embedded)
                    return row;
                var values = new Dictionary<string, object?>(row.Values) { [alias] = MaskValues(embedded, lookup, secret) };
                return new GatewayRow(values);
            }).ToList();
        }
        return result;
    }

    private string MaskingSecret(string projectId) => _maskingRoot + ":mask:" + projectId;

    /// <summary>
    /// Tek satırın hassas kolonları. Ana satır ve gömülü satır aynı dönüşümü kullanır: iki kopyada yeni bir tip
    /// yalnız birine eklenir, gömülü satırlar sessizce maskesiz kalırdı (docs/last-review H-2).
    /// </summary>
    internal static Dictionary<string, object?> MaskValues(IReadOnlyDictionary<string, object?> source, HashSet<string> lookup, string secret)
    {
        var values = new Dictionary<string, object?>(source);
        foreach (var column in values.Keys.ToList())
        {
            if (!lookup.Contains(column)) continue;
            values[column] = values[column] switch
            {
                null => null,
                long l => PiiMasker.MaskNumber(l, secret),
                int i => PiiMasker.MaskNumber(i, secret),
                // Tanınmayan tip metne çevrilip maskelenir: "tanımadım" diye geçirmek korumayı tek tiple delerdi.
                var other => PiiMasker.Mask(other.ToString(), secret),
            };
        }
        return values;
    }
}
