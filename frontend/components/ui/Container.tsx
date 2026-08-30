import React from 'react';

/**
 * Sayfa içeriğini ortalayan tek genişlik primitifi.
 *
 * <b>Neden gerekti:</b> denetimde `max-w-*` kullanımı sayıldı — **23 farklı
 * değer**. Her sayfa kendi genişliğini seçiyordu (iniş `max-w-4xl`, demo
 * `max-w-6xl`, modal `max-w-2xl`) ve aralarında hiçbir ilişki yoktu. Vercel'de
 * ölçülen karşılığı: tek bir dış container (1400px) + tek bir okuma sütunu
 * (~960px).
 *
 * Üç genişlik yeterli çünkü projede üç gerçek içerik türü var:
 * okunacak metin, uygulama arayüzü, ve geniş veri (tablo/canvas).
 */

type Width = 'prose' | 'app' | 'wide';

const WIDTH_CLASS: Record<Width, string> = {
  // Token'lar globals.css'te: --w-prose / --w-app / --w-wide
  prose: 'max-w-[var(--w-prose)]',
  app: 'max-w-[var(--w-app)]',
  wide: 'max-w-[var(--w-wide)]',
};

interface ContainerProps extends React.HTMLAttributes<HTMLDivElement> {
  width?: Width;
  /** Yatay iç boşluğu kapatır — kenardan kenara uzanan şeritler için. */
  bleed?: boolean;
  as?: 'div' | 'section' | 'main' | 'header' | 'footer';
}

export default function Container({
  width = 'app',
  bleed = false,
  as: Tag = 'div',
  className = '',
  children,
  ...rest
}: ContainerProps) {
  return (
    <Tag
      className={`mx-auto w-full ${WIDTH_CLASS[width]} ${bleed ? '' : 'px-4 sm:px-6 lg:px-8'} ${className}`}
      {...rest}
    >
      {children}
    </Tag>
  );
}
