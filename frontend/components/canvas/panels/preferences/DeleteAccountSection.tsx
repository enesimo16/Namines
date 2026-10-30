import React, { useState, type FormEvent } from 'react';
import { Trash2 } from 'lucide-react';
import { authService } from '../../../../services/api';
import { useToastStore } from '../../../../store/useToastStore';
import { useT } from '../../../../lib/i18n';
import { cardClass, inputClass } from './ui';

interface DeleteAccountSectionProps {
  email?: string;
  mfaEnabled: boolean | null;
  /** Hesap silindikten sonra oturumu kapatıp pencereyi kapatmak için. */
  onDeleted: () => void;
}

interface ApiErrorBody {
  Message?: string;
  message?: string;
  code?: string;
  organizations?: { id: string; name: string }[];
}

/**
 * Hesap silme tehlike bölgesi. Onay için e-posta adresi YAZILIR (yanlışlıkla tıklamaya karşı);
 * gerçek yeniden doğrulamayı (parola / MFA kodu / taze giriş) sunucu zorlar, burada yalnızca toplanır.
 */
export default function DeleteAccountSection({ email, mfaEnabled, onDeleted }: DeleteAccountSectionProps) {
  const t = useT();
  const showToast = useToastStore(state => state.showToast);
  const [confirmation, setConfirmation] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const confirmed = !!email && confirmation.trim().toLowerCase() === email.toLowerCase();

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!confirmed || busy) return;
    setBusy(true);
    setError(null);
    try {
      await authService.deleteAccount({
        confirmation: confirmation.trim(),
        password: password || undefined,
        twoFactorCode: code.trim() || undefined,
      });
      showToast(t('deleteAccount.done'), 'info');
      onDeleted();
    } catch (err) {
      const body = (err as { response?: { data?: ApiErrorBody } })?.response?.data;
      if (body?.code === 'TRANSFER_OWNERSHIP_REQUIRED' && body.organizations?.length) {
        setError(t('deleteAccount.transferOwnership', { orgs: body.organizations.map(o => o.name).join(', ') }));
      } else if (body?.code === 'REAUTH_REQUIRED') {
        setError(t('deleteAccount.reauthRequired'));
      } else {
        setError(body?.Message ?? body?.message ?? t('deleteAccount.failed'));
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className={`${cardClass} p-5 space-y-3 border-danger/30`}>
      <div className="flex items-center gap-2 border-b border-line pb-3">
        <Trash2 className="w-4 h-4 text-danger" />
        <h4 className="text-xs font-semibold text-danger uppercase tracking-wider">{t('deleteAccount.title')}</h4>
      </div>
      <p className="text-xs text-content-secondary leading-relaxed">{t('deleteAccount.warning')}</p>

      <label className="block space-y-1.5">
        <span className="text-[11px] text-content-muted">{t('deleteAccount.confirmLabel', { email: email ?? '' })}</span>
        <input
          type="email"
          autoComplete="off"
          value={confirmation}
          onChange={(e) => setConfirmation(e.target.value)}
          className={inputClass}
        />
      </label>

      <label className="block space-y-1.5">
        <span className="text-[11px] text-content-muted">{t('deleteAccount.passwordLabel')}</span>
        <input
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          className={inputClass}
        />
      </label>

      {mfaEnabled && (
        <label className="block space-y-1.5">
          <span className="text-[11px] text-content-muted">{t('deleteAccount.codeLabel')}</span>
          <input
            type="text"
            inputMode="numeric"
            autoComplete="one-time-code"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            className={inputClass}
          />
        </label>
      )}

      <p className="text-[11px] text-content-muted leading-normal">{t('deleteAccount.reauthNote')}</p>

      {error && <p role="alert" className="text-xs text-danger leading-normal">{error}</p>}

      <button
        type="submit"
        disabled={!confirmed || busy}
        className="px-4 py-2 text-xs rounded-[var(--radius-control)] bg-danger/10 text-danger border border-danger/20 hover:bg-danger/20 transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
      >
        {busy ? t('deleteAccount.working') : t('deleteAccount.button')}
      </button>
    </form>
  );
}
