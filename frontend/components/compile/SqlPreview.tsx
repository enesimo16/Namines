'use client';

import React from 'react';
import { useSchemaStore } from '../../store/useSchemaStore';
import CodeStudioViewer from './CodeStudioViewer';

interface SqlPreviewProps {
  sql: string;
  focusTable?: string;
}

export default function SqlPreview({ sql, focusTable }: SqlPreviewProps) {
  const { schema, dbType } = useSchemaStore();
  const filename = `${schema?.name || 'database'}_schema.sql`;

  return (
    <CodeStudioViewer
      code={sql}
      language="sql"
      filename={filename}
      dialectBadge={dbType}
      downloadLabel="Download .sql"
      statusText="DDL Syntax Verified · ANSI Compliant"
      focusTable={focusTable}
    />
  );
}
