using System;
using System.Collections.Generic;
using Namines.Core.Enums;

namespace Namines.Core.Models;

public class DatabaseSchema
{
    public string SchemaId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public List<SchemaTable> Tables { get; set; } = new();
    public List<SchemaRelation> Relations { get; set; } = new();
    public string? CloudProvider { get; set; } = "None";
    public bool IncludeBiModule { get; set; } = false;
}

public class SchemaTable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StableUuid { get; set; } = Guid.NewGuid().ToString();
    public List<SchemaColumn> Columns { get; set; } = new();

    /// <summary>
    /// Tablo üzerindeki index'ler. Eski kayıtlarda bu alan yoktur → boş liste olur,
    /// yani mevcut şemalar bozulmadan çalışmaya devam eder.
    /// </summary>
    public List<SchemaIndex> Indexes { get; set; } = new();

    /// <summary>Tablo seviyesi UNIQUE kısıtları.</summary>
    public List<SchemaUnique> Uniques { get; set; } = new();

    /// <summary>Tablo seviyesi CHECK kısıtları.</summary>
    public List<SchemaCheck> Checks { get; set; } = new();
}

public class SchemaColumn
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StableUuid { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty;
    public int? Length { get; set; }
    public bool IsPK { get; set; }
    public bool IsFK { get; set; }
    public bool IsNullable { get; set; }
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Değeri veritabanı mı üretiyor? (04 §3 <c>identity</c>)
    ///
    /// <b>Üç durumlu ve varsayılanı <c>null</c>.</b> <c>null</c> = "söylenmedi",
    /// yani bugüne kadarki davranış korunur: tek kolonlu tamsayı birincil anahtar
    /// otomatik artan sayılır. <c>true</c> = kesinlikle otomatik, <c>false</c> =
    /// kesinlikle değil.
    ///
    /// <b>Neden <c>false</c>'a ihtiyaç var:</b> dışarıdan atanan bir kimlik
    /// (başka bir sistemden gelen sipariş numarası gibi) tamsayı birincil anahtar
    /// olabilir ve veritabanının onu ezmesi veri kaybıdır. Bugün bunu "hayır"
    /// diyebilmenin yolu yoktu; çıkarım her zaman "evet" diyordu.
    ///
    /// Bu alan üretilen panelde de okunuyor: anahtarı veritabanı üretiyorsa
    /// kullanıcıdan istemenin anlamı yok (bkz. G40).
    /// </summary>
    public bool? Identity { get; set; }
}

public class SchemaRelation
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SourceTableId { get; set; } = string.Empty;
    public string SourceColumnId { get; set; } = string.Empty;
    public string TargetTableId { get; set; } = string.Empty;
    public string TargetColumnId { get; set; } = string.Empty;

    /// <summary>
    /// Hedef satır silindiğinde ne olacağı. Varsayılan <see cref="ReferentialAction.NoAction"/>.
    /// Eski kayıtlarda bu alan yoktur; JSON'dan okunurken varsayılana düşer — yani mevcut
    /// şemalar otomatik olarak güvenli davranışa geçer.
    /// </summary>
    public ReferentialAction OnDelete { get; set; } = ReferentialAction.NoAction;

    /// <summary>
    /// Hedef anahtar güncellendiğinde ne olacağı. Varsayılan <see cref="ReferentialAction.NoAction"/>.
    /// Oracle ON UPDATE'i hiç desteklemez — o motorda yok sayılır.
    /// </summary>
    public ReferentialAction OnUpdate { get; set; } = ReferentialAction.NoAction;
}
