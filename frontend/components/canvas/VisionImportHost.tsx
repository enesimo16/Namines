'use client';

import { useEffect, useState } from 'react';
import VisionUploadModal from './panels/VisionUploadModal';

/**
 * "Görselden içe aktar" modalının her zaman bağlı sahibi.
 *
 * `namines:open-vision-modal` olayını eskiden yalnızca CanvasExportToolbar dinliyordu;
 * o bileşen tuvalde tablo yokken bağlı olmadığından boş tuvaldeki "Import from Image"
 * düğmesi hiçbir şey yapmıyordu.
 */
export default function VisionImportHost() {
  const [isOpen, setIsOpen] = useState(false);

  useEffect(() => {
    const open = () => setIsOpen(true);
    window.addEventListener('namines:open-vision-modal', open);
    return () => window.removeEventListener('namines:open-vision-modal', open);
  }, []);

  return <VisionUploadModal isOpen={isOpen} onClose={() => setIsOpen(false)} />;
}
