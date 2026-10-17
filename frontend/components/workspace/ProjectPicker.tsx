'use client';

import type { CloudProjectDto } from '../../types/api';
import { Field } from './WorkspaceKit';
import Select from '../ui/Select';

/**
 * Ground ve Vault'un ortak proje seçicisi.
 *
 * Eski düzen 256px'lik bir yan çubuktu (proje adı + "Not connected" alt satırı) ve
 * içerik alanını daraltıyordu. Çalışma alanı sayfalarında projeyi zaten üst
 * gezinme belirliyor; burada yalnızca "hangi proje?" sorusunun kompakt cevabı var.
 */
export default function ProjectPicker({
  projects,
  selectedId,
  onSelect,
  label = 'Project',
  describe,
  children,
}: {
  projects: CloudProjectDto[] | null;
  selectedId: string | null;
  onSelect: (id: string) => void;
  label?: string;
  /** Seçenek metnine eklenen kısa durum (ör. "PostgreSQL" / "not connected"). */
  describe?: (project: CloudProjectDto) => string;
  children?: React.ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="w-full sm:w-80">
        <Field label={label} htmlFor="workspace-project-picker">
          <Select
            id="workspace-project-picker"
            value={selectedId ?? ''}
            onChange={e => onSelect(e.target.value)}
            disabled={!projects || projects.length === 0}
          >
            {!projects && <option value="">Loading…</option>}
            {projects && projects.length === 0 && <option value="">No projects yet</option>}
            {projects?.map(p => (
              <option key={p.id} value={p.id}>
                {p.name}{describe ? ` — ${describe(p)}` : ''}
              </option>
            ))}
          </Select>
        </Field>
      </div>
      {children}
    </div>
  );
}
