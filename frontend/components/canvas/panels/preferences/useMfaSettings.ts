import { useEffect, useState, type FormEvent } from 'react';
import { authService } from '../../../../services/api';
import { useToastStore } from '../../../../store/useToastStore';

/**
 * Çok faktörlü doğrulama (TOTP).
 *
 * `setup` yalnızca kurulum sırasında dolu; içindeki `sharedKey` KULLANICININ
 * SIRRI ve hiçbir üçüncü tarafa gönderilmiyor (ör. bir QR üretme servisine).
 * Kurtarma kodları sunucudan BİR KEZ geliyor; kullanıcı kaydedene kadar
 * ekranda tutuluyor ve başka hiçbir yere yazılmıyor.
 */
export function useMfaSettings(isOpen: boolean, isAuthenticated: boolean) {
  const showToast = useToastStore(state => state.showToast);
  const [enabled, setEnabled] = useState<boolean | null>(null);
  const [recoveryLeft, setRecoveryLeft] = useState(0);
  const [setup, setSetup] = useState<{ sharedKey: string; otpauthUri: string } | null>(null);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null);

  const refreshStatus = () =>
    authService.mfa.status()
      .then(status => {
        setEnabled(status.enabled);
        setRecoveryLeft(status.recoveryCodesLeft);
      })
      // Giriş yapılmamış ya da uç erişilemez: durum bilinmiyor bırakılıyor.
      .catch(() => setEnabled(null));

  // Arayüz "Turn on" mu "Turn off" mu göstereceğini bilmeli.
  useEffect(() => {
    if (!isOpen || !isAuthenticated) return;
    authService.mfa.status()
      .then(status => {
        setEnabled(status.enabled);
        setRecoveryLeft(status.recoveryCodesLeft);
      })
      .catch(() => setEnabled(null));
  }, [isOpen, isAuthenticated]);

  const startSetup = async () => {
    setBusy(true);
    try {
      setSetup(await authService.mfa.setup());
      setCode('');
    } catch {
      showToast('Two-factor setup could not be started.', 'error');
    } finally {
      setBusy(false);
    }
  };

  const enable = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      const result = await authService.mfa.enable(code);
      setRecoveryCodes(result.recoveryCodes ?? []);
      setSetup(null);
      setCode('');
      await refreshStatus();
      showToast('Two-factor authentication is on.', 'success');
    } catch {
      // Sunucu kodu neden reddettiğini söylemiyor (doğru): sayaç tabanlı
      // ipucu vermek saldırgana yardım ederdi. Kullanıcıya en olası sebep
      // gösteriliyor.
      showToast('That code is not valid. Check your device clock and try again.', 'error');
    } finally {
      setBusy(false);
    }
  };

  const disable = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await authService.mfa.disable(code);
      setCode('');
      await refreshStatus();
      showToast('Two-factor authentication is off.', 'info');
    } catch {
      showToast('That code is not valid.', 'error');
    } finally {
      setBusy(false);
    }
  };

  const cancelSetup = () => {
    setSetup(null);
    setCode('');
  };

  const dismissRecoveryCodes = () => setRecoveryCodes(null);

  return {
    enabled, recoveryLeft, setup, code, setCode, busy, recoveryCodes,
    startSetup, enable, disable, cancelSetup, dismissRecoveryCodes,
  };
}

export type MfaSettings = ReturnType<typeof useMfaSettings>;
