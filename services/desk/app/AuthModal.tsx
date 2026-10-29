'use client';

import React, { useState, useRef, useEffect } from 'react';
import { X, AlertTriangle } from 'lucide-react';
import { login, register, setToken, AuthError } from '../lib/auth';
import { useT } from '../lib/i18n';
import { assetUrl } from '../lib/config';

interface AuthModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (token: string) => void;
  initialMode?: 'login' | 'signup';
}

export default function AuthModal({
  isOpen,
  onClose,
  onSuccess,
  initialMode = 'login',
}: AuthModalProps) {
  const t = useT();
  const modalRef = useRef<HTMLDivElement>(null);

  const [isLogin, setIsLogin] = useState(initialMode === 'login');
  const [userType, setUserType] = useState<'individual' | 'corporate'>('individual');

  const [email, setEmail] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [companyName, setCompanyName] = useState('');

  const [isLoading, setIsLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  useEffect(() => {
    setIsLogin(initialMode === 'login');
    setErrorMsg(null);
  }, [initialMode, isOpen]);

  // Escape to close
  useEffect(() => {
    if (!isOpen) return;
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const handleOAuth = (provider: string) => {
    setErrorMsg(`${provider}: ${t('authModal.oauthNotice')}`);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);
    setErrorMsg(null);

    try {
      if (isLogin) {
        const jwt = await login(email.trim(), password);
        setToken(jwt);
        onSuccess(jwt);
        onClose();
      } else {
        const jwt = await register({
          email: email.trim(),
          password,
          username: username.trim() || undefined,
          type: userType,
          companyName: userType === 'corporate' ? companyName.trim() || undefined : undefined,
        });
        setToken(jwt);
        onSuccess(jwt);
        onClose();
      }
    } catch (err) {
      setErrorMsg(err instanceof AuthError ? err.message : isLogin ? t('authModal.signInFailed') : t('authModal.signUpFailed'));
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div
      className="overlay"
      onClick={e => { if (e.target === e.currentTarget) onClose(); }}
      role="presentation"
    >
      <div
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="auth-modal-title"
        className="dialog auth-dialog"
      >
        {/* Header */}
        <div className="auth-header">
          <div className="auth-brand">
            <img src={assetUrl('/brand/namines-symbol.svg')} alt="Namines" className="auth-brand-logo" />
            <span className="auth-brand-text">Namines Desk</span>
            <span className="brand-badge">auth</span>
          </div>
          <button
            onClick={onClose}
            className="icon-btn auth-close-btn"
            aria-label={t('common.close')}
          >
            <X size={16} />
          </button>
        </div>

        {/* Tab selector */}
        <div className="auth-tabs">
          <button
            type="button"
            onClick={() => { setIsLogin(true); setErrorMsg(null); }}
            className={`auth-tab ${isLogin ? 'is-active' : ''}`}
          >
            {t('authModal.signIn')}
          </button>
          <button
            type="button"
            onClick={() => { setIsLogin(false); setErrorMsg(null); }}
            className={`auth-tab ${!isLogin ? 'is-active' : ''}`}
          >
            {t('authModal.signUp')}
          </button>
        </div>

        {/* OAuth Buttons */}
        <div className="auth-oauth">
          <button
            type="button"
            onClick={() => handleOAuth('Google')}
            className="oauth-btn"
          >
            <svg className="oauth-icon" viewBox="0 0 24 24">
              <path fill="#4285F4" d="M21.35 11.1H12v2.9h5.35c-.23 1.4-1.6 4.1-5.35 4.1-3.22 0-5.85-2.66-5.85-5.95S8.78 6.2 12 6.2c1.84 0 3.07.78 3.77 1.45l2.57-2.48C16.7 3.66 14.55 2.7 12 2.7 6.98 2.7 2.9 6.78 2.9 11.8s4.08 9.1 9.1 9.1c5.25 0 8.74-3.69 8.74-8.89 0-.6-.07-1.05-.14-1.51z"/>
            </svg>
            <span>{t('authModal.continueWithGoogle')}</span>
          </button>
          <button
            type="button"
            onClick={() => handleOAuth('GitHub')}
            className="oauth-btn"
          >
            <svg className="oauth-icon" viewBox="0 0 24 24" fill="currentColor">
              <path d="M12 2C6.48 2 2 6.58 2 12.25c0 4.53 2.87 8.37 6.84 9.73.5.1.68-.22.68-.5 0-.24-.01-.87-.01-1.71-2.78.62-3.37-1.37-3.37-1.37-.45-1.18-1.11-1.49-1.11-1.49-.91-.64.07-.63.07-.63 1 .07 1.53 1.05 1.53 1.05.9 1.57 2.36 1.12 2.93.86.09-.67.35-1.12.64-1.38-2.22-.26-4.56-1.14-4.56-5.06 0-1.12.39-2.03 1.03-2.75-.1-.26-.45-1.31.1-2.72 0 0 .84-.28 2.75 1.05a9.3 9.3 0 0 1 2.5-.35c.85 0 1.7.12 2.5.35 1.91-1.33 2.75-1.05 2.75-1.05.55 1.41.2 2.46.1 2.72.64.72 1.03 1.63 1.03 2.75 0 3.93-2.34 4.79-4.57 5.05.36.32.68.94.68 1.9 0 1.37-.01 2.47-.01 2.81 0 .28.18.61.69.5A9.99 9.99 0 0 0 22 12.25C22 6.58 17.52 2 12 2z"/>
            </svg>
            <span>{t('authModal.continueWithGithub')}</span>
          </button>
        </div>

        <div className="auth-divider">
          <span className="auth-divider-line" />
          <span className="auth-divider-text">{t('authModal.or')}</span>
          <span className="auth-divider-line" />
        </div>

        {errorMsg && (
          <div className="notice notice-error" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <AlertTriangle size={15} style={{ flexShrink: 0 }} />
            <span>{errorMsg}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="auth-form">
          {!isLogin && (
            <>
              <div className="auth-type-selector">
                <button
                  type="button"
                  onClick={() => setUserType('individual')}
                  className={`auth-type-btn ${userType === 'individual' ? 'is-active' : ''}`}
                >
                  {t('authModal.personal')}
                </button>
                <button
                  type="button"
                  onClick={() => setUserType('corporate')}
                  className={`auth-type-btn ${userType === 'corporate' ? 'is-active' : ''}`}
                >
                  {t('authModal.corporate')}
                </button>
              </div>

              {userType === 'corporate' && (
                <div className="field">
                  <label htmlFor="auth-company">{t('authModal.companyName')}</label>
                  <input
                    id="auth-company"
                    type="text"
                    value={companyName}
                    placeholder="Acme Corp"
                    onChange={e => setCompanyName(e.target.value)}
                    required
                  />
                </div>
              )}

              <div className="field">
                <label htmlFor="auth-username">{t('authModal.username')}</label>
                <input
                  id="auth-username"
                  type="text"
                  value={username}
                  placeholder="johndoe"
                  onChange={e => setUsername(e.target.value)}
                  required
                />
              </div>
            </>
          )}

          <div className="field">
            <label htmlFor="auth-email">{t('authModal.email')}</label>
            <input
              id="auth-email"
              type="email"
              value={email}
              placeholder="name@example.com"
              onChange={e => setEmail(e.target.value)}
              autoFocus
              required
            />
          </div>

          <div className="field">
            <label htmlFor="auth-password">{t('authModal.password')}</label>
            <input
              id="auth-password"
              type="password"
              value={password}
              placeholder="••••••••"
              onChange={e => setPassword(e.target.value)}
              required
            />
          </div>

          <button
            type="submit"
            className="btn btn-primary"
            style={{ width: '100%', marginTop: 8 }}
            disabled={isLoading}
          >
            {isLoading
              ? isLogin ? t('authModal.signingIn') : t('authModal.signingUp')
              : isLogin ? t('authModal.signIn') : t('authModal.createAccount')}
          </button>
        </form>

        <div className="auth-footer-toggle">
          <button
            type="button"
            className="auth-toggle-link"
            onClick={() => { setIsLogin(prev => !prev); setErrorMsg(null); }}
          >
            {isLogin ? t('authModal.dontHaveAccount') : t('authModal.alreadyHaveAccount')}
          </button>
        </div>
      </div>
    </div>
  );
}
