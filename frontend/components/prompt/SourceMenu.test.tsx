// @vitest-environment jsdom
import { describe, expect, it, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, cleanup } from '@testing-library/react';
import { SourceMenu } from './SourceMenu';
import type { SchemaSourceDescriptor } from '../../types/source';

const sources: SchemaSourceDescriptor[] = [
  {
    id: 'dbconnect', displayName: 'Database connection', description: 'Live database.',
    kind: 'connect', capabilities: ['import', 'watch'], producesGuess: false,
  },
  {
    id: 'openapi', displayName: 'OpenAPI / GraphQL URL', description: 'From a spec.',
    kind: 'import', capabilities: ['import'], producesGuess: true,
  },
  {
    id: 'starter', displayName: 'Starter schemas', description: 'Ready-made.',
    kind: 'starter', capabilities: ['import'], producesGuess: false,
  },
  {
    id: 'webhook', displayName: 'Something new', description: 'Unknown group.',
    kind: 'webhook', capabilities: ['import'], producesGuess: false,
  },
];

describe('SourceMenu', () => {
  // Bu vitest yapılandırmasında RTL'in otomatik temizliği devrede değil
  // (`globals` kapalı); temizlenmezse önceki testin DOM'u kalıyor ve
  // sorgular "birden çok eşleşme" diye düşüyor. Projedeki diğer bileşen
  // testleri de aynı satırı taşıyor.
  afterEach(() => cleanup());

  const open = () => fireEvent.click(screen.getByRole('button', { name: /add source/i }));

  it('stays closed until it is opened', () => {
    render(<SourceMenu sources={sources} onSelect={() => {}} />);
    expect(screen.queryByText('Database connection')).toBeNull();
  });

  it('groups sources by kind', () => {
    render(<SourceMenu sources={sources} onSelect={() => {}} />);
    open();

    expect(screen.getByText('Connect')).toBeTruthy();
    expect(screen.getByText('Import')).toBeTruthy();
    expect(screen.getByText('Start from')).toBeTruthy();
  });

  it('shows a guess badge only on sources that infer', () => {
    // "Çıkarım olduğu her ekranda söylenmeli" (second-phase/06-VERI-KAYNAKLARI.md).
    render(<SourceMenu sources={sources} onSelect={() => {}} />);
    open();

    expect(screen.getAllByText('guess')).toHaveLength(1);
  });

  it('puts a source of an unknown kind in Import rather than dropping it', () => {
    // Sunucu yeni bir grup eklerse kaynak KAYBOLMAMALI; en muhafazakâr grup
    // "tek seferlik içe aktarma" — sürekli ilişki VAAT ETMEYEN grup.
    render(<SourceMenu sources={sources} onSelect={() => {}} />);
    open();

    expect(screen.getByText('Something new')).toBeTruthy();
  });

  it('reports the chosen source and closes', () => {
    const onSelect = vi.fn();
    render(<SourceMenu sources={sources} onSelect={onSelect} />);
    open();
    fireEvent.click(screen.getByText('OpenAPI / GraphQL URL'));

    expect(onSelect).toHaveBeenCalledWith('openapi');
    expect(screen.queryByText('Database connection')).toBeNull();
  });

  it('closes on Escape', () => {
    render(<SourceMenu sources={sources} onSelect={() => {}} />);
    open();
    fireEvent.keyDown(document, { key: 'Escape' });

    expect(screen.queryByText('Database connection')).toBeNull();
  });
});
