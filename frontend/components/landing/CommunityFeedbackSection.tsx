'use client';

import { useState } from 'react';
import {
  Bug,
  CheckCircle2,
  Database,
  Lightbulb,
  Loader2,
  MessageSquarePlus,
  Send,
} from 'lucide-react';
import api from '../../services/api';
import { useToastStore } from '../../store/useToastStore';

type FeedbackCategory = 'idea' | 'engine' | 'bug' | 'general';

const CATEGORIES: { id: FeedbackCategory; label: string; icon: typeof Lightbulb }[] = [
  { id: 'idea', label: 'Feature Idea', icon: Lightbulb },
  { id: 'engine', label: 'Dialect Request', icon: Database },
  { id: 'bug', label: 'Report Issue', icon: Bug },
  { id: 'general', label: 'General', icon: MessageSquarePlus },
];

export default function CommunityFeedbackSection() {
  const [category, setCategory] = useState<FeedbackCategory>('idea');
  const [message, setMessage] = useState('');
  const [email, setEmail] = useState('');
  const [botTrap, setBotTrap] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const showToast = useToastStore((s) => s.showToast);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (botTrap) {
      setSubmitted(true);
      return;
    }

    if (message.trim().length < 5) {
      showToast('Please provide a little more detail for our engineering team.', 'warning');
      return;
    }

    setIsSubmitting(true);
    try {
      await api.post('/feedback', {
        message: message.trim(),
        category: category === 'engine' ? 'idea' : category,
        email: email.trim() || undefined,
      });
      setSubmitted(true);
      showToast('Thank you! Your feedback has been sent directly to the core engineering team.', 'success');
      setMessage('');
      setEmail('');
    } catch {
      showToast('Failed to submit feedback. Please try again or reach out to hello@namines.com', 'error');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <section
      id="feedback"
      className="w-full max-w-[1144px] mx-auto px-4 sm:px-6 lg:px-8 py-16 lg:py-24"
    >
      <div className="rounded-[var(--radius-modal)] border border-surface-500 bg-surface-900/95 p-6 sm:p-8 lg:p-10">
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-10 items-start">
          {/* Left Column */}
          <div className="lg:col-span-5 flex flex-col gap-4">
            <span className="font-mono text-xs font-medium uppercase tracking-[0.14em] text-accent-text">
              Roadmap &amp; Feedback
            </span>
            <h2 className="font-mono text-2xl sm:text-3xl font-semibold tracking-tight text-content-primary leading-tight">
              Request an engine, dialect, or rule
            </h2>
            <p className="font-sans text-sm sm:text-base text-content-secondary leading-relaxed">
              Tell our database engineering team which SQL dialect, ORM target, or migration check you need next.
            </p>

            <div className="flex flex-col gap-2.5 pt-2 font-mono text-xs text-content-muted">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-accent-text shrink-0" />
                <span>Reviewed directly by core database engineers</span>
              </div>
              <div className="flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-accent-text shrink-0" />
                <span>Feeds directly into golden-file test suites</span>
              </div>
            </div>
          </div>

          {/* Right Column: Form */}
          <div className="lg:col-span-7">
            {submitted ? (
              <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-850 p-8 text-center flex flex-col items-center gap-3">
                <div className="w-11 h-11 rounded-full border border-surface-500 bg-surface-900 text-success-text flex items-center justify-center">
                  <CheckCircle2 className="w-5 h-5" />
                </div>
                <h3 className="font-mono text-base font-semibold text-content-primary">
                  Feedback received
                </h3>
                <p className="font-sans text-sm text-content-muted max-w-sm">
                  Our engineering team reviews every submission. Thank you for helping shape Namines.
                </p>
                <button
                  type="button"
                  onClick={() => setSubmitted(false)}
                  className="mt-2 px-4 py-2 min-h-[44px] rounded-[var(--radius-control)] border border-surface-500 bg-surface-800 hover:bg-surface-750 text-content-primary font-mono text-xs font-medium transition-colors cursor-pointer"
                >
                  Send another request
                </button>
              </div>
            ) : (
              <form
                onSubmit={handleSubmit}
                className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-850 p-5 sm:p-6 flex flex-col gap-4"
              >
                {/* Honeypot */}
                <div
                  aria-hidden="true"
                  style={{ opacity: 0, position: 'absolute', top: 0, left: 0, height: 0, width: 0, zIndex: -1 }}
                >
                  <input
                    type="text"
                    name="bot_protection_field"
                    value={botTrap}
                    onChange={(e) => setBotTrap(e.target.value)}
                    tabIndex={-1}
                    autoComplete="off"
                  />
                </div>

                <div>
                  <label className="block font-mono text-xs font-medium text-content-secondary uppercase tracking-wider mb-2">
                    Category
                  </label>
                  <div className="flex flex-wrap gap-2">
                    {CATEGORIES.map((cat) => {
                      const Icon = cat.icon;
                      const isSelected = category === cat.id;
                      return (
                        <button
                          key={cat.id}
                          type="button"
                          onClick={() => setCategory(cat.id)}
                          className={`flex-1 min-w-[130px] flex items-center justify-center gap-1.5 px-3 py-2.5 min-h-[44px] rounded-[var(--radius-control)] font-mono text-xs font-medium whitespace-nowrap transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring ${
                            isSelected
                              ? 'bg-accent text-accent-on border border-accent'
                              : 'bg-surface-900 text-content-muted hover:text-content-primary border border-surface-500'
                          }`}
                        >
                          <Icon className="w-3.5 h-3.5 shrink-0" />
                          <span>{cat.label}</span>
                        </button>
                      );
                    })}
                  </div>
                </div>

                <div>
                  <label
                    htmlFor="feedback-message"
                    className="block font-mono text-xs font-medium text-content-secondary uppercase tracking-wider mb-2"
                  >
                    Requirement Details
                  </label>
                  <textarea
                    id="feedback-message"
                    required
                    value={message}
                    onChange={(e) => setMessage(e.target.value)}
                    rows={4}
                    maxLength={4000}
                    placeholder="e.g., CockroachDB dialect support with distributed sequence generation and automated GitHub PR previews..."
                    className="w-full bg-surface-900 border border-surface-500 focus:border-focus-ring rounded-[var(--radius-control)] p-3 text-sm text-content-primary focus:outline-none resize-none placeholder:text-content-subtle font-sans leading-relaxed"
                  />

                  <div className="flex flex-wrap items-center gap-1.5 mt-2">
                    <span className="font-mono text-xs text-content-subtle">Quick add:</span>
                    {[
                      'CockroachDB Dialect',
                      'Prisma Lint Rules',
                      'Snowflake DDL',
                      'Custom Check Constraints',
                    ].map((tag) => (
                      <button
                        key={tag}
                        type="button"
                        onClick={() =>
                          setMessage((prev) =>
                            prev ? `${prev} + [${tag}]` : `I would like support for: ${tag}`,
                          )
                        }
                        className="tap-44 font-mono text-xs px-2 py-1 rounded-[var(--radius-control)] bg-surface-900 hover:bg-surface-800 text-content-muted hover:text-content-primary border border-surface-500/70 transition-colors cursor-pointer"
                      >
                        +{tag}
                      </button>
                    ))}
                  </div>
                </div>

                <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-3 pt-1">
                  <input
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="Email (optional, for follow-up)"
                    aria-label="Email for follow-up"
                    className="w-full sm:flex-1 min-h-[44px] bg-surface-900 border border-surface-500 focus:border-focus-ring rounded-[var(--radius-control)] px-3.5 py-2 text-sm text-content-primary focus:outline-none placeholder:text-content-subtle font-sans"
                  />
                  <button
                    type="submit"
                    disabled={isSubmitting}
                    className="inline-flex items-center justify-center gap-2 px-5 py-2.5 min-h-[44px] rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on font-mono text-xs font-semibold uppercase tracking-wider transition-colors cursor-pointer disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring"
                  >
                    {isSubmitting ? (
                      <>
                        <Loader2 className="w-3.5 h-3.5 animate-spin" />
                        <span>Sending...</span>
                      </>
                    ) : (
                      <>
                        <span>Submit</span>
                        <Send className="w-3.5 h-3.5" />
                      </>
                    )}
                  </button>
                </div>
              </form>
            )}
          </div>
        </div>
      </div>
    </section>
  );
}
