'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Users, Loader2, Check, AlertTriangle } from 'lucide-react';
import { teamService } from '../../../services/api';
import { useAuthStore } from '../../../store/useAuthStore';
import { useAuthModalStore } from '../../../store/useAuthModalStore';
import { useToastStore } from '../../../store/useToastStore';

type Preview = { organization: string; role: string; expiresAt: string };

/**
 * Davet bağlantısıyla ekibe katılma ekranı.
 *
 * Katılım OTOMATİK DEĞİL: bağlantı tek kullanımlık ve açıldığı anda tükenirse,
 * yanlış hesapla giriş yapmış biri linke dokunduğunda başkasına ayrılmış koltuk
 * harcanmış olurdu. Önce ne olduğu gösteriliyor, kullanıcı onaylıyor.
 */
export default function JoinTeamPage() {
  const params = useParams<{ token: string }>();
  const token = params?.token ?? '';
  const router = useRouter();

  const { isAuthenticated, user } = useAuthStore();
  const openAuthModal = useAuthModalStore(s => s.open);
  const showToast = useToastStore(s => s.showToast);

  const [preview, setPreview] = useState<Preview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isJoining, setIsJoining] = useState(false);
  const [joined, setJoined] = useState(false);

  useEffect(() => {
    if (!token) return;
    let cancelled = false;

    teamService
      .previewInvite(token)
      .then(data => {
        if (!cancelled) setPreview(data);
      })
      .catch(err => {
        // Sunucu üç ayrı sebebi ayırıyor (kullanılmış / iptal / süresi dolmuş);
        // hepsini "geçersiz bağlantı" diye göstermek, kişinin yeni link mi
        // isteyeceğini yoksa zaten katılmış mı olduğunu anlamasını engellerdi.
        if (!cancelled) setError(err?.response?.data?.error ?? 'This invite link is not valid.');
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [token]);

  const handleJoin = async () => {
    if (!isAuthenticated) {
      openAuthModal();
      return;
    }

    setIsJoining(true);
    try {
      await teamService.acceptInvite(token);
      setJoined(true);
      showToast('You joined the team.', 'success');
      setTimeout(() => router.push('/'), 1200);
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Could not join the team.');
    } finally {
      setIsJoining(false);
    }
  };

  return (
    <div className="min-h-[calc(100vh-56px)] flex items-center justify-center px-4 py-10">
      <div className="w-full max-w-md glass-panel rounded-[var(--radius-modal)] p-6 text-content-primary">
        <div className="flex items-center gap-2 mb-5">
          <Users className="w-4 h-4" />
          <h1 className="text-base font-bold">Team invitation</h1>
        </div>

        {isLoading ? (
          <div className="flex items-center gap-2 text-xs text-content-muted py-8 justify-center">
            <Loader2 className="w-4 h-4 animate-spin" /> Checking the link…
          </div>
        ) : joined ? (
          <div className="text-center py-6 space-y-2">
            <Check className="w-8 h-8 mx-auto text-success-text" />
            <p className="text-sm font-semibold">You are in.</p>
            <p className="text-[11px] text-content-muted">Taking you to the workspace…</p>
          </div>
        ) : error ? (
          <div className="space-y-4">
            <div className="flex items-start gap-2.5 border border-content-primary/15 rounded-[var(--radius-card)] p-4 bg-surface-700">
              <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5 text-content-muted" />
              <p className="text-[11px] text-content-secondary leading-relaxed">{error}</p>
            </div>
            <button
              type="button"
              onClick={() => router.push('/')}
              className="w-full bg-white/[0.06] hover:bg-white/[0.1] text-content-secondary font-semibold py-2.5 rounded-[var(--radius-card)] text-sm transition-colors"
            >
              Go to Namines
            </button>
          </div>
        ) : (
          <div className="space-y-5">
            <div className="border border-content-primary/15 rounded-[var(--radius-card)] p-4 bg-surface-700 space-y-2">
              <p className="text-sm">
                You have been invited to <strong>{preview?.organization}</strong>
              </p>
              <p className="text-[11px] text-content-muted">
                You will join as <strong className="text-content-secondary">{preview?.role}</strong>. Team members
                share the same projects and can see each other&apos;s changes.
              </p>
              {preview?.expiresAt && (
                <p className="text-[10px] text-content-subtle">
                  This link expires {new Date(preview.expiresAt).toLocaleDateString()} and works only once.
                </p>
              )}
            </div>

            {isAuthenticated ? (
              <p className="text-[10px] text-content-muted text-center">
                Joining as <strong className="text-content-secondary">{user?.username}</strong>
              </p>
            ) : (
              <p className="text-[10px] text-content-muted text-center">
                You need an account first — signing up is free.
              </p>
            )}

            <button
              type="button"
              onClick={handleJoin}
              disabled={isJoining}
              className="w-full bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold py-2.5 rounded-[var(--radius-card)] text-sm transition-colors flex items-center justify-center gap-2 disabled:opacity-50"
            >
              {isJoining ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" /> Joining…
                </>
              ) : isAuthenticated ? (
                'Join the team'
              ) : (
                'Log in or sign up to join'
              )}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
