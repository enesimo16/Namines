import React, { useEffect, useRef, useState } from 'react';
import { Loader2 } from 'lucide-react';

/**
 * Mermaid DİNAMİK yükleniyor (B-36 / PERF-004).
 *
 * ÖLÇÜM: mermaid'in ayrıştırıcısı 102 KB'lik bir chunk olarak `/compile`
 * rotasının İLK yüklemesindeydi (toplam 1830 KB). Oysa bu bileşen yalnızca
 * "Mermaid ER" sekmesi açıldığında render ediliyor — DDL/EF/Prisma
 * sekmelerinde çalışan hiçbir kod mermaid'e ihtiyaç duymuyor.
 *
 * Yükleme TEK KEZ yapılıyor ve promise önbelleğe alınıyor: iki sekme arasında
 * gidip gelmek kütüphaneyi yeniden indirmiyor. `initialize` de yalnızca ilk
 * yüklemede çağrılıyor — önceki sürüm onu her mount'ta yeniden çağırıyordu.
 */
type MermaidApi = typeof import('mermaid').default;

let mermaidPromise: Promise<MermaidApi> | null = null;

function loadMermaid(): Promise<MermaidApi> {
  mermaidPromise ??= import('mermaid').then(mod => {
    const mermaid = mod.default;
    mermaid.initialize({
      startOnLoad: false,
      theme: 'dark',
      // 'strict' — 'loose' DEĞİL. Diyagram metni şemadan türüyor: tablo ve
      // kolon adları, yani kullanıcının (ve AI'ın) yazdığı metin. 'loose' modda
      // Mermaid etiketlerdeki HTML'i sanitize etmiyor ve tıklama işleyicilerine
      // izin veriyor; üretilen SVG de aşağıda dangerouslySetInnerHTML ile
      // basıldığı için bu doğrudan XSS demek.
      //
      // Şemalar /share/[token] ile PAYLAŞILABİLDİĞİ için bu saklı bir XSS
      // olurdu: bir kullanıcının şeması, onu görüntüleyen başkasının
      // tarayıcısında kod çalıştırırdı.
      securityLevel: 'strict',
      fontFamily: 'sans-serif',
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
  const [svgContent, setSvgContent] = useState<string>('');
  const [isRendering, setIsRendering] = useState(false);

  useEffect(() => {
    // `cancelled`: kullanıcı hızlıca sekme değiştirirse geç gelen bir render
    // sonucu, artık görünmeyen (ya da başka koda ait) bir SVG'yi basmasın.
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
        console.error("Mermaid rendering failed", error);
        if (!cancelled) {
          setSvgContent('<div class="p-4 text-[13px]" style="color:var(--color-danger-text)">An error occurred while generating the Mermaid diagram.</div>');
        }
      } finally {
        if (!cancelled) setIsRendering(false);
      }
    };

    renderMermaid();
    return () => { cancelled = true; };
  }, [mermaidCode]);

  return (
    <div className="w-full h-full bg-surface-700 rounded-[var(--radius-control)] overflow-hidden border border-surface-500 relative">
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
