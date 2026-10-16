'use client';

import { useParams } from 'next/navigation';
import UnifiedReviewConsole from '../../../components/review/UnifiedReviewConsole';

export default function ChangeRequestDetailPage() {
  const params = useParams();
  const id = params?.id as string | undefined;

  return <UnifiedReviewConsole initialCrId={id} />;
}
