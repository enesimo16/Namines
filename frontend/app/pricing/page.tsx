'use client';

import { Fragment, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Check, ChevronDown, ArrowRight, Minus, Plus } from 'lucide-react';
import Footer from '../../components/layout/Footer';
import TalkToSalesModal from '../../components/landing/TalkToSalesModal';
import { IsometricPricingVault } from '../../components/landing/IsometricVisuals';
import { useLocale, useLangAttr } from '../../lib/i18n';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';

interface Tier {
  id: 'free' | 'pro' | 'team' | 'enterprise';
  name: string;
  badge?: string;
  description: string;
  monthlyNumeric: number | null;
  annualNumeric: number | null;
  unit: string;
  ctaLabel: string;
  ctaAction: 'new' | 'upgrade' | 'sales';
  highlighted?: boolean;
  features: string[];
}

function getTiers(isTr: boolean): Tier[] {
  return [
    {
      id: 'free',
      name: 'Free',
      description: isTr
        ? 'Deterministik şema tasarımı ve DDL derlemeyi keşfeden bireysel geliştiriciler için.'
        : 'For solo developers exploring deterministic schema design and DDL compilation.',
      monthlyNumeric: 0,
      annualNumeric: 0,
      unit: isTr ? 'süresiz ücretsiz' : 'forever',
      ctaLabel: isTr ? 'Ücretsiz başla' : 'Start free',
      ctaAction: 'new',
      features: isTr
        ? [
            '3 adede kadar aktif şema projesi',
            '6 SQL lehçesinin tamamı (Postgres, MySQL, MSSQL, Oracle, MariaDB, SQLite)',
            '19 Eject hedefi (Prisma, Drizzle, EF Core, TS SDK)',
            'Şablon kataloğu ve herkese açık Schema Hub',
            'Standart NAI üretim kotası',
          ]
        : [
            'Up to 3 active schema projects',
            'All 6 SQL dialects (Postgres, MySQL, MSSQL, Oracle, MariaDB, SQLite)',
            '19 Eject targets (Prisma, Drizzle, EF Core, TS SDK)',
            'Blueprints catalog & public Schema Hub',
            'Standard NAI generation quota',
          ],
    },
    {
      id: 'pro',
      name: 'Pro',
      badge: isTr ? 'En Popüler' : 'Most Popular',
      description: isTr
        ? 'Canlı sandbox doğrulaması ile üretime backend çıkaran mühendisler için.'
        : 'For engineers shipping production backends with live sandbox verification.',
      monthlyNumeric: 19,
      annualNumeric: 15,
      unit: isTr ? 'kullanıcı / ay' : 'per user / month',
      ctaLabel: isTr ? 'Pro ile başla' : 'Start Pro trial',
      ctaAction: 'upgrade',
      highlighted: true,
      features: isTr
        ? [
            'Sınırsız şema projesi ve dal (branch)',
            'PR Değişiklik İnceleme & SchemaImpactAnalyzer',
            'Geçici Docker Sandbox Run Tests',
            'Universal MCP Sunucusu & CLI erişimi',
            'Namines Desk Veri Stüdyosu & Vault yedeklemeleri',
          ]
        : [
            'Unlimited schema projects & branches',
            'PR Change Review & SchemaImpactAnalyzer',
            'Ephemeral Docker Sandbox Run Tests',
            'Universal MCP Server & CLI access',
            'Namines Desk Data Studio & Vault backups',
          ],
    },
    {
      id: 'team',
      name: 'Team',
      description: isTr
        ? 'Sıfır kaymalı CI/CD ve ortak inceleme akışı kuran mühendislik ekipleri için.'
        : 'For engineering squads enforcing zero-drift CI/CD and collaborative reviews.',
      monthlyNumeric: 49,
      annualNumeric: 39,
      unit: isTr ? 'koltuk / ay' : 'per seat / month',
      ctaLabel: isTr ? 'Team alanı oluştur' : 'Create Team workspace',
      ctaAction: 'upgrade',
      features: isTr
        ? [
            'Pro’daki her şey + paylaşımlı ekip dalları',
            'Otomatik risk puanlamalı GitHub PR Botu',
            'Güvenli risk otomatik onay & ChangeRequestAuditLog',
            'Kapsamlı Gateway REST API anahtarları (read / write / rpc)',
            'Namines Flow webhook ve otomasyon hatları',
          ]
        : [
            'Everything in Pro, plus shared team branches',
            'GitHub PR Bot with automated risk scoring',
            'Safe-risk auto-approval & ChangeRequestAuditLog',
            'Scoped Gateway REST API keys (read / write / rpc)',
            'Namines Flow webhook & automation pipelines',
          ],
    },
    {
      id: 'enterprise',
      name: 'Enterprise',
      description: isTr
        ? 'VPC / self-hosted izolasyon, SSO ve özel SLA gerektiren kurumlar için.'
        : 'For organizations requiring VPC / self-hosted isolation, SSO, and custom SLAs.',
      monthlyNumeric: null,
      annualNumeric: null,
      unit: isTr ? 'yıllık sözleşme' : 'annual contract',
      ctaLabel: isTr ? 'Satış ile görüş' : 'Talk to Sales',
      ctaAction: 'sales',
      features: isTr
        ? [
            'Self-hosted / özel VPC kurulumu',
            'OIDC / SAML SSO ve ayrıntılı RBAC',
            'Adanmış Ground yönetilen Postgres kümeleri',
            'Özel NAI model yönlendirme ve sıfır veri tutma',
            'Öncelikli mühendislik desteği & güvenlik incelemesi',
          ]
        : [
            'Self-hosted / private VPC deployment',
            'OIDC / SAML SSO & granular RBAC',
            'Dedicated Ground managed Postgres clusters',
            'Custom NAI model routing & zero-retention',
            'Priority engineering support & security review',
          ],
    },
  ];
}

