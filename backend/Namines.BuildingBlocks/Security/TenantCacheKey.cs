using System;

namespace Namines.BuildingBlocks.Security;

/// <summary>
/// Çok kiracılı (multi-tenant) önbellek anahtarı üreticisi (Kritik Kontrol #1: Tenant Isolation).
///
/// <b>Kural:</b> Kullanıcı veya projeye özel hiçbir veri kiracı/kullanıcı bağlamı olmadan
/// önbelleğe yazılamaz. Anahtar bileşenlerindeki `:` ayraçları temizlenir ki anahtar
/// çakışması (key collision/injection) yapılamasın.
/// </summary>
public static class TenantCacheKey
{
    public static string ForUser(string userId, string area, string key)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("Tenant cache key requires a non-empty userId.", nameof(userId));

        return $"nmn:u:{Sanitize(userId)}:{Sanitize(area)}:{Sanitize(key)}";
    }

    public static string ForProject(string projectId, string area, string key, string? organizationId = null)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Tenant cache key requires a non-empty projectId.", nameof(projectId));

        var orgSegment = string.IsNullOrWhiteSpace(organizationId) ? "default" : Sanitize(organizationId);
        return $"nmn:org:{orgSegment}:p:{Sanitize(projectId)}:{Sanitize(area)}:{Sanitize(key)}";
    }

    public static string ForOrganization(string organizationId, string area, string key)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
            throw new ArgumentException("Tenant cache key requires a non-empty organizationId.", nameof(organizationId));

        return $"nmn:org:{Sanitize(organizationId)}:{Sanitize(area)}:{Sanitize(key)}";
    }

    public static string Global(string area, string key) =>
        $"nmn:global:{Sanitize(area)}:{Sanitize(key)}";

    private static string Sanitize(string segment) =>
        string.IsNullOrWhiteSpace(segment)
            ? "empty"
            : segment.Trim().Replace(':', '_');
}
