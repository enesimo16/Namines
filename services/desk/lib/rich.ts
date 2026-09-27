import { createElement, Fragment, type ReactNode } from 'react';

/**
 * Sözlük metnindeki `<b>`, `<strong>`, `<i>`, `<code>` işaretlerini React
 * öğesine çevirir.
 *
 * <b>Neden:</b> Desk'in açıklama metinleri cümlenin ortasında vurgu taşıyor
 * ("<b>Desk never runs DDL from here</b> — ..."). Cümleyi vurgu noktalarından
 * ayrı anahtarlara bölmek, çevirmeni kelime sırasına kilitlerdi — Türkçede
 * vurgulanan kısım cümlenin başka bir yerine düşüyor.
 *
 * `dangerouslySetInnerHTML` BİLİNÇLİ OLARAK kullanılmıyor: yalnızca bu dört
 * etiket tanınıyor, gerisi düz metin olarak kalıyor. Metin statik sözlükten
 * geliyor ama bir yer tutucuya kullanıcı verisi (proje adı) girebilir.
 */
const TAGS = ['b', 'strong', 'i', 'code'] as const;
type Tag = typeof TAGS[number];
const PATTERN = /<(b|strong|i|code)>([\s\S]*?)<\/\1>/g;

export function rich(text: string): ReactNode {
  const parts: ReactNode[] = [];
  let last = 0;
  let index = 0;
  for (const match of text.matchAll(PATTERN)) {
    const start = match.index ?? 0;
    if (start > last) parts.push(text.slice(last, start));
    parts.push(createElement(match[1] as Tag, { key: index++ }, match[2]));
    last = start + match[0].length;
  }
  if (last < text.length) parts.push(text.slice(last));
  return createElement(Fragment, null, ...parts);
}

/** Test için: işaretleri atıp düz metni döndürür. */
export function stripRich(text: string): string {
  return text.replace(PATTERN, '$2');
}

export { TAGS };
