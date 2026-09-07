import { useAuthStore } from '../store/useAuthStore';
import { useAIGatewayStore } from '../store/useAIGatewayStore';
import { useAIPolicyStore } from '../store/useAIPolicyStore';
import { useQuotaStore } from '../store/useQuotaStore';

export function useAIGateway() {
  const { isAuthenticated } = useAuthStore();
  const { openGateway } = useAIGatewayStore();
  const { policy } = useAIPolicyStore();
  const { remaining, setExhaustedModalOpen } = useQuotaStore();

  const checkAccess = (featureName: string): boolean => {
    // Map featureName to policy key
    let mode = 1; // Default to Quota/Paid mode (1)
    const nameLower = featureName.toLowerCase();

    if (nameLower.includes('seed') || nameLower.includes('live database')) {
      mode = policy.smartSeed;
    } else if (nameLower.includes('dba') || nameLower.includes('fix')) {
      mode = policy.dbaAnalysis;
    } else if (nameLower.includes('revision')) {
      mode = policy.schemaRevision;
    } else if (nameLower.includes('coder') || nameLower.includes('reverse') || nameLower.includes('engineer') || nameLower.includes('vision') || nameLower.includes('scaffold') || nameLower.includes('export')) {
      mode = policy.scaffolding;
    } else if (nameLower.includes('migration') || nameLower.includes('parse')) {
      mode = policy.migration;
    } else if (nameLower.includes('voice') || nameLower.includes('speech') || nameLower.includes('transcribe') || nameLower.includes('audio')) {
      mode = policy.voice;
    } else if (nameLower.includes('generation')) {
      mode = policy.schemaGeneration;
    } else if (nameLower.includes('doc') || nameLower.includes('readme') || nameLower.includes('pdf')) {
      mode = policy.documentation;
    }

    // If policy mode is Deterministic (0), it is 0-cost, so allow bypass immediately
    if (mode === 0) {
      return true;
    }

    // Guest (kimliksiz) kullanıcı — giriş gateway'i göster.
    if (!isAuthenticated) {
      openGateway(featureName);
      return false;
    }

    // Giriş yapmış kullanıcı, kota bittiyse durdur.
    if (remaining <= 0) {
      setExhaustedModalOpen(true);
      return false;
    }

    return true;
  };

  return { checkAccess };
}
