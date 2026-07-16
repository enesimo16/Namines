'use client';

import React, { useState } from 'react';
import { usePathname } from 'next/navigation';
import { MessageSquarePlus, X, Bug, Lightbulb, MessageCircle, Loader2 } from 'lucide-react';
import api from '../../services/api';
import { useToastStore } from '../../store/useToastStore';

type Category = 'bug' | 'idea' | 'general';

const CATS: { key: Category; label: string; icon: React.ReactNode }[] = [
  { key: 'bug', label: 'Bug', icon: <Bug className="w-4 h-4" /> },
  { key: 'idea', label: 'Idea', icon: <Lightbulb className="w-4 h-4" /> },
  { key: 'general', label: 'General', icon: <MessageCircle className="w-4 h-4" /> },
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
      {/* Floating trigger on the bottom-left */}
      {!open && (
        <button
          onClick={() => setOpen(true)}
          title="Send feedback"
          className="fixed bottom-5 left-5 z-[9990] flex items-center gap-2 px-4 py-2.5 rounded-full bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-bold shadow-[0_8px_24px_rgba(79,70,229,0.4)] transition-all hover:scale-105 active:scale-95 cursor-pointer"
        >
          <MessageSquarePlus className="w-4 h-4" />
          <span>Feedback</span>
        </button>
      )}

      {open && (
        <div className="fixed bottom-5 left-5 z-[9991] w-[340px] max-w-[calc(100vw-40px)] rounded-2xl bg-[#0b1120]/95 backdrop-blur-2xl border border-indigo-500/20 shadow-[0_20px_60px_rgba(0,0,0,0.7)] p-5 animate-in slide-in-from-bottom-3 duration-200">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-extrabold text-indigo-100 uppercase tracking-wider">Feedback</h3>
            <button onClick={() => setOpen(false)} className="p-1 text-zinc-400 hover:text-white rounded-lg hover:bg-white/5 cursor-pointer">
              <X className="w-4 h-4" />
            </button>
          </div>

          <div className="flex gap-2 mb-3">
            {CATS.map((c) => (
              <button
                key={c.key}
                onClick={() => setCategory(c.key)}
                className={`flex-1 flex items-center justify-center gap-1.5 py-2 rounded-xl text-xs font-semibold border transition-all ${
                  category === c.key
                    ? 'bg-indigo-500/15 border-indigo-500/50 text-indigo-200'
                    : 'bg-zinc-950/40 border-zinc-800 text-zinc-400 hover:text-zinc-200'
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
            rows={4}
            maxLength={4000}
            placeholder="What is on your mind? A bug, an idea, or general thoughts?"
            className="w-full bg-[#0a0f1d] border border-zinc-800 focus:border-indigo-500/50 rounded-xl py-2.5 px-3.5 text-sm text-white focus:outline-none focus:ring-1 focus:ring-indigo-500/10 resize-none placeholder:text-zinc-600"
          />

          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="Email (optional - for replies)"
            className="w-full mt-2 bg-[#0a0f1d] border border-zinc-800 focus:border-indigo-500/50 rounded-xl py-2.5 px-3.5 text-sm text-white focus:outline-none focus:ring-1 focus:ring-indigo-500/10 placeholder:text-zinc-600"
          />

          <button
            onClick={submit}
            disabled={sending}
            className="w-full mt-3 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-sm font-bold transition-all disabled:opacity-50 flex items-center justify-center gap-2 cursor-pointer active:scale-[0.98]"
          >
            {sending ? <Loader2 className="w-4 h-4 animate-spin" /> : 'Submit'}
          </button>
        </div>
      )}
    </>
  );
}
