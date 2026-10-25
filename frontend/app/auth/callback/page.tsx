'use client';

import { useEffect, useState, Suspense } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Loader2, CheckCircle2, AlertTriangle } from 'lucide-react';
import { authService } from '../../../services/api';
import { useAuthStore } from '../../../store/useAuthStore';
import { useToastStore } from '../../../store/useToastStore';

function CallbackContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const setAuth = useAuthStore(s => s.setAuth);
  const showToast = useToastStore(s => s.showToast);

  const [status, setStatus] = useState<'processing' | 'success' | 'error'>('processing');
  const [errorMessage, setErrorMessage] = useState<string>('');

  useEffect(() => {
    const processOAuth = async () => {
      const code = searchParams.get('code');
      const providerParam = searchParams.get('provider') || 'github';
      const provider = providerParam.toLowerCase() === 'google' ? 'google' : 'github';
      const error = searchParams.get('error');

      if (error) {
        setStatus('error');
        setErrorMessage(`OAuth provider error: ${error}`);
        return;
      }

      if (!code) {
        setStatus('error');
        setErrorMessage('No authorization code was received from OAuth provider.');
        return;
      }

      try {
        const redirectUri = `${window.location.origin}/auth/callback?provider=${provider}`;
        const data = await authService.oauthCallback({
          provider,
          code,
          redirectUri,
        });

        setAuth(data.token, {
          username: data.user.username,
          email: data.user.email,
          type: data.user.type,
          isDev: data.user.isDev,
          companyName: data.user.companyName ?? undefined,
        }, data.quota);

        setStatus('success');
        showToast(`Successfully logged in via ${provider}!`, 'success');

        setTimeout(() => {
          router.replace('/');
        }, 1200);
      } catch (err: unknown) {
        setStatus('error');
        const msg = err instanceof Error ? err.message : 'OAuth exchange failed.';
        setErrorMessage(msg);
        showToast(msg, 'error');
      }
    };

    processOAuth();
  }, [searchParams, router, setAuth, showToast]);

  return (
    <div className="min-h-[60vh] flex items-center justify-center p-6">
      <div className="w-full max-w-md p-8 rounded-[var(--radius-modal)] bg-surface-850 border border-line text-center shadow-md">
        {status === 'processing' && (
          <div className="flex flex-col items-center gap-4">
            <Loader2 className="w-8 h-8 text-accent animate-spin" />
            <h2 className="text-base font-semibold text-ink-primary">Completing Sign-In</h2>
            <p className="text-xs text-ink-muted">
              Exchanging authorization code and securely creating your session...
            </p>
          </div>
        )}

        {status === 'success' && (
          <div className="flex flex-col items-center gap-4">
            <CheckCircle2 className="w-8 h-8 text-success" />
            <h2 className="text-base font-semibold text-ink-primary">Authenticated</h2>
            <p className="text-xs text-ink-muted">Redirecting you to your workspace...</p>
          </div>
        )}

        {status === 'error' && (
          <div className="flex flex-col items-center gap-4">
            <AlertTriangle className="w-8 h-8 text-danger-text" />
            <h2 className="text-base font-semibold text-ink-primary">Sign-In Failed</h2>
            <p className="text-xs text-danger-text font-mono break-all">{errorMessage}</p>
            <button
              type="button"
              onClick={() => router.replace('/')}
              className="mt-2 px-4 py-2 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-xs font-medium text-ink-primary cursor-pointer transition-colors"
            >
              Return Home
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

export default function AuthCallbackPage() {
  return (
    <Suspense
      fallback={
        <div className="min-h-[60vh] flex items-center justify-center p-6">
          <Loader2 className="w-8 h-8 text-accent animate-spin" />
        </div>
      }
    >
      <CallbackContent />
    </Suspense>
  );
}
