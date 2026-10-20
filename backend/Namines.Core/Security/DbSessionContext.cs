using System;
using System.Collections.Generic;

namespace Namines.Core.Security;

/// <summary>
/// Çoklu kiracı (multi-tenant) veritabanı oturum bağlamı.
/// Veritabanı bağlantısı açıldığında oturum değişkenlerine (PostgreSQL set_config) aktarılır.
/// PostgreSQL RLS politikalarında 'current_setting('app.current_tenant', true)' olarak okunur.
///
/// İki katmanlı güvenlik modeli (08 §5):
/// 1. Katman (Uygulama): Gateway / API seviyesinde kiracı doğrulama.
/// 2. Katman (Veritabanı): PostgreSQL RLS motoru seviyesinde satır izolasyonu.
/// </summary>
public sealed record DbSessionContext
{
    /// <summary>Aktif kiracı ID'si (organization_id / tenant_id).</summary>
    public string? TenantId { get; init; }

    /// <summary>İsteği yapan kullanıcının ID'si.</summary>
    public string? UserId { get; init; }

    /// <summary>Kullanıcının rolü (admin, member, readonly vb.).</summary>
    public string? Role { get; init; }

    /// <summary>İsteğe bağlı ek güvenlik claim'leri.</summary>
    public IReadOnlyDictionary<string, string>? Claims { get; init; }

    public static DbSessionContext ForTenant(string tenantId, string? userId = null, string? role = null) =>
        new() { TenantId = tenantId, UserId = userId, Role = role };
}
