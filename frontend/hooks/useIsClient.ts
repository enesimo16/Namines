import { useSyncExternalStore } from 'react';

const subscribe = () => () => {};

/**
 * Sunucuda/hidrasyon sırasında `false`, istemcide hidrasyon sonrası `true`.
 *
 * `useEffect + setState` ile "mounted" bayrağı tutmanın yerine geçer (ek render ve
 * `set-state-in-effect` lint hatası olmadan). Yalnızca tarayıcıya özgü durumdan
 * (localStorage'dan gelen oturum, `window` ölçüleri) türeyen çıktıyı sunucu HTML'iyle
 * eşleştirmek için kullanılır.
 */
export function useIsClient(): boolean {
  return useSyncExternalStore(subscribe, () => true, () => false);
}
