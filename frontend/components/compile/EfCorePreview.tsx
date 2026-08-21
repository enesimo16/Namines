import React, { useEffect, useRef, useState } from 'react';
import Prism from 'prismjs';
import 'prismjs/components/prism-clike';
import 'prismjs/components/prism-csharp';
import 'prismjs/themes/prism-tomorrow.css';
import { DatabaseSchema } from '../../types/schema';
import { Package } from 'lucide-react';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { Panel, PanelBar, ActionButton } from './PanelKit';


interface EfCorePreviewProps {
  schema: DatabaseSchema;
}

export default function EfCorePreview({ schema }: EfCorePreviewProps) {
  const codeRef = useRef<HTMLElement>(null);

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
  const { dbType } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);
  const [isDownloading, setIsDownloading] = useState(false);

  useEffect(() => {
    if (codeRef.current) {
      Prism.highlightElement(codeRef.current);
    }
  }, [codeString]);

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
    <Panel scroll={false}>
      <div className="h-full flex flex-col">
        <PanelBar
          left={
            <>
              <span className="text-[11px] font-mono text-content-secondary truncate">AppDbContext.cs</span>
              <span className="text-[10px] font-mono text-content-muted shrink-0">EF Core 8.0</span>
            </>
          }
        >
          <ActionButton icon={Package} onClick={handleDownloadEfCore} busy={isDownloading} tone="primary">
            Download .zip
          </ActionButton>
        </PanelBar>

        <div className="flex-1 min-h-0 overflow-auto bg-surface-900">
          <pre className="!bg-transparent !m-0 !p-3 !text-[11px] !leading-relaxed">
            <code ref={codeRef} className="language-csharp">
              {codeString}
            </code>
          </pre>
        </div>
      </div>
    </Panel>
  );
}

