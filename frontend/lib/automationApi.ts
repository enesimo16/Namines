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
import { newUid } from '../store/useAutomationStore';
import type {
  AutomationRule,
  AutomationActionType,
  AutomationActionConfig,
  AutomationActionStep,
  AutomationCondition,
} from '../store/useAutomationStore';
import type { NaminesFlowEvent } from './naminesFlowEventBus';

/**
 * Sunucunun döndürdüğü/beklediği ham şekil. Koşullar ve aksiyon
 * yapılandırmaları tel üzerinde JSON STRING olarak taşınıyor (sunucu tarafında
 * serbest biçimli saklandıkları için); çeviri yalnızca burada yapılıyor ki
 * store ve bileşenler yazılı tiplerle çalışsın.
 */
interface AutomationActionDto {
  actionType: string;
  actionConfigJson: string;
}

interface AutomationRuleDto {
  id: string;
  projectId: string;
  scopeTableId: string | null;
  name: string;
  triggerType: string;
  conditionsJson: string;
  actions: AutomationActionDto[];
  enabled: boolean;
  lastRun?: { status: string; triggeredAt: string; actionType: string; errorMessage: string | null } | null;
}

/** Bozuk/eksik JSON sessizce yedek değere düşüyor — tek bir kayıt yüzünden canvas boş kalmasın. */
const parseObject = (json: string | undefined): AutomationActionConfig => {
  if (!json) return {};
  try {
    const parsed = JSON.parse(json);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : {};
  } catch {
    return {};
  }
};

const parseConditions = (json: string | undefined): AutomationCondition[] => {
  if (!json) return [];
  try {
    const parsed = JSON.parse(json);
    // `uid` sunucuda saklanmıyor; okurken üretiliyor (React listeleri için,
    // bkz. AutomationCondition).
    return Array.isArray(parsed) ? parsed.map(c => ({ ...c, uid: newUid() })) : [];
  } catch {
    return [];
  }
};

const fromDto = (dto: AutomationRuleDto): AutomationRule => ({
  id: dto.id,
  scopeTableId: dto.scopeTableId ?? '',
  name: dto.name ?? '',
  triggerType: dto.triggerType as NaminesFlowEvent['type'],
  conditions: parseConditions(dto.conditionsJson),
  actions: (dto.actions ?? []).map(a => ({
    uid: newUid(),
    actionType: a.actionType as AutomationActionType,
    actionConfig: parseObject(a.actionConfigJson),
  })),
  enabled: dto.enabled,
  lastRun: dto.lastRun ?? null,
});

/** `uid` istemci tarafı; tele çıkmıyor. */
const toActionDtos = (actions: AutomationActionStep[]): AutomationActionDto[] =>
  actions.map(a => ({
    actionType: a.actionType,
    actionConfigJson: JSON.stringify(a.actionConfig ?? {}),
  }));

const toConditionsJson = (conditions: AutomationCondition[]): string =>
  JSON.stringify((conditions ?? []).map(({ field, op, value }) => ({ field, op, value })));

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
    // Boş string proje geneli demek; sunucu bunu null olarak bekliyor.
    scopeTableId: scopeTableId === '' ? null : scopeTableId,
    name: '',
    triggerType,
    conditionsJson: '[]',
    actions: [{ actionType, actionConfigJson: '{}' }],
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
export async function updateAutomationRule(id: string, rule: AutomationRule): Promise<AutomationRule> {
  const response = await api.put<AutomationRuleDto>(`/automation/rules/${id}`, {
    name: rule.name,
    scopeTableId: rule.scopeTableId === '' ? null : rule.scopeTableId,
    triggerType: rule.triggerType,
    conditionsJson: toConditionsJson(rule.conditions),
    actions: toActionDtos(rule.actions ?? []),
    enabled: rule.enabled,
  });
  return fromDto(response.data);
}

export async function deleteAutomationRule(id: string): Promise<void> {
  await api.delete(`/automation/rules/${id}`);
}

/** Sunucunun tuttuğu tek bir çalıştırma kaydı. */
export interface AutomationRun {
  id: string;
  ruleId: string;
  triggeredAt: string;
  actionType: string;
  status: 'Success' | 'Failed' | 'Skipped' | string;
  isTest: boolean;
  errorMessage: string | null;
  resultSummary: string | null;
}

export async function fetchRuleRuns(ruleId: string, limit = 20): Promise<AutomationRun[]> {
  const response = await api.get<AutomationRun[]>(`/automation/rules/${ruleId}/runs`, { params: { limit } });
  return response.data;
}

export interface AutomationTestResult {
  /** Sunucuda hiç çalışmayan (dolayısıyla log üretmeyen) istemci aksiyonu sayısı. */
  clientOnlyActions: number;
  runs: AutomationRun[];
}

export async function testAutomationRule(ruleId: string): Promise<AutomationTestResult> {
  const response = await api.post<AutomationTestResult>(`/automation/rules/${ruleId}/test`);
  return response.data;
}
