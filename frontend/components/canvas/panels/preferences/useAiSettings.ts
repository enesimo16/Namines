import { useEffect, useState, type FormEvent } from 'react';
import { useAIPolicyStore, type AIPolicy, type AiAdvancedSettings } from '../../../../store/useAIPolicyStore';
import { useToastStore } from '../../../../store/useToastStore';
import { LOCAL_ADVANCED_FALLBACK, readLocalAdvanced, writeLocalAdvanced } from '../../../../lib/aiAdvancedPrefs';
import { useOnOpen } from './useOnOpen';

export function useAiSettings(isOpen: boolean, onClose: () => void) {
  const { policy, updatePolicy, isLoading, fetchPolicy } = useAIPolicyStore();
  const showToast = useToastStore(state => state.showToast);
  const [localPolicy, setLocalPolicy] = useState<AIPolicy>({ ...policy });
  const [advanced, setAdvanced] = useState<AiAdvancedSettings>(LOCAL_ADVANCED_FALLBACK);
  const [showAdvancedScreen, setShowAdvancedScreen] = useState(false);

  // Asıl kaynak sunucu; yerel kopya yalnızca yanıt gelene kadar ve eski
  // kurulumların göçü için okunuyor — yanıt gelince üzerine yazılıyor.
  useOnOpen(isOpen, () => setAdvanced(readLocalAdvanced()));

  useEffect(() => {
    if (!isOpen) return;
    fetchPolicy().then(() => {
      const fresh = useAIPolicyStore.getState().policy;
      setLocalPolicy({ ...fresh });
      // Bu satır olmadan kullanıcı ayarı kaydediyor, modalı kapatıp açıyor ve
      // eski değeri görüyordu — kaydın işe yaramadığı izlenimi veriyordu.
      if (fresh.advanced) setAdvanced(fresh.advanced);
    });
  }, [isOpen, fetchPolicy]);

  const setAdvancedField = (field: keyof AiAdvancedSettings, value: string) =>
    setAdvanced(prev => ({ ...prev, [field]: value }));

  const savePolicy = async (e: FormEvent) => {
    e.preventDefault();
    try {
      // Gelişmiş tercihler de aynı istekte gidiyor: ayrı bir uç olsaydı biri
      // başarılı biri başarısız olabilir ve kullanıcı yarısı kaydedilmiş bir
      // yapılandırmayla kalırdı.
      await updatePolicy({ ...localPolicy, advanced });
      writeLocalAdvanced(advanced);
      showToast('AI Routing and advanced settings updated.', 'success');
      onClose();
    } catch {
      showToast('Failed to save AI Policies.', 'error');
    }
  };

  const saveAdvanced = async (e: FormEvent) => {
    e.preventDefault();
    // Sunucuya da yazılıyor: bu ayarlar şema üretiminde gerçekten okunuyor,
    // yalnızca tarayıcıda kalırlarsa hiçbir etkileri olmaz.
    try {
      await updatePolicy({ ...localPolicy, advanced });
    } catch {
      showToast('Advanced settings could not be saved to the server.', 'error');
      return;
    }
    writeLocalAdvanced(advanced);
    showToast('Advanced AI configurations saved successfully.', 'success');
  };

  return {
    localPolicy, setLocalPolicy, advanced, setAdvancedField,
    showAdvancedScreen, setShowAdvancedScreen, isLoading, savePolicy, saveAdvanced,
  };
}

export type AiSettings = ReturnType<typeof useAiSettings>;
