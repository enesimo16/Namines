'use client';

import { useState } from 'react';
import { MessageSquarePlus, Lightbulb, Bug, Database, Send, CheckCircle2, Loader2 } from 'lucide-react';
import api from '../../services/api';
import { useToastStore } from '../../store/useToastStore';

type FeedbackCategory = 'idea' | 'engine' | 'bug' | 'general';

const CATEGORIES: { id: FeedbackCategory; label: string; icon: typeof Lightbulb }[] = [
  { id: 'idea', label: 'Feature Idea', icon: Lightbulb },
  { id: 'engine', label: 'Engine / Dialect Request', icon: Database },
  { id: 'bug', label: 'Report Issue', icon: Bug },
  { id: 'general', label: 'General Feedback', icon: MessageSquarePlus },
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

    // Bot Honeypot: Silent dummy success for spam bots
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
    <section id="feedback" className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-16 sm:py-24">
      <div className="relative rounded-[var(--radius-modal)] border border-surface-500/80 bg-surface-900/90 p-5 sm:p-8 lg:p-12 shadow-2xl backdrop-blur-xl overflow-hidden">
        {/* Subtle Ambient Background Glow */}
        <div
          aria-hidden="true"
          className="pointer-events-none absolute -bottom-20 -right-20 w-96 h-96 rounded-full opacity-15 blur-[120px]"
          style={{ backgroundColor: 'var(--namines-teal-glow)' }}
        />

        <div className="grid grid-cols-1 lg:grid-cols-12 gap-10 items-center relative z-10">
          
          {/* Left Column: Heading & Value Prop */}
          <div className="lg:col-span-5 flex flex-col gap-4">
            <h2 className="text-3xl sm:text-4xl font-bold tracking-tight text-content-primary">
              Request an Engine, Dialect, or Feature
            </h2>

            <p className="text-sm text-content-muted leading-relaxed">
              Tell our database engineering team what engine, ORM export, or CI/CD migration rule you need next. Every submission directly influences our dialect roadmap and deterministic test suites.
            </p>

            <div className="flex flex-col gap-2.5 pt-2 font-mono text-xs text-content-secondary">
              <div className="flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-accent-text" />
                <span>Reviewed directly by core database engineers</span>
              </div>
              <div className="flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-accent-text" />
                <span>Priority triage for production enterprise users</span>
              </div>
            </div>
          </div>

          {/* Right Column: Interactive Form */}
          <div className="lg:col-span-7">
            {submitted ? (
              <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-850/80 p-8 text-center flex flex-col items-center gap-3">
                <div className="w-12 h-12 rounded-full bg-accent/20 text-accent flex items-center justify-center mb-1">
                  <CheckCircle2 className="w-6 h-6" />
                </div>
                <h3 className="text-lg font-bold text-content-primary">Thank you for your feedback!</h3>
                <p className="text-xs text-content-muted max-w-sm">
                  Our core team reviews every suggestion. We appreciate you helping shape the future of Namines.
                </p>
                <button
                  type="button"
                  onClick={() => setSubmitted(false)}
                  className="mt-3 px-4 py-2 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-primary text-xs font-semibold transition-colors cursor-pointer"
                >
                  Send another request
                </button>
              </div>
            ) : (
              <form onSubmit={handleSubmit} className="rounded-[var(--radius-card)] border border-surface-500/70 bg-surface-850/80 p-5 sm:p-7 shadow-xl flex flex-col gap-4">
                
                {/* Honeypot Bot Protection */}
                <div aria-hidden="true" style={{ opacity: 0, position: 'absolute', top: 0, left: 0, height: 0, width: 0, zIndex: -1 }}>
                  <input
                    type="text"
                    name="bot_protection_field"
                    value={botTrap}
                    onChange={(e) => setBotTrap(e.target.value)}
                    tabIndex={-1}
                    autoComplete="off"
                  />
                </div>

                {/* Category Selector Pills — Fully Responsive, Never Truncated */}
                <div>
                  <label className="block text-xs font-mono font-semibold text-content-secondary uppercase tracking-wider mb-2">
                    Request Category
                  </label>
                  <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
                    {CATEGORIES.map((cat) => {
                      const Icon = cat.icon;
                      const isSelected = category === cat.id;
                      return (
                        <button
                          key={cat.id}
                          type="button"
                          onClick={() => setCategory(cat.id)}
                          className={`tap-44 flex items-center justify-center gap-1.5 px-2.5 py-2.5 rounded-[var(--radius-control)] text-xs font-mono transition-all cursor-pointer text-center ${
                            isSelected
                              ? 'bg-accent text-accent-on font-bold shadow-sm'
                              : 'bg-surface-800 text-content-muted hover:text-content-primary border border-surface-600/60 hover:bg-surface-750'
                          }`}
                        >
                          <Icon className="w-3.5 h-3.5 shrink-0" />
                          <span className="whitespace-nowrap">{cat.label}</span>
                        </button>
                      );
                    })}
                  </div>
                </div>

                {/* Message Textarea */}
                <div>
                  <div className="flex items-center justify-between mb-1.5">
                    <label className="text-xs font-mono font-semibold text-content-secondary uppercase tracking-wider">
                      Describe your engine or feature requirement
                    </label>
                  </div>
                  <textarea
                    required
                    value={message}
                    onChange={(e) => setMessage(e.target.value)}
                    rows={4}
                    maxLength={4000}
                    placeholder="e.g., We need CockroachDB dialect support with distributed sequence generation and automated GitHub PR migration previews..."
                    className="w-full bg-surface-900 border border-surface-500/80 focus:border-focus-ring rounded-[var(--radius-control)] p-3 text-xs sm:text-sm text-content-primary focus:outline-none resize-none placeholder:text-content-subtle font-sans leading-relaxed"
                  />
                  {/* Quick-suggestion chips */}
                  <div className="flex flex-wrap items-center gap-1.5 mt-2">
                    <span className="text-[10px] font-mono text-content-subtle">Quick tag:</span>
                    {[
                      'CockroachDB Dialect',
                      'Prisma Lint Rules',
                      'Snowflake DDL',
                      'Custom Check Constraints',
                    ].map((tag) => (
                      <button
                        key={tag}
                        type="button"
                        onClick={() => setMessage((prev) => (prev ? `${prev} + [${tag}]` : `I would like support for: ${tag}`))}
                        className="text-[10px] font-mono px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 text-content-muted hover:text-content-primary border border-surface-600/50 transition-colors cursor-pointer"
                      >
                        +{tag}
                      </button>
                    ))}
                  </div>
                </div>

                {/* Email (Optional) & Submit Button Row */}
                <div className="flex flex-col sm:flex-row items-center gap-3 pt-1">
                  <input
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="Your email (optional, for follow-up)"
                    className="w-full sm:flex-1 bg-surface-900 border border-surface-500/80 focus:border-focus-ring rounded-[var(--radius-control)] px-3.5 py-2.5 text-xs text-content-primary focus:outline-none placeholder:text-content-subtle"
                  />
                  <button
                    type="submit"
                    disabled={isSubmitting}
                    className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-6 py-2.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on font-bold text-xs uppercase tracking-wider transition-all shadow-md shadow-accent/15 cursor-pointer disabled:opacity-50 hover:scale-[1.02] active:scale-95"
                  >
                    {isSubmitting ? (
                      <>
                        <Loader2 className="w-3.5 h-3.5 animate-spin" />
                        <span>Sending...</span>
                      </>
                    ) : (
                      <>
                        <span>Submit Feedback</span>
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
