'use client';

import React, { useState } from 'react';
import { Download, Table2, Key, Link2 } from 'lucide-react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { Panel, PanelBar, ActionButton, PanelEmpty, Segmented, StatStrip } from './PanelKit';

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
      
      const successMsg = "Data dictionary PDF successfully downloaded!";
      showToast(successMsg, "success");
    } catch (error) {
      console.error("Failed to download PDF", error);
      const errorMsg = "An error occurred while generating PDF.";
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
      <Panel scroll={false}>
        <PanelEmpty icon={Table2} title={t.noTables} hint={t.noTablesDesc} />
      </Panel>
    );
  }

  const totalColumns = schema.tables.reduce((s, tb) => s + tb.columns.length, 0);

  return (
    <Panel scroll={false}>
      <div className="h-full flex flex-col">
        <PanelBar
          left={<span className="text-[11px] font-mono text-content-secondary truncate">{t.fileTitle}</span>}
        >
          <Segmented
            ariaLabel="Document language"
            value={lang}
            onChange={setLang}
            options={[{ value: 'tr' as Lang, label: 'TR' }, { value: 'en' as Lang, label: 'EN' }]}
          />
          <ActionButton icon={Download} onClick={handleDownloadPdf} busy={isDownloading} tone="primary">
            PDF
          </ActionButton>
        </PanelBar>

        <StatStrip
          items={[
            { label: t.columns, value: totalColumns },
            { label: t.relations, value: schema.relations.length },
          ]}
        />

        {/* Kart-başına-tablo yerine TEK sürekli tablo: grup başlıkları sticky,
            böylece uzun sözlükte hangi tabloya bakıldığı kaybolmuyor. */}
        <div className="flex-1 min-h-0 overflow-auto">
          <table className="w-full text-left border-collapse">
            <thead className="sticky top-0 z-10">
              <tr className="bg-surface-800 text-content-muted text-micro uppercase font-bold tracking-wider">
                <th className="py-1.5 px-3 font-semibold">{t.colName}</th>
                <th className="py-1.5 px-3 font-semibold">{t.colType}</th>
                <th className="py-1.5 px-3 font-semibold">{t.colConstraints}</th>
                <th className="py-1.5 px-3 font-semibold">{t.colNullable}</th>
                <th className="py-1.5 px-3 font-semibold">{t.colDefault}</th>
              </tr>
            </thead>
            <tbody>
              {schema.tables.map((table) => {
                const relCount = getRelationsCount(table.id);
                return (
                  <React.Fragment key={table.id}>
                    <tr className="sticky top-[26px] z-[9]">
                      <td colSpan={5} className="bg-surface-600 border-y border-surface-500 py-1.5 px-3">
                        <div className="flex items-center gap-2">
                          <Table2 className="w-3.5 h-3.5 text-accent-text shrink-0" />
                          <span className="text-[12px] font-semibold text-content-primary font-mono">{table.name}</span>
                          <span className="text-[10px] text-content-muted">
                            {table.columns.length} {t.columns}
                            {relCount > 0 && ` · ${relCount} ${t.relations}`}
                          </span>
                        </div>
                      </td>
                    </tr>
                    {table.columns.map((col) => (
                      <tr key={col.id} className="border-b border-surface-500/40 hover:bg-surface-600/50 transition-colors">
                        <td className="py-1.5 px-3 font-mono text-[11px] text-content-primary">{col.name}</td>
                        <td className="py-1.5 px-3 font-mono text-[11px] text-content-muted">
                          {col.type.toUpperCase()}{col.length ? `(${col.length})` : ''}
                        </td>
                        <td className="py-1.5 px-3">
                          <div className="flex flex-wrap gap-1">
                            {col.isPK && (
                              <span className="inline-flex items-center gap-0.5 text-micro font-bold px-1.5 py-0.5 rounded bg-accent-subtle text-accent-text">
                                <Key className="w-2.5 h-2.5" /> PK
                              </span>
                            )}
                            {col.isFK && (
                              <span className="inline-flex items-center gap-0.5 text-micro font-bold px-1.5 py-0.5 rounded bg-surface-500/30 text-content-muted">
                                <Link2 className="w-2.5 h-2.5" /> FK
                              </span>
                            )}
                            {!col.isPK && !col.isFK && <span className="text-[10px] text-content-muted">—</span>}
                          </div>
                        </td>
                        <td className="py-1.5 px-3 text-[10px] font-medium text-content-muted">
                          {col.isNullable ? t.yes : t.no}
                        </td>
                        <td className="py-1.5 px-3 font-mono text-[11px] text-content-muted">
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
      </div>
    </Panel>
  );
}
