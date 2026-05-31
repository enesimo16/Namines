'use client';

import React, { useState } from 'react';
import { X, Lock, Mail, User, Building2, CheckCircle2, AlertTriangle, ShieldCheck, Sparkles } from 'lucide-react';
import { useAuthStore } from '../../../store/useAuthStore';
import { useToastStore } from '../../../store/useToastStore';
import { useProjectHistoryStore } from '../../../store/useProjectHistoryStore';
import { authService } from '../../../services/api';

interface AuthModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function AuthModal({ isOpen, onClose }: AuthModalProps) {
  const { setAuth } = useAuthStore();
  const { showToast } = useToastStore();
  const { projects } = useProjectHistoryStore();

  const [isLogin, setIsLogin] = useState(true);
  const [userType, setUserType] = useState<'individual' | 'corporate'>('individual');
  
  const [email, setEmail] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [companyName, setCompanyName] = useState('');
  
  const [isLoading, setIsLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  if (!isOpen) return null;

  const handleSyncLocalProjects = async () => {
    if (projects.length === 0) return;
    
    try {
      const projectsToSync = projects.map(p => ({
        id: p.id,
        name: p.name,
        dbType: p.dbType,
        schemaJson: JSON.stringify(p.schema),
        nodePositionsJson: JSON.stringify(p.nodePositions)
      }));

      await authService.syncProjects(projectsToSync);
      showToast(`${projects.length} yerel proje bulut hesabınızla başarıyla senkronize edildi!`, 'success');
    } catch (err: any) {
      console.error('Senkronizasyon hatası:', err);
      showToast('Projeler buluta aktarılırken bir hata oluştu.', 'warning');
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsLoading(true);
    setErrorMsg(null);

    try {
      if (isLogin) {
        // Login Flow
        const data = await authService.login(email, password);
        setAuth(data.token, {
          username: data.user.username,
          email: data.user.email,
          type: data.user.type,
          companyName: data.user.companyName
        });
        showToast('Başarıyla giriş yapıldı. Bulut yedekleme aktif!', 'success');
        
        // Trigger local-to-cloud synchronization
        await handleSyncLocalProjects();
        onClose();
      } else {
        // Register Flow
        const data = await authService.register(
          email, 
          password, 
          username || undefined, 
          userType, 
          userType === 'corporate' ? companyName : undefined
        );
        
        setAuth(data.token, {
          username: data.user.username,
          email: data.user.email,
          type: data.user.type,
          companyName: data.user.companyName
        });
        
        showToast(`Hesabınız oluşturuldu. Hoş geldiniz!`, 'success');
        
        // Trigger local-to-cloud synchronization
        await handleSyncLocalProjects();
        onClose();
      }
    } catch (err: any) {
      const msg = err.response?.data?.message || 'Bir hata oluştu. Lütfen bilgilerinizi kontrol edin.';
      setErrorMsg(msg);
      showToast(msg, 'error');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[9999] flex items-center justify-center p-4 bg-black/75 backdrop-blur-sm animate-fade-in">
      
      {/* Glow Backdrop Orbs */}
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[500px] h-[500px] bg-gradient-to-tr from-indigo-500/10 to-violet-500/10 rounded-full blur-[90px] pointer-events-none -z-10" />

      {/* Main Glassmorphic Container */}
      <div className="relative w-full max-w-md bg-[#09111F]/90 backdrop-blur-2xl border border-indigo-500/20 rounded-3xl shadow-[0_20px_60px_rgba(0,0,0,0.8)] overflow-hidden flex flex-col p-6 pb-12 animate-in zoom-in-95 duration-200">
        
        {/* Header */}
        <div className="flex items-center justify-between pb-4 border-b border-indigo-500/10">
          <div className="flex items-center gap-2">
            <ShieldCheck className="w-5 h-5 text-indigo-400" />
            <h3 className="text-sm font-extrabold uppercase tracking-wider text-indigo-100">
              {isLogin ? 'BULUT OTURUMU AÇ' : 'YENİ BULUT HESABI'}
            </h3>
          </div>
          <button 
            onClick={onClose}
            className="p-1 hover:bg-white/5 rounded-lg text-zinc-400 hover:text-white transition-all cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Info Banner */}
        <div className="my-4 p-3 rounded-xl bg-indigo-500/10 border border-indigo-500/20 flex gap-2.5 items-start">
          <CheckCircle2 className="w-4.5 h-4.5 text-indigo-400 shrink-0 mt-0.5" />
          <p className="text-[10px] text-indigo-200/90 leading-relaxed font-semibold">
            Kayıt olduktan sonra tasarladığınız tüm şemalar ve dal geçmişiniz sunucu üzerinde güvenle saklanır, asla kaybolmaz.
          </p>
        </div>

        {/* Dynamic Tab Selector styles based on login state to match the Stitch designs perfectly! */}
        {isLogin ? (
          /* LOGIN TABS: Capsule shape tab selector matching stitch_screen_1.png */
          <div className="flex bg-zinc-950/60 p-1 rounded-xl border border-zinc-800/80 mb-5">
            <button
              type="button"
              onClick={() => { setIsLogin(true); setErrorMsg(null); }}
              className="flex-1 py-1.5 text-center text-xs font-bold rounded-lg tracking-wide transition-all duration-200 bg-[#4F46E5] text-white shadow-[0_2px_10px_rgba(79,70,229,0.4)]"
            >
              Giriş Yap
            </button>
            <button
              type="button"
              onClick={() => { setIsLogin(false); setErrorMsg(null); }}
              className="flex-1 py-1.5 text-center text-xs font-bold rounded-lg tracking-wide transition-all duration-200 bg-transparent text-zinc-400 hover:text-zinc-200"
            >
              Kayıt Ol
            </button>
          </div>
        ) : (
          /* SIGNUP TABS: Flat Underlined border tab style matching stitch_screen_2.png */
          <div className="flex border-b border-zinc-800/80 mb-5 pb-px">
            <button
              type="button"
              onClick={() => { setIsLogin(true); setErrorMsg(null); }}
              className="flex-1 py-2 text-center text-xs font-bold tracking-wide transition-all duration-200 text-zinc-400 hover:text-zinc-200 border-b-2 border-transparent"
            >
              Giriş Yap
            </button>
            <button
              type="button"
              onClick={() => { setIsLogin(false); setErrorMsg(null); }}
              className="flex-1 py-2 text-center text-xs font-bold tracking-wide transition-all duration-200 text-white border-b-2 border-indigo-500 drop-shadow-[0_0_8px_rgba(99,102,241,0.6)]"
            >
              Kayıt Ol
            </button>
          </div>
        )}

        {/* Error Message Display */}
        {errorMsg && (
          <div className="mb-4 px-3.5 py-2.5 rounded-xl bg-rose-500/10 border border-rose-500/20 text-rose-300 text-[10px] font-semibold flex items-center gap-2">
            <AlertTriangle className="w-4 h-4 shrink-0 animate-pulse text-rose-400" />
            <span>{errorMsg}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="flex flex-col gap-3.5">
          
          {/* Account Tier Toggle (Only on Register) - Cyan outline border matching stitch_screen_2.png */}
          {!isLogin && (
            <div className="flex flex-col gap-1.5 mb-1">
              <label className="text-[9px] font-bold text-indigo-400 uppercase tracking-widest px-1">Hesap Türü</label>
              <div className="grid grid-cols-2 gap-3">
                <button
                  type="button"
                  onClick={() => setUserType('individual')}
                  className={`py-2 px-3 text-center border rounded-xl flex flex-col items-center justify-center gap-0.5 transition-all ${
                    userType === 'individual'
                      ? 'bg-indigo-500/5 border-cyan-500 text-cyan-200 shadow-[0_0_15px_rgba(6,182,212,0.15)]'
                      : 'bg-zinc-950/20 border-zinc-800/80 text-zinc-500 hover:text-zinc-300 hover:border-zinc-700'
                  }`}
                >
                  <span className="text-[11px] font-extrabold tracking-wide">Bireysel</span>
                  <span className="text-[8px] opacity-75 leading-none">Geliştiriciler için</span>
                </button>
                <button
                  type="button"
                  onClick={() => setUserType('corporate')}
                  className={`py-2 px-3 text-center border rounded-xl flex flex-col items-center justify-center gap-0.5 transition-all ${
                    userType === 'corporate'
                      ? 'bg-indigo-500/5 border-cyan-500 text-cyan-200 shadow-[0_0_15px_rgba(6,182,212,0.15)]'
                      : 'bg-zinc-950/20 border-zinc-800/80 text-zinc-500 hover:text-zinc-300 hover:border-zinc-700'
                  }`}
                >
                  <span className="text-[11px] font-extrabold tracking-wide">Kurumsal</span>
                  <span className="text-[8px] opacity-75 leading-none">Ekipler ve Firmalar için</span>
                </button>
              </div>
            </div>
          )}

          {/* Email Field - Left icon for Login, Right icon for Register matching Stitch! */}
          <div className="flex flex-col gap-1.5">
            <label className="text-[9px] font-bold text-indigo-400 uppercase tracking-widest px-1">E-posta Adresi</label>
            <div className="relative flex items-center">
              {isLogin && <Mail className="absolute left-3 w-4 h-4 text-zinc-500 pointer-events-none" />}
              <input
                type="email"
                required
                placeholder="ornek@eposta.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className={`w-full bg-[#09111F]/70 border border-zinc-800 focus:border-indigo-500/50 rounded-xl py-2.5 text-xs text-white focus:outline-none focus:ring-1 focus:ring-indigo-500/10 transition-all font-medium ${
                  isLogin ? 'pl-9 pr-4' : 'pl-4 pr-9'
                }`}
              />
              {!isLogin && <Mail className="absolute right-3 w-4 h-4 text-zinc-500 pointer-events-none" />}
            </div>
          </div>

          {/* Username Field (Only on Register) - Right icon matching stitch_screen_2.png */}
          {!isLogin && (
            <div className="flex flex-col gap-1.5">
              <label className="text-[9px] font-bold text-indigo-400 uppercase tracking-widest px-1">Kullanıcı Adı</label>
              <div className="relative flex items-center">
                <input
                  type="text"
                  required
                  placeholder="kullaniciadiniz"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  className="w-full bg-[#09111F]/70 border border-zinc-800 focus:border-indigo-500/50 rounded-xl py-2.5 pl-4 pr-9 text-xs text-white focus:outline-none focus:ring-1 focus:ring-indigo-500/10 transition-all font-medium"
                />
                <User className="absolute right-3 w-4 h-4 text-zinc-500 pointer-events-none" />
              </div>
            </div>
          )}

          {/* Company Name Field (Only on Register & Corporate Tier) */}
          {!isLogin && userType === 'corporate' && (
            <div className="flex flex-col gap-1.5 animate-in slide-in-from-top-2 duration-200">
              <label className="text-[9px] font-bold text-indigo-400 uppercase tracking-widest px-1">Şirket Adı</label>
              <div className="relative flex items-center">
                <input
                  type="text"
                  required={userType === 'corporate'}
                  placeholder="Şirket A.Ş."
                  value={companyName}
                  onChange={(e) => setCompanyName(e.target.value)}
                  className="w-full bg-[#09111F]/70 border border-zinc-800 focus:border-indigo-500/50 rounded-xl py-2.5 pl-4 pr-9 text-xs text-white focus:outline-none focus:ring-1 focus:ring-indigo-500/10 transition-all font-medium"
                />
                <Building2 className="absolute right-3 w-4 h-4 text-zinc-500 pointer-events-none" />
              </div>
            </div>
          )}

          {/* Password Field - Left icon for Login, Right icon for Register matching Stitch! */}
          <div className="flex flex-col gap-1.5">
            <label className="text-[9px] font-bold text-indigo-400 uppercase tracking-widest px-1">Şifre</label>
            <div className="relative flex items-center">
              {isLogin && <Lock className="absolute left-3 w-4 h-4 text-zinc-500 pointer-events-none" />}
              <input
                type="password"
                required
                placeholder="••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className={`w-full bg-[#09111F]/70 border border-zinc-800 focus:border-indigo-500/50 rounded-xl py-2.5 text-xs text-white focus:outline-none focus:ring-1 focus:ring-indigo-500/10 transition-all font-medium ${
                  isLogin ? 'pl-9 pr-4' : 'pl-4 pr-9'
                }`}
              />
              {!isLogin && <Lock className="absolute right-3 w-4 h-4 text-zinc-500 pointer-events-none" />}
            </div>
          </div>

          {/* Submit Button - Solid matching blue-purple colors and copy matching stitch_screen_1/2.png exactly! */}
          <button
            type="submit"
            disabled={isLoading}
            className="w-full mt-4 bg-[#4F46E5] hover:bg-[#4338CA] text-white font-extrabold text-xs tracking-wider uppercase py-3 rounded-xl transition-all duration-300 shadow-[0_4px_15px_rgba(79,70,229,0.35)] hover:scale-[1.02] active:scale-[0.98] disabled:opacity-50 flex items-center justify-center gap-2 cursor-pointer"
          >
            {isLoading ? (
              <span className="h-4 w-4 border-2 border-white/30 border-t-white rounded-full animate-spin"></span>
            ) : (
              isLogin ? (
                <>
                  <span>BULUT OTURUMUNU BAŞLAT</span>
                  <span>🌟</span>
                </>
              ) : (
                <>
                  <span>KAYIT OL VE EŞİTLE</span>
                  <span>💅</span>
                </>
              )
            )}
          </button>
        </form>

        {/* Bottom decorative wave SVG matching stitch_screen_1.png waves! */}
        <div className="absolute bottom-0 left-0 w-full overflow-hidden leading-[0] translate-y-[1px] pointer-events-none rounded-b-3xl">
          <svg className="relative block w-[100%+1.3px] h-[30px]" data-name="Layer 1" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1200 120" preserveAspectRatio="none">
            <path d="M321.39,56.44c58-10.79,114.16-30.13,172-41.86,82.39-16.72,168.19-17.73,250.45-.39C823.78,31,906.67,72,985.66,92.83c70.05,18.48,146.53,26.09,214.34,3V120H0V0C26.9,8.75,55.05,17,83,23.61,167,43.23,252,69.29,321.39,56.44Z" fill="url(#wave-gradient)" opacity="0.15"></path>
            <path d="M985.66,92.83C906.67,72,823.78,31,743.84,14.19c-82.26-17.34-168.06-16.33-250.45.39-57.84,11.73-114,31.07-172,41.86C252,69.29,167,43.23,83,23.61,55.05,17,26.9,8.75,0,0V120H1200V26C1132.19,49.09,1055.71,41.48,985.66,92.83Z" fill="url(#wave-gradient-2)" opacity="0.1"></path>
            <defs>
              <linearGradient id="wave-gradient" x1="0%" y1="0%" x2="100%" y2="0%">
                <stop offset="0%" stopColor="#06b6d4" />
                <stop offset="100%" stopColor="#4f46e5" />
              </linearGradient>
              <linearGradient id="wave-gradient-2" x1="0%" y1="0%" x2="100%" y2="0%">
                <stop offset="0%" stopColor="#4f46e5" />
                <stop offset="100%" stopColor="#d946ef" />
              </linearGradient>
            </defs>
          </svg>
        </div>
      </div>
    </div>
  );
}
