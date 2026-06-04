'use client';

import React, { useState } from 'react';
import { Download, Table2, Key, Link2, Loader2, FileSpreadsheet, Globe } from 'lucide-react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';

interface DataDictionaryPreviewProps {
  schema: DatabaseSchema;
  projectName: string;
}

type Lang = 'tr' | 'en';

export default function DataDictionaryPreview({ schema, projectName }: DataDictionaryPreviewProps) {
  const showToast = useToastStore(state => state.showToast);
  const [isDownloading, setIsDownloading] = useState(false);
  const [lang, setLang] = useState<Lang>('tr');

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
      
      const successMsg = lang === 'tr' 
        ? "Veri sözlüğü PDF'i başarıyla indirildi!" 
        : "Data dictionary PDF successfully downloaded!";
      showToast(successMsg, "success");
    } catch (error) {
      console.error("Failed to download PDF", error);
      const errorMsg = lang === 'tr'
        ? "PDF oluşturulurken bir hata oluştu."
        : "An error occurred while generating PDF.";
      showToast(errorMsg, "error");
    } finally {
      setIsDownloading(false);
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
      noTablesDesc: "Veri sözlüğünü görüntülemek için lütfen tasarım tuvaline gidip tablolar oluşturun.",
      downloadPdf: "PDF İndir (.pdf)",
      columns: "kolon",
      relations: "ilişki",
      colName: "Sütun Adı",
      colType: "Tip",
      colConstraints: "Kısıtlar",
      colNullable: "Null?",
      colDefault: "Varsayılan",
      yes: "EVET",
      no: "HAYIR",
      fileTitle: "veri_sozlugu.pdf"
    },
    en: {
      noTables: "No Tables Found",
      noTablesDesc: "Go back to the diagram editor and create some tables first to view the data dictionary.",
      downloadPdf: "Download PDF (.pdf)",
      columns: "columns",
      relations: "relations",
      colName: "Column Name",
      colType: "Type",
      colConstraints: "Constraints",
      colNullable: "Nullable",
      colDefault: "Default",
      yes: "YES",
      no: "NO",
      fileTitle: "data_dictionary.pdf"
    }
  };

  const t = translations[lang];

  if (!schema.tables || schema.tables.length === 0) {
    return (
      <div className="w-full h-full bg-[#030307]/60 backdrop-blur-md rounded-xl border border-zinc-800/80 shadow-2xl flex flex-col items-center justify-center p-8 text-center">
        <Table2 className="w-12 h-12 text-zinc-600 mb-4 animate-pulse" />
        <h3 className="text-base font-semibold text-zinc-300">{t.noTables}</h3>
        <p className="text-xs text-zinc-500 max-w-sm mt-1">{t.noTablesDesc}</p>
      </div>
    );
  }

  return (
    <div className="w-full h-full bg-[#030307]/60 backdrop-blur-md rounded-xl overflow-hidden border border-zinc-800/80 shadow-2xl flex flex-col">
      {/* Header section with inline action button and language toggle */}
      <div className="shrink-0 px-5 py-3 bg-zinc-950/40 backdrop-blur-sm border-b border-zinc-800/60 flex justify-between items-center z-10 select-none">
        <div className="flex items-center gap-2">
          <FileSpreadsheet className="w-4 h-4 text-rose-400" />
          <span className="text-xs font-semibold text-zinc-300 tracking-wide font-mono">{t.fileTitle}</span>
        </div>
        
        <div className="flex items-center gap-4">
          {/* Segmented language selector */}
          <div className="flex bg-zinc-900 border border-zinc-800 p-0.5 rounded-lg items-center">
            <button
              onClick={() => setLang('tr')}
              className={`px-2.5 py-1 text-[10px] font-bold uppercase rounded-md transition-all cursor-pointer ${
                lang === 'tr'
                  ? 'bg-zinc-800 text-indigo-400 shadow-md'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              TR
            </button>
            <button
              onClick={() => setLang('en')}
              className={`px-2.5 py-1 text-[10px] font-bold uppercase rounded-md transition-all cursor-pointer ${
                lang === 'en'
                  ? 'bg-zinc-800 text-indigo-400 shadow-md'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              EN
            </button>
          </div>

          {/* Download PDF Button */}
          <button
            onClick={handleDownloadPdf}
            disabled={isDownloading}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs bg-rose-500/10 hover:bg-rose-500/20 text-rose-400 border border-rose-500/20 hover:border-rose-500/30 rounded-lg transition-all duration-300 disabled:opacity-50 select-none cursor-pointer active:scale-95 shadow-[0_0_15px_rgba(244,63,94,0.05)] font-medium"
          >
            {isDownloading ? (
              <Loader2 className="w-3.5 h-3.5 animate-spin" />
            ) : (
              <Download className="w-3.5 h-3.5" />
            )}
            <span>{t.downloadPdf}</span>
          </button>
        </div>
      </div>

      {/* Main dictionary content area */}
      <div className="flex-1 overflow-auto p-6 space-y-8 custom-scrollbar">
        {schema.tables.map((table) => {
          const relCount = getRelationsCount(table.id);
          return (
            <div 
              key={table.id} 
              className="bg-zinc-950/30 border border-zinc-800/50 rounded-xl p-5 hover:border-zinc-800 transition-all duration-300 shadow-lg relative group overflow-hidden"
            >
              <div className="absolute top-0 right-0 w-[150px] h-[150px] bg-zinc-800/5 rounded-full blur-[60px] pointer-events-none" />
              
              {/* Table Name Header */}
              <div className="flex items-center justify-between mb-4 border-b border-zinc-900 pb-3">
                <div className="flex items-center gap-2.5">
                  <div className="p-1.5 bg-zinc-900 rounded-lg border border-zinc-800 text-zinc-400 group-hover:text-indigo-400 group-hover:border-indigo-500/25 transition-all">
                    <Table2 className="w-4 h-4" />
                  </div>
                  <div>
                    <h4 className="text-sm font-bold text-white tracking-wide">{table.name}</h4>
                    <p className="text-[10px] text-zinc-500 font-mono mt-0.5">ID: {table.id.substring(0, 8)}...</p>
                  </div>
                </div>
                <div className="flex gap-2">
                  <span className="text-[10px] font-semibold px-2.5 py-0.5 rounded-full bg-zinc-900 border border-zinc-800 text-zinc-400">
                    {table.columns.length} {t.columns}
                  </span>
                  {relCount > 0 && (
                    <span className="text-[10px] font-semibold px-2.5 py-0.5 rounded-full bg-indigo-500/10 border border-indigo-500/20 text-indigo-400">
                      {relCount} {t.relations}
                    </span>
                  )}
                </div>
              </div>

              {/* Columns Table */}
              <div className="overflow-x-auto">
                <table className="w-full text-left border-collapse">
                  <thead>
                    <tr className="border-b border-zinc-900 text-zinc-500 text-[10px] uppercase font-bold tracking-wider bg-zinc-950/50">
                      <th className="py-2.5 px-3 rounded-l-lg">{t.colName}</th>
                      <th className="py-2.5 px-3">{t.colType}</th>
                      <th className="py-2.5 px-3">{t.colConstraints}</th>
                      <th className="py-2.5 px-3">{t.colNullable}</th>
                      <th className="py-2.5 px-3 rounded-r-lg">{t.colDefault}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {table.columns.map((col) => (
                      <tr 
                        key={col.id} 
                        className="border-b border-zinc-900/40 text-xs text-zinc-300 hover:bg-zinc-900/20 transition-colors"
                      >
                        <td className="py-2.5 px-3 font-semibold font-mono text-zinc-200">
                          {col.name}
                        </td>
                        <td className="py-2.5 px-3 font-mono text-zinc-400 text-[11px]">
                          {col.type.toUpperCase()}{col.length ? `(${col.length})` : ''}
                        </td>
                        <td className="py-2.5 px-3">
                          <div className="flex flex-wrap gap-1">
                            {col.isPK && (
                              <span className="inline-flex items-center gap-0.5 text-[9px] font-extrabold px-1.5 py-0.5 rounded bg-yellow-500/10 border border-yellow-500/20 text-yellow-500">
                                <Key className="w-2.5 h-2.5" />
                                PK
                              </span>
                            )}
                            {col.isFK && (
                              <span className="inline-flex items-center gap-0.5 text-[9px] font-extrabold px-1.5 py-0.5 rounded bg-blue-500/10 border border-blue-500/20 text-blue-400">
                                <Link2 className="w-2.5 h-2.5" />
                                FK
                              </span>
                            )}
                            {!col.isPK && !col.isFK && (
                              <span className="text-[10px] text-zinc-600">-</span>
                            )}
                          </div>
                        </td>
                        <td className="py-2.5 px-3">
                          <span className={`text-[10px] font-semibold ${col.isNullable ? 'text-zinc-500' : 'text-zinc-400'}`}>
                            {col.isNullable ? t.yes : t.no}
                          </span>
                        </td>
                        <td className="py-2.5 px-3 font-mono text-zinc-500 text-[11px]">
                          {col.defaultValue !== null ? col.defaultValue : 'NULL'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