interface ComparisonGroup {
  category: string;
  rows: { feature: string; values: [string, string, string, string] }[];
}

function getComparisonGroups(isTr: boolean): ComparisonGroup[] {
  return [
    {
      category: isTr ? 'Çekirdek Motor & Derleme' : 'Core Engine & Compilation',
      rows: [
        {
          feature: isTr ? 'SQL Lehçeleri & Kanonik IR' : 'SQL Dialects & Canonical IR',
          values: ['6 engines', '6 engines', '6 engines', '6 engines + custom'],
        },
        {
          feature: isTr ? 'Eject Hedefleri (ORM, SDK, Admin)' : 'Eject Targets (ORM, SDK, Admin)',
          values: ['19 targets', '19 targets', '19 targets', '19 targets'],
        },
        {
          feature: 'Docker Sandbox Run Tests',
          values: ['5 / day', 'Unlimited', 'Unlimited', 'Dedicated runner'],
        },
        {
          feature: isTr ? 'Sıfır Kalıcı İnceleme (Introspect)' : 'Zero-Persistence Introspection',
          values: ['Included', 'Included', 'Included', 'VPC Isolated'],
        },
      ],
    },
    {
      category: isTr ? 'CI/CD & Yönetişim' : 'CI/CD & Governance',
      rows: [
        {
          feature: isTr ? 'GitHub PR Bot & 3-Yollu Merge' : 'GitHub PR Bot & 3-Way Merge',
          values: ['—', 'Public repos', 'Private + Public', 'Org-wide + Audit'],
        },
        {
          feature: isTr ? 'Güvenli Risk Otomatik Onayı' : 'Safe-Risk Auto-Approval',
          values: ['—', '—', 'Included', 'Custom Policy'],
        },
        {
          feature: 'ChangeRequestAuditLog',
          values: ['—', '30 days', '1 year', 'Unlimited retention'],
        },
      ],
    },
    {
      category: isTr ? 'Çalışma Alanı, Uygulamalar & Güvenlik' : 'Workspace, Apps & Security',
      rows: [
        {
          feature: 'Namines Desk, Vault & Flow',
          values: ['Read-only', 'Included', 'Team shared', 'VPC isolated'],
        },
        {
          feature: 'Universal MCP Server (@namines/mcp)',
          values: ['Included', 'Included', 'Included', 'Custom endpoint'],
        },
        {
          feature: 'SSO / SAML & SLA',
          values: ['—', '—', '—', 'SAML / OIDC + 99.9%'],
        },
      ],
    },
  ];
}

