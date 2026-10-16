// @vitest-environment jsdom
import { describe, expect, it, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, cleanup } from '@testing-library/react';
import { SourceDropdown } from './SourceDropdown';
import type { SchemaSourceDescriptor } from '../../types/source';

const source = (id: string, displayName: string, producesGuess = false): SchemaSourceDescriptor =>
  ({ id, displayName, description: `${displayName} description`, kind: 'import', capabilities: ['import'], producesGuess });

const testSources = [
  source('starter', 'Starter schemas'),
  source('github', 'GitHub repository'),
  source('dbconnect', 'Database connection'),
  source('code', 'Code files'),
  source('openapi', 'OpenAPI / GraphQL URL', true),
  source('jsonshape', 'Sample JSON response', true),
  source('image', 'Image', true),
];

describe('SourceDropdown', () => {
  afterEach(() => cleanup());

  it('renders trigger button', () => {
    render(<SourceDropdown sources={testSources} onSelect={() => {}} />);
    expect(screen.getByRole('button', { name: /templates & import/i })).toBeTruthy();
  });

  it('opens menu on click and lists all sources', () => {
    render(<SourceDropdown sources={testSources} onSelect={() => {}} />);
    const trigger = screen.getByRole('button', { name: /templates & import/i });
    fireEvent.click(trigger);

    expect(screen.getByText('Starter schemas')).toBeTruthy();
    expect(screen.getByText('GitHub repository')).toBeTruthy();
    expect(screen.getByText('Database connection')).toBeTruthy();
    expect(screen.getByText('Code files')).toBeTruthy();
    expect(screen.getByText('OpenAPI / GraphQL URL')).toBeTruthy();
    expect(screen.getByText('Sample JSON response')).toBeTruthy();
    expect(screen.getByText('Image')).toBeTruthy();
  });

  it('reports the chosen source on click and closes menu', () => {
    const onSelect = vi.fn();
    render(<SourceDropdown sources={testSources} onSelect={onSelect} />);
    const trigger = screen.getByRole('button', { name: /templates & import/i });
    fireEvent.click(trigger);

    fireEvent.click(screen.getByText('Starter schemas'));
    expect(onSelect).toHaveBeenCalledWith('starter');
    expect(screen.queryByText('Starter schemas')).toBeNull();
  });

  it('closes on Escape key', () => {
    render(<SourceDropdown sources={testSources} onSelect={() => {}} />);
    const trigger = screen.getByRole('button', { name: /templates & import/i });
    fireEvent.click(trigger);

    expect(screen.getByText('Starter schemas')).toBeTruthy();
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByText('Starter schemas')).toBeNull();
  });

  it('renders nothing when sources array is empty', () => {
    const { container } = render(<SourceDropdown sources={[]} onSelect={() => {}} />);
    expect(container.firstChild).toBeNull();
  });
});
