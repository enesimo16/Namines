'use client';

import { useEffect } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { useToastStore } from '../../store/useToastStore';
import { useQuotaStore } from '../../store/useQuotaStore';

/**
 * Stripe ödeme dönüşünü (`/?upgrade=success|canceled`) her rotada yakalar.
 * Backend başarı URL'sini kök sayfaya veriyor; eskiden dinleyici yalnızca /new'deydi,
 * kullanıcı landing'e düşünce mesaj hiç görünmüyordu. Görsel çıktısı yoktur.
 */
export default function UpgradeReturnHandler() {
  const router = useRouter();
  const pathname = usePathname();
  const showToast = useToastStore((s) => s.showToast);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const upgrade = params.get('upgrade');
    if (upgrade !== 'success' && upgrade !== 'canceled') return;

    if (upgrade === 'success') {
      showToast('Subscription activated! Your plan is being updated.', 'success');
      // Plan değişikliği webhook ile gelir; kotayı tazele.
      useQuotaStore.getState().fetchQuota().catch(() => {});
    } else {
      showToast('Upgrade canceled. You can try again anytime.', 'info');
    }

    params.delete('upgrade');
    const rest = params.toString();
    router.replace(rest ? `${pathname}?${rest}` : pathname);
  }, [pathname, router, showToast]);

  return null;
}
