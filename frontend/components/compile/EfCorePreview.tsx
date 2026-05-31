import React, { useEffect, useRef } from 'react';
import Prism from 'prismjs';
import 'prismjs/components/prism-clike';
import 'prismjs/components/prism-csharp';
import 'prismjs/themes/prism-tomorrow.css';
import { DatabaseSchema } from '../../types/schema';

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

  useEffect(() => {
    if (codeRef.current) {
      Prism.highlightElement(codeRef.current);
    }
  }, [codeString]);

  return (
    <div className="w-full h-full bg-[#030307]/60 backdrop-blur-md rounded-xl overflow-hidden border border-zinc-800/80 shadow-2xl relative flex flex-col">
      <div className="shrink-0 px-4 py-2.5 bg-zinc-950/40 backdrop-blur-sm border-b border-zinc-800/60 flex justify-between items-center z-10 select-none">
        <span className="text-xs text-zinc-400 font-mono">AppDbContext.cs &amp; EntityModel.cs</span>
        <span className="text-[10px] text-zinc-500 font-mono">Entity Framework Core 8.0</span>
      </div>
      <div className="flex-1 overflow-auto custom-scrollbar">
        <pre className="!bg-transparent !m-0 !p-4 !text-sm">
          <code ref={codeRef} className="language-csharp">
            {codeString}
          </code>
        </pre>
      </div>
    </div>
  );
}
