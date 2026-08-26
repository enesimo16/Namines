using System;

namespace Namines.Core.Models.Auth;

/// <summary>
/// second-phase/10-COKLU-DB.md — iki AYRI veritabanı (proje) arasındaki
/// mantıksal ilişki.
///
/// <b>Gerçek bir yabancı anahtar DEĞİL.</b> Veritabanı bunu doğrulamaz, hiçbir
/// motor bunu bir kısıt olarak üretmez — bu yalnızca Namines'in KAYDIDIR.
/// Amaç, iki ayrı veritabanının aslında birbirine bağlı olduğunu (ör.
/// <c>orders-db.orders.user_id</c> → <c>auth-db.users.id</c>) görünür kılmak,
/// veritabanının kendisinin göremediği bir şeyi.
/// </summary>
public class CrossDatabaseRelation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string SourceProjectId { get; set; } = null!;
    public CloudProject SourceProject { get; set; } = null!;
    /// <summary>Kaynak şemadaki tablo/kolon id'si (o projenin <c>SchemaJson</c>'ındaki <c>SchemaTable.Id</c>/<c>SchemaColumn.Id</c>).</summary>
    public string SourceTableId { get; set; } = null!;
    public string SourceColumnId { get; set; } = null!;

    public string TargetProjectId { get; set; } = null!;
    public CloudProject TargetProject { get; set; } = null!;
    public string TargetTableId { get; set; } = null!;
    public string TargetColumnId { get; set; } = null!;

    /// <summary>Kullanıcının bu ilişkiye eklediği serbest açıklama, ör. "aynı kullanıcı kimliği".</summary>
    public string? Note { get; set; }

    public string CreatedByUserId { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
