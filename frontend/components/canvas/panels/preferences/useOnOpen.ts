import { useState } from 'react';

/**
 * Modal kapalıdan açığa geçtiği render'da `onOpen`'ı çağırır.
 *
 * Senkron sıfırlama/yükleme (localStorage okumak gibi) efekt içinde yapılınca
 * React fazladan bir render turu tetikliyor ("cascading render"). React'in bu
 * durum için önerdiği desen, geçişi RENDER sırasında önceki değerle
 * karşılaştırıp yakalamak — `onOpen` yalnızca bu bileşenin kendi state'ini
 * değiştirmeli.
 */
export function useOnOpen(isOpen: boolean, onOpen: () => void) {
  const [wasOpen, setWasOpen] = useState(false);
  if (isOpen !== wasOpen) {
    setWasOpen(isOpen);
    if (isOpen) onOpen();
  }
}
