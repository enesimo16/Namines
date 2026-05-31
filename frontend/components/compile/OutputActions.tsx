import React from 'react';
import { Download, Copy, Package } from 'lucide-react';
import { schemaService } from '../../services/api';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';

interface OutputActionsProps {
  sql: string;
  dbType: string;
}

export default function OutputActions({ sql, dbType }: OutputActionsProps) {
  const { schema, projectName } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);

  const handleCopy = () => {
    navigator.clipboard.writeText(sql);
    showToast("SQL code successfully copied to clipboard!", "success");
  };

  const handleDownloadSql = () => {
    const blob = new Blob([sql], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${schema?.name || 'database'}_schema.sql`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  const handleDownloadEfCore = async () => {
    if (!schema) return;
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
    } catch (error) {
      console.error("Failed to download EF Core zip", error);
      showToast("An error occurred while downloading EF Core files.", "error");
    }
  };

  const handleDownloadPdf = async () => {
    if (!schema) return;
    try {
      const pdfBlob = await schemaService.generatePdf(schema, projectName);
      const url = URL.createObjectURL(pdfBlob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${projectName.replace(/\s+/g, '_')}_DataDictionary.pdf`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (error) {
      console.error("Failed to download PDF", error);
      showToast("An error occurred while generating PDF.", "error");
    }
  };

  const handleDownloadReadme = async () => {
    if (!schema) return;
    try {
      const readmeText = await schemaService.generateReadme(schema);
      const blob = new Blob([readmeText], { type: 'text/markdown' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `README.md`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (error) {
      console.error("Failed to download Readme", error);
      showToast("An error occurred while generating README.", "error");
    }
  };

  return (
    <div className="flex flex-col gap-3 w-full">
      <button 
        onClick={handleCopy}
        className="flex items-center justify-center gap-2 w-full py-3 bg-zinc-800 hover:bg-zinc-750 text-zinc-200 rounded-lg transition-colors border border-zinc-700 cursor-pointer"
      >
        <Copy className="w-4 h-4" />
        Copy SQL
      </button>

      <button 
        onClick={handleDownloadSql}
        className="flex items-center justify-center gap-2 w-full py-3 bg-blue-600/20 hover:bg-blue-600/30 text-blue-400 rounded-lg transition-colors border border-blue-500/30 cursor-pointer"
      >
        <Download className="w-4 h-4" />
        Download SQL (.sql)
      </button>

      <button 
        onClick={handleDownloadEfCore}
        className="flex items-center justify-center gap-2 w-full py-3 bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg transition-colors shadow-lg shadow-indigo-500/20 cursor-pointer"
      >
        <Package className="w-4 h-4" />
        Download EF Core (.zip)
      </button>

      <button 
        onClick={handleDownloadPdf}
        className="flex items-center justify-center gap-2 w-full py-3 bg-rose-600/20 hover:bg-rose-600/30 text-rose-400 rounded-lg transition-colors border border-rose-500/30 cursor-pointer"
      >
        <Download className="w-4 h-4" />
        Download Data Dictionary (.pdf)
      </button>

      <button 
        onClick={handleDownloadReadme}
        className="flex items-center justify-center gap-2 w-full py-3 bg-zinc-800 hover:bg-zinc-750 text-zinc-300 rounded-lg transition-colors border border-zinc-700 cursor-pointer"
      >
        <Download className="w-4 h-4" />
        Download README.md
      </button>
    </div>
  );
}
