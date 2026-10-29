'use client';

import { useEffect } from 'react';
import { useFeatureStore } from '../../store/useFeatureStore';

/** Bayrakların yenilenme aralığı. Admin bir özelliği kapattığında açık sekmeler en geç bu sürede öğrenir. */
export const FEATURE_FLAGS_REFRESH_MS = 60_000;

/**
 * Özellik bayraklarını uygulama açılışında bir kez çeker, sonra periyodik ve
 * sekme tekrar görünür olunca yeniler.
 *
 * Bu olmadan `flags` boş kalır ve `isFeatureEnabled` tanımsız her anahtar için
 * `true` döndüğünden (fail-open) admin'in kapattığı özellikler istemcide hiç
 * kapanmazdı. Görsel çıktısı yoktur.
 */
export default function FeatureFlagsLoader() {
  useEffect(() => {
    const refresh = () => { void useFeatureStore.getState().fetchFeatures(); };
    refresh();

    const interval = setInterval(() => {
      if (document.visibilityState === 'visible') refresh();
    }, FEATURE_FLAGS_REFRESH_MS);
    const onVisible = () => {
      if (document.visibilityState === 'visible') refresh();
    };
    document.addEventListener('visibilitychange', onVisible);

    return () => {
      clearInterval(interval);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, []);

  return null;
}
