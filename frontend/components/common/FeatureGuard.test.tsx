// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import FeatureGuard from './FeatureGuard';
import { useFeatureStore } from '../../store/useFeatureStore';

describe('FeatureGuard component', () => {
  beforeEach(() => {
    useFeatureStore.setState({
      flags: {},
      isLoading: false,
      error: null,
      hasLoaded: true,
    });
  });

  it('özellik aktifken çocuk bileşeni normal render eder', () => {
    useFeatureStore.getState().setFlagLocal('canvas', true, 'DisabledNotice');

    render(
      <FeatureGuard featureKey="canvas">
        <button>Aktif Tuval Butonu</button>
      </FeatureGuard>
    );

    expect(screen.getByText('Aktif Tuval Butonu')).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('özellik kapalı ve mod Hidden iken içeriği tamamen gizler (null döner)', () => {
    useFeatureStore.getState().setFlagLocal('flow', false, 'Hidden');

    const { container } = render(
      <FeatureGuard featureKey="flow">
        <button>Gizli Akış Butonu</button>
      </FeatureGuard>
    );

    expect(screen.queryByText('Gizli Akış Butonu')).not.toBeInTheDocument();
    expect(container.firstChild).toBeNull();
  });

  it('özellik kapalı ve mod Hidden iken fallback varsa onu render eder', () => {
    useFeatureStore.getState().setFlagLocal('flow', false, 'Hidden');

    render(
      <FeatureGuard featureKey="flow" fallback={<div>Alternatif İçerik</div>}>
        <button>Gizli Akış Butonu</button>
      </FeatureGuard>
    );

    expect(screen.queryByText('Gizli Akış Butonu')).not.toBeInTheDocument();
    expect(screen.getByText('Alternatif İçerik')).toBeInTheDocument();
  });

  it('özellik kapalı ve mod DisabledNotice iken devre dışı bırakır ve uyarı mesajı gösterir', () => {
    useFeatureStore.getState().setFlagLocal(
      'ai_dba',
      false,
      'DisabledNotice',
      'AI DBA şu anda bakımda.'
    );

    render(
      <FeatureGuard featureKey="ai_dba">
        <button>AI DBA Çalıştır</button>
      </FeatureGuard>
    );

    // Çocuk bileşen DOM'da var fakat pasif sarmalayıcı içinde
    expect(screen.getByText('AI DBA Çalıştır')).toBeInTheDocument();

    // Uyarı mesajı görünür
    expect(screen.getByRole('status')).toBeInTheDocument();
    expect(screen.getByText('AI DBA şu anda bakımda.')).toBeInTheDocument();
  });
});
