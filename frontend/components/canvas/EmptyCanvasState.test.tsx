// @vitest-environment jsdom
import { describe, it, expect, afterEach, beforeEach, vi } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import EmptyCanvasState from './EmptyCanvasState';
import { useSchemaStore } from '../../store/useSchemaStore';

const mockPush = vi.fn();
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: mockPush,
  }),
}));

describe('EmptyCanvasState', () => {
  beforeEach(() => {
    mockPush.mockClear();
    useSchemaStore.setState({
      schema: {
        schemaId: 'test-empty',
        name: 'Empty Schema',
        tables: [],
        relations: [],
      },
    });
  });

  afterEach(() => {
    cleanup();
  });

  it('renders "Create Your Database Schema" when canvas is empty', () => {
    render(<EmptyCanvasState />);
    expect(screen.getByText('Create Your Database Schema')).toBeInTheDocument();
    expect(screen.getByText('Generate with AI')).toBeInTheDocument();
  });

  it('navigates to /new when "Generate with AI" is clicked', () => {
    render(<EmptyCanvasState />);
    const generateBtn = screen.getByText('Generate with AI').closest('button');
    expect(generateBtn).toBeInTheDocument();
    fireEvent.click(generateBtn!);
    expect(mockPush).toHaveBeenCalledWith('/new');
  });

  it('renders null when schema has tables', () => {
    useSchemaStore.setState({
      schema: {
        schemaId: 'test-with-tables',
        name: 'Populated Schema',
        tables: [
          {
            id: 't1',
            name: 'users',
            columns: [],
          },
        ],
        relations: [],
      },
    });

    const { container } = render(<EmptyCanvasState />);
    expect(container.firstChild).toBeNull();
  });
});
