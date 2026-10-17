'use client';

import React, { useEffect, useRef, useState } from 'react';
import { Loader2, ZoomIn, ZoomOut, RotateCcw, Code2, Eye, Copy, Check } from 'lucide-react';
import { useToastStore } from '../../store/useToastStore';

type MermaidApi = typeof import('mermaid').default;

let mermaidPromise: Promise<MermaidApi> | null = null;

function loadMermaid(): Promise<MermaidApi> {
  mermaidPromise ??= import('mermaid').then(mod => {
    const mermaid = mod.default;
    mermaid.initialize({
      startOnLoad: false,
      theme: 'dark',
      securityLevel: 'strict',
      fontFamily: 'var(--font-sans), sans-serif',
      er: {
        useMaxWidth: false,
        fontSize: 16,
      },
      flowchart: {
        useMaxWidth: false,
      },
      themeVariables: {
        fontSize: '16px',
        fontFamily: 'var(--font-sans), sans-serif',
      },
    });
    return mermaid;
  });
  return mermaidPromise;
}

interface MermaidPreviewProps {
  mermaidCode: string;
}

export default function MermaidPreview({ mermaidCode }: MermaidPreviewProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const showToast = useToastStore(state => state.showToast);
  const [svgContent, setSvgContent] = useState<string>('');
  const [isRendering, setIsRendering] = useState(false);
  const [zoom, setZoom] = useState(1);
  const [showCode, setShowCode] = useState(false);
  const [copiedCode, setCopiedCode] = useState(false);

  useEffect(() => {
    let cancelled = false;

    const renderMermaid = async () => {
      if (!mermaidCode || !containerRef.current) return;

      setIsRendering(true);
      try {
        const mermaid = await loadMermaid();
        const id = `mermaid-svg-${Date.now()}`;
        const { svg } = await mermaid.render(id, mermaidCode);
        if (!cancelled) setSvgContent(svg);
      } catch (error) {
        console.error('Mermaid rendering failed', error);
        if (!cancelled) {
          setSvgContent(
            '<div class="p-6 text-center"><p class="text-xs font-semibold" style="color:var(--color-danger-text)">An error occurred while generating the Mermaid diagram.</p><p class="text-[11px] text-content-muted mt-1">Try switching diagram types in the toolbar.</p></div>'
          );
        }
      } finally {
        if (!cancelled) setIsRendering(false);
      }
    };

    renderMermaid();
    return () => {
      cancelled = true;
    };
  }, [mermaidCode]);

  const handleZoomIn = () => setZoom(prev => Math.min(6.0, prev + 0.25));
  const handleZoomOut = () => setZoom(prev => Math.max(0.3, prev - 0.25));
  const handleResetZoom = () => setZoom(1);

  const handleCopyCode = async () => {
    try {
      await navigator.clipboard.writeText(mermaidCode);
      setCopiedCode(true);
      showToast('Mermaid markup copied to clipboard!', 'success');
      setTimeout(() => setCopiedCode(false), 2000);
    } catch {
      showToast('Failed to copy markup.', 'error');
    }
  };

  return (
    <div className="w-full h-full bg-surface-900 rounded-[var(--radius-card)] overflow-hidden border border-line-strong flex flex-col relative select-none">
      {/* ── Diagram Floating Toolbar ────────────────────────────────────────── */}
      <div className="shrink-0 h-10 px-3 bg-surface-800/90 border-b border-line-strong flex items-center justify-between gap-2 z-10">
        {/* Left: View Mode Toggle (Visual vs Code) */}
        <div className="flex items-center gap-1 bg-surface-700/80 p-0.5 rounded-[var(--radius-control)] border border-line">
          <button
            onClick={() => setShowCode(false)}
            className={`tap-44 flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer ${
              !showCode ? 'bg-surface-800 text-content-primary shadow-xs' : 'text-content-muted hover:text-content-primary'
            }`}
          >
            <Eye className="w-3.5 h-3.5" />
            <span>Visual</span>
          </button>
          <button
            onClick={() => setShowCode(true)}
            className={`tap-44 flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer ${
              showCode ? 'bg-surface-800 text-content-primary shadow-xs' : 'text-content-muted hover:text-content-primary'
            }`}
          >
            <Code2 className="w-3.5 h-3.5" />
            <span>Markup</span>
          </button>
        </div>

        {/* Right: Zoom controls & Copy */}
        <div className="flex items-center gap-1">
          {showCode ? (
            <button
              onClick={handleCopyCode}
              className="tap-44 flex items-center gap-1.5 h-7 px-2.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-xs text-content-primary border border-line cursor-pointer"
            >
              {copiedCode ? <Check className="w-3 h-3 text-accent-text" /> : <Copy className="w-3 h-3 text-content-muted" />}
              <span className="text-[11px]">{copiedCode ? 'Copied' : 'Copy Markup'}</span>
            </button>
          ) : (
            <div className="flex items-center gap-0.5 bg-surface-700/80 p-0.5 rounded-[var(--radius-control)] border border-line">
              <button
                onClick={handleZoomOut}
                className="tap-44 w-7 h-7 flex items-center justify-center text-content-muted hover:text-content-primary rounded-[var(--radius-control)] cursor-pointer"
                title="Zoom Out"
                aria-label="Zoom Out"
              >
                <ZoomOut className="w-3.5 h-3.5" />
              </button>
              <span className="text-[10px] font-mono text-content-secondary px-1.5 min-w-[44px] text-center">
                {Math.round(zoom * 100)}%
              </span>
              <button
                onClick={handleZoomIn}
                className="tap-44 w-7 h-7 flex items-center justify-center text-content-muted hover:text-content-primary rounded-[var(--radius-control)] cursor-pointer"
                title="Zoom In"
                aria-label="Zoom In"
              >
                <ZoomIn className="w-3.5 h-3.5" />
              </button>
              <button
                onClick={handleResetZoom}
                className="tap-44 w-7 h-7 flex items-center justify-center text-content-muted hover:text-content-primary rounded-[var(--radius-control)] cursor-pointer ml-0.5 border-l border-line"
                title="Reset Zoom (100%)"
                aria-label="Reset Zoom"
              >
                <RotateCcw className="w-3 h-3" />
              </button>
            </div>
          )}
        </div>
      </div>

      {/* ── Content View (Visual Canvas or Raw Markup) ──────────────────────── */}
      {showCode ? (
        <div className="flex-1 min-h-0 overflow-auto p-4 bg-surface-900 select-text">
          <pre className="font-mono text-[11px] text-content-secondary leading-relaxed bg-surface-800 p-4 rounded-[var(--radius-control)] border border-line">
            {mermaidCode}
          </pre>
        </div>
      ) : (
        <div className="flex-1 min-h-0 overflow-auto bg-surface-900 p-6 relative">
          {isRendering && (
            <div className="absolute inset-0 z-20 bg-surface-900/80 backdrop-blur-sm flex items-center justify-center">
              <Loader2 className="w-5 h-5 animate-spin text-accent-text" />
            </div>
          )}
          <style>{`
            .mermaid-preview-container svg {
              min-width: 900px !important;
              width: auto !important;
              height: auto !important;
              max-width: none !important;
            }
            .mermaid-preview-container svg text {
              font-size: 15px !important;
              font-weight: 500 !important;
            }
          `}</style>
          <div className="min-w-fit min-h-fit inline-block">
            <div
              ref={containerRef}
              className="mermaid-preview-container transition-transform duration-100 origin-top-left [&>svg]:max-w-none"
              style={{ transform: `scale(${zoom})` }}
              dangerouslySetInnerHTML={{ __html: svgContent }}
            />
          </div>
        </div>
      )}

      {/* ── Diagram Bottom Status Strip ─────────────────────────────────────── */}
      <div className="shrink-0 h-6 px-3 bg-surface-800/80 border-t border-line flex items-center justify-between text-[10px] font-mono text-content-muted">
        <span>Vector SVG Diagram Renderer</span>
        <span>Interactive Zoom (up to 600%)</span>
      </div>
    </div>
  );
}
