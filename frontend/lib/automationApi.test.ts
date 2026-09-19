import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../services/api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

import api from '../services/api';
import { fetchAutomationRules, createAutomationRule, updateAutomationRule } from './automationApi';
import type { AutomationRule } from '../store/useAutomationStore';

/**
 * İstemci ile sunucu arasındaki TEL BİÇİMİ.
 *
 * Bu dikiş tarayıcıda gözle görülmüyor: yanlış alan adı gönderilirse sunucu
 * yine 200 döner, yalnızca alanı yok sayar — yani hata, kullanıcı sayfayı
 * yenileyip düzenlemesinin kaybolduğunu görene kadar sessiz kalır. Koşullar ve
 * aksiyonlar tel üzerinde JSON STRING olarak taşındığı için burada iki ayrı
 * çeviri var ve ikisi de sessizce bozulabilir.
 */

const serverDto = {
  id: 'r1',
  projectId: 'p1',
  scopeTableId: null,
  name: 'Kritik kolon',
  triggerType: 'ColumnDeleted',
  conditionsJson: '[{"field":"columnName","op":"endsWith","value":"_id"}]',
  actions: [
    { actionType: 'Toast', actionConfigJson: '{}' },
    { actionType: 'Webhook', actionConfigJson: '{"url":"https://example.test/hook"}' },
  ],
  enabled: true,
};

beforeEach(() => vi.clearAllMocks());

describe('fetchAutomationRules', () => {
  it('sunucu bicimini store bicimine cevirir', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: [serverDto] });

    const [rule] = await fetchAutomationRules('p1');

    expect(rule.name).toBe('Kritik kolon');
    // null kapsam istemcide BOS STRING: store her yerde string bekliyor.
    expect(rule.scopeTableId).toBe('');
    expect(rule.conditions).toEqual([{ field: 'columnName', op: 'endsWith', value: '_id' }]);
    expect(rule.actions).toEqual([
      { actionType: 'Toast', actionConfig: {} },
      { actionType: 'Webhook', actionConfig: { url: 'https://example.test/hook' } },
    ]);
  });

  it('bozuk JSON tek bir kurali degil yalnizca o alani dusurur', async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: [{ ...serverDto, conditionsJson: '{bozuk', actions: [{ actionType: 'Toast', actionConfigJson: 'yok' }] }],
    });

    const [rule] = await fetchAutomationRules('p1');

    expect(rule.conditions).toEqual([]);
    expect(rule.actions).toEqual([{ actionType: 'Toast', actionConfig: {} }]);
  });

  it('aksiyonsuz kural bos dizi olarak geliyor', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: [{ ...serverDto, actions: undefined }] });
    const [rule] = await fetchAutomationRules('p1');
    expect(rule.actions).toEqual([]);
  });
});

describe('createAutomationRule', () => {
  it('sunucunun bekledigi alanlari gonderir', async () => {
    vi.mocked(api.post).mockResolvedValue({ data: serverDto });

    await createAutomationRule('p1', 't1', 'TableDeleted', 'Toast');

    expect(api.post).toHaveBeenCalledWith('/automation/rules', {
      projectId: 'p1',
      scopeTableId: 't1',
      name: '',
      triggerType: 'TableDeleted',
      conditionsJson: '[]',
      actions: [{ actionType: 'Toast', actionConfigJson: '{}' }],
    });
  });

  it('bos kapsam sunucuya null gider', async () => {
    // Sunucu "proje geneli"ni NULL ile anliyor; bos string gonderilirse
    // hicbir tabloyla eslesmeyen olu bir kural olusurdu.
    vi.mocked(api.post).mockResolvedValue({ data: serverDto });

    await createAutomationRule('p1', '', 'RelationAdded', 'Toast');

    expect(api.post).toHaveBeenCalledWith('/automation/rules', expect.objectContaining({ scopeTableId: null }));
  });
});

describe('updateAutomationRule', () => {
  const rule: AutomationRule = {
    id: 'r1',
    scopeTableId: '',
    name: 'Kritik kolon',
    triggerType: 'ColumnDeleted',
    conditions: [{ field: 'columnName', op: 'endsWith', value: '_id' }],
    actions: [
      { actionType: 'Toast', actionConfig: {} },
      { actionType: 'Webhook', actionConfig: { url: 'https://example.test/hook' } },
    ],
    enabled: true,
  };

  it('kuralin tamamini sunucunun bekledigi bicimde yazar', async () => {
    vi.mocked(api.put).mockResolvedValue({ data: serverDto });

    await updateAutomationRule('r1', rule);

    expect(api.put).toHaveBeenCalledWith('/automation/rules/r1', {
      name: 'Kritik kolon',
      scopeTableId: null,
      triggerType: 'ColumnDeleted',
      conditionsJson: '[{"field":"columnName","op":"endsWith","value":"_id"}]',
      actions: [
        { actionType: 'Toast', actionConfigJson: '{}' },
        { actionType: 'Webhook', actionConfigJson: '{"url":"https://example.test/hook"}' },
      ],
      enabled: true,
    });
  });

  it('aksiyon SIRASI korunuyor — zincirin anlami buna bagli', async () => {
    vi.mocked(api.put).mockResolvedValue({ data: serverDto });

    await updateAutomationRule('r1', {
      ...rule,
      actions: [rule.actions[1], rule.actions[0]],
    });

    const sent = vi.mocked(api.put).mock.calls[0][1] as { actions: { actionType: string }[] };
    expect(sent.actions.map(a => a.actionType)).toEqual(['Webhook', 'Toast']);
  });
});
