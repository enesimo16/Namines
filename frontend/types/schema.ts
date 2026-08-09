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

export interface SchemaTable {
  id: string;
  name: string;
  columns: SchemaColumn[];
  stableUuid?: string;
  color?: string;
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
