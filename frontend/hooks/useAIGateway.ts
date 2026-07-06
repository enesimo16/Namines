import { useAuthStore } from '../store/useAuthStore';
import { useAIGatewayStore } from '../store/useAIGatewayStore';
import { useByokStore } from '../store/useByokStore';
import { useAIPolicyStore } from '../store/useAIPolicyStore';
import { useQuotaStore } from '../store/useQuotaStore';

export function useAIGateway() {
  const { isAuthenticated } = useAuthStore();
  const { apiKey } = useByokStore();
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

    // If policy mode is BYOK (5) and the user has a local secure key configured, allow bypass
    if (mode === 5 && apiKey) {
      return true;
    }

    // For Guest user (no auth, no local key)
    if (!isAuthenticated && !apiKey) {
      openGateway(featureName);
      return false;
    }

    // For Auth user with quota, check if quota is remaining
    if (isAuthenticated && mode !== 0 && mode !== 5 && remaining <= 0) {
      setExhaustedModalOpen(true);
      return false;
    }

    // For BYOK mode where key is missing
    if (mode === 5 && !apiKey) {
      window.dispatchEvent(new CustomEvent('namines:open-ai-settings'));
      return false;
    }

    return true;
  };

  return { checkAccess };
}
