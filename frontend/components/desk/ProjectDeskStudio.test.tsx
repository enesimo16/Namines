// @vitest-environment jsdom
import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent, cleanup } from '@testing-library/react';

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));

const listRows = vi.fn();
const runSql = vi.fn();
vi.mock('../../services/deskService', async (orig) => ({
  ...(await orig<typeof import('../../services/deskService')>()),
  listRows: (...a: unknown[]) => listRows(...a),
  runSql: (...a: unknown[]) => runSql(...a),
}));

import { ProjectDeskStudio } from './ProjectDeskStudio';
import { DeskError } from '../../services/deskService';
import { useAuthStore } from '../../store/useAuthStore';
import type { CloudProjectDto } from '../../types/api';

const schema = {
  schemaId: 's', name: 'shop',
  tables: [{
    id: 't1', name: 'users',
    columns: [
      { id: 'c1', name: 'id', type: 'int', length: null, isPK: true, isFK: false, isNullable: false, defaultValue: null },
      { id: 'c2', name: 'email', type: 'varchar', length: 100, isPK: false, isFK: false, isNullable: false, defaultValue: null },
    ],
  }],
  relations: [],
};

const dto = (hasConnection: boolean): CloudProjectDto => ({
  id: 'p1', name: 'Shop', dbType: 'PostgreSQL', schemaJson: JSON.stringify(schema),
  nodePositionsJson: '{}', createdAt: '', updatedAt: '', hasConnection,
}) as CloudProjectDto;

const renderStudio = (hasConnection: boolean) =>
  render(
    <ProjectDeskStudio
      projectId="p1" projectName="Shop" resolvedOwner="me" projectDto={dto(hasConnection)} onExitProject={() => {}}
    />,
  );

describe('ProjectDeskStudio canlı / örnek mod', () => {
  beforeEach(() => {
    cleanup();
    vi.clearAllMocks();
    listRows.mockResolvedValue({
      rows: [{ id: 41, email: 'real@acme.io' }], page: 1, pageSize: 25, totalCount: 1, stablePagination: true,
    });
  });

  it('oturum açık + bağlantı var: gerçek satırları listRows ile çeker, örnek bandı göstermez', async () => {
    useAuthStore.setState({ isAuthenticated: true });
    renderStudio(true);

    expect(await screen.findByText('real@acme.io')).toBeInTheDocument();
    expect(listRows).toHaveBeenCalledWith('p1', 'users', expect.objectContaining({ page: 1, pageSize: 25 }));
    expect(screen.queryByText('Sample data preview')).not.toBeInTheDocument();
    expect(screen.getByText('Live data')).toBeInTheDocument();
    expect(screen.queryByText('Seed 5 Rows')).not.toBeInTheDocument();
  });

  it('bağlantı yok: örnek veri bandı + sunucuya istek yok', () => {
    useAuthStore.setState({ isAuthenticated: true });
    renderStudio(false);

    expect(screen.getByText('Sample data preview')).toBeInTheDocument();
    expect(screen.getByText(/no stored database connection/i)).toBeInTheDocument();
    expect(listRows).not.toHaveBeenCalled();
  });

  it('oturum kapalı: bağlantı olsa bile örnek mod', () => {
    useAuthStore.setState({ isAuthenticated: false });
    renderStudio(true);

    expect(screen.getByText('Sample data preview')).toBeInTheDocument();
    expect(listRows).not.toHaveBeenCalled();
  });

  it('canlı modda SQL konsolu desk-sql çalıştırır; 403 not-enabled ipucunu gösterir', async () => {
    useAuthStore.setState({ isAuthenticated: true });
    runSql
      .mockResolvedValueOnce({ columns: ['n'], rows: [{ n: 7 }], affectedRows: -1, truncated: false, durationMs: 12 })
      .mockRejectedValueOnce(new DeskError('The SQL console is not enabled for this project yet.', 403, 'sql-disabled'));
    renderStudio(true);
    await screen.findByText('real@acme.io');

    fireEvent.click(screen.getByRole('tab', { name: /SQL Console/i }));
    fireEvent.click(screen.getByRole('button', { name: /Run Query/i }));

    await waitFor(() => expect(screen.getByText('Execution time: 12ms')).toBeInTheDocument());
    expect(runSql).toHaveBeenCalledWith('p1', expect.stringContaining('SELECT * FROM "users"'));
    expect(screen.getByText('7')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Run Query/i }));
    expect(await screen.findByText(/Raw SQL is disabled for this project/)).toBeInTheDocument();
  });
});
