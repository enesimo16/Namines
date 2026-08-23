export interface SchemaColumn {
  id: string;
  name: string;
  type: string;
  length: number | null;
  isPK: boolean;
  isFK: boolean;
  isNullable: boolean;
  defaultValue: string | null;
  stableUuid?: string;

  /**
   * Değeri veritabanı mı üretiyor? (04 §3)
   *
   * ÜÇ DURUMLU ve varsayılanı `undefined` — "söylenmedi" demek. O durumda
   * bugüne kadarki çıkarım geçerli: tek kolonlu tamsayı birincil anahtar
   * otomatik artan sayılır. `false` ise "bu kimliği ben atıyorum" — dışarıdan
   * gelen bir sipariş numarası tamsayı anahtar olabilir ve veritabanının onu
   * ezmesi sessiz veri kaybıdır.
   */
  identity?: boolean | null;

  /** Doluysa kolonun tipi bu enum'dan gelir ve `type` yok sayılır. */
  enumRef?: string | null;

  /** Değeri başka kolonlardan hesaplanan kolonun ifadesi, ör. `quantity * price`. */
  generated?: string | null;

  /** Metin karşılaştırma/sıralama kuralı, ör. `tr-TR-x-icu`. */
  collation?: string | null;

  /** Dizi kolonu. Yalnızca PostgreSQL destekler; diğerlerinde derleme reddedilir. */
  isArray?: boolean;
}

/**
 * Bir kolonun alabileceği sabit değer kümesi (04 §3).
 *
 * "Durum" kolonunu `varchar` yapıp değerleri uygulamada kontrol etmek,
 * veritabanına yanlış değerin yazılmasını hiçbir zaman engellemez.
 */
export interface SchemaEnum {
  id: string;
  name: string;
  stableUuid?: string;
  /** Sıra korunur — PostgreSQL enum değerlerini tanımlandıkları sırayla sıralar. */
  values: string[];
}

/** Index'te yer alan kolon ve sıralama yönü. */
export interface SchemaIndexColumn {
  columnId: string;
  descending?: boolean;
}

/**
 * Tablo üzerinde bir index.
 *
 * Faz 1'de bu kavram modelde hiç yoktu — üretilen şemalar index'siz geliyordu.
 * Yabancı anahtar kolonunda index olmaması, üretimdeki en yaygın performans hatasıdır.
 */
export interface SchemaIndex {
  id: string;
  stableUuid?: string;
  /** Boşsa backend deterministik ad türetir (IX_Tablo_Kolonlar). */
  name?: string;
  columns: SchemaIndexColumn[];
  isUnique?: boolean;
  /** Kısmi index koşulu. MSSQL/PostgreSQL/SQLite destekler. */
  where?: string;
  /** Kapsayan index kolonları. Yalnızca MSSQL ve PostgreSQL. */
  includeColumnIds?: string[];
  /** btree | hash | gin | gist | brin | fulltext | spatial */
  method?: string;
}

/** Tablo seviyesi UNIQUE kısıtı. */
export interface SchemaUnique {
  id: string;
  stableUuid?: string;
  name?: string;
  columnIds: string[];
}

/** Tablo seviyesi CHECK kısıtı. İfade ham SQL'dir. */
export interface SchemaCheck {
  id: string;
  stableUuid?: string;
  name?: string;
  expression: string;
}

export interface SchemaTable {
  id: string;
  name: string;
  columns: SchemaColumn[];
  stableUuid?: string;
  color?: string;
  /** Eski kayıtlarda bu alanlar yoktur → undefined; üreticiler boş liste gibi davranır. */
  indexes?: SchemaIndex[];
  uniques?: SchemaUnique[];
  checks?: SchemaCheck[];
}

/**
 * Yabancı anahtarın, işaret ettiği satır silindiğinde/güncellendiğinde ne yapacağı.
 * Backend'deki Namines.Core.Enums.ReferentialAction ile birebir eşleşir.
 *
 * Varsayılan 'NoAction'dır. Eskiden tüm FK'lara koşulsuz CASCADE yazılıyordu;
 * bu, SQL Server'da çalıştırılamayan DDL (Msg 1785) ve diğer motorlarda sessiz
 * veri kaybı üretiyordu.
 */
export type ReferentialAction =
  | 'NoAction'
  | 'Restrict'
  | 'Cascade'
  | 'SetNull'
  | 'SetDefault';

export interface SchemaRelation {
  id: string;
  type: string; // OneToOne, OneToMany, ManyToMany
  sourceTableId: string;
  sourceColumnId: string;
  targetTableId: string;
  targetColumnId: string;
  /** Hedef satır silindiğinde. Varsayılan 'NoAction'. */
  onDelete?: ReferentialAction;
  /** Hedef anahtar güncellendiğinde. Varsayılan 'NoAction'. Oracle desteklemez. */
  onUpdate?: ReferentialAction;
}

export interface DatabaseSchema {
  schemaId: string;
  name: string;
  tables: SchemaTable[];
  relations: SchemaRelation[];
  /** Eski kayıtlarda yoktur → undefined; üreticiler boş liste gibi davranır. */
  enums?: SchemaEnum[];
  cloudProvider?: 'None' | 'AWS' | 'Azure';
  includeBiModule?: boolean;
}
