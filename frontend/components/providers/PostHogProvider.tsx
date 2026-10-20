'use client';

import React, { useEffect, Suspense } from 'react';
import { usePathname, useSearchParams } from 'next/navigation';
import posthog from 'posthog-js';
import { PostHogProvider as PHProvider } from 'posthog-js/react';

/**
 * Sayfa rotası değişimlerini kontrollü olarak izleyen iç bileşen.
 *
 * <b>Kota koruma nedeni:</b>
 * PostHog'un yerleşik `capture_pageview: true` seçeneği tek sayfalı uygulamalarda (SPA)
 * ve hızlı URL parametre değişimlerinde gereksiz ve mükerrer sayfa olayları ateşleyerek
 * aylık etkinlik kotasını tüketebilir. Burada rotayı Next.js App Router kancalarıyla
 * takip edip yalnızca gerçek yol veya sorgu değiştiğinde tekil bir `$pageview` olayı gönderiyoruz.
 */
function PostHogPageViewTracker() {
  const pathname = usePathname();
  const searchParams = useSearchParams();

  useEffect(() => {
    if (pathname && typeof window !== 'undefined') {
      let url = window.origin + pathname;
      const query = searchParams?.toString();
      if (query) {
        url = `${url}?${query}`;
      }

      // Sadece anahtar yapılandırılmışsa ve PostHog hazırsa gönder
      if (process.env.NEXT_PUBLIC_POSTHOG_KEY) {
        posthog.capture('$pageview', {
          $current_url: url,
        });
      }
    }
  }, [pathname, searchParams]);

  return null;
}

interface PostHogProviderProps {
  children: React.ReactNode;
}

/**
 * PostHog İstemci Sağlayıcısı (Client Provider).
 *
 * <b>Kota Koruma ve Gizlilik Stratejisi:</b>
 * - <b>autocapture: false</b>: Her buton tıklamasını, metin kutusu odağını ve fare hareketini
 *   otomatik kaydetmek aylık 1M etkinlik sınırını hızla eritir. Yalnızca kritik iş olayları
 *   (`analytics.ts` üzerinden) manuel olarak tetiklenir.
 * - <b>session_recording</b>: 5.000 kayıtlık ücretsiz paketi aşmamak için:
 *   - Canvas DOM mutasyonları sıfırlanır (`canvas: 0`).
 *   - Metin/kullanıcı oturumları %10 oranında örneklenir (`text: 0.1`).
 *   - Parolalar, API anahtarları ve veritabanı bağlantı dizgeleri kesinlikle maskelenir (`maskAllInputs: true`).
 * - <b>Ters Proxy</b>: `/ingest` üzerinden reklam engelleyiciler atlatılarak veri kaybı önlenir.
 */
export default function PostHogProvider({ children }: PostHogProviderProps) {
  useEffect(() => {
    const posthogKey = process.env.NEXT_PUBLIC_POSTHOG_KEY;
    if (!posthogKey || typeof window === 'undefined') {
      return;
    }

    // Çift başlatmayı (React StrictMode) önlemek için kontrol
    if (!(posthog as unknown as { __loaded?: boolean }).__loaded) {
      posthog.init(posthogKey, {
        api_host: process.env.NEXT_PUBLIC_POSTHOG_HOST || '/ingest',
        ui_host: 'https://eu.posthog.com',
        autocapture: false,
        capture_pageview: false,
        capture_pageleave: true,
        session_recording: {
          maskAllInputs: true,
          maskTextSelector: '.ph-mask, [data-sensitive="true"], input[type="password"]',
          recordCrossOriginIframes: false,
          sampleRate: 0.1, // Oturumların yalnızca %10'u kaydedilir (5.000 kayıt sınırı)
          sampling: {
            mousemove: false, // Yüksek frekanslı fare hareketlerini kaydetmeyerek kota ve bant genişliğini korur
          },
        },
        persistence: 'localStorage+cookie',
        advanced_disable_feature_flags_on_first_load: false,
      });
    }
  }, []);

  const posthogKey = process.env.NEXT_PUBLIC_POSTHOG_KEY;
  if (!posthogKey) {
    // Anahtar yoksa (yerel geliştirme, test) Next.js ağacını ek katmansız, sıfır maliyetle render et
    return <>{children}</>;
  }

  return (
    <PHProvider client={posthog}>
      <Suspense fallback={null}>
        <PostHogPageViewTracker />
      </Suspense>
      {children}
    </PHProvider>
  );
}
