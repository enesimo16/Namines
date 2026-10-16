import React from 'react';
import { Loader2 } from 'lucide-react';

export type SpinnerSize = 'sm' | 'md' | 'lg';

const SPINNER_SIZE: Record<SpinnerSize, string> = {
  sm: 'w-3.5 h-3.5',
  md: 'w-5 h-5',
  lg: 'w-8 h-8',
};

export interface SpinnerProps {
  size?: SpinnerSize;
  className?: string;
  label?: string;
}

export function Spinner({
  size = 'md',
  className = '',
  label = 'Yükleniyor...',
}: SpinnerProps) {
  return (
    <div role="status" className="inline-flex items-center justify-center">
      <Loader2
        className={`animate-spin text-content-muted ${SPINNER_SIZE[size]} ${className}`}
        aria-hidden="true"
      />
      <span className="sr-only">{label}</span>
    </div>
  );
}

export default Spinner;
