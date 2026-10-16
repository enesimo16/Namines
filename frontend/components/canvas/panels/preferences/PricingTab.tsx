import React from 'react';
import { Check, CreditCard } from 'lucide-react';
import type { BillingInterval } from '../../../../services/api';
import { useQuotaStore } from '../../../../store/useQuotaStore';
import { cardClass, primaryBtnClass } from './ui';
import type { Plans, PriceView } from './usePlans';

function PlanPriceTag({ price, interval }: { price: PriceView; interval: BillingInterval }) {
  if (!price) {
    return <div className="text-2xl font-bold text-content-muted">—</div>;
  }

  const amount = Number.isInteger(price.amount) ? price.amount : price.amount.toFixed(2);

  return (
    <div>
      <div className="text-2xl font-bold text-content-primary">
        ${amount} <span className="text-xs font-normal text-content-muted">/ month</span>
      </div>
      {interval === 'yearly' && (
        <p className="text-[11px] text-content-muted font-medium mt-0.5">
          ${price.total} billed once a year
        </p>
      )}
      {!price.available && (
        <p className="text-[11px] text-content-secondary font-medium mt-0.5">
          {interval === 'yearly'
            ? 'Yearly billing is not set up yet.'
            : 'Checkout is not set up yet.'}
        </p>
      )}
    </div>
  );
}

function upgradeLabel(plan: string, price: PriceView, interval: BillingInterval) {
  if (!price) return `Upgrade to ${plan}`;
  if (!price.available) {
    return interval === 'yearly'
      ? `${plan} — yearly billing coming soon`
      : `${plan} — checkout not available yet`;
  }
  const amount = Number.isInteger(price.amount) ? price.amount : price.amount.toFixed(2);
  return interval === 'yearly'
    ? `Upgrade to ${plan} — $${price.total}/yr`
    : `Upgrade to ${plan} — $${amount}/mo`;
}

function FeatureList({ items, iconClass }: { items: string[]; iconClass: string }) {
  return (
    <ul className="space-y-2 text-xs text-content-secondary font-medium">
      {items.map(f => (
        <li key={f} className="flex items-center gap-2">
          <Check className={`w-3.5 h-3.5 ${iconClass} shrink-0`} />
          <span>{f}</span>
        </li>
      ))}
    </ul>
  );
}

function CurrentPlanActions({ label, isUpgrading, onManage }: { label: string; isUpgrading: boolean; onManage: () => void }) {
  return (
    <>
      <div className="flex items-center gap-2 text-xs text-success font-semibold border border-success/30 bg-success-subtle rounded-[var(--radius-control)] px-4 py-2.5">
        <Check className="w-4 h-4" />
        {label}
      </div>
      <button
        type="button"
        onClick={onManage}
        disabled={isUpgrading}
        className="w-full flex items-center justify-center gap-2 bg-surface-700 hover:bg-surface-600 border border-line-strong text-content-primary text-xs font-semibold rounded-[var(--radius-control)] px-4 py-2.5 transition-all disabled:opacity-50 cursor-pointer"
      >
        <CreditCard className="w-3.5 h-3.5" />
        {isUpgrading ? 'Opening portal...' : 'Manage Subscription'}
      </button>
    </>
  );
}

