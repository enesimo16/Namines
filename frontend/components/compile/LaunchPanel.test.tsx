// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import LaunchPanel from './LaunchPanel';
import { launchApi } from '../../services/launchApi';
import { groundApi } from '../../services/vaultGroundApi';

vi.mock('../../services/launchApi');
vi.mock('../../services/vaultGroundApi');
vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));

const emptySchema = { name: 'test', tables: [], relations: [] } as never;

describe('LaunchPanel', () => {
  beforeEach(() => {
    vi.mocked(groundApi.providers).mockResolvedValue([
      { name: 'LocalPostgres', liveVerified: true, supportsBranching: false, supportsRegionChoice: false, responsibility: '', problem: null },
    ]);
  });

  it('shows a save-project prompt when there is no project yet', () => {
    render(<LaunchPanel projectId={null} projectName="" ddlScript="" schema={emptySchema} />);
    expect(screen.getByText(/save your project first/i)).toBeInTheDocument();
  });

  it('walks through the checklist to Ready and shows Open Desk + Download', async () => {
    vi.mocked(launchApi.launch).mockResolvedValue({
      status: 'Ready', projectId: 'p1', deskHandoffToken: 'tok', backupWarning: null,
    });

    render(<LaunchPanel projectId="p1" projectName="Demo" ddlScript="CREATE TABLE x();" schema={emptySchema} />);

    await waitFor(() => expect(screen.getByText('LocalPostgres')).toBeInTheDocument());
    fireEvent.click(screen.getByText('LocalPostgres'));
    fireEvent.click(screen.getByRole('button', { name: /launch/i }));

    await waitFor(() => expect(screen.getByText('Open Desk')).toBeInTheDocument());
    expect(screen.getByText('Download project')).toBeInTheDocument();
    expect(screen.getByText('Ready')).toBeInTheDocument();
  });

  it('surfaces a visible backup warning instead of silently succeeding', async () => {
    vi.mocked(launchApi.launch).mockResolvedValue({
      status: 'Ready', projectId: 'p1', deskHandoffToken: 'tok',
      backupWarning: 'Initial backup failed: disk full. Back it up manually from the Vault tab.',
    });

    render(<LaunchPanel projectId="p1" projectName="Demo" ddlScript="CREATE TABLE x();" schema={emptySchema} />);
    await waitFor(() => expect(screen.getByText('LocalPostgres')).toBeInTheDocument());
    fireEvent.click(screen.getByText('LocalPostgres'));
    fireEvent.click(screen.getByRole('button', { name: /launch/i }));

    await waitFor(() =>
      expect(screen.getByText(/initial backup failed: disk full/i)).toBeInTheDocument());
  });

  it('shows the Review changes path when the target already has tables', async () => {
    vi.mocked(launchApi.launch).mockResolvedValue({ status: 'NeedsReview' });

    render(<LaunchPanel projectId="p1" projectName="Demo" ddlScript="CREATE TABLE x();" schema={emptySchema} />);
    await waitFor(() => expect(screen.getByText('LocalPostgres')).toBeInTheDocument());
    fireEvent.click(screen.getByText('LocalPostgres'));
    fireEvent.click(screen.getByRole('button', { name: /launch/i }));

    await waitFor(() => expect(screen.getByText('Review changes')).toBeInTheDocument());
    expect(screen.getByText(/schema NOT applied/i)).toBeInTheDocument();
  });
});
