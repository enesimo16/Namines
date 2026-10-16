import React from 'react';

export interface LabelProps extends React.LabelHTMLAttributes<HTMLLabelElement> {
  required?: boolean;
  optional?: boolean;
}

export function Label({
  children,
  required,
  optional,
  className = '',
  ...props
}: LabelProps) {
  return (
    <label
      className={[
        'text-micro font-bold uppercase tracking-wider text-content-muted flex items-center gap-1.5 select-none',
        className,
      ].filter(Boolean).join(' ')}
      {...props}
    >
      <span>{children}</span>
      {required && <span className="text-danger-text text-xs" aria-hidden="true">*</span>}
      {optional && <span className="text-micro font-normal lowercase text-content-muted">(isteğe bağlı)</span>}
    </label>
  );
}

export default Label;
