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
  cloudProvider?: 'None' | 'AWS' | 'Azure';
  includeBiModule?: boolean;
}
