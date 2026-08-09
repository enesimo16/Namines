using System;
using System.Collections.Generic;
using Namines.Core.Enums;

namespace Namines.Core.Models;

/// <summary>
/// Index'te yer alan bir kolon ve sıralama yönü.
/// </summary>
public class SchemaIndexColumn
{
    /// <summary>Hedef kolonun <see cref="SchemaColumn.Id"/> değeri.</summary>
    public string ColumnId { get; set; } = string.Empty;

    /// <summary>true → DESC. Bileşik index'lerde sıralama yönü sorgu planını etkiler.</summary>
    public bool Descending { get; set; }
}

/// <summary>
/// Tablo üzerinde bir index.
///
/// Faz 1'de bu kavram HİÇ YOKTU — bir veritabanı tasarım aracının index üretememesi,
/// üretilen şemanın üretimde kullanılamaz olması demekti. FK kolonlarında index
/// olmaması da en yaygın performans hatasıdır.
/// </summary>
public class SchemaIndex
{
    public string Id { get; set; } = string.Empty;
    public string StableUuid { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Boş bırakılırsa üreticiler deterministik bir ad türetir (IX_Tablo_Kolonlar).</summary>
    public string? Name { get; set; }

    public List<SchemaIndexColumn> Columns { get; set; } = new();

    /// <summary>Benzersiz index. UNIQUE CONSTRAINT'ten farklıdır — bkz. <see cref="SchemaUnique"/>.</summary>
    public bool IsUnique { get; set; }

    /// <summary>
    /// Kısmi (filtreli) index koşulu — ör. "deleted_at IS NULL".
    /// PostgreSQL, SQL Server (filtered index) ve SQLite destekler; MySQL/MariaDB/Oracle desteklemez.
    /// </summary>
    public string? Where { get; set; }

    /// <summary>
    /// Kapsayan (covering) index'e eklenecek kolon id'leri — index-only scan sağlar.
    /// Yalnızca SQL Server (INCLUDE) ve PostgreSQL 11+ destekler.
    /// </summary>
    public List<string> IncludeColumnIds { get; set; } = new();

    /// <summary>
    /// Index yöntemi: btree (varsayılan) · hash · gin · gist · brin · fulltext · spatial.
    /// Motor desteklemiyorsa yok sayılır ve btree kullanılır.
    /// </summary>
    public string? Method { get; set; }
}

/// <summary>
/// Tablo seviyesinde UNIQUE kısıtı.
///
/// Neden index'ten ayrı: bir yabancı anahtar UNIQUE CONSTRAINT'e referans verebilir,
/// unique index'e her motorda veremez. Ayrıca kısıt olarak adlandırılır ve
/// bilgi şemasında farklı görünür.
/// </summary>
public class SchemaUnique
{
    public string Id { get; set; } = string.Empty;
    public string StableUuid { get; set; } = Guid.NewGuid().ToString();
    public string? Name { get; set; }
    public List<string> ColumnIds { get; set; } = new();
}

/// <summary>
/// Tablo seviyesinde CHECK kısıtı — ör. "Quantity &gt; 0".
///
/// İfade, kullanıcının yazdığı ham SQL'dir ve olduğu gibi aktarılır.
/// Motor-bağımsız bir ifade dili Faz 1'in (NSL) konusudur.
/// </summary>
public class SchemaCheck
{
    public string Id { get; set; } = string.Empty;
    public string StableUuid { get; set; } = Guid.NewGuid().ToString();
    public string? Name { get; set; }

    /// <summary>Ham SQL koşulu. Boşsa üreticiler bu kısıtı atlar.</summary>
    public string Expression { get; set; } = string.Empty;
}