function getFaqs(isTr: boolean) {
  if (isTr) {
    return [
      {
        q: 'Free katmanında başlamak için kredi kartı gerekiyor mu?',
        a: 'Hayır. Ödeme bilgisi girmeden şema tasarlayabilir, 6 SQL lehçesinin tamamına derleyebilir, 19 hedefe kod çıkarabilir ve Blueprint kataloğunu kullanabilirsiniz.',
      },
      {
        q: 'Introspect kullanırken canlı veritabanı kimlik bilgilerim saklanır mı?',
        a: 'Hayır. Canlı veritabanı incelemesi varsayılan olarak sıfır-kalıcılık (zero-persistence) ile bellekte çalışır; bağlantıyı şifreli Namines Vault’a özellikle kaydetmedikçe bilgiler silinir.',
      },
      {
        q: 'Team koltuk sayısını veya faturalandırma dönemini sonradan değiştirebilir miyiz?',
        a: 'Evet. Çalışma alanı ayarlarından dilediğiniz zaman Team koltuğu ekleyip çıkarabilir, planınızı yükseltip düşürebilir veya aylık/yıllık faturalandırma arasında geçiş yapabilirsiniz.',
      },
      {
        q: 'Universal MCP Server kullanımı kotadan düşer mi?',
        a: '@namines/mcp üzerinden yapılan deterministik derleme, diff ve eject işlemleri AI üretim kredinizden harcamaz; planınıza dahildir.',
      },
    ];
  }
  return [
    {
      q: 'Do I need a credit card to start on the Free tier?',
      a: 'No. You can design schemas, compile across all 6 SQL dialects, eject to 19 targets, and explore Blueprints without entering payment details.',
    },
    {
      q: 'Are my live database credentials stored when using Introspect?',
      a: 'No. Live database introspection is zero-persistence by default—structural metadata is read in memory and discarded unless you explicitly save a connection into encrypted Namines Vault.',
    },
    {
      q: 'Can we adjust Team seats or switch billing intervals later?',
      a: 'Yes. You can add or remove Team seats, upgrade, downgrade, or switch between monthly and annual billing at any time from your workspace settings.',
    },
    {
      q: 'How does the Universal MCP Server count toward quotas?',
      a: 'Deterministic compilation, diffing, and eject operations via @namines/mcp are included in your plan without consuming AI generation credits.',
    },
  ];
}

