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
}

export interface SchemaRelation {
  id: string;
  type: string; // OneToOne, OneToMany, ManyToMany
  sourceTableId: string;
  sourceColumnId: string;
  targetTableId: string;
  targetColumnId: string;
}

export interface DatabaseSchema {
  schemaId: string;
  name: string;
  tables: SchemaTable[];
  relations: SchemaRelation[];
  cloudProvider?: 'None' | 'AWS' | 'Azure';
  includeBiModule?: boolean;
}
