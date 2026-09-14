// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, cleanup } from '@testing-library/react';
import LaunchPanel from './LaunchPanel';
import { launchApi, LaunchError } from '../../services/launchApi';
import { groundApi } from '../../services/vaultGroundApi';
import { changeRequestService } from '../../services/api';

vi.mock('../../services/launchApi');
vi.mock('../../services/vaultGroundApi');
vi.mock('../../services/api', () => ({ changeRequestService: { createQuick: vi.fn() } }));
vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));

const emptySchema = { name: 'test', tables: [], relations: [] } as never;

describe('LaunchPanel', () => {
  afterEach(() => cleanup());

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

  it('attributes a DDL failure to the apply step, not the provision step', async () => {
    vi.mocked(launchApi.launch).mockResolvedValue({ status: 'DdlFailed', error: 'syntax error at CREATE' });

    render(<LaunchPanel projectId="p1" projectName="Demo" ddlScript="CREATE TABLE x();" schema={emptySchema} />);
    await waitFor(() => expect(screen.getByText('LocalPostgres')).toBeInTheDocument());
    fireEvent.click(screen.getByText('LocalPostgres'));
    fireEvent.click(screen.getByRole('button', { name: /launch/i }));

    await waitFor(() => expect(screen.getByText('Database opened')).toBeInTheDocument());
    expect(screen.getByText('Schema application failed')).toBeInTheDocument();
    expect(screen.getByText('syntax error at CREATE')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });

  it('offers a Try again button after a thrown/network error, not just after ProvisionFailed/DdlFailed', async () => {
    vi.mocked(launchApi.launch).mockRejectedValue(new LaunchError('Launch failed.', 0));

    render(<LaunchPanel projectId="p1" projectName="Demo" ddlScript="CREATE TABLE x();" schema={emptySchema} />);
    await waitFor(() => expect(screen.getByText('LocalPostgres')).toBeInTheDocument());
    fireEvent.click(screen.getByText('LocalPostgres'));
    fireEvent.click(screen.getByRole('button', { name: /launch/i }));

    await waitFor(() => expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument());
  });

  it('shows an error when sending a NeedsReview outcome to review fails', async () => {
    vi.mocked(launchApi.launch).mockResolvedValue({ status: 'NeedsReview' });
    vi.mocked(changeRequestService.createQuick).mockRejectedValue(new Error('network error'));

    render(<LaunchPanel projectId="p1" projectName="Demo" ddlScript="CREATE TABLE x();" schema={emptySchema} />);
    await waitFor(() => expect(screen.getByText('LocalPostgres')).toBeInTheDocument());
    fireEvent.click(screen.getByText('LocalPostgres'));
    fireEvent.click(screen.getByRole('button', { name: /launch/i }));

    await waitFor(() => expect(screen.getByText('Review changes')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Review changes'));

    await waitFor(() => expect(screen.getByText(/could not send this to review/i)).toBeInTheDocument());
  });
});
