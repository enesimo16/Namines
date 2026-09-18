// @vitest-environment jsdom
import { describe, expect, it, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, cleanup } from '@testing-library/react';
import { JsonShapeModal } from './JsonShapeModal';
import type { ShapeInferenceResult } from '../../types/source';

const inferred: ShapeInferenceResult = {
  isGuess: true,
  entities: [
    {
      name: 'User', sampleCount: 12, endpointCount: 3, confidence: 'high',
      fields: [
        { name: 'id', type: 'BIGINT', seenCount: 12, isUncertain: false },
        { name: 'email', type: 'VARCHAR', seenCount: 12, isUncertain: false },
        { name: 'nickname', type: 'VARCHAR', seenCount: 1, isUncertain: true },
      ],
    },
    {
      name: 'Order', sampleCount: 4, endpointCount: 1, confidence: 'low',
      fields: [
        { name: 'id', type: 'BIGINT', seenCount: 4, isUncertain: false },
        { name: 'user_id', type: 'BIGINT', seenCount: 4, isUncertain: false },
      ],
    },
  ],
  relations: [{ fromEntity: 'Order', fromField: 'user_id', toEntity: 'User' }],
};

function paste(json: string) {
  fireEvent.change(screen.getByLabelText('JSON response'), { target: { value: json } });
  fireEvent.click(screen.getByRole('button', { name: /add response/i }));
}

describe('JsonShapeModal', () => {
  afterEach(() => cleanup());

  it('does not infer before a response has been added', () => {
    const onInfer = vi.fn();
    render(<JsonShapeModal onInfer={onInfer} onApply={() => {}} onClose={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: /infer/i }));

    expect(onInfer).not.toHaveBeenCalled();
  });

  it('says the result is a guess, every time', async () => {
    // second-phase/06'nın açık kuralı: "çıkarım olduğu HER ekranda söylenmeli".
    const onInfer = vi.fn().mockResolvedValue(inferred);
    render(<JsonShapeModal onInfer={onInfer} onApply={() => {}} onClose={() => {}} />);

    paste('{"id":1}');
    fireEvent.click(screen.getByRole('button', { name: /infer/i }));

    await waitFor(() => expect(screen.getByText(/guess/i)).toBeTruthy());
  });

  it('shows how confident each entity is and marks uncertain fields', async () => {
    const onInfer = vi.fn().mockResolvedValue(inferred);
    render(<JsonShapeModal onInfer={onInfer} onApply={() => {}} onClose={() => {}} />);

    paste('{"id":1}');
    fireEvent.click(screen.getByRole('button', { name: /infer/i }));

    await waitFor(() => expect(screen.getByText('User')).toBeTruthy());
    // Gerçek sunucu "high" | "medium" | "low" gönderiyor; sayı varsayan bir
    // fixture ekranda NaN% üretiyordu ve test bunu göremiyordu.
    expect(screen.getByText(/high confidence/)).toBeTruthy();
    expect(screen.getByText(/nickname/)).toBeTruthy();
    expect(screen.getAllByText(/uncertain/i).length).toBeGreaterThan(0);
  });

  it('turns only the accepted entities into tables', async () => {
    // "Otomatik onay yok" — kullanıcı reddettiği varlık şemaya girmemeli.
    const onApply = vi.fn();
    const onInfer = vi.fn().mockResolvedValue(inferred);
    render(<JsonShapeModal onInfer={onInfer} onApply={onApply} onClose={() => {}} />);

    paste('{"id":1}');
    fireEvent.click(screen.getByRole('button', { name: /infer/i }));
    await waitFor(() => expect(screen.getByText('Order')).toBeTruthy());

    fireEvent.click(screen.getByLabelText('Accept Order'));      // Order'ı reddet
    fireEvent.click(screen.getByRole('button', { name: /add .* canvas/i }));

    const schema = onApply.mock.calls[0][0];
    expect(schema.tables.map((t: { name: string }) => t.name)).toEqual(['User']);
    // İlişkinin bir ucu reddedildi — ilişki de gelmemeli, yoksa şema kırık olur.
    expect(schema.relations).toHaveLength(0);
  });

  it('keeps a relation when both of its ends are accepted', async () => {
    const onApply = vi.fn();
    const onInfer = vi.fn().mockResolvedValue(inferred);
    render(<JsonShapeModal onInfer={onInfer} onApply={onApply} onClose={() => {}} />);

    paste('{"id":1}');
    fireEvent.click(screen.getByRole('button', { name: /infer/i }));
    await waitFor(() => expect(screen.getByText('Order')).toBeTruthy());

    fireEvent.click(screen.getByRole('button', { name: /add .* canvas/i }));

    const schema = onApply.mock.calls[0][0];
    expect(schema.tables).toHaveLength(2);
    expect(schema.relations).toHaveLength(1);
  });

  it('shows the server message when inference fails', async () => {
    const axiosLike = Object.assign(new Error('Request failed with status code 400'), {
      response: { status: 400, data: { message: 'At least one observed response is required.' } },
    });
    const onInfer = vi.fn().mockRejectedValue(axiosLike);
    render(<JsonShapeModal onInfer={onInfer} onApply={() => {}} onClose={() => {}} />);

    paste('{"id":1}');
    fireEvent.click(screen.getByRole('button', { name: /infer/i }));

    await waitFor(() => expect(screen.getByText(/At least one observed response/)).toBeTruthy());
  });

  it('refuses text that is not JSON before sending anything', async () => {
    const onInfer = vi.fn();
    render(<JsonShapeModal onInfer={onInfer} onApply={() => {}} onClose={() => {}} />);

    fireEvent.change(screen.getByLabelText('JSON response'), { target: { value: 'not json' } });
    fireEvent.click(screen.getByRole('button', { name: /add response/i }));

    await waitFor(() => expect(screen.getByText(/valid JSON/i)).toBeTruthy());
    expect(onInfer).not.toHaveBeenCalled();
  });
});
