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
        setSvgContent('<div class="text-red-400 p-4">Mermaid diyagramı oluşturulurken hata oluştu.</div>');
      } finally {
        setIsRendering(false);
      }
    };

    renderMermaid();
  }, [mermaidCode]);

  return (
    <div className="w-full h-full bg-[#1d1f21] rounded-xl overflow-hidden border border-zinc-800 shadow-2xl relative flex items-center justify-center">
      {isRendering && (
        <div className="absolute inset-0 z-20 bg-zinc-950/80 backdrop-blur-sm flex items-center justify-center">
          <Loader2 className="w-8 h-8 animate-spin text-indigo-400" />
        </div>
      )}
      <div 
        ref={containerRef}
        className="w-full h-full p-4 overflow-auto custom-scrollbar flex items-center justify-center [&>svg]:max-w-none"
        dangerouslySetInnerHTML={{ __html: svgContent }}
      />
    </div>
  );
}
