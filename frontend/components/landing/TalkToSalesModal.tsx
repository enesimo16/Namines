'use client';

import { useState, useEffect } from 'react';
import {
  X,
  Building2,
  CheckCircle2,
  ShieldCheck,
  Zap,
  Loader2,
  Send,
  Clock,
} from 'lucide-react';
import api from '../../services/api';
import { useToastStore } from '../../store/useToastStore';

interface TalkToSalesModalProps {
  isOpen: boolean;
  onClose: () => void;
}

const TEAM_SIZES = ['1-10 devs', '11-50 devs', '51-250 devs', '250+ enterprise'] as const;

const ENGINES = [
  'PostgreSQL (12-17)',
  'MySQL (8.0+)',
  'SQL Server (2019+)',
  'Oracle Database',
  'MariaDB (10.5+)',
  'Multi-engine / Heterogeneous',
] as const;

export default function TalkToSalesModal({ isOpen, onClose }: TalkToSalesModalProps) {
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [company, setCompany] = useState('');
  const [teamSize, setTeamSize] = useState<string>(TEAM_SIZES[1]);
  const [engine, setEngine] = useState<string>(ENGINES[0]);
  const [note, setNote] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSubmitted, setIsSubmitted] = useState(false);
  const showToast = useToastStore((s) => s.showToast);

  // Close on Escape key
  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  // Lock body scroll when open
  useEffect(() => {
    if (isOpen) {
      document.body.style.overflow = 'hidden';
    } else {
      document.body.style.overflow = 'unset';
    }
    return () => {
      document.body.style.overflow = 'unset';
    };
  }, [isOpen]);

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email.trim() || !name.trim()) {
      showToast('Please provide your name and work email.', 'warning');
      return;
    }

    setIsSubmitting(true);
    try {
      // Post to feedback/inquiry endpoint
      await api.post('/feedback', {
        category: 'general',
        email: email.trim(),
        message: `[Enterprise Sales Inquiry]
Name: ${name.trim()}
Company: ${company.trim() || 'N/A'}
Team Size: ${teamSize}
Target Engine: ${engine}
Notes: ${note.trim() || 'None provided'}`,
      });
      setIsSubmitted(true);
      showToast('Inquiry received! Our engineering leadership will contact you shortly.', 'success');
    } catch {
      // Başarısızlığı başarı gibi göstermek satış talebini sessizce kaybettiriyordu.
      showToast('We could not send your inquiry. Please try again or email us directly.', 'error');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="sales-modal-title"
      className="fixed inset-0 z-50 flex items-center justify-center p-3 sm:p-4 bg-surface-950/80 backdrop-blur-md animate-in fade-in duration-200 overflow-y-auto"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="relative w-full max-w-2xl rounded-[var(--radius-modal)] border border-surface-500/80 bg-surface-900 shadow-2xl p-5 sm:p-8 my-auto overflow-hidden animate-in zoom-in-95 duration-200">
        {/* Ambient Top Glow */}
        <div
          aria-hidden="true"
          className="pointer-events-none absolute -top-24 left-1/2 -translate-x-1/2 w-96 h-96 rounded-full opacity-15 blur-[100px]"
          style={{ backgroundColor: 'var(--namines-teal-glow)' }}
        />

        {/* Close Button */}
        <button
          type="button"
          onClick={onClose}
          aria-label="Close dialog"
          className="absolute top-4 sm:top-6 right-4 sm:right-6 tap-44 p-2 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 text-content-muted hover:text-content-primary border border-surface-600/60 transition-colors cursor-pointer"
        >
          <X className="w-4 h-4" />
        </button>

        {isSubmitted ? (
          /* High-converting Confirmation State */
          <div className="text-center py-8 sm:py-12 flex flex-col items-center gap-4">
            <div className="w-14 h-14 rounded-full bg-success/20 text-success-text flex items-center justify-center shadow-lg">
              <CheckCircle2 className="w-8 h-8" />
            </div>
            <h3 className="text-2xl font-bold tracking-tight text-content-primary">
              We received your inquiry, {name.split(' ')[0]}!
            </h3>
            <p className="text-sm text-content-muted max-w-md leading-relaxed">
              Our engineering solutions team will review your database architecture needs ({engine}) and schedule a private 25-minute architecture briefing at <span className="font-semibold text-content-primary">{email}</span> within 1 business day.
            </p>
            <div className="flex items-center gap-2 mt-2 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 border border-surface-600/60 text-xs font-mono text-content-secondary">
              <Clock className="w-3.5 h-3.5 text-accent-text" />
              <span>Typical response time: under 3 hours</span>
            </div>
            <button
              type="button"
              onClick={onClose}
              className="mt-6 px-6 py-2.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on font-bold text-xs uppercase tracking-wider transition-all cursor-pointer shadow-md"
            >
              Done &middot; Return to site
            </button>
          </div>
        ) : (
          /* Form Content — Shopify Polaris Style */
          <div>
            {/* Modal Header */}
            <div className="mb-6 sm:mb-8 pr-8">
              <div className="inline-flex items-center gap-2 px-2.5 py-1 rounded-[var(--radius-control)] bg-accent/15 border border-accent/25 text-accent-text text-xs font-mono font-bold uppercase tracking-wider mb-2.5">
                <ShieldCheck className="w-3.5 h-3.5" />
                <span>Enterprise &amp; Dedicated Deployment</span>
              </div>
              <h2 id="sales-modal-title" className="text-2xl sm:text-3xl font-bold tracking-tight text-content-primary">
                Talk to our database engineering team
              </h2>
              <p className="mt-2 text-xs sm:text-sm text-content-muted leading-relaxed">
                Whether you need dedicated ephemeral Docker runners, custom DDL dialect extensions, or strict HIPAA/SOC2 isolation, let&apos;s architect it together.
              </p>
            </div>

            {/* Enterprise Assurance Pillars */}
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-2.5 mb-6 p-3 rounded-[var(--radius-card)] bg-surface-850 border border-surface-600/60 font-mono text-[11px]">
              <div className="flex items-center gap-2 text-content-secondary">
                <Zap className="w-3.5 h-3.5 text-accent-text shrink-0" />
                <span>Custom 99.99% SLA</span>
              </div>
              <div className="flex items-center gap-2 text-content-secondary">
                <ShieldCheck className="w-3.5 h-3.5 text-success-text shrink-0" />
                <span>Private VPC Peering</span>
              </div>
              <div className="flex items-center gap-2 text-content-secondary">
                <Building2 className="w-3.5 h-3.5 text-accent-text shrink-0" />
                <span>SOC2 &amp; Custom BAA</span>
              </div>
            </div>

            {/* Inquiry Form */}
            <form onSubmit={handleSubmit} className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {/* Full Name */}
                <div>
                  <label className="block text-xs font-mono font-semibold text-content-secondary uppercase tracking-wider mb-1.5">
                    Your Name *
                  </label>
                  <input
                    type="text"
                    required
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="e.g. Alex Mercer"
                    className="w-full bg-surface-850 border border-surface-500/80 focus:border-focus-ring rounded-[var(--radius-control)] px-3.5 py-2.5 text-xs sm:text-sm text-content-primary focus:outline-none placeholder:text-content-subtle"
                  />
                </div>

                {/* Work Email */}
                <div>
                  <label className="block text-xs font-mono font-semibold text-content-secondary uppercase tracking-wider mb-1.5">
                    Work Email *
                  </label>
                  <input
                    type="email"
                    required
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="alex@company.com"
                    className="w-full bg-surface-850 border border-surface-500/80 focus:border-focus-ring rounded-[var(--radius-control)] px-3.5 py-2.5 text-xs sm:text-sm text-content-primary focus:outline-none placeholder:text-content-subtle"
                  />
                </div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {/* Company Name */}
                <div>
                  <label className="block text-xs font-mono font-semibold text-content-secondary uppercase tracking-wider mb-1.5">
                    Company / Organization
                  </label>
                  <input
                    type="text"
                    value={company}
                    onChange={(e) => setCompany(e.target.value)}
                    placeholder="Acme Corp"
                    className="w-full bg-surface-850 border border-surface-500/80 focus:border-focus-ring rounded-[var(--radius-control)] px-3.5 py-2.5 text-xs sm:text-sm text-content-primary focus:outline-none placeholder:text-content-subtle"
                  />
                </div>

                {/* Production Database */}
                <div>
                  <label className="block text-xs font-mono font-semibold text-content-secondary uppercase tracking-wider mb-1.5">
                    Primary Production Engine
                  </label>
                  <select
                    value={engine}
                    onChange={(e) => setEngine(e.target.value)}
                    className="w-full bg-surface-850 border border-surface-500/80 focus:border-focus-ring rounded-[var(--radius-control)] px-3.5 py-2.5 text-xs sm:text-sm text-content-primary focus:outline-none cursor-pointer"
                  >
                    {ENGINES.map((eng) => (
                      <option key={eng} value={eng} className="bg-surface-900 text-content-primary">
                        {eng}
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              {/* Engineering Team Size Pills */}
              <div>
                <label className="block text-xs font-mono font-semibold text-content-secondary uppercase tracking-wider mb-2">
                  Engineering Team Size
                </label>
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
                  {TEAM_SIZES.map((size) => (
                    <button
                      key={size}
                      type="button"
                      onClick={() => setTeamSize(size)}
                      className={`tap-44 px-3 py-2 rounded-[var(--radius-control)] text-xs font-mono transition-all cursor-pointer ${
                        teamSize === size
                          ? 'bg-accent text-accent-on font-bold shadow-sm'
                          : 'bg-surface-850 text-content-muted hover:text-content-primary border border-surface-600/60'
                      }`}
                    >
                      {size}
                    </button>
                  ))}
                </div>
              </div>

              {/* Architecture Requirements */}
              <div>
                <label className="block text-xs font-mono font-semibold text-content-secondary uppercase tracking-wider mb-1.5">
                  What are your migration or lifecycle requirements?
                </label>
                <textarea
                  rows={3}
                  value={note}
                  onChange={(e) => setNote(e.target.value)}
                  placeholder="e.g. We have 450 PostgreSQL tables and need automated zero-downtime migration review inside our GitLab CI pipeline with SOC2 compliance..."
                  className="w-full bg-surface-850 border border-surface-500/80 focus:border-focus-ring rounded-[var(--radius-control)] p-3 text-xs sm:text-sm text-content-primary focus:outline-none resize-none placeholder:text-content-subtle font-sans"
                />
              </div>

              {/* Footer Actions */}
              <div className="flex flex-col-reverse sm:flex-row items-center justify-between gap-3 pt-2">
                <span className="text-[11px] font-mono text-content-subtle text-center sm:text-left">
                  🔒 Enterprise NDA signed upon request &bull; Direct founder response
                </span>
                <button
                  type="submit"
                  disabled={isSubmitting}
                  className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-7 py-3 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on font-bold text-xs uppercase tracking-wider transition-all shadow-lg shadow-accent/20 cursor-pointer disabled:opacity-50 hover:scale-[1.02] active:scale-95"
                >
                  {isSubmitting ? (
                    <>
                      <Loader2 className="w-3.5 h-3.5 animate-spin" />
                      <span>Transmitting...</span>
                    </>
                  ) : (
                    <>
                      <span>Submit Request</span>
                      <Send className="w-3.5 h-3.5" />
                    </>
                  )}
                </button>
              </div>
            </form>
          </div>
        )}
      </div>
    </div>
  );
}
