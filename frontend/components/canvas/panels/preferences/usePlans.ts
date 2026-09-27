import { useEffect, useState } from 'react';
import api, { authService, type BillingInterval, type PlanPricing } from '../../../../services/api';
import { useToastStore } from '../../../../store/useToastStore';

export type PlanId = 'pro' | 'team';
export type PriceView = { amount: number; total: number; available: boolean } | null;

export function usePlans(isOpen: boolean, isAuthenticated: boolean) {
  const showToast = useToastStore(state => state.showToast);
  // Fiyatlar sunucudan; liste alınamazsa kartlar fiyatsız gösterilmiyor —
  // tutarı bilmeden "Upgrade" düğmesine bastırmak, kullanıcıyı ne ödeyeceğini
  // bilmediği bir ödeme akışına sokmak olurdu.
  const [plans, setPlans] = useState<PlanPricing[] | null>(null);
  const [interval, setInterval] = useState<BillingInterval>('monthly');
  const [subscriptionStatus, setSubscriptionStatus] = useState<string | null>(null);
  const [planTier, setPlanTier] = useState<string | null>(null);
  const [isUpgrading, setIsUpgrading] = useState(false);

  useEffect(() => {
    if (!isOpen) return;
    // Fiyat listesi giriş gerektirmiyor — çıkış yapmış biri de ne ödeyeceğini
    // görebilmeli.
    authService.getPlans()
      .then(setPlans)
      .catch(() => setPlans(null));

    if (!isAuthenticated) return;
    authService.getSubscriptionStatus()
      .then(data => { if (data) setSubscriptionStatus(data.status ?? null); })
      .catch(() => {});
    // Pro/Team ayrımı subscription/status'te yok (yalnızca active/inactive
    // diyor); hangi kart "You are on this plan" göstersin, quota/status'teki
    // Plan alanından geliyor.
    api.get('/quota/status')
      .then(res => setPlanTier(res.data?.plan ?? null))
      .catch(() => {});
  }, [isOpen, isAuthenticated]);

  const manageSubscription = async () => {
    setIsUpgrading(true);
    try {
      if (!isAuthenticated) { showToast('Please log in to manage billing.', 'warning'); return; }
      const data = await authService.createBillingPortal();
      if (data.url) window.open(data.url, '_blank');
    } catch {
      showToast('Could not open billing portal. Please try again.', 'error');
    } finally {
      setIsUpgrading(false);
    }
  };

  const upgrade = async (plan: PlanId = 'pro') => {
    setIsUpgrading(true);
    try {
      if (!isAuthenticated) { showToast('Please log in to upgrade.', 'warning'); return; }
      const data = await authService.createCheckoutSession(plan, interval);
      if (data.redirect === 'portal') {
        await manageSubscription();
      } else if (data.url) {
        // `location.href = ...` yerine `assign(...)`: lint kuralı href'e
        // atamayı "değiştirilemez değere yazma" olarak işaretliyor.
        window.location.assign(data.url);
      }
    } catch {
      showToast('Checkout could not be initiated. Please try again.', 'error');
    } finally {
      setIsUpgrading(false);
    }
  };

  /**
   * Kart başlığındaki tutar. Yıllıkta AYA DÜŞEN tutar gösteriliyor, altında
   * yıllık toplam: aylık $15 ile yıllık $150'yi yan yana koymak, karşılaştırmayı
   * kullanıcıya zihinden böldürmek olurdu.
   */
  const priceLabel = (plan: PlanId): PriceView => {
    const price = plans?.find(p => p.plan === plan)?.prices.find(pr => pr.interval === interval) ?? null;
    if (!price) return null;
    const amount = interval === 'yearly' ? price.monthlyEquivalentUsd : price.amountUsd;
    return { amount, total: price.amountUsd, available: price.available };
  };

  const discountOf = (plan: PlanId) =>
    plans?.find(p => p.plan === plan)?.yearlyDiscountPercent ?? null;

  return {
    interval, setInterval, subscriptionStatus, planTier, isUpgrading,
    upgrade, manageSubscription, priceLabel, discountOf,
  };
}

export type Plans = ReturnType<typeof usePlans>;
