import React from 'react';
import { Key, Lock, LogOut, Shield } from 'lucide-react';
import { useQuotaStore } from '../../../../store/useQuotaStore';
import MfaQrCode from '../MfaQrCode';
import DeleteAccountSection from './DeleteAccountSection';
import { NotLoggedIn, cardClass, inputClass, primaryBtnClass } from './ui';
import type { MfaSettings } from './useMfaSettings';

interface AccountTabProps {
  isAuthenticated: boolean;
  username?: string;
  email?: string;
  userType?: string;
  planTier: string | null;
  mfa: MfaSettings;
  onLogout: () => void;
  onAccountDeleted: () => void;
}

export default function AccountTab({ isAuthenticated, username, email, userType, planTier, mfa, onLogout, onAccountDeleted }: AccountTabProps) {
  const { dailyLimit, remaining, resetAt } = useQuotaStore();

  if (!isAuthenticated) {
    return (
      <div className="space-y-6">
        <NotLoggedIn icon={Lock} message="Please log in to manage two-factor authentication and account access." />
      </div>
    );
  }

  const remainingPercent = Math.round((remaining / dailyLimit) * 100);
  const isFreeUser = planTier !== 'Pro' && planTier !== 'Team' && planTier !== 'Dev';
  const planLabel = planTier === 'Dev' ? 'Dev — Unlimited'
    : planTier === 'Team' ? 'Team Member'
    : planTier === 'Pro' ? 'Pro Member'
    : 'Free Member';
  const isCorporate = userType === 'corporate';

  return (
    <div className="space-y-6">
      <div className="space-y-6">
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div className={`${isFreeUser ? 'md:col-span-1' : 'md:col-span-3'} ${cardClass} p-4 flex flex-col justify-between`}>
            <div className="space-y-1">
              <span className="text-[11px] font-medium text-content-muted uppercase tracking-wider">Account Level</span>
              <p className="text-sm font-semibold text-content-primary truncate">{username}</p>
            </div>
            <div className="mt-4">
              <span className="inline-block px-2.5 py-1 rounded-full text-[10px] font-mono font-medium uppercase tracking-wider bg-surface-700 border border-line text-content-secondary">
                {planLabel}
              </span>
            </div>
          </div>

          {isFreeUser && (
            <div className={`md:col-span-2 ${cardClass} p-4 space-y-3`}>
              <div className="flex justify-between items-center text-[11px] font-medium text-content-muted uppercase tracking-wider">
                <span>Daily Cloud Credits</span>
                <div className="flex items-center gap-2">
                  {resetAt && (
                    <span className="text-[10px] text-content-muted font-normal normal-case">
                      (resets at {new Date(resetAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} {new Date(resetAt).toLocaleDateString([], { month: 'short', day: 'numeric' })})
                    </span>
                  )}
                  <span className="text-content-primary font-semibold">{remainingPercent}% left</span>
                </div>
              </div>
              <div className="w-full h-2 bg-surface-900 rounded-full overflow-hidden border border-line">
                <div className="h-full bg-accent-text transition-all duration-500 rounded-full" style={{ width: `${remainingPercent}%` }} />
              </div>
              <p className="text-[11px] text-content-muted leading-normal">
                {remainingPercent <= 20
                  ? `Warning: Your daily cloud credits are running low (${remainingPercent}% remaining). Default local engine compiles code when exhausted.`
                  : `Your daily cloud credits quota is active (${remainingPercent}% remaining). Choose higher tier models in AI Configurations tab.`
                }
              </p>
            </div>
          )}
        </div>

        {/* License Authorization Levels */}
        <div className={`${cardClass} p-5 space-y-3`}>
          <div className="flex items-center gap-2 border-b border-line pb-3">
            <Lock className="w-4 h-4 text-content-muted" />
            <h4 className="text-xs font-semibold text-content-primary uppercase tracking-wider">License Authorization Levels</h4>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div className="flex items-center justify-between p-3 bg-surface-900/60 border border-line-strong rounded-[var(--radius-control)]">
              <span className="text-[11px] font-medium text-content-secondary">SignalR Realtime Multiplayer</span>
              <span className={`text-[10px] font-mono font-medium px-2 py-0.5 rounded-[var(--radius-control)] border ${isCorporate ? 'border-accent/40 bg-accent-subtle text-accent-text' : 'border-line bg-surface-700 text-content-muted'}`}>
                {isCorporate ? 'ENABLED' : 'DISABLED'}
              </span>
            </div>
            <div className="flex items-center justify-between p-3 bg-surface-900/60 border border-line-strong rounded-[var(--radius-control)]">
              <span className="text-[11px] font-medium text-content-secondary">Automated DBA Linter</span>
              <span className={`text-[10px] font-mono font-medium px-2 py-0.5 rounded-[var(--radius-control)] border ${isCorporate ? 'border-accent/40 bg-accent-subtle text-accent-text' : 'border-line bg-surface-700 text-content-secondary'}`}>
                {isCorporate ? 'UNLIMITED' : '100% DAILY'}
              </span>
            </div>
            <div className="flex items-center justify-between p-3 bg-surface-900/60 border border-line-strong rounded-[var(--radius-control)]">
              <span className="text-[11px] font-medium text-content-secondary">Cloud Workspace Sync</span>
              <span className="text-[10px] font-mono font-medium px-2 py-0.5 rounded-[var(--radius-control)] border border-accent/40 bg-accent-subtle text-accent-text">ACTIVE</span>
            </div>
          </div>
        </div>

        {/* 2FA Section */}
        <div className={`${cardClass} p-5 space-y-4`}>
          <div className="flex items-center gap-2 border-b border-line pb-3">
            <Shield className="w-4 h-4 text-content-muted" />
            <h4 className="text-xs font-semibold text-content-primary uppercase tracking-wider">
              Two-Factor Authentication
            </h4>
            {mfa.enabled === true && (
              <span className="ml-auto text-[10px] font-mono font-medium px-2 py-0.5 rounded-full border border-accent/40 bg-accent-subtle text-accent-text">ACTIVE</span>
            )}
          </div>

          {mfa.enabled === null ? (
            <p className="text-xs text-content-muted italic">Checking status…</p>
          ) : mfa.recoveryCodes ? (
            <div className="space-y-3">
              <p className="text-xs text-content-primary font-semibold">
                Save these recovery codes now — they are shown only once.
              </p>
              <p className="text-[11px] text-content-muted leading-normal">
                Each code works once. They are the only way back into your account if you lose
                your authenticator device.
              </p>
              <div className="grid grid-cols-2 gap-1.5 bg-surface-900 p-3 rounded-[var(--radius-control)] border border-line font-mono text-xs text-content-secondary select-all">
                {mfa.recoveryCodes.map(code => <span key={code}>{code}</span>)}
              </div>
              <button
                type="button"
                onClick={mfa.dismissRecoveryCodes}
                className={`px-4 py-2 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer ${primaryBtnClass}`}
              >
                I have saved them
              </button>
            </div>
          ) : mfa.enabled ? (
            <form onSubmit={mfa.disable} className="space-y-3">
              <p className="text-xs text-content-secondary leading-relaxed">
                Two-factor authentication is protecting this account.
                {mfa.recoveryLeft > 0 && ` ${mfa.recoveryLeft} recovery code${mfa.recoveryLeft === 1 ? '' : 's'} left.`}
              </p>
              <p className="text-[11px] text-content-muted leading-normal">
                Turning it off requires a current code — so a stolen session cannot remove your
                protection.
              </p>
              <div className="flex gap-2">
                <input
                  type="text"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  value={mfa.code}
                  onChange={(e) => mfa.setCode(e.target.value)}
                  placeholder="6-digit code"
                  aria-label="Authentication code to turn off two-factor"
                  className={`flex-1 ${inputClass}`}
                />
                <button
                  type="submit"
                  disabled={mfa.busy || !mfa.code.trim()}
                  className="px-4 py-2 text-xs rounded-[var(--radius-control)] bg-danger/10 text-danger border border-danger/20 hover:bg-danger/20 transition-colors cursor-pointer disabled:opacity-50"
                >
                  {mfa.busy ? 'Working…' : 'Turn off'}
                </button>
              </div>
            </form>
          ) : mfa.setup ? (
            <form onSubmit={mfa.enable} className="space-y-3">
              <p className="text-[11px] font-medium text-content-secondary uppercase tracking-wider">
                Step 1 — add this key to your authenticator app
              </p>
              <MfaQrCode otpauthUri={mfa.setup.otpauthUri} />
              <p className="text-[11px] text-content-muted leading-normal">
                Scan the code, or type the key below by hand.
              </p>
              <div className="font-mono text-xs bg-surface-900 border border-line p-2.5 rounded-[var(--radius-control)] select-all text-content-secondary break-all">
                {mfa.setup.sharedKey}
              </div>
              <a
                href={mfa.setup.otpauthUri}
                className="inline-block text-[11px] text-accent-text hover:underline"
              >
                Or open it in your authenticator app
              </a>
              <p className="text-[11px] font-medium text-content-secondary uppercase tracking-wider pt-1">
                Step 2 — enter the code it shows
              </p>
              <div className="flex gap-2">
                <input
                  type="text"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  value={mfa.code}
                  onChange={(e) => mfa.setCode(e.target.value)}
                  placeholder="6-digit code"
                  aria-label="Authentication code to turn on two-factor"
                  className={`flex-1 ${inputClass}`}
                />
                <button
                  type="submit"
                  disabled={mfa.busy || !mfa.code.trim()}
                  className={`px-4 py-2 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer disabled:opacity-50 ${primaryBtnClass}`}
                >
                  {mfa.busy ? 'Checking…' : 'Verify'}
                </button>
              </div>
              <button
                type="button"
                onClick={mfa.cancelSetup}
                className="text-[11px] text-content-muted hover:text-content-secondary cursor-pointer"
              >
                Cancel
              </button>
            </form>
          ) : (
            <div className="space-y-3">
              <p className="text-xs text-content-secondary leading-relaxed">
                Add a second step to sign-in using an authenticator app. A leaked password alone
                will no longer be enough to reach your databases.
              </p>
              <button
                type="button"
                onClick={mfa.startSetup}
                disabled={mfa.busy}
                className={`px-4 py-2 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer disabled:opacity-50 ${primaryBtnClass}`}
              >
                {mfa.busy ? 'Starting…' : 'Turn on'}
              </button>
            </div>
          )}
        </div>

        {/* API Keys */}
        <div className={`${cardClass} p-5 space-y-3`}>
          <div className="flex items-center gap-2 border-b border-line pb-3">
            <Key className="w-4 h-4 text-content-muted" />
            <h4 className="text-xs font-semibold text-content-primary uppercase tracking-wider">API Keys</h4>
          </div>
          <p className="text-xs text-content-secondary leading-relaxed">
            API keys are managed per project, not per account &mdash; each key carries its own
            table-level read/write permissions and its own audit trail.
          </p>
          <p className="text-[11px] text-content-muted leading-normal">
            Open your project in Namines Desk and go to <span className="font-semibold text-content-secondary">API Keys</span> to
            create, scope, or revoke a key.
          </p>
        </div>

        <DeleteAccountSection email={email} mfaEnabled={mfa.enabled} onDeleted={onAccountDeleted} />

        <button
          onClick={onLogout}
          className="flex md:hidden items-center justify-center gap-2 w-full py-2.5 bg-surface-800 hover:bg-danger-subtle text-content-muted hover:text-danger border border-line hover:border-danger/20 text-xs font-medium rounded-[var(--radius-control)] transition-all cursor-pointer"
        >
          <LogOut className="w-4 h-4" />
          <span>Log Out</span>
        </button>
      </div>
    </div>
  );
}
