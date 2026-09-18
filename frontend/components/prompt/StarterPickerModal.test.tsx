// @vitest-environment jsdom
import { describe, expect, it, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, cleanup } from '@testing-library/react';
import { StarterPickerModal } from './StarterPickerModal';
import { TEMPLATES } from '../../lib/templates';

describe('StarterPickerModal', () => {
  afterEach(() => cleanup());

  it('offers every starter schema the product ships', () => {
    // Listeyi elle yazmak, yeni bir şablon eklendiğinde menüde görünmemesi
    // demekti — katalog tek kaynak.
    render(<StarterPickerModal onPick={() => {}} onClose={() => {}} />);

    for (const template of TEMPLATES) {
      expect(screen.getAllByText(template.label).length).toBeGreaterThan(0);
    }
  });

  it('hands back the picked schema, not just its name', () => {
    const onPick = vi.fn();
    render(<StarterPickerModal onPick={onPick} onClose={() => {}} />);

    const first = TEMPLATES[0];
    fireEvent.click(screen.getAllByText(first.label)[0]);

    expect(onPick).toHaveBeenCalledTimes(1);
    const picked = onPick.mock.calls[0][0];
    expect(picked.key).toBe(first.key);
    expect(picked.schema.tables.length).toBeGreaterThan(0);
  });

  it('says how big each starter is before it is picked', () => {
    // "Prompt yazmadan başla" yolunda kullanıcının tek bilgisi bu ekran:
    // kaç tablo geleceğini seçmeden önce görmeli.
    render(<StarterPickerModal onPick={() => {}} onClose={() => {}} />);

    const first = TEMPLATES[0];
    expect(screen.getAllByText(new RegExp(`${first.schema.tables.length} tables`)).length).toBeGreaterThan(0);
  });

  it('can be dismissed', () => {
    const onClose = vi.fn();
    render(<StarterPickerModal onPick={() => {}} onClose={onClose} />);

    fireEvent.click(screen.getByRole('button', { name: /close/i }));

    expect(onClose).toHaveBeenCalled();
  });
});
