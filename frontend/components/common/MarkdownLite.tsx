import React from 'react';

/**
 * Bağımlılıksız, hafif markdown render'ı. AI'nin ürettiği README/açıklama metni
 * ham "# Başlık" / "**kalın**" gibi düz metin olarak <pre> içinde gösteriliyordu —
 * okunaksızdı. Tam bir markdown kütüphanesi eklemek yerine (bkz. FRONTEND.md §5,
 * yeni bağımlılık önce mevcut araçlarla çözülüp çözülemeyeceği kontrol edilir)
 * yalnızca AI çıktısında gerçekten görülen alt küme desteklenir: başlıklar,
 * madde işaretleri, numaralı listeler, satır-içi/blok kod, kalın metin.
 */

function renderInline(text: string, keyPrefix: string): React.ReactNode[] {
  const parts = text.split(/(\*\*[^*]+\*\*|`[^`]+`)/g).filter(Boolean);
  return parts.map((part, i) => {
    if (part.startsWith('**') && part.endsWith('**')) {
      return <strong key={`${keyPrefix}-${i}`} className="text-content-primary font-semibold">{part.slice(2, -2)}</strong>;
    }
    if (part.startsWith('`') && part.endsWith('`')) {
      return (
        <code key={`${keyPrefix}-${i}`} className="bg-surface-600 border border-content-primary/10 rounded-[var(--radius-control)] px-1.5 py-0.5 text-[11px] font-mono text-accent-text">
          {part.slice(1, -1)}
        </code>
      );
    }
    return <React.Fragment key={`${keyPrefix}-${i}`}>{part}</React.Fragment>;
  });
}

export default function MarkdownLite({ text }: { text: string }) {
  const lines = text.split('\n');
  const blocks: React.ReactNode[] = [];
  let listBuffer: { ordered: boolean; items: string[] } | null = null;
  let codeBuffer: string[] | null = null;

  const flushList = (key: string) => {
    if (!listBuffer) return;
    const ListTag = listBuffer.ordered ? 'ol' : 'ul';
    blocks.push(
      <ListTag key={key} className={`space-y-1 pl-5 my-2 text-xs text-content-secondary leading-relaxed ${listBuffer.ordered ? 'list-decimal' : 'list-disc'}`}>
        {listBuffer.items.map((item, i) => (
          <li key={i}>{renderInline(item, `${key}-li-${i}`)}</li>
        ))}
      </ListTag>
    );
    listBuffer = null;
  };

  lines.forEach((line, idx) => {
    const key = `l-${idx}`;

    if (line.trim().startsWith('```')) {
      if (codeBuffer === null) {
        flushList(`${key}-flush`);
        codeBuffer = [];
      } else {
        blocks.push(
          <pre key={key} className="bg-surface-700 border border-content-primary/8 rounded-[var(--radius-control)] p-3 my-2 overflow-x-auto">
            <code className="text-[11px] font-mono text-content-secondary whitespace-pre">{codeBuffer.join('\n')}</code>
          </pre>
        );
        codeBuffer = null;
      }
      return;
    }
    if (codeBuffer !== null) {
      codeBuffer.push(line);
      return;
    }

    const heading = line.match(/^(#{1,3})\s+(.*)$/);
    if (heading) {
      flushList(`${key}-flush`);
      const level = heading[1].length;
      const cls = level === 1
        ? 'text-sm font-bold text-content-primary font-mono mt-3 mb-1.5'
        : level === 2
          ? 'text-[13px] font-bold text-content-primary font-mono mt-2.5 mb-1'
          : 'text-xs font-bold text-content-secondary uppercase tracking-wide mt-2 mb-1';
      blocks.push(<p key={key} className={cls}>{renderInline(heading[2], key)}</p>);
      return;
    }

    const bullet = line.match(/^\s*[-*]\s+(.*)$/);
    const numbered = line.match(/^\s*\d+\.\s+(.*)$/);
    if (bullet || numbered) {
      const ordered = !!numbered;
      const content = (bullet ? bullet[1] : numbered![1]);
      if (!listBuffer || listBuffer.ordered !== ordered) {
        flushList(`${key}-flush`);
        listBuffer = { ordered, items: [] };
      }
      listBuffer.items.push(content);
      return;
    }

    flushList(`${key}-flush`);
    if (line.trim() === '') {
      blocks.push(<div key={key} className="h-1.5" />);
    } else {
      blocks.push(<p key={key} className="text-xs text-content-secondary leading-relaxed">{renderInline(line, key)}</p>);
    }
  });
  flushList('final-flush');

  return <div className="space-y-0.5">{blocks}</div>;
}
