'use client';

import React from 'react';
import { useParams, notFound } from 'next/navigation';
import AdminConsole, { VALID_ADMIN_TABS, type AdminTab } from '../AdminConsole';

export default function AdminTabPage() {
  const params = useParams();
  const rawTab = (params?.tab as string)?.toLowerCase() as AdminTab | undefined;

  if (rawTab && !VALID_ADMIN_TABS.includes(rawTab)) {
    notFound();
  }

  return <AdminConsole initialTab={rawTab || 'overview'} />;
}
