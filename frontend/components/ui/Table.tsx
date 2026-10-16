import React from 'react';

export function Table({
  children,
  className = '',
  caption,
}: {
  children: React.ReactNode;
  className?: string;
  caption?: string;
}) {
  return (
    <div className="overflow-x-auto w-full">
      <table className={`w-full text-xs text-left ${className}`}>
        {caption && <caption className="sr-only">{caption}</caption>}
        {children}
      </table>
    </div>
  );
}

export function TableHeader({
  children,
  className = '',
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <thead className={`border-y border-line-strong bg-surface-700/60 text-micro font-bold uppercase tracking-wider text-content-muted ${className}`}>
      {children}
    </thead>
  );
}

export function TableBody({
  children,
  className = '',
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return <tbody className={`divide-y divide-line ${className}`}>{children}</tbody>;
}

export function TableRow({
  children,
  className = '',
  onClick,
}: {
  children: React.ReactNode;
  className?: string;
  onClick?: () => void;
}) {
  return (
    <tr
      onClick={onClick}
      className={[
        'transition-colors duration-[var(--dur-fast)]',
        onClick ? 'cursor-pointer hover:bg-surface-700/50' : 'hover:bg-surface-800/40',
        className,
      ].filter(Boolean).join(' ')}
    >
      {children}
    </tr>
  );
}

export function TableHead({
  children,
  align = 'left',
  className = '',
}: {
  children?: React.ReactNode;
  align?: 'left' | 'center' | 'right';
  className?: string;
}) {
  const alignClass = align === 'right' ? 'text-right' : align === 'center' ? 'text-center' : 'text-left';
  return (
    <th scope="col" className={`px-5 py-3 whitespace-nowrap ${alignClass} ${className}`}>
      {children}
    </th>
  );
}

export function TableCell({
  children,
  align = 'left',
  mono = false,
  className = '',
}: {
  children?: React.ReactNode;
  align?: 'left' | 'center' | 'right';
  mono?: boolean;
  className?: string;
}) {
  const alignClass = align === 'right' ? 'text-right' : align === 'center' ? 'text-center' : 'text-left';
  return (
    <td
      className={[
        'px-5 py-3 text-content-secondary whitespace-nowrap',
        alignClass,
        mono ? 'font-mono text-micro' : '',
        className,
      ].filter(Boolean).join(' ')}
    >
      {children}
    </td>
  );
}

export default Table;
