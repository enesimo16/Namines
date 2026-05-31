'use client';

import { useState, useCallback } from 'react';
import * as htmlToImage from 'html-to-image';

interface ExportOptions {
  /** Dosya adı (uzantısız). Varsayılan: 'namines-diagram' */
  fileName?: string;
  /** Pixel ratio — yüksek değer = daha keskin görsel. Varsayılan: 2 */
  pixelRatio?: number;
}

interface UseCanvasExportReturn {
  isExporting: boolean;
  exportAsPng: (options?: ExportOptions) => Promise<void>;
  exportAsJpeg: (options?: ExportOptions) => Promise<void>;
}

/** React Flow canvas'ını PNG / JPEG olarak indirir. */
export function useCanvasExport(): UseCanvasExportReturn {
  const [isExporting, setIsExporting] = useState(false);

  /** React Flow'un render viewport'unu DOM'dan bulur. */
  const getViewport = (): HTMLElement | null => {
    return document.querySelector('.react-flow__viewport') as HTMLElement | null;
  };

  /** Ortak indirme yardımcısı */
  const triggerDownload = (dataUrl: string, fileName: string) => {
    const link = document.createElement('a');
    link.href = dataUrl;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  const exportAsPng = useCallback(async (options?: ExportOptions) => {
    const viewport = getViewport();
    if (!viewport) {
      console.warn('[useCanvasExport] .react-flow__viewport bulunamadı.');
      return;
    }
    const fileName = `${options?.fileName ?? 'namines-diagram'}.png`;
    setIsExporting(true);
    try {
      const dataUrl = await htmlToImage.toPng(viewport, {
        pixelRatio: options?.pixelRatio ?? 2,
        backgroundColor: '#09090b', // zinc-950
        style: { borderRadius: '0' },
        skipFonts: true, // CORS cssRules hatasını önler
        fontEmbedCSS: '', 
      });
      triggerDownload(dataUrl, fileName);
    } catch (err) {
      console.error('[useCanvasExport] PNG export hatası:', err);
    } finally {
      setIsExporting(false);
    }
  }, []);

  const exportAsJpeg = useCallback(async (options?: ExportOptions) => {
    const viewport = getViewport();
    if (!viewport) {
      console.warn('[useCanvasExport] .react-flow__viewport bulunamadı.');
      return;
    }
    const fileName = `${options?.fileName ?? 'namines-diagram'}.jpg`;
    setIsExporting(true);
    try {
      const dataUrl = await htmlToImage.toJpeg(viewport, {
        pixelRatio: options?.pixelRatio ?? 2,
        backgroundColor: '#09090b',
        quality: 0.92,
        skipFonts: true,
        fontEmbedCSS: '',
      });
      triggerDownload(dataUrl, fileName);
    } catch (err) {
      console.error('[useCanvasExport] JPEG export hatası:', err);
    } finally {
      setIsExporting(false);
    }
  }, []);

  return { isExporting, exportAsPng, exportAsJpeg };
}
