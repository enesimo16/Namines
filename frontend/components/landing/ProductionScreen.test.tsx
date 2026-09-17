// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach, beforeAll } from 'vitest';
import { render, screen, cleanup } from '@testing-library/react';
import ProductionScreen from './ProductionScreen';
import type { AgentStepEvent, AgentResultEvent } from '../../lib/sseSchemaStream';

// jsdom `matchMedia`'yı uygulamıyor; bileşen "hareketi azalt" tercihini
// okumak için onu kullanıyor. Stub `matches: false` diyor — yani testler
// varsayılan (animasyonlu) yolu koşuyor.
beforeAll(() => {
  // jsdom düzen (layout) hesaplamadığı için `scrollTo` da yok; bileşen her
  // yeni adımda listeyi aşağı kaydırıyor.
  Element.prototype.scrollTo = () => {};

  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia;
});

type Summary = AgentResultEvent['agent'];

const summary = (over: Partial<Summary> = {}): Summary => ({
  archetype: 'Ecommerce',
  rounds: 1,
  clean: true,
  portableEverywhere: true,
  findings: [],
  portability: [],
  ...over,
});

describe('ProductionScreen', () => {
  afterEach(() => cleanup());

  it('renders every step kind the backend can emit', () => {
    // Backend AgentStep.Kind ile birebir. Bir tür eklenip burası
    // güncellenmezse ikon tablosu undefined döner ve bileşen ÇÖKER —
    // bu test o çökmeyi derleme değil, koşum anında da yakalar.
    const steps: AgentStepEvent[] = [
      { kind: 'plan', message: 'Planning…' },
      { kind: 'draft', message: 'Generating draft…' },
      { kind: 'inspect', message: 'Compiling on PostgreSQL…' },
      { kind: 'finding', message: '[rule] NSL004: type mismatch' },
      { kind: 'repair', message: 'Repairing (round 1/2)…' },
      { kind: 'clean', message: 'Clean on PostgreSQL' },
    ];

    render(<ProductionScreen steps={steps} isRunning={false} summary={null} onClose={vi.fn()} />);

    expect(screen.getByText('Planning…')).toBeInTheDocument();
    expect(screen.getByText('Clean on PostgreSQL')).toBeInTheDocument();
  });

  it('lists every unresolved finding instead of hiding them', () => {
    // Sunucu bulguları bilerek döndürüyor: "çalışıyor gibi görünen bir şema,
    // hiç vermemekten kötüdür". UI onları yutarsa o karar geri alınmış olur.
    render(
      <ProductionScreen
        steps={[]}
        isRunning={false}
        summary={summary({
          clean: false,
          findings: [
            '[rule] NSL004: Foreign key type does not match',
            '[compile] PostgreSQL: undefined enum',
          ],
        })}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getByText(/2 unresolved problems/i)).toBeInTheDocument();
    expect(screen.getByText(/NSL004/)).toBeInTheDocument();
    expect(screen.getByText(/undefined enum/)).toBeInTheDocument();
  });

  it('uses the singular form for a single finding', () => {
    render(
      <ProductionScreen
        steps={[]}
        isRunning={false}
        summary={summary({ clean: false, findings: ['[rule] NSL016: nullable primary key'] })}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getByText(/1 unresolved problem\b/i)).toBeInTheDocument();
  });

  it('reports success when the schema compiled cleanly', () => {
    render(
      <ProductionScreen
        steps={[]}
        isRunning={false}
        summary={summary({ clean: true, rounds: 3 })}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getByText(/compiled with no errors after 3 rounds/i)).toBeInTheDocument();
  });

  it('keeps portability notes separate from findings', () => {
    // Taşınabilirlik notu bir hata DEĞİL: kullanıcı bu motoru seçti. Aynı
    // listeye koymak, istenmemiş bir uyumu düzeltilmesi gereken bir soruna
    // çevirirdi.
    render(
      <ProductionScreen
        steps={[]}
        isRunning={false}
        summary={summary({
          clean: true,
          portableEverywhere: false,
          portability: ['[compile] Oracle: arrays are not supported'],
        })}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getByText(/compiled with no errors/i)).toBeInTheDocument();
    expect(screen.getByText(/1 issue on other engines/i)).toBeInTheDocument();
    expect(screen.queryByText(/unresolved problem/i)).not.toBeInTheDocument();
  });

  it('surfaces merge notes from chunked (50-60 table) generation instead of hiding them', () => {
    // Final whole-branch review I4: mergeNotes'un backend'de üretilip
    // frontend'de HİÇBİR YERDE okunmaması, birleştiricinin var olma sebebi
    // olan dürüstlük garantisini (düşürülen ilişki, tekilleştirilen tablo,
    // "Domain 'Billing' failed") kullanıcıdan görünmez kılıyordu.
    render(
      <ProductionScreen
        steps={[]}
        isRunning={false}
        summary={summary({
          clean: true,
          mergeNotes: [
            "[merge] Domain 'Billing' failed: chunk 'Billing' did not return a readable schema",
            "[merge] Duplicate table 'order_items' from another chunk was dropped.",
          ],
        })}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getByText(/2 notes from merging this schema's parts/i)).toBeInTheDocument();
    expect(screen.getByText(/Domain 'Billing' failed/)).toBeInTheDocument();
    expect(screen.getByText(/Duplicate table 'order_items'/)).toBeInTheDocument();
  });

  it('does not render a merge notes section for the single-call (small schema) path', () => {
    // Tek çağrılık yolda mergeNotes her zaman boş dizi (ya da alan hiç
    // gelmeyebilir) — bu durumda bölüm hiç görünmemeli.
    render(
      <ProductionScreen
        steps={[]}
        isRunning={false}
        summary={summary({ clean: true, mergeNotes: [] })}
        onClose={vi.fn()}
      />,
    );

    expect(screen.queryByText(/notes from merging/i)).not.toBeInTheDocument();
  });

  it('shows nothing extra while the run is still in progress', () => {
    render(
      <ProductionScreen
        steps={[{ kind: 'draft', message: 'Generating draft…' }]}
        isRunning
        summary={null}
        onClose={vi.fn()}
      />,
    );

    expect(screen.getByText('Generating schema')).toBeInTheDocument();
    expect(screen.queryByText(/unresolved/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /continue/i })).not.toBeInTheDocument();
  });
});
