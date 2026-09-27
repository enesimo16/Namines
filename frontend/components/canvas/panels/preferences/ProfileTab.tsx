import React from 'react';
import { Save, User } from 'lucide-react';
import { NotLoggedIn, cardClass, inputClass, primaryBtnClass } from './ui';
import type { ProfileFields, ProfileForm } from './useProfileForm';

const FIELDS: { field: keyof ProfileFields; label: string; placeholder: string; wide?: boolean }[] = [
  { field: 'fullName', label: 'Full Name', placeholder: 'John Doe' },
  { field: 'companyName', label: 'Company / Organization', placeholder: 'Acme Corp' },
  { field: 'location', label: 'Location', placeholder: 'Istanbul, Turkey' },
  { field: 'githubUrl', label: 'GitHub Profile URL', placeholder: 'https://github.com/username' },
  { field: 'linkedinUrl', label: 'LinkedIn Profile URL', placeholder: 'https://linkedin.com/in/username' },
  { field: 'twitterUrl', label: 'Twitter / X Profile URL', placeholder: 'https://x.com/username' },
  { field: 'websiteUrl', label: 'Portfolio Website', placeholder: 'https://myportfolio.com', wide: true },
];

export default function ProfileTab({ isAuthenticated, form }: { isAuthenticated: boolean; form: ProfileForm }) {
  const { profile, setField, isSaving, save } = form;

  return (
    <div className="space-y-6">
      {!isAuthenticated ? (
        <NotLoggedIn icon={User} message="Please log in to manage your developer profile information." />
      ) : (
        <form onSubmit={save} className={`${cardClass} p-5 space-y-5`}>
          <div className="flex items-center gap-2 border-b border-content-primary/10 pb-3">
            <User className="w-4 h-4 text-content-muted" />
            <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">Developer Profile Information</h4>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {FIELDS.map(({ field, label, placeholder, wide }) => (
              <div key={field} className={`space-y-1.5${wide ? ' md:col-span-2' : ''}`}>
                <label className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">{label}</label>
                <input aria-label={label} type="text" value={profile[field]} onChange={(e) => setField(field, e.target.value)} placeholder={placeholder} className={inputClass} />
              </div>
            ))}
            <div className="space-y-1.5 md:col-span-2">
              <label className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">Short Bio</label>
              <textarea aria-label="Short Bio" value={profile.bio} onChange={(e) => setField('bio', e.target.value)} placeholder="Full-stack developer passionate about databases and scalable architecture..." rows={3} className={`${inputClass} resize-none`} />
            </div>
          </div>

          <div className="flex items-center justify-between pt-2">
            <p className="text-[10px] text-content-subtle font-medium">
              {isAuthenticated ? 'Saved to cloud + local cache.' : 'Log in to enable cloud sync.'}
            </p>
            <button
              type="submit"
              disabled={isSaving}
              className={`flex items-center gap-1.5 px-4 py-2 disabled:opacity-60 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer active:scale-95 ${primaryBtnClass}`}
            >
              <Save className="w-3.5 h-3.5" />
              <span>{isSaving ? 'Saving...' : 'Save Profile'}</span>
            </button>
          </div>
        </form>
      )}
    </div>
  );
}
