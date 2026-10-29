import type { CloudProjectDto } from '../../types/api';

/**
 * Desk iki kaynaktan beslenir: projenin saklı bağlantısı üzerinden GERÇEK veri
 * ya da tarayıcıda üretilen örnek veri. Gerçek veri için ikisi birden gerekir:
 * oturum açık VE projede saklı canlı bağlantı var. Aksi hâlde örnek moda düşer
 * ve nedeni söylenir — sessizce örnek göstermek gerçek veri sanılırdı.
 */
export type DeskMode =
  | { kind: 'live' }
  | { kind: 'sample'; reason: 'signed-out' | 'no-connection' };

export function resolveDeskMode(
  isAuthenticated: boolean,
  projectDto: Pick<CloudProjectDto, 'hasConnection'> | null | undefined,
): DeskMode {
  if (!isAuthenticated) return { kind: 'sample', reason: 'signed-out' };
  if (!projectDto?.hasConnection) return { kind: 'sample', reason: 'no-connection' };
  return { kind: 'live' };
}
