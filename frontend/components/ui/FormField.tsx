import React from 'react';
import Label from './Label';

export interface FormFieldProps {
  label?: string;
  htmlFor?: string;
  required?: boolean;
  optional?: boolean;
  hint?: string;
  error?: string;
  children: React.ReactNode;
  className?: string;
}

export function FormField({
  label,
  htmlFor,
  required,
  optional,
  hint,
  error,
  children,
  className = '',
}: FormFieldProps) {
  return (
    <div className={`flex flex-col gap-1.5 w-full ${className}`}>
      {label && (
        <Label htmlFor={htmlFor} required={required} optional={optional}>
          {label}
        </Label>
      )}
      {children}
      {error ? (
        <span role="alert" className="text-micro text-danger-text">
          {error}
        </span>
      ) : hint ? (
        <span className="text-micro text-content-muted leading-relaxed">
          {hint}
        </span>
      ) : null}
    </div>
  );
}

export default FormField;
