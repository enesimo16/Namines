'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';

/**
 * `/admin` yalnızca yönlendirir. Eskiden burada AdminConsole da çiziliyordu; hem
 * `/admin/overview` rotasında hem burada iki kez bağlanıp tüm uç noktaları iki kez çekiyordu.
 */
export default function AdminPage() {
  const router = useRouter();

  useEffect(() => {
    router.replace('/admin/overview');
  }, [router]);

  return null;
}
