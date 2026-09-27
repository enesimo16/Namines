import { useEffect, useState } from 'react';
import { useOnOpen } from './useOnOpen';

export interface LocalStats {
  schemas: number;
  aiRequests: number;
  dbaAudits: number;
  mockRecords: number;
}

const readStats = (): LocalStats => ({
  schemas: Number(localStorage.getItem('namines-stats-schemas') || '0'),
  aiRequests: Number(localStorage.getItem('namines-stats-ai-requests') || '0'),
  dbaAudits: Number(localStorage.getItem('namines-stats-dba-audits') || '0'),
  mockRecords: Number(localStorage.getItem('namines-stats-mock-records') || '0'),
});

export function useLocalStats(isOpen: boolean): LocalStats {
  const [stats, setStats] = useState<LocalStats>({ schemas: 0, aiRequests: 0, dbaAudits: 0, mockRecords: 0 });
  useOnOpen(isOpen, () => setStats(readStats()));

  // Kaldırılan sahte jeton özelliğinin bıraktığı veri temizleniyor: jetonlar
  // hiçbir zaman geçerli değildi, bırakılması kullanıcıyı yanıltmaya devam ederdi.
  useEffect(() => {
    if (!isOpen) return;
    try { localStorage.removeItem('namines-api-tokens'); } catch { /* private mode */ }
  }, [isOpen]);

  return stats;
}
