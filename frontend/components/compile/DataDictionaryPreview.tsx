'use client';

import React, { useState, useMemo } from 'react';
import { Download, Table2, Key, Link2, Search, FileSpreadsheet, X, Check } from 'lucide-react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { PanelEmpty } from './PanelKit';

interface DataDictionaryPreviewProps {
  schema: DatabaseSchema;
  projectName: string;
}

type Lang = 'tr' | 'en';

export default function DataDictionaryPreview({ schema, projectName }: DataDictionaryPreviewProps) {
  const showToast = useToastStore(state => state.showToast);
  const [isDownloading, setIsDownloading] = useState(false);
  const [lang, setLang] = useState<Lang>('en');
  const [searchFilter, setSearchFilter] = useState('');
  const [selectedTableFilter, setSelectedTableFilter] = useState<string>('all');
  const [copiedCsv, setCopiedCsv] = useState(false);

  const handleDownloadPdf = async () => {
    if (!schema) return;
    setIsDownloading(true);
    try {
      const pdfBlob = await schemaService.generatePdf(schema, projectName, lang);
      const url = URL.createObjectURL(pdfBlob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${projectName.replace(/\s+/g, '_')}_DataDictionary.pdf`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      showToast("Data dictionary PDF successfully downloaded!", "success");
    } catch (error) {
      console.error("Failed to download PDF", error);
      showToast("An error occurred while generating PDF.", "error");
    } finally {
      setIsDownloading(false);
    }
  };

  const handleCopyCsv = () => {
    if (!schema?.tables) return;
    try {
      let csv = 'Table,Column,Type,Length,Nullable,PK,FK,Default\n';
      schema.tables.forEach(table => {
        table.columns.forEach(col => {
          csv += `"${table.name}","${col.name}","${col.type}","${col.length || ''}","${col.isNullable ? 'YES' : 'NO'}","${col.isPK ? 'YES' : 'NO'}","${col.isFK ? 'YES' : 'NO'}","${col.defaultValue || ''}"\n`;
        });
      });
      navigator.clipboard.writeText(csv);
      setCopiedCsv(true);
      showToast('Data dictionary copied as CSV!', 'success');
      setTimeout(() => setCopiedCsv(false), 2000);
    } catch {
      showToast('Failed to copy CSV.', 'error');
    }
  };

  const getRelationsCount = (tableId: string) => {
    return schema.relations.filter(
      r => r.sourceTableId === tableId || r.targetTableId === tableId
    ).length;
  };

  const translations = {
    tr: {
      noTables: "Tablo Bulunamadı",
      noTablesDesc: "Veri sözlüğünü görüntülemek için önce diyagram editörüne dönüp tablolar ekleyin.",
      columns: "kolon",
      relations: "ilişki",
      colName: "Kolon Adı",
      colType: "Veri Tipi",
      colConstraints: "Kısıtlar",
      colNullable: "Boş Olabilir",
      colDefault: "Varsayılan",
      yes: "EVET",
      no: "HAYIR",
      fileTitle: "data_dictionary.pdf",
      searchPlaceholder: "Tablo veya kolon ara...",
    },
    en: {
      noTables: "No Tables Found",
      noTablesDesc: "Go back to the diagram editor and create some tables first to view the data dictionary.",
      columns: "columns",
      relations: "relations",
      colName: "Column Name",
      colType: "Data Type",
      colConstraints: "Constraints",
      colNullable: "Nullable",
      colDefault: "Default Value",
      yes: "YES",
      no: "NO",
      fileTitle: "data_dictionary.pdf",
      searchPlaceholder: "Search tables & columns...",
    }
  };

  const t = translations[lang];

  // Filtered tables based on search & table filter
  const tables = schema?.tables;
  const filteredTables = useMemo(() => {
    if (!tables) return [];
    const query = searchFilter.trim().toLowerCase();
    return tables.filter(table => {
      if (selectedTableFilter !== 'all' && table.name !== selectedTableFilter) {
        return false;
      }
      if (!query) return true;
      const matchTable = table.name.toLowerCase().includes(query);
      const matchColumn = table.columns.some(c =>
        c.name.toLowerCase().includes(query) || c.type.toLowerCase().includes(query)
      );
      return matchTable || matchColumn;
    });
  }, [tables, searchFilter, selectedTableFilter]);

  if (!schema.tables || schema.tables.length === 0) {
    return (
      <div className="w-full h-full bg-surface-900 border border-line-strong rounded-[var(--radius-card)] flex items-center justify-center">
        <PanelEmpty icon={Table2} title={t.noTables} hint={t.noTablesDesc} />
      </div>
    );
  }

  const totalColumns = schema.tables.reduce((s, tb) => s + tb.columns.length, 0);

  return (
    <div className="w-full h-full bg-surface-900 rounded-[var(--radius-card)] border border-line-strong flex flex-col overflow-hidden select-none">
      
      {/* ── Top Header Toolbar ──────────────────────────────────────────────── */}
      <div className="shrink-0 min-h-11 px-3.5 py-1.5 bg-surface-800 border-b border-line-strong flex flex-wrap items-center justify-between gap-2.5">
        
        {/* Left: Title & Language */}
        <div className="flex items-center gap-2">
          <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] bg-surface-700/80 border border-line shadow-xs">
            <Table2 className="w-3.5 h-3.5 text-namines-teal-vibrant shrink-0" />
            <span className="text-xs font-mono font-semibold text-content-primary">
              Data Dictionary
            </span>
          </div>

          <div className="flex items-center gap-0.5 bg-surface-700/80 p-0.5 rounded-[var(--radius-control)] border border-line">
            <button
              onClick={() => setLang('en')}
              className={`tap-44 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px] font-bold cursor-pointer ${
                lang === 'en' ? 'bg-surface-800 text-content-primary shadow-xs' : 'text-content-muted hover:text-content-secondary'
              }`}
            >
              EN
            </button>
            <button
              onClick={() => setLang('tr')}
              className={`tap-44 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px] font-bold cursor-pointer ${
                lang === 'tr' ? 'bg-surface-800 text-content-primary shadow-xs' : 'text-content-muted hover:text-content-secondary'
              }`}
            >
              TR
            </button>
          </div>
        </div>

        {/* Center: Search Filter */}
        <div className="relative flex-1 max-w-xs min-w-[160px]">
          <Search className="w-3.5 h-3.5 text-content-muted absolute left-2.5 top-1/2 -translate-y-1/2 pointer-events-none" />
          <input
            type="text"
            value={searchFilter}
            onChange={e => setSearchFilter(e.target.value)}
            placeholder={t.searchPlaceholder}
            className="w-full bg-surface-700/70 border border-line rounded-[var(--radius-control)] h-8 pl-8 pr-7 text-xs text-content-primary placeholder:text-content-muted/60 focus:outline-none focus:border-focus-ring"
          />
          {searchFilter && (
            <button
              onClick={() => setSearchFilter('')}
              className="absolute right-2 top-1/2 -translate-y-1/2 text-content-muted hover:text-content-primary"
            >
              <X className="w-3 h-3" />
            </button>
          )}
        </div>

        {/* Right: Actions */}
        <div className="flex items-center gap-1.5 shrink-0">
          <button
            onClick={handleCopyCsv}
            className="tap-44 flex items-center gap-1.5 h-8 px-2.5 rounded-[var(--radius-control)] bg-surface-700/80 hover:bg-surface-700 border border-line text-xs font-medium text-content-primary transition-colors cursor-pointer"
            title="Copy as CSV"
          >
            {copiedCsv ? <Check className="w-3.5 h-3.5 text-namines-teal-vibrant" /> : <FileSpreadsheet className="w-3.5 h-3.5 text-content-muted" />}
            <span className="text-[11px]">{copiedCsv ? 'Copied' : 'Copy CSV'}</span>
          </button>

          <button
            onClick={handleDownloadPdf}
            disabled={isDownloading}
            className="tap-44 flex items-center gap-1.5 h-8 px-3 rounded-[var(--radius-control)] bg-content-primary hover:opacity-90 text-surface-900 text-xs font-semibold transition-colors cursor-pointer disabled:opacity-50"
            title="Download PDF Data Dictionary"
          >
            <Download className="w-3.5 h-3.5" />
            <span className="text-[11px]">Download PDF</span>
          </button>
        </div>
      </div>

      {/* ── Table Quick Selector Strip ──────────────────────────────────────── */}
      <div className="shrink-0 px-3 py-1.5 bg-surface-800/60 border-b border-line flex items-center gap-1.5 overflow-x-auto scrollbar-none">
        <button
          onClick={() => setSelectedTableFilter('all')}
          className={`tap-44 shrink-0 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px] font-mono font-medium transition-colors cursor-pointer ${
            selectedTableFilter === 'all'
              ? 'bg-surface-700 text-content-primary font-bold border border-line-strong'
              : 'text-content-muted hover:text-content-secondary'
          }`}
        >
          All ({schema.tables.length})
        </button>
        {schema.tables.map(tb => (
          <button
            key={tb.id}
            onClick={() => setSelectedTableFilter(tb.name)}
            className={`tap-44 shrink-0 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px] font-mono transition-colors cursor-pointer ${
              selectedTableFilter === tb.name
                ? 'bg-surface-700 text-content-primary font-bold border border-line-strong'
                : 'text-content-muted hover:text-content-secondary'
            }`}
          >
            {tb.name} ({tb.columns.length})
          </button>
        ))}
      </div>

      {/* ── Data Dictionary Table Body ──────────────────────────────────────── */}
      <div className="flex-1 min-h-0 overflow-auto select-text">
        <table className="w-full text-left border-collapse">
          <thead className="sticky top-0 z-20">
            <tr className="bg-surface-800 text-content-muted text-micro uppercase font-bold tracking-wider border-b border-line-strong">
              <th className="py-2 px-3.5 font-semibold">{t.colName}</th>
              <th className="py-2 px-3 font-semibold">{t.colType}</th>
              <th className="py-2 px-3 font-semibold">{t.colConstraints}</th>
              <th className="py-2 px-3 font-semibold">{t.colNullable}</th>
              <th className="py-2 px-3 font-semibold">{t.colDefault}</th>
            </tr>
          </thead>
          <tbody>
            {filteredTables.map((table) => {
              const relCount = getRelationsCount(table.id);
              return (
                <React.Fragment key={table.id}>
                  {/* Table Section Header */}
                  <tr className="sticky top-[33px] z-10">
                    <td colSpan={5} className="bg-surface-700/90 backdrop-blur-sm border-y border-line-strong py-2 px-3.5 shadow-xs">
                      <div className="flex items-center gap-2">
                        <Table2 className="w-4 h-4 text-namines-teal-vibrant shrink-0" />
                        <span className="text-xs font-bold text-content-primary font-mono">{table.name}</span>
                        <span className="text-[10px] text-content-muted font-mono bg-surface-800 px-2 py-0.5 rounded-full border border-line">
                          {table.columns.length} {t.columns}
                          {relCount > 0 && ` · ${relCount} ${t.relations}`}
                        </span>
                      </div>
                    </td>
                  </tr>

                  {/* Table Column Rows */}
                  {table.columns.map((col) => (
                    <tr
                      key={col.id}
                      className="border-b border-line hover:bg-surface-800/40 transition-colors"
                    >
                      <td className="py-2 px-3.5 font-mono text-[11px] text-content-primary font-semibold">
                        {col.name}
                      </td>
                      <td className="py-2 px-3 font-mono text-[11px] text-content-secondary">
                        <span className="px-1.5 py-0.5 rounded-[var(--radius-control)] bg-surface-800 border border-line">
                          {col.type.toUpperCase()}{col.length ? `(${col.length})` : ''}
                        </span>
                      </td>
                      <td className="py-2 px-3">
                        <div className="flex flex-wrap gap-1">
                          {col.isPK && (
                            <span className="inline-flex items-center gap-1 text-[10px] font-mono font-bold px-1.5 py-0.5 rounded-[var(--radius-control)] bg-surface-700 text-namines-teal-vibrant border border-line">
                              <Key className="w-2.5 h-2.5" /> PK
                            </span>
                          )}
                          {col.isFK && (
                            <span className="inline-flex items-center gap-1 text-[10px] font-mono font-bold px-1.5 py-0.5 rounded-[var(--radius-control)] bg-surface-700 text-content-secondary border border-line">
                              <Link2 className="w-2.5 h-2.5" /> FK
                            </span>
                          )}
                          {!col.isPK && !col.isFK && (
                            <span className="text-[10px] text-content-muted">—</span>
                          )}
                        </div>
                      </td>
                      <td className="py-2 px-3 text-[10px] font-medium font-mono">
                        {col.isNullable ? (
                          <span className="text-content-muted">{t.yes}</span>
                        ) : (
                          <span className="text-content-primary font-bold">{t.no}</span>
                        )}
                      </td>
                      <td className="py-2 px-3 font-mono text-[11px] text-content-muted">
                        {col.defaultValue !== null ? col.defaultValue : 'NULL'}
                      </td>
                    </tr>
                  ))}
                </React.Fragment>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* ── Bottom Summary Status Bar ───────────────────────────────────────── */}
      <div className="shrink-0 h-7 px-3 bg-surface-800/90 border-t border-line-strong flex items-center justify-between text-[10px] font-mono text-content-muted">
        <span>{schema.tables.length} tables · {totalColumns} columns total</span>
        <span>Schema Catalog Standard</span>
      </div>
    </div>
  );
}
