// @vitest-environment jsdom
import { describe, expect, it, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, cleanup } from '@testing-library/react';
import { GithubScanModal } from './GithubScanModal';
import type { RepositoryScanResult } from '../../types/source';

const okResult: RepositoryScanResult = {
  format: 'prisma',
  branch: 'main',
  schema: { tables: [] },
  parsedFiles: ['prisma/schema.prisma'],
  skipped: [{ name: 'node_modules/x.sql', reason: 'Build output or dependency directory.' }],
  treeTruncated: false,
};

function type(value: string) {
  fireEvent.change(screen.getByLabelText('Repository URL'), { target: { value } });
}

describe('GithubScanModal', () => {
  afterEach(() => cleanup());

  it('does not scan until a repository is given', () => {
    const onScan = vi.fn();
    render(<GithubScanModal onScan={onScan} onImport={() => {}} onClose={() => {}} />);

    fireEvent.click(screen.getByRole('button', { name: /^scan$/i }));

    expect(onScan).not.toHaveBeenCalled();
  });

  it('shows what was read and what was skipped, with reasons', async () => {
    // "Atlananlar" kapatılamaz: kullanıcının her şeyi gördüğünü sanması,
    // eksik şemayla ilerlemesinin tek sebebi olur (01-DEPO-TARAMA.md).
    const onScan = vi.fn().mockResolvedValue(okResult);
    render(<GithubScanModal onScan={onScan} onImport={() => {}} onClose={() => {}} />);

    type('github.com/acme/shop');
    fireEvent.click(screen.getByRole('button', { name: /^scan$/i }));

    await waitFor(() => expect(screen.getByText('prisma/schema.prisma')).toBeTruthy());
    expect(screen.getByText('node_modules/x.sql')).toBeTruthy();
    expect(screen.getByText('Build output or dependency directory.')).toBeTruthy();
  });

  it('warns when the repository tree was truncated', async () => {
    const onScan = vi.fn().mockResolvedValue({ ...okResult, treeTruncated: true });
    render(<GithubScanModal onScan={onScan} onImport={() => {}} onClose={() => {}} />);

    type('github.com/acme/shop');
    fireEvent.click(screen.getByRole('button', { name: /^scan$/i }));

    await waitFor(() => expect(screen.getByText(/too large to list in full/i)).toBeTruthy());
  });

  it('shows the server message when the scan fails', async () => {
    // Hata GERÇEK axios biçiminde: `message` alanı işe yaramaz bir cümle
    // ("Request failed with status code 400"), kullanıcıya lazım olan metin
    // `response.data.message` içinde. Testi düz bir Error ile yazmak, bu
    // ayrımı görmeyip kullanıcıya o işe yaramaz cümleyi göstermek demekti —
    // canlı doğrulamada tam olarak bu oldu.
    const axiosLike = Object.assign(new Error('Request failed with status code 400'), {
      response: { status: 400, data: { message: 'It does not exist, or it is private.' } },
    });
    const onScan = vi.fn().mockRejectedValue(axiosLike);
    render(<GithubScanModal onScan={onScan} onImport={() => {}} onClose={() => {}} />);

    type('github.com/acme/secret');
    fireEvent.click(screen.getByRole('button', { name: /^scan$/i }));

    await waitFor(() => expect(screen.getByText(/it is private/i)).toBeTruthy());
    expect(screen.queryByText(/Request failed with status code/i)).toBeNull();
  });

  it('only offers the import once a scan has succeeded', async () => {
    const onImport = vi.fn();
    const onScan = vi.fn().mockResolvedValue(okResult);
    render(<GithubScanModal onScan={onScan} onImport={onImport} onClose={() => {}} />);

    expect(screen.queryByRole('button', { name: /import/i })).toBeNull();

    type('github.com/acme/shop');
    fireEvent.click(screen.getByRole('button', { name: /^scan$/i }));

    await waitFor(() => expect(screen.getByRole('button', { name: /import/i })).toBeTruthy());
    fireEvent.click(screen.getByRole('button', { name: /import/i }));

    expect(onImport).toHaveBeenCalledWith(okResult);
  });

  it('passes the branch through when one is given', async () => {
    const onScan = vi.fn().mockResolvedValue(okResult);
    render(<GithubScanModal onScan={onScan} onImport={() => {}} onClose={() => {}} />);

    type('github.com/acme/shop');
    fireEvent.change(screen.getByLabelText('Branch (optional)'), { target: { value: 'develop' } });
    fireEvent.click(screen.getByRole('button', { name: /^scan$/i }));

    await waitFor(() => expect(onScan).toHaveBeenCalledWith('github.com/acme/shop', 'develop'));
  });
});
