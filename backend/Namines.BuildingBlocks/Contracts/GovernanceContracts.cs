using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models.Auth;

namespace Namines.BuildingBlocks.Contracts;

/// <summary>Kullanıcının özellik başına model tercihi ve gelişmiş ayar JSON'u (Governance'ın verisi).</summary>
public sealed record AiPolicySnapshot(
    AIMode SmartSeed,
    AIMode Documentation,
    AIMode Scaffolding,
    AIMode SchemaGeneration,
    AIMode SchemaRevision,
    AIMode DbaAnalysis,
    AIMode Migration,
    AIMode Voice,
    string? AdvancedJson)
{
    /// <summary>Hiç dokunulmamış kullanıcının varsayılanı: ucuz model ağırlıklı, şema üretimi dengeli.</summary>
    public static AiPolicySnapshot Default { get; } = new(
        AIMode.Low, AIMode.Low, AIMode.Low, AIMode.Medium, AIMode.Low, AIMode.Low, AIMode.Low, AIMode.Low, null);
}

/// <summary>
/// Kullanıcının AI politikası: hangi özellik hangi modeli kullanır. Politika kararı Governance'ın; AI çağıranlar
/// (middleware, sağlayıcı katmanı) yalnızca bu sözleşmeden okur.
/// </summary>
public interface IAiPolicies
{
    /// <summary>Kayıt yoksa null: çağıran "varsayılan" mı "ilk kez oluştur" mu diyeceğine kendisi karar verir.</summary>
    Task<AiPolicySnapshot?> FindAsync(string userId, CancellationToken ct = default);

    /// <summary>Yoksa varsayılanlarla oluşturur (kayıt ve eski hesaplar); eşzamanlı iki çağrı aynı satırda buluşur.</summary>
    Task<AiPolicySnapshot> EnsureAsync(string userId, CancellationToken ct = default);

    /// <summary><paramref name="advancedJson"/> null ise gelişmiş ayarlara dokunulmaz (eski istemci ayarı sıfırlamasın).</summary>
    Task SaveAsync(string userId, AiPolicySnapshot modes, string? advancedJson, CancellationToken ct = default);

    /// <summary>
    /// Tercih JSON'u olan her satırı <paramref name="transform"/> ile dener; null dönerse satır olduğu gibi kalır.
    /// Değişen satır sayısını döndürür (açılış düzeltmesi).
    /// </summary>
    Task<int> RewriteAdvancedJsonAsync(Func<string, string?> transform, CancellationToken ct = default);
}

/// <summary>Değiştirilemez denetim satırı: yaşam döngüsü olayları (silme, hesap kapatma) için.</summary>
public sealed record GovernanceAuditEntry(
    string SubjectId,
    string ProjectId,
    string? Title,
    ChangeRequestAuditAction Action,
    string? ActorUserId,
    string? Details);

/// <summary>
/// Yalnızca-ekleme denetim yazıcısı. Satırlar sonradan değiştirilemez (veritabanı tetikleyicisi); sonuçlar yeni
/// satır olarak eklenir. Çağıranın işlemine katılır (<see cref="Namines.BuildingBlocks.Persistence.ICrossModuleTransaction"/>).
/// </summary>
public interface IGovernanceAudit
{
    Task<string> AppendAsync(GovernanceAuditEntry entry, CancellationToken ct = default);
}

/// <summary>Silme ve saklama süresi işleri; Governance verisine tek kapı.</summary>
public interface IGovernanceMaintenance
{
    Task<int> CountChangeRequestsAsync(string projectId, CancellationToken ct = default);

    /// <summary>Projenin değişiklik istekleri ve oyları gider; denetim satırları KALIR (kanıt, FK yok).</summary>
    Task<int> DeleteProjectChangeRequestsAsync(string projectId, CancellationToken ct = default);

    /// <summary>
    /// Saklama süresi dolan denetim satırlarını siler. Satırlar tetikleyiciyle korunur; yalnızca bu yol silme
    /// iznini işlem başına açar.
    /// </summary>
    Task<int> PurgeAuditAsync(DateTime cutoffUtc, TimeSpan pause, CancellationToken ct = default);

    /// <summary>
    /// Hesap silme: politika satırı gider; oy, istek ve denetim satırlarındaki kullanıcı kimliği boşaltılır (satırlar kalır:
    /// "kim onayladı" kanıtı kişisiz de anlamlı). Çağıranın işleminde.
    /// </summary>
    Task<int> ErasePersonalDataAsync(string userId, CancellationToken ct = default);
}

/// <summary>Yönetim panelinin proje başına değişiklik isteği özeti.</summary>
public sealed record ProjectChangeRequestFlags(int Total, bool HasActiveMigrations);

public interface IGovernanceAdminQueries
{
    Task<ProjectChangeRequestFlags> GetProjectFlagsAsync(string projectId, CancellationToken ct = default);
}
