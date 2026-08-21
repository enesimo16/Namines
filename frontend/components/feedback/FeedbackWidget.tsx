'use client';

import React, { useState } from 'react';
import { usePathname } from 'next/navigation';
import { MessageSquarePlus, X, Bug, Lightbulb, MessageCircle, Loader2 } from 'lucide-react';
import api from '../../services/api';
import { useToastStore } from '../../store/useToastStore';

type Category = 'bug' | 'idea' | 'general';

const CATS: { key: Category; label: string; icon: React.ReactNode }[] = [
  { key: 'bug', label: 'Bug', icon: <Bug className="w-3.5 h-3.5" /> },
  { key: 'idea', label: 'Idea', icon: <Lightbulb className="w-3.5 h-3.5" /> },
  { key: 'general', label: 'General', icon: <MessageCircle className="w-3.5 h-3.5" /> },
];

export default function FeedbackWidget() {
  const pathname = usePathname();
  const [open, setOpen] = useState(false);
  const [category, setCategory] = useState<Category>('general');
  const [message, setMessage] = useState('');
  const [email, setEmail] = useState('');
  const [sending, setSending] = useState(false);
  const showToast = useToastStore((s) => s.showToast);

  // Render only on the homepage
  if (pathname !== '/') {
    return null;
  }

  const submit = async () => {
    if (message.trim().length < 3) {
      showToast('Please write a bit more detail.', 'warning');
      return;
    }
    setSending(true);
    try {
      await api.post('/feedback', { message, category, email: email || undefined });
      showToast('Thank you for your feedback!', 'success');
      setOpen(false);
      setMessage('');
      setEmail('');
      setCategory('general');
    } catch {
      showToast('Failed to send feedback. Please try again.', 'error');
    } finally {
      setSending(false);
    }
  };

  return (
    <>
      {/* Floating trigger */}
      {!open && (
        <button
          onClick={() => setOpen(true)}
          title="Send feedback"
          className="fixed bottom-4 left-4 z-[9990] flex items-center gap-1.5 px-3.5 py-2 rounded-full bg-surface-600 hover:bg-white/[0.08] border border-content-primary/15 text-content-primary hover:text-content-primary text-xs font-semibold transition-all cursor-pointer"
        >
          <MessageSquarePlus className="w-3.5 h-3.5" />
          <span className="hidden sm:inline">Feedback</span>
        </button>
      )}

      {open && (
        <div className="fixed bottom-4 left-4 right-4 sm:right-auto z-[9991] sm:w-[320px] max-w-full rounded-xl bg-surface-800 border border-content-primary/10 shadow-[0_20px_60px_rgba(0,0,0,0.6)] p-4 animate-in slide-in-from-bottom-3 duration-200">
          <div className="flex items-center justify-between mb-3">
            <h3 className="text-xs font-bold text-content-primary uppercase tracking-wider">Feedback</h3>
            <button onClick={() => setOpen(false)} className="p-1 text-content-muted hover:text-content-primary rounded-md hover:bg-white/[0.06] cursor-pointer" aria-label="Close">
              <X className="w-3.5 h-3.5" />
            </button>
          </div>

          <div className="flex gap-1.5 mb-2.5">
            {CATS.map((c) => (
              <button
                key={c.key}
                onClick={() => setCategory(c.key)}
                className={`flex-1 flex items-center justify-center gap-1 py-1.5 rounded-lg text-[11px] font-semibold border transition-all ${
                  category === c.key
                    ? 'bg-white/[0.08] border-white/25 text-content-primary'
                    : 'bg-surface-700 border-content-primary/10 text-content-muted hover:text-content-primary'
                }`}
              >
                {c.icon}
                <span>{c.label}</span>
              </button>
            ))}
          </div>

          <textarea
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            rows={3}
            maxLength={4000}
            placeholder="A bug, an idea, or general thoughts?"
            className="w-full bg-surface-700 border border-content-primary/10 focus:border-focus-ring rounded-lg py-2 px-3 text-sm text-content-primary focus:outline-none resize-none placeholder:text-content-muted"
          />

          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="Email (optional)"
            className="w-full mt-2 bg-surface-700 border border-content-primary/10 focus:border-focus-ring rounded-lg py-2 px-3 text-sm text-content-primary focus:outline-none placeholder:text-content-muted"
          />

          <button
            onClick={submit}
            disabled={sending}
            className="w-full mt-2.5 py-2 rounded-lg bg-content-primary hover:bg-content-secondary text-surface-900 text-sm font-semibold transition-all disabled:opacity-50 flex items-center justify-center gap-2 cursor-pointer"
          >
            {sending ? <Loader2 className="w-4 h-4 animate-spin" /> : 'Submit'}
          </button>
        </div>
      )}
    </>
  );
}