export default function PricingTab({ plans }: { plans: Plans }) {
  const { dailyLimit } = useQuotaStore();
  const { interval, setInterval, subscriptionStatus, planTier, isUpgrading, upgrade, manageSubscription, priceLabel, discountOf } = plans;
  const proPrice = priceLabel('pro');
  const teamPrice = priceLabel('team');

  return (
    <div className="space-y-5">
      {/* Aylık / Yıllık geçişi — Shopify Polaris Segmented Switch */}
      <div className="flex items-center justify-center gap-2.5">
        <div className="inline-flex rounded-full bg-surface-800 border border-line-strong p-1">
          {(['monthly', 'yearly'] as BillingInterval[]).map(opt => (
            <button
              key={opt}
              type="button"
              onClick={() => setInterval(opt)}
              className={`px-4 py-1.5 rounded-full text-xs font-semibold transition-all cursor-pointer ${
                interval === opt
                  ? 'bg-accent text-accent-on shadow-sm'
                  : 'text-content-muted hover:text-content-primary'
              }`}
            >
              {opt === 'monthly' ? 'Monthly' : 'Yearly'}
            </button>
          ))}
        </div>
        {discountOf('pro') !== null && (
          <span className="text-[10px] font-mono font-bold text-success border border-success/30 bg-success-subtle px-2.5 py-1 rounded-full uppercase tracking-wider">
            Save {discountOf('pro')}% yearly
          </span>
        )}
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {/* Free Plan Card */}
        <div className={`${cardClass} p-5 flex flex-col justify-between`}>
          <div className="space-y-4">
            <div className="space-y-1">
              <span className="text-[11px] uppercase font-semibold text-content-muted tracking-wider">Plan</span>
              <h4 className="text-sm font-semibold text-content-primary">Free Member</h4>
              <p className="text-[11px] text-content-muted leading-relaxed font-medium">Ideal for individual developers and builders.</p>
            </div>
            <div className="text-2xl font-bold text-content-primary">$0 <span className="text-xs font-normal text-content-muted">/ forever</span></div>
            <div className="h-px bg-line" />
            <FeatureList
              iconClass="text-success"
              items={[
                planTier === 'Free' && dailyLimit > 0
                  ? `${(dailyLimit / 1000).toFixed(0)}K AI tokens / day — today's fair share`
                  : 'A daily AI budget shared fairly across free users',
                'NAI v1 Flash and NAI v1 models', 'All 6 database engines + SQL export', 'DBA linter & schema diagnostics', '1 external database connection', '3 ephemeral test runs / day',
              ]}
            />
          </div>
        </div>

        {/* Pro Plan Card */}
        <div className="bg-surface-800/90 border border-accent/60 rounded-[var(--radius-card)] p-5 flex flex-col justify-between relative overflow-hidden shadow-md">
          <div className="absolute top-0 right-0 bg-accent text-accent-on text-[10px] font-mono font-bold uppercase tracking-wider px-3 py-0.5 rounded-bl-[var(--radius-control)]">
            Recommended
          </div>
          <div className="space-y-4">
            <div className="space-y-1">
              <span className="text-[11px] uppercase font-semibold text-accent-text tracking-wider">Plan</span>
              <h4 className="text-sm font-semibold text-content-primary">Pro Member</h4>
              <p className="text-[11px] text-content-muted leading-relaxed font-medium">For engineering teams and professionals.</p>
            </div>
            <PlanPriceTag price={proPrice} interval={interval} />
            <div className="h-px bg-line" />
            <FeatureList
              iconClass="text-accent-text"
              items={['200K AI tokens / day — 10x Free', 'NAI v1 Pro model unlocked', '2 branch databases + change review', '20 ephemeral test runs / day', '3 external database connections', 'Gateway API: 600 req/min']}
            />
          </div>
          <div className="mt-5 flex flex-col gap-2">
            {subscriptionStatus === 'active' && planTier !== 'Team' ? (
              <CurrentPlanActions label="You are on the Pro plan" isUpgrading={isUpgrading} onManage={manageSubscription} />
            ) : (
              <button
                type="button"
                onClick={() => upgrade('pro')}
                disabled={isUpgrading || proPrice?.available === false}
                className={`w-full flex items-center justify-center gap-2 text-xs rounded-[var(--radius-control)] px-4 py-2.5 transition-all disabled:opacity-50 disabled:cursor-wait cursor-pointer ${primaryBtnClass}`}
              >
                {isUpgrading ? 'Redirecting to Stripe...' : upgradeLabel('Pro', proPrice, interval)}
              </button>
            )}
            <p className="text-center text-[10px] text-content-muted font-medium">Secured by Stripe · Cancel anytime</p>
          </div>
        </div>

        {/* Team Plan Card */}
        <div className={`${cardClass} p-5 flex flex-col justify-between`}>
          <div className="space-y-4">
            <div className="space-y-1">
              <span className="text-[11px] uppercase font-semibold text-content-muted tracking-wider">Plan</span>
              <h4 className="text-sm font-semibold text-content-primary">Team</h4>
              <p className="text-[11px] text-content-muted leading-relaxed font-medium">For teams that need higher limits and shared branch databases.</p>
            </div>
            <PlanPriceTag price={teamPrice} interval={interval} />
            <div className="h-px bg-line" />
            <FeatureList
              iconClass="text-accent-text"
              items={['Everything in Pro', '3 seats — you + 2 invited members', 'Shared workspace: projects visible to all', 'Team activity feed — see who changed what', '20 branch databases, unlimited test runs', 'Gateway API: 3,000 req/min']}
            />
          </div>
          <div className="mt-5 flex flex-col gap-2">
            {planTier === 'Team' ? (
              <CurrentPlanActions label="You are on the Team plan" isUpgrading={isUpgrading} onManage={manageSubscription} />
            ) : (
              <button
                type="button"
                onClick={() => upgrade('team')}
                disabled={isUpgrading || teamPrice?.available === false}
                className="w-full flex items-center justify-center gap-2 bg-surface-700 hover:bg-surface-600 border border-line-strong text-content-primary text-xs font-semibold rounded-[var(--radius-control)] px-4 py-2.5 transition-all disabled:opacity-50 disabled:cursor-wait cursor-pointer"
              >
                {isUpgrading ? 'Redirecting to Stripe...' : upgradeLabel('Team', teamPrice, interval)}
              </button>
            )}
            <p className="text-center text-[10px] text-content-muted font-medium">Secured by Stripe · Cancel anytime</p>
          </div>
        </div>
      </div>
    </div>
  );
}
