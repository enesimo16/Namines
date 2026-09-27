import React from 'react';
import { cardClass } from './ui';

const FEATURE_COSTS: [string, string][] = [
  ['Schema Generation (Prompt)', '5%'],
  ['Smart Seeding', '5%'],
  ['Schema Revision', '8%'],
  ['DBA Linter & Diagnostics', '8%'],
  ['Backend Scaffolding', '10%'],
  ['Schema Migration', '10%'],
  ['Documentation & Reports', '10%'],
  ['Vision / Reverse Engineer', '15%'],
  ['Voice Input / Transcription', '5%'],
];

const tableCell = "py-1.5 px-3 text-right font-mono text-accent-text font-bold";

const faqs: { q: string; a: React.ReactNode }[] = [
  {
    q: 'How do daily quotas and credit deductions work?',
    a: (
      <div className="space-y-4 text-content-secondary">
        <p className="text-[10px] text-content-muted">
          Free members receive a daily <strong className="text-content-primary">100% cloud credit bar</strong>. Daily usage is calculated based on the base cost of the AI feature and the selected AI Model&apos;s routing multiplier. If credits are exhausted, features automatically fall back to the local engine to ensure uninterrupted usage.
        </p>

        <div className="space-y-2">
          <h5 className="text-[10px] font-bold text-content-secondary uppercase tracking-wider">Base Feature Cost</h5>
          <div className="border border-content-primary/15 rounded-[var(--radius-control)] overflow-hidden bg-surface-600">
            <table className="w-full text-left border-collapse text-[10px]">
              <thead>
                <tr className="bg-white/[0.04] border-b border-content-primary/15 text-content-muted font-bold uppercase">
                  <th className="py-1.5 px-3">Feature</th>
                  <th className="py-1.5 px-3 text-right">Base Cost</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-content-primary/8 text-content-secondary">
                {FEATURE_COSTS.map(([feature, cost]) => (
                  <tr key={feature}><td className="py-1.5 px-3 font-medium">{feature}</td><td className={tableCell}>{cost}</td></tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        <div className="space-y-2">
          <h5 className="text-[10px] font-bold text-content-secondary uppercase tracking-wider">AI Model Multiplier</h5>
          <div className="border border-content-primary/15 rounded-[var(--radius-control)] overflow-hidden bg-surface-600">
            <table className="w-full text-left border-collapse text-[10px]">
              <thead>
                <tr className="bg-white/[0.04] border-b border-content-primary/15 text-content-muted font-bold uppercase">
                  <th className="py-1.5 px-3">Routing Engine</th>
                  <th className="py-1.5 px-3 text-right">Multiplier</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-content-primary/8 text-content-secondary">
                <tr><td className="py-1.5 px-3 font-medium">NAI v1 Flash</td><td className={tableCell}>0.5x</td></tr>
                <tr><td className="py-1.5 px-3 font-medium">NAI v1</td><td className={tableCell}>1x</td></tr>
                <tr><td className="py-1.5 px-3 font-medium">NAI v1 Pro <span className="opacity-60">(paid plans)</span></td><td className={tableCell}>2x</td></tr>
              </tbody>
            </table>
          </div>
        </div>

        <div className="bg-surface-600 rounded-[var(--radius-control)] p-2.5 text-micro leading-relaxed text-content-muted">
          <strong>Calculation Example:</strong> Running a <span className="text-content-primary font-bold">DBA Audit (8% base)</span> with the <span className="text-content-primary font-bold">Medium engine (2x)</span> will deduct <span className="text-accent-text font-bold">16%</span> from your daily quota. Running it with the Default engine costs <span className="text-accent-text font-bold">0%</span>.
        </div>
      </div>
    )
  },
  {
    q: 'What is the "Default (Namines)" option?',
    a: (
      <p>
        This option routes your requests through the default Namines local/cloud engine. It runs optimized, standard template generation and local heuristic rules on your browser or free cloud cluster at zero token cost.
      </p>
    )
  },
  {
    q: 'How does cloud sync and backup work?',
    a: (
      <p>
        Your projects are automatically stored in the local WASM database. If you have an active account, your designs are securely uploaded and backed up to our cloud storage, enabling seamless transition across devices.
      </p>
    )
  }
];

export default function HelpTab({ openFaq, onToggle }: { openFaq: number | null; onToggle: (index: number) => void }) {
  return (
    <div className="space-y-2.5">
      {faqs.map((faq, idx) => (
        <div key={idx} className={`${cardClass} overflow-hidden`}>
          <button
            type="button"
            onClick={() => onToggle(idx)}
            className="w-full flex justify-between items-center px-4 py-3.5 text-left text-xs font-semibold text-content-primary hover:bg-white/[0.03] transition-colors cursor-pointer select-none"
          >
            <span>{faq.q}</span>
            <span className="text-content-muted font-bold text-sm leading-none">{openFaq === idx ? '−' : '+'}</span>
          </button>
          {openFaq === idx && (
            <div className="px-4 pb-4 text-[11px] text-content-secondary leading-relaxed border-t border-content-primary/10 pt-2.5 animate-in fade-in duration-200">
              {faq.a}
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
