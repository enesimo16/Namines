'use client';

import React from 'react';
import { useSchemaStore } from '../../store/useSchemaStore';

export default function SchemaTextualSummary() {
  const schema = useSchemaStore(state => state.schema);

  if (!schema || !schema.tables || schema.tables.length === 0) {
    return (
      <div className="sr-only" aria-live="polite">
        <h2>Database Schema Summary</h2>
        <p>The database schema is currently empty.</p>
      </div>
    );
  }

  const findTableName = (tableId: string) => {
    return schema.tables.find(t => t.id === tableId)?.name || 'Unknown Table';
  };

  const findColumnName = (tableId: string, columnId: string) => {
    const table = schema.tables.find(t => t.id === tableId);
    return table?.columns.find(c => c.id === columnId)?.name || 'Unknown Column';
  };

  return (
    <div className="sr-only" aria-live="polite" aria-atomic="true">
      <h2>Database Schema Text Summary</h2>
      <p>
        The schema "{schema.name}" contains {schema.tables.length} tables and {schema.relations ? schema.relations.length : 0} relationships.
      </p>

      <h3>Tables</h3>
      <ul>
        {schema.tables.map(table => {
          const pkColumns = table.columns.filter(c => c.isPK).map(c => c.name);
          const fkColumns = table.columns.filter(c => c.isFK).map(c => c.name);
          
          return (
            <li key={table.id}>
              <h4>Table: {table.name}</h4>
              <p>Contains {table.columns.length} columns.</p>
              <ul>
                {table.columns.map(col => (
                  <li key={col.id}>
                    Column: {col.name}, Type: {col.type}
                    {col.length ? `(length: ${col.length})` : ''}
                    {col.isPK ? ', Primary Key' : ''}
                    {col.isFK ? ', Foreign Key' : ''}
                    {col.isNullable ? ', Nullable' : ', Non-Nullable'}
                    {col.defaultValue ? `, Default value: ${col.defaultValue}` : ''}
                  </li>
                ))}
              </ul>
              {pkColumns.length > 0 && (
                <p>Primary key: {pkColumns.join(', ')}</p>
              )}
              {fkColumns.length > 0 && (
                <p>Foreign key columns: {fkColumns.join(', ')}</p>
              )}
            </li>
          );
        })}
      </ul>

      {schema.relations && schema.relations.length > 0 && (
        <>
          <h3>Relationships</h3>
          <ul>
            {schema.relations.map(rel => {
              const sourceTable = findTableName(rel.sourceTableId);
              const sourceCol = findColumnName(rel.sourceTableId, rel.sourceColumnId);
              const targetTable = findTableName(rel.targetTableId);
              const targetCol = findColumnName(rel.targetTableId, rel.targetColumnId);
              
              return (
                <li key={rel.id}>
                  Relation type: {rel.type}. From table {sourceTable} (column: {sourceCol}) referencing table {targetTable} (column: {targetCol}).
                </li>
              );
            })}
          </ul>
        </>
      )}
    </div>
  );
}
