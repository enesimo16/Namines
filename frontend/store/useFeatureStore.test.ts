// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { useFeatureStore } from './useFeatureStore';

describe('useFeatureStore', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    useFeatureStore.setState({
      flags: {},
      isLoading: false,
      error: null,
      hasLoaded: false,
    });
  });

  it('varsayılan olarak tanımlı olmayan özellikler için true döner (fail-open)', () => {
    const isEnabled = useFeatureStore.getState().isFeatureEnabled('unknown_tool');
    expect(isEnabled).toBe(true);

    const state = useFeatureStore.getState().getFeatureState('unknown_tool');
    expect(state.isEnabled).toBe(true);
    expect(state.mode).toBe('DisabledNotice');
  });

  it('fetchFeatures API yanıtını sözlüğe dönüştürür ve durumu günceller', async () => {
    const mockFlags = [
      {
        featureKey: 'canvas',
        displayName: 'Studio Tuvali',
        description: 'ERD Tuvali',
        category: 'Studio',
        isEnabled: true,
        mode: 'DisabledNotice',
        noticeMessage: 'Bakımda',
      },
      {
        featureKey: 'flow',
        displayName: 'Namines Flow',
        description: 'Otomasyon',
        category: 'Studio',
        isEnabled: false,
        mode: 'Hidden',
        noticeMessage: null,
      },
    ];

    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockFlags,
    });

    await useFeatureStore.getState().fetchFeatures();

    expect(useFeatureStore.getState().isLoading).toBe(false);
    expect(useFeatureStore.getState().hasLoaded).toBe(true);
    expect(useFeatureStore.getState().isFeatureEnabled('canvas')).toBe(true);
    expect(useFeatureStore.getState().isFeatureEnabled('flow')).toBe(false);

    const flowState = useFeatureStore.getState().getFeatureState('flow');
    expect(flowState.isEnabled).toBe(false);
    expect(flowState.mode).toBe('Hidden');
  });

  it('setFlagLocal yerel durumu günceller ve büyük/küçük harf duyarsızdır', () => {
    useFeatureStore.getState().setFlagLocal('SMART_SEED', false, 'DisabledNotice', 'Özel test mesajı');

    expect(useFeatureStore.getState().isFeatureEnabled('smart_seed')).toBe(false);
    expect(useFeatureStore.getState().isFeatureEnabled('SMART_SEED')).toBe(false);

    const state = useFeatureStore.getState().getFeatureState('smart_seed');
    expect(state.isEnabled).toBe(false);
    expect(state.mode).toBe('DisabledNotice');
    expect(state.noticeMessage).toBe('Özel test mesajı');
  });
});
