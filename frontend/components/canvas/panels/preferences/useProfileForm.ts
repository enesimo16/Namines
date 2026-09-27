import { useEffect, useState, type FormEvent } from 'react';
import { authService } from '../../../../services/api';
import { useToastStore } from '../../../../store/useToastStore';
import { useOnOpen } from './useOnOpen';

export interface ProfileFields {
  fullName: string;
  companyName: string;
  githubUrl: string;
  linkedinUrl: string;
  websiteUrl: string;
  twitterUrl: string;
  bio: string;
  location: string;
}

const EMPTY_PROFILE: ProfileFields = {
  fullName: '', companyName: '', githubUrl: '', linkedinUrl: '',
  websiteUrl: '', twitterUrl: '', bio: '', location: '',
};

// Yalnızca bu beşinin yerel kopyası var; kalan üçü (twitter, bio, konum)
// her zaman yalnızca sunucuda tutuldu.
const CACHE_KEYS = {
  fullName: 'namines-full-name',
  companyName: 'namines-company',
  githubUrl: 'namines-github',
  linkedinUrl: 'namines-linkedin',
  websiteUrl: 'namines-website',
} as const;

type CachedField = keyof typeof CACHE_KEYS;
const CACHED_FIELDS = Object.keys(CACHE_KEYS) as CachedField[];

function readCache(): Pick<ProfileFields, CachedField> {
  const result = {} as Pick<ProfileFields, CachedField>;
  for (const field of CACHED_FIELDS) result[field] = localStorage.getItem(CACHE_KEYS[field]) || '';
  return result;
}

export function useProfileForm(isOpen: boolean, isAuthenticated: boolean) {
  const showToast = useToastStore(state => state.showToast);
  const [profile, setProfile] = useState<ProfileFields>(EMPTY_PROFILE);
  const [isSaving, setIsSaving] = useState(false);

  // Girişsizken form yerel kopyadan dolar; girişliyken sunucu yanıtını bekler.
  useOnOpen(isOpen, () => {
    if (!isAuthenticated) setProfile(prev => ({ ...prev, ...readCache() }));
  });

  useEffect(() => {
    if (!isOpen || !isAuthenticated) return;
    authService.getProfile()
      .then(data => {
        if (!data) return;
        const cache = readCache();
        setProfile({
          fullName: data.fullName || cache.fullName,
          companyName: data.companyName || cache.companyName,
          githubUrl: data.githubUrl || cache.githubUrl,
          linkedinUrl: data.linkedinUrl || cache.linkedinUrl,
          websiteUrl: data.websiteUrl || cache.websiteUrl,
          twitterUrl: data.twitterUrl || '',
          bio: data.bio || '',
          location: data.location || '',
        });
      })
      .catch(() => setProfile(prev => ({ ...prev, ...readCache() })));
  }, [isOpen, isAuthenticated]);

  const setField = (field: keyof ProfileFields, value: string) =>
    setProfile(prev => ({ ...prev, [field]: value }));

  const save = async (e: FormEvent) => {
    e.preventDefault();
    for (const field of CACHED_FIELDS) localStorage.setItem(CACHE_KEYS[field], profile[field]);

    if (isAuthenticated) {
      setIsSaving(true);
      try {
        await authService.updateProfile(profile);
        showToast('Profile saved to cloud successfully.', 'success');
      } catch {
        showToast('Profile saved locally. Cloud sync failed.', 'warning');
      } finally {
        setIsSaving(false);
      }
    } else {
      showToast('Profile saved locally. Log in to sync to cloud.', 'info');
    }
  };

  return { profile, setField, isSaving, save };
}

export type ProfileForm = ReturnType<typeof useProfileForm>;
