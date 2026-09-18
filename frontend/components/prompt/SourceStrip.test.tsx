// @vitest-environment jsdom
import { describe, expect, it, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, cleanup } from '@testing-library/react';
import { SourceStrip } from './SourceStrip';
import type { SchemaSourceDescriptor } from '../../types/source';

const source = (id: string, displayName: string, producesGuess = false): SchemaSourceDescriptor =>
  ({ id, displayName, description: `${displayName} description`, kind: 'import', capabilities: ['import'], producesGuess });

// Sunucunun gönderdiği sırayla — şeridin gösterdiği sıra BUDUR.
const sources = [
  source('starter', 'Starter schemas'),
  source('github', 'GitHub repository'),
  source('dbconnect', 'Database connection'),
  source('code', 'Code files'),
  source('openapi', 'OpenAPI / GraphQL URL', true),
  source('jsonshape', 'Sample JSON response', true),
  source('image', 'Image', true),
];

describe('SourceStrip', () => {
  afterEach(() => cleanup());

  it('shows the first four sources without any click', () => {
    // Şeridin tek amacı bu: kullanıcı hiçbir şeye tıklamadan seçeneklerin
    // VAR olduğunu görmeli — menü gizliyken kimse aramıyordu.
    render(<SourceStrip sources={sources} onSelect={() => {}} />);

    expect(screen.getByText('Starter schemas')).toBeTruthy();
    expect(screen.getByText('GitHub repository')).toBeTruthy();
    expect(screen.getByText('Database connection')).toBeTruthy();
    expect(screen.getByText('Code files')).toBeTruthy();
  });

  it('keeps the rest behind a single overflow card that counts them', () => {
    render(<SourceStrip sources={sources} onSelect={() => {}} />);

    expect(screen.queryByText('Sample JSON response')).toBeNull();
    expect(screen.getByRole('button', { name: /3 more/i })).toBeTruthy();
  });

  it('reveals the remaining sources when the overflow is opened', () => {
    render(<SourceStrip sources={sources} onSelect={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: /3 more/i }));

    expect(screen.getByText('Sample JSON response')).toBeTruthy();
    expect(screen.getByText('Image')).toBeTruthy();
  });

  it('never hides a source, however many the server sends', () => {
    // Şeride sığmayan bir kaynağın DÜŞMESİ, ürüne eklenen bir özelliğin
    // kullanıcıya hiç ulaşmaması demek.
    const many = [...sources, source('future', 'Something new')];
    render(<SourceStrip sources={many} onSelect={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: /4 more/i }));

    expect(screen.getByText('Something new')).toBeTruthy();
  });

  it('marks the sources that only guess', () => {
    render(<SourceStrip sources={sources} onSelect={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: /3 more/i }));

    expect(screen.getAllByText('guess')).toHaveLength(3);
  });

  it('reports the chosen source', () => {
    const onSelect = vi.fn();
    render(<SourceStrip sources={sources} onSelect={onSelect} />);

    fireEvent.click(screen.getByText('Starter schemas'));

    expect(onSelect).toHaveBeenCalledWith('starter');
  });

  it('reports a source chosen from the overflow and closes it', () => {
    const onSelect = vi.fn();
    render(<SourceStrip sources={sources} onSelect={onSelect} />);

    fireEvent.click(screen.getByRole('button', { name: /3 more/i }));
    fireEvent.click(screen.getByText('Image'));

    expect(onSelect).toHaveBeenCalledWith('image');
    expect(screen.queryByText('Sample JSON response')).toBeNull();
  });

  it('closes the overflow on Escape', () => {
    render(<SourceStrip sources={sources} onSelect={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: /3 more/i }));
    fireEvent.keyDown(document, { key: 'Escape' });

    expect(screen.queryByText('Sample JSON response')).toBeNull();
  });

  it('shows no overflow card when everything already fits', () => {
    render(<SourceStrip sources={sources.slice(0, 3)} onSelect={() => {}} />);

    expect(screen.queryByRole('button', { name: /more/i })).toBeNull();
  });

  it('renders nothing when the catalog could not be loaded', () => {
    // Sunucu listesi gelmediyse boş bir şerit göstermek, ürünü kırık
    // gösterirdi — üretim akışı zaten çalışıyor.
    const { container } = render(<SourceStrip sources={[]} onSelect={() => {}} />);

    expect(container.firstChild).toBeNull();
  });
});
