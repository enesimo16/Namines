'use client';

import { useState, useCallback } from 'react';
import * as htmlToImage from 'html-to-image';

interface ExportOptions {
  /** File name (without extension). Default: 'namines-diagram' */
  fileName?: string;
  /** Pixel ratio — higher value = sharper image. Default: 2 */
  pixelRatio?: number;
}

interface UseCanvasExportReturn {
  isExporting: boolean;
  exportAsPng: (options?: ExportOptions) => Promise<void>;
  exportAsJpeg: (options?: ExportOptions) => Promise<void>;
}

/** Downloads the React Flow canvas as PNG / JPEG. */
export function useCanvasExport(): UseCanvasExportReturn {
  const [isExporting, setIsExporting] = useState(false);

  /** Finds the React Flow render viewport in DOM. */
  const getViewport = (): HTMLElement | null => {
    return document.querySelector('.react-flow__viewport') as HTMLElement | null;
  };

  /** Common download helper */
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
      console.warn('[useCanvasExport] .react-flow__viewport not found.');
      return;
    }
    const fileName = `${options?.fileName ?? 'namines-diagram'}.png`;
    setIsExporting(true);
    try {
      const dataUrl = await htmlToImage.toPng(viewport, {
        pixelRatio: options?.pixelRatio ?? 2,
        backgroundColor: '#09090b', // zinc-950
        style: { borderRadius: '0' },
        skipFonts: true, // Prevents CORS cssRules error
        fontEmbedCSS: '', 
      });
      triggerDownload(dataUrl, fileName);
    } catch (err) {
      console.error('[useCanvasExport] PNG export error:', err);
    } finally {
      setIsExporting(false);
    }
  }, []);

  const exportAsJpeg = useCallback(async (options?: ExportOptions) => {
    const viewport = getViewport();
    if (!viewport) {
      console.warn('[useCanvasExport] .react-flow__viewport not found.');
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
      console.error('[useCanvasExport] JPEG export error:', err);
    } finally {
      setIsExporting(false);
    }
  }, []);

  return { isExporting, exportAsPng, exportAsJpeg };
}
