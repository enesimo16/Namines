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

    /// <summary>
    /// Adlandırılmış enum tipleri (04 §3 <c>enums</c>).
    ///
    /// Eski kayıtlarda bu alan yoktur → boş liste olur, yani mevcut şemalar
    /// bozulmadan çalışmaya devam eder.
    /// </summary>
    public List<SchemaEnum> Enums { get; set; } = new();
}

/// <summary>
/// Bir kolonun alabileceği sabit değer kümesi (04 §3 <c>enums</c>).
///
/// <b>Neden ayrı bir kavram:</b> "durum" kolonunu <c>varchar</c> yapıp değerleri
/// uygulamada kontrol etmek, veritabanına yanlış değerin yazılmasını hiçbir
/// zaman engellemez — ve o veri bir kez yazıldıktan sonra temizlenmesi gereken
/// bir borçtur. Enum, kuralı verinin yanına koyar.
/// </summary>
public class SchemaEnum
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StableUuid { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// İzin verilen değerler. <b>Sıra korunur</b> — PostgreSQL enum değerlerini
    /// tanımlandıkları sırayla SIRALAR, yani sırayı değiştirmek
    /// <c>ORDER BY status</c> sonucunu değiştirir.
    /// </summary>
    public List<string> Values { get; set; } = new();
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

    /// <summary>
    /// Doluysa kolonun tipi bu enum'dan gelir ve <see cref="Type"/> yok sayılır
    /// (04 §3 <c>type.enumRef</c>).
    ///
    /// <see cref="SchemaEnum.Name"/> ile eşleşmeyen bir ad, DDL üretiminde
    /// <b>hata verir</b> — sessizce metne düşmek, kısıtı olmayan bir kolon
    /// üretip kullanıcının koruma sandığı şeyi yok etmek olurdu.
    /// </summary>
    public string? EnumRef { get; set; }

    /// <summary>
    /// Değeri başka kolonlardan hesaplanan kolonun ifadesi
    /// (04 §3 <c>generated</c>), ör. <c>quantity * unit_price</c>.
    ///
    /// Doluysa kolona yazılamaz; <see cref="DefaultValue"/> ve <c>NOT NULL</c>
    /// ile birlikte kullanılmaz — motorların çoğu bunu reddeder ve ikisini
    /// birden üretmek çalıştırılamayan DDL demektir.
    /// </summary>
    public string? Generated { get; set; }

    /// <summary>
    /// Metin karşılaştırma/sıralama kuralı (04 §3 <c>collation</c>),
    /// ör. <c>tr_TR.utf8</c> ya da <c>Turkish_CI_AS</c>.
    ///
    /// <b>Sessiz bir doğruluk meselesi:</b> yanlış collation'da "İstanbul" ile
    /// "istanbul" eşit sayılmaz ya da sıralama beklenenden farklı çıkar — ve
    /// bunu ancak kullanıcı şikâyet edince fark edersin.
    /// </summary>
    public string? Collation { get; set; }

    /// <summary>
    /// Kolon bir dizi mi (04 §3 <c>type.array</c>), ör. PostgreSQL <c>text[]</c>.
    ///
    /// <b>Desteklemeyen motorda DDL üretimi hata verir</b>, skalere düşmez:
    /// dizi olma özelliğini sessizce atmak, kolonun ANLAMINI değiştirir ve
    /// uygulama tek bir değer bekleyen bir kolona liste yazmaya çalışır.
    /// </summary>
    public bool IsArray { get; set; }
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
