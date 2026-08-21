import React, { useEffect, useRef, useState } from 'react';
import mermaid from 'mermaid';
import { Loader2 } from 'lucide-react';

interface MermaidPreviewProps {
  mermaidCode: string;
}

export default function MermaidPreview({ mermaidCode }: MermaidPreviewProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [svgContent, setSvgContent] = useState<string>('');
  const [isRendering, setIsRendering] = useState(false);

  useEffect(() => {
    mermaid.initialize({
      startOnLoad: false,
      theme: 'dark',
      securityLevel: 'loose',
      fontFamily: 'sans-serif'
    });
  }, []);

  useEffect(() => {
    const renderMermaid = async () => {
      if (!mermaidCode || !containerRef.current) return;
      
      setIsRendering(true);
      try {
        const id = `mermaid-svg-${Date.now()}`;
        const { svg } = await mermaid.render(id, mermaidCode);
        setSvgContent(svg);
      } catch (error) {
        console.error("Mermaid rendering failed", error);
        setSvgContent('<div class="p-4 text-[13px]" style="color:#e08787">An error occurred while generating the Mermaid diagram.</div>');
      } finally {
        setIsRendering(false);
      }
    };

    renderMermaid();
  }, [mermaidCode]);

  return (
    <div className="w-full h-full bg-surface-700 rounded-lg overflow-hidden border border-surface-500 relative">
      {isRendering && (
        <div className="absolute inset-0 z-20 bg-surface-900/80 backdrop-blur-sm flex items-center justify-center">
          <Loader2 className="w-4 h-4 animate-spin text-content-muted" />
        </div>
      )}
      <div
        ref={containerRef}
        className="w-full h-full p-3 overflow-auto flex items-center justify-center [&>svg]:max-w-none"
        dangerouslySetInnerHTML={{ __html: svgContent }}
      />
    </div>
  );
}
