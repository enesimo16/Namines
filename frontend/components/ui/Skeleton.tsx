import React from 'react';

export interface SkeletonProps extends React.HTMLAttributes<HTMLDivElement> {
  width?: string | number;
  height?: string | number;
  circle?: boolean;
}

export function Skeleton({
  width,
  height,
  circle = false,
  className = '',
  style,
  ...props
}: SkeletonProps) {
  return (
    <div
      aria-hidden="true"
      style={{ width, height, ...style }}
      className={[
        'animate-pulse bg-surface-700/60',
        circle ? 'rounded-full' : 'rounded-[var(--radius-control)]',
        className,
      ].filter(Boolean).join(' ')}
      {...props}
    />
  );
}

export default Skeleton;