export default function PricingPage() {
  const router = useRouter();
  const locale = useLocale();
  const lang = useLangAttr();
  const isTr = locale === 'tr';
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const openAuthModal = useAuthModalStore((s) => s.open);

  const [annual, setAnnual] = useState(true);
  const [teamSeats, setTeamSeats] = useState(5);
  const [openFaq, setOpenFaq] = useState<number | null>(0);
  const [isSalesOpen, setIsSalesOpen] = useState(false);

  const tiers = getTiers(isTr);
  const comparisonGroups = getComparisonGroups(isTr);
  const faqs = getFaqs(isTr);

  const teamUnitPrice = annual ? 39 : 49;
  const teamMonthlyTotal = teamSeats * teamUnitPrice;

  const handlePlanAction = (tier: Tier) => {
    if (tier.ctaAction === 'sales') {
      setIsSalesOpen(true);
      return;
    }
    if (tier.ctaAction === 'upgrade') {
      if (!isAuthenticated) {
        openAuthModal();
        return;
      }
      router.push('/ground');
      return;
    }
    router.push('/new');
  };

  return (
    <div lang={lang} className="min-h-screen bg-surface-900 text-content-primary flex flex-col">
      <main className="flex-1">
        {/* Hero Header with 3D Isometric Visual */}
        <section className="border-b border-surface-500/60">
          <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-14 lg:py-18 grid grid-cols-1 lg:grid-cols-[1.2fr_0.8fr] gap-10 items-center">
            <div className="space-y-5">
              <h1 className="text-3xl sm:text-4xl lg:text-5xl font-bold tracking-tight leading-[1.12]">
                {isTr
                  ? 'Deterministik veritabanı yaşam döngüleri için öngörülebilir fiyatlandırma.'
                  : 'Predictable pricing for deterministic database lifecycles.'}
              </h1>
              <p className="text-base sm:text-lg text-content-secondary max-w-xl leading-relaxed">
                {isTr
                  ? '6 SQL lehçesi ve 19 eject hedefi ile ücretsiz başlayın. Ekibiniz CI/CD PR kontrolleri, geçici sandbox doğrulaması ve Vault yönetişimine ihtiyaç duyduğunda ölçeklendirin.'
                  : 'Start free with all 6 SQL dialects and 19 eject targets. Scale up when your team needs CI/CD PR checks, ephemeral sandbox verification, and Vault governance.'}
              </p>

              {/* Billing Interval Toggle + Team Seat Estimator */}
              <div className="pt-2 flex flex-wrap items-center gap-4">
                <div className="inline-flex items-center gap-1 p-1 rounded-[var(--radius-control)] bg-surface-800 border border-surface-500">
                  <button
                    type="button"
                    onClick={() => setAnnual(false)}
                    className={`px-4 py-1.5 rounded-[var(--radius-control)] text-[13px] font-medium transition-colors cursor-pointer ${
                      !annual
                        ? 'bg-surface-900 text-content-primary shadow-xs'
                        : 'text-content-secondary hover:text-content-primary'
                    }`}
                  >
                    {isTr ? 'Aylık' : 'Monthly'}
                  </button>
                  <button
                    type="button"
                    onClick={() => setAnnual(true)}
                    className={`px-4 py-1.5 rounded-[var(--radius-control)] text-[13px] font-medium transition-colors cursor-pointer inline-flex items-center gap-2 ${
                      annual
                        ? 'bg-surface-900 text-content-primary shadow-xs'
                        : 'text-content-secondary hover:text-content-primary'
                    }`}
                  >
                    <span>{isTr ? 'Yıllık' : 'Annual'}</span>
                    <span className="px-1.5 py-0.5 rounded-full bg-accent/15 text-accent-text border border-accent/30 text-[10px] font-mono font-semibold">
                      {isTr ? '%20 İndirim' : 'Save 20%'}
                    </span>
                  </button>
                </div>

                {/* Team Seat Estimator */}
                <div className="inline-flex items-center gap-3 px-3.5 py-1.5 rounded-[var(--radius-control)] bg-surface-800/60 border border-surface-500/80">
                  <span className="text-[12px] font-mono text-content-muted">
                    {isTr ? 'Team koltuk:' : 'Team seats:'}
                  </span>
                  <div className="inline-flex items-center gap-1.5">
                    <button
                      type="button"
                      aria-label="Decrease team seats"
                      onClick={() => setTeamSeats((s) => Math.max(1, s - 1))}
                      className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-900 border border-surface-500 inline-flex items-center justify-center text-content-primary hover:bg-surface-700 cursor-pointer"
                    >
                      <Minus className="w-3.5 h-3.5" />
                    </button>
                    <span className="w-7 text-center font-mono text-[13px] font-semibold text-content-primary">
                      {teamSeats}
                    </span>
                    <button
                      type="button"
                      aria-label="Increase team seats"
                      onClick={() => setTeamSeats((s) => Math.min(50, s + 1))}
                      className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-900 border border-surface-500 inline-flex items-center justify-center text-content-primary hover:bg-surface-700 cursor-pointer"
                    >
                      <Plus className="w-3.5 h-3.5" />
                    </button>
                  </div>
                  <span className="text-[12px] font-mono text-accent-text font-semibold">
                    ${teamMonthlyTotal}/mo
                  </span>
                </div>
              </div>
            </div>

            <div className="hidden lg:block">
              <IsometricPricingVault />
            </div>
          </div>
        </section>

        {/* Tier Cards */}
        <section className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-12 lg:py-16">
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-6">
            {tiers.map((tier) => {
              const numeric = annual ? tier.annualNumeric : tier.monthlyNumeric;
              const priceLabel = numeric === null ? (isTr ? 'Özel' : 'Custom') : `$${numeric}`;
              return (
                <div
                  key={tier.id}
                  className={`rounded-[var(--radius-card)] p-6 flex flex-col justify-between border transition-colors ${
                    tier.highlighted
                      ? 'bg-surface-800/75 border-accent shadow-lg'
                      : 'bg-surface-800/35 border-surface-500/80 hover:border-surface-400'
                  }`}
                >
                  <div className="space-y-5">
                    <div className="space-y-2">
                      <div className="flex items-center justify-between gap-2">
                        <h2 className="text-lg font-bold text-content-primary">{tier.name}</h2>
                        {tier.badge && (
                          <span className="px-2 py-0.5 rounded-full bg-accent/15 text-accent-text border border-accent/30 text-[10px] font-mono font-semibold uppercase">
                            {tier.badge}
                          </span>
                        )}
                      </div>
                      <p className="text-[13px] text-content-secondary leading-relaxed min-h-[40px]">
                        {tier.description}
                      </p>
                    </div>

                    <div className="pt-3 border-t border-surface-500/60">
                      <div className="flex items-baseline gap-2">
                        <span className="text-3xl font-bold font-mono text-content-primary">
                          {priceLabel}
                        </span>
                        {tier.id === 'team' && (
                          <span className="text-[11px] font-mono text-accent-text">
                            (${teamMonthlyTotal}/mo {isTr ? `· ${teamSeats} koltuk` : `for ${teamSeats}`})
                          </span>
                        )}
                      </div>
                      <div className="text-[12px] text-content-muted mt-0.5">
                        {tier.unit}
                        {annual && numeric !== null && numeric > 0
                          ? isTr
                            ? ' · yıllık faturalandırılır'
                            : ' · billed annually'
                          : ''}
                      </div>
                    </div>

                    <ul className="space-y-2.5 pt-2">
                      {tier.features.map((feat) => (
                        <li
                          key={feat}
                          className="flex items-start gap-2.5 text-[13px] text-content-secondary"
                        >
                          <Check className="w-4 h-4 text-accent-text shrink-0 mt-0.5" />
                          <span>{feat}</span>
                        </li>
                      ))}
                    </ul>
                  </div>

                  <div className="pt-6 mt-6 border-t border-surface-500/60">
                    {tier.ctaAction === 'new' ? (
                      <Link
                        href="/new"
                        className="tap-44 w-full py-2.5 px-4 rounded-[var(--radius-control)] border border-surface-500 bg-surface-900 hover:bg-surface-800 text-[14px] font-semibold text-content-primary transition-colors inline-flex items-center justify-center gap-2"
                      >
                        <span>{tier.ctaLabel}</span>
                        <ArrowRight className="w-4 h-4" />
                      </Link>
                    ) : (
                      <button
                        type="button"
                        onClick={() => handlePlanAction(tier)}
                        className={`tap-44 w-full py-2.5 px-4 rounded-[var(--radius-control)] text-[14px] font-semibold transition-colors inline-flex items-center justify-center gap-2 cursor-pointer ${
                          tier.highlighted
                            ? 'bg-accent hover:bg-accent-hover text-accent-on'
                            : 'border border-surface-500 bg-surface-900 hover:bg-surface-800 text-content-primary'
                        }`}
                      >
                        <span>{tier.ctaLabel}</span>
                        <ArrowRight className="w-4 h-4" />
                      </button>
                    )}
                  </div>
                </div>
              );
            })}
          </div>

          {/* Guarantee Strip */}
          <div className="mt-8 grid grid-cols-1 sm:grid-cols-3 gap-4">
            {[
              {
                title: isTr ? 'Satıcı Bağımlılığı Yok' : 'Zero Vendor Lock-In',
                body: isTr
                  ? 'İstediğiniz anda temiz SQL, Prisma, Drizzle, EF Core veya TypeScript SDK üretip dışa aktarın.'
                  : 'Eject clean SQL, Prisma, Drizzle, EF Core, or TypeScript SDKs at any time.',
              },
              {
                title: isTr ? 'Sıfır Kalıcı Keşif' : 'Zero-Persistence Discovery',
                body: isTr
                  ? 'Canlı veritabanı incelemesi kimlik bilgilerini saklamadan bellekte çalışır.'
                  : 'Live database introspection runs in memory without storing credentials.',
              },
              {
                title: isTr ? 'Deterministik Doğrulama' : 'Deterministic Verification',
                body: isTr
                  ? 'Her DDL değişikliği dağıtımdan önce gerçek motor kurallarına göre doğrulanır.'
                  : 'Every DDL change is validated against real engine rules before deploy.',
              },
            ].map((item) => (
              <div
                key={item.title}
                className="p-4 rounded-[var(--radius-card)] bg-surface-800/25 border border-surface-500/60 space-y-1"
              >
                <div className="text-[13px] font-semibold text-content-primary">{item.title}</div>
                <p className="text-[12px] text-content-muted leading-relaxed">{item.body}</p>
              </div>
            ))}
          </div>
        </section>

        {/* Grouped Feature Comparison Matrix */}
        <section className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 pb-16">
          <div className="space-y-4">
            <h2 className="text-2xl font-bold tracking-tight">
              {isTr ? 'Plan karşılaştırması' : 'Plan comparison'}
            </h2>
            <div className="rounded-[var(--radius-card)] border border-surface-500/80 overflow-x-auto">
              <table className="w-full text-left border-collapse min-w-[680px]">
                <thead>
                  <tr className="bg-surface-800/80 border-b border-surface-500/80 text-[12px] font-mono uppercase tracking-wider text-content-muted">
                    <th className="py-3.5 px-5">{isTr ? 'Özellik' : 'Capability'}</th>
                    <th className="py-3.5 px-5">Free</th>
                    <th className="py-3.5 px-5 text-accent-text">Pro</th>
                    <th className="py-3.5 px-5">Team</th>
                    <th className="py-3.5 px-5">Enterprise</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-surface-500/60 text-[13px]">
                  {comparisonGroups.map((group) => (
                    <Fragment key={group.category}>
                      <tr className="bg-surface-800/40">
                        <td
                          colSpan={5}
                          className="py-2.5 px-5 text-[11px] font-mono uppercase tracking-wider text-accent-text font-semibold"
                        >
                          {group.category}
                        </td>
                      </tr>
                      {group.rows.map((row) => (
                        <tr key={row.feature} className="hover:bg-surface-800/25">
                          <td className="py-3.5 px-5 font-medium text-content-primary">
                            {row.feature}
                          </td>
                          {row.values.map((val, idx) => (
                            <td
                              key={idx}
                              className="py-3.5 px-5 text-content-secondary font-mono text-[12px]"
                            >
                              {val === 'Included' ? (
                                <span className="inline-flex items-center gap-1.5 text-accent-text">
                                  <Check className="w-3.5 h-3.5" />
                                  <span>{isTr ? 'Dahil' : 'Included'}</span>
                                </span>
                              ) : (
                                val
                              )}
                            </td>
                          ))}
                        </tr>
                      ))}
                    </Fragment>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </section>

        {/* FAQ */}
        <section className="w-full max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 pb-20">
          <div className="space-y-6">
            <div className="text-[12px] font-mono uppercase tracking-wider text-accent-text">
              {isTr ? 'Sıkça Sorulan Sorular' : 'Frequently Asked Questions'}
            </div>
            <div className="divide-y divide-surface-500/70 border-y border-surface-500/70">
              {faqs.map((item, idx) => {
                const isOpen = openFaq === idx;
                return (
                  <div key={item.q}>
                    <button
                      type="button"
                      onClick={() => setOpenFaq(isOpen ? null : idx)}
                      aria-expanded={isOpen}
                      className="tap-44 w-full py-4 flex items-center justify-between gap-4 text-left text-[15px] font-semibold text-content-primary cursor-pointer"
                    >
                      <span>{item.q}</span>
                      <ChevronDown
                        className={`w-4 h-4 text-content-muted shrink-0 transition-transform duration-200 ${
                          isOpen ? 'rotate-180' : ''
                        }`}
                      />
                    </button>
                    {isOpen && (
                      <p className="pb-4 text-[14px] text-content-secondary leading-relaxed">
                        {item.a}
                      </p>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        </section>
      </main>

      <Footer />

      <TalkToSalesModal
        isOpen={isSalesOpen}
        onClose={() => setIsSalesOpen(false)}
      />
    </div>
  );
}
