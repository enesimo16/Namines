import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { API_BASE_URL } from '../../../lib/apiConfig';

/**
 * Paylaşılan şema sayfasının meta etiketleri (new-phase/23-GTM.md §2 Döngü 1).
 *
 * <b>Neden ayrı bir layout:</b> sayfanın kendisi `'use client'` — React Flow
 * tarayıcıda çalışıyor. İstemci bileşenleri `generateMetadata` sağlayamaz, ve
 * sosyal ağ tarayıcıları JavaScript ÇALIŞTIRMAZ: istemcide eklenen bir meta
 * etiketi hiç görülmez. Sunucu bileşeni olan bu layout, etiketleri HTML'e
 * gerçekten koyan tek yer.
 *
 * Önizlemesiz bir bağlantı akışta düz bir URL olarak görünür ve tıklanmaz;
 * viral döngünün tamamı bu etiketlere bağlı.
 */

interface ShareMeta {
  name: string;
  engine: string;
  tables: number;
  relations: number;
  description: string;
}

async function fetchMeta(token: string): Promise<ShareMeta | null> {
  try {
    const response = await fetch(`${API_BASE_URL}/share/meta/${encodeURIComponent(token)}`, {
      // Paylaşılan şema nadiren değişir ama tamamen dondurmak, düzeltilen bir
      // adın önizlemede eski kalması demek olurdu.
      next: { revalidate: 300 },
    });
    if (!response.ok) return null;
    return (await response.json()) as ShareMeta;
  } catch {
    // Backend ulaşılamazsa sayfa yine açılmalı; yalnızca zengin önizleme kaybolur.
    return null;
  }
}

export async function generateMetadata(
  { params }: { params: Promise<{ token: string }> },
): Promise<Metadata> {
  const { token } = await params;
  const meta = await fetchMeta(token);

  if (!meta) {
    return {
      title: 'Shared schema — Namines',
      // noindex: var olmayan ya da kaldırılmış bir paylaşımın arama sonuçlarına
      // düşmesi, tıklayan herkesi boş bir sayfaya götürür.
      robots: { index: false },
    };
  }

  const title = `${meta.name} — database schema`;
  const image = `${API_BASE_URL}/share/og/${encodeURIComponent(token)}.svg`;

  return {
    title,
    description: meta.description,
    openGraph: {
      title,
      description: meta.description,
      images: [{ url: image, width: 1200, height: 630 }],
      type: 'article',
    },
    twitter: {
      card: 'summary_large_image',
      title,
      description: meta.description,
      images: [image],
    },
  };
}

export default function ShareLayout({ children }: { children: ReactNode }) {
  return children;
}
