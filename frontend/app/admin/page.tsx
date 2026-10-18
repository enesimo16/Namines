'use client';

import React, { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import AdminConsole from './AdminConsole';

export default function AdminPage() {
  const router = useRouter();

  useEffect(() => {
    router.replace('/admin/overview');
  }, [router]);

  return <AdminConsole initialTab="overview" />;
}
