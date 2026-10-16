'use client';

import React, { useState } from 'react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import CodeStudioViewer from './CodeStudioViewer';

interface EfCorePreviewProps {
  schema: DatabaseSchema;
}

export default function EfCorePreview({ schema }: EfCorePreviewProps) {
  const { dbType } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);
  const [isDownloading, setIsDownloading] = useState(false);

  const mapToCsharpType = (sqlType: string): string => {
    const type = (sqlType || '').toUpperCase();
    if (type.includes('INT') || type.includes('INTEGER')) return 'int';
    if (type.includes('BIGINT')) return 'long';
    if (type.includes('SMALLINT')) return 'short';
    if (type.includes('TINYINT')) return 'byte';
    if (type.includes('VARCHAR') || type.includes('TEXT') || type.includes('NVARCHAR') || type.includes('CHAR')) return 'string';
    if (type.includes('DECIMAL') || type.includes('NUMERIC')) return 'decimal';
    if (type.includes('FLOAT') || type.includes('REAL')) return 'float';
    if (type.includes('DOUBLE')) return 'double';
    if (type.includes('BIT') || type.includes('BOOL') || type.includes('BOOLEAN')) return 'bool';
    if (type.includes('DATE') || type.includes('TIME') || type.includes('DATETIME') || type.includes('TIMESTAMP')) return 'DateTime';
    return 'string';
  };

  const generateEfCoreCode = (): string => {
    if (!schema || !schema.tables || schema.tables.length === 0) {
      return '// Please return to the diagram and add at least one table...';
    }

    const firstTable = schema.tables[0];
    const firstTableName = firstTable.name;

    let code = `using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Namines.Generated
{
    /// <summary>
    /// AppDbContext template generated for your project by Namines CoderAI.
    /// </summary>
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

`;

    // DbSets
    schema.tables.forEach(t => {
      code += `        public DbSet<${t.name}> ${t.name}s { get; set; }\n`;
    });

    code += `
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
`;

    // Fluent API PK configurations
    schema.tables.forEach(t => {
      t.columns.forEach(c => {
        if (c.isPK) {
          code += `            modelBuilder.Entity<${t.name}>().HasKey(e => e.${c.name});\n`;
        }
      });
    });

    code += `        }
    }

    /// <summary>
    /// Sample Entity model corresponding to the ${firstTableName} table in your schema.
    /// </summary>
    public class ${firstTableName}
    {
`;

    // First table columns
    firstTable.columns.forEach(c => {
      const csharpType = mapToCsharpType(c.type);
      code += `        public ${csharpType} ${c.name} { get; set; }\n`;
    });

    code += `    }
}
`;
    return code;
  };

  const codeString = generateEfCoreCode();

  const handleDownloadEfCore = async () => {
    if (!schema) return;
    setIsDownloading(true);
    try {
      const zipBlob = await schemaService.compileEfCore(schema, dbType);
      const url = URL.createObjectURL(zipBlob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${schema.name || 'Models'}_EFCore.zip`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      showToast("EF Core models successfully downloaded as ZIP!", "success");
    } catch (error) {
      console.error("Failed to download EF Core zip", error);
      showToast("An error occurred while downloading EF Core files.", "error");
    } finally {
      setIsDownloading(false);
    }
  };

  return (
    <CodeStudioViewer
      code={codeString}
      language="csharp"
      filename="AppDbContext.cs"
      dialectBadge="EF Core 8.0"
      onDownload={handleDownloadEfCore}
      downloadLabel="Download .zip"
      isDownloading={isDownloading}
      statusText="Entity Framework Core 8.0 · Scaffolding Ready"
    />
  );
}
