/**
 * `useAutomationStore`'un sunucu tarafı karşılığı — `/api/automation/rules`
 * (AutomationController, Bölüm 3).
 *
 * Auth/fetch deseni `services/api.ts`'teki paylaşılan `api` axios
 * istemcisinden kopyalandı (bkz. `authService.syncProjects` orada
 * `/auth/sync`'e POST atıyor): base URL tek kaynağı olan `API_BASE_URL`
 * (`lib/apiConfig.ts`), CSRF başlığı, `withCredentials: true` (httpOnly auth
 * cookie'si) ve bir request interceptor'ın `namines-auth` localStorage
 * girdisinden okuyup eklediği `Authorization: Bearer <token>` başlığı zaten
 * o istemcide merkezi — ham `fetch` burada TEKRAR uygulanmıyor.
 */
import api from '../services/api';
import type { AutomationRule, AutomationActionType } from '../store/useAutomationStore';
import type { NaminesFlowEvent } from './naminesFlowEventBus';

/** Sunucunun döndürdüğü/beklediği ham şekil — `ActionConfigJson` düz bir JSON string. */
interface AutomationRuleDto {
  id: string;
  projectId: string;
  scopeTableId: string | null;
  triggerType: string;
  actionType: string;
  actionConfigJson: string;
  enabled: boolean;
}

const parseActionConfig = (json: string): { url?: string } => {
  try {
    const parsed = JSON.parse(json);
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
};

const fromDto = (dto: AutomationRuleDto): AutomationRule => ({
  id: dto.id,
  scopeTableId: dto.scopeTableId ?? '',
  triggerType: dto.triggerType as NaminesFlowEvent['type'],
  actionType: dto.actionType as AutomationActionType,
  actionConfig: parseActionConfig(dto.actionConfigJson),
  enabled: dto.enabled,
});

export async function fetchAutomationRules(projectId: string): Promise<AutomationRule[]> {
  const response = await api.get<AutomationRuleDto[]>('/automation/rules', { params: { projectId } });
  return response.data.map(fromDto);
}

export async function createAutomationRule(
  projectId: string, scopeTableId: string,
  triggerType: NaminesFlowEvent['type'], actionType: AutomationActionType,
): Promise<AutomationRule> {
  const response = await api.post<AutomationRuleDto>('/automation/rules', {
    projectId,
    scopeTableId,
    triggerType,
    actionType,
    actionConfigJson: '{}',
  });
  return fromDto(response.data);
}

/**
 * Kuralın düzenlenebilir alanlarını sunucuya yazar (PUT /automation/rules/{id}).
 *
 * Sunucu TÜM alanları bekliyor (kısmi patch değil), o yüzden çağıran taraf
 * birleştirilmiş nihai hâli geçirmeli — `useAutomationStore.updateRule` bunu
 * yerel state'i güncelledikten SONRA oradan okuyarak yapıyor.
 */
export async function updateAutomationRule(
  id: string,
  fields: {
    triggerType: NaminesFlowEvent['type'];
    actionType: AutomationActionType;
    actionConfig: { url?: string };
    enabled: boolean;
  },
): Promise<AutomationRule> {
  const response = await api.put<AutomationRuleDto>(`/automation/rules/${id}`, {
    triggerType: fields.triggerType,
    actionType: fields.actionType,
    actionConfigJson: JSON.stringify(fields.actionConfig ?? {}),
    enabled: fields.enabled,
  });
  return fromDto(response.data);
}

export async function deleteAutomationRule(id: string): Promise<void> {
  await api.delete(`/automation/rules/${id}`);
}
