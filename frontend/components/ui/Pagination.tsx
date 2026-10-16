import React from 'react';
import Button from './Button';

export interface PaginationProps {
  page: number;
  totalPages: number;
  onPageChange: (nextPage: number) => void;
  totalItems?: number;
  className?: string;
}

export function Pagination({
  page,
  totalPages,
  onPageChange,
  totalItems,
  className = '',
}: PaginationProps) {
  return (
    <div
      role="navigation"
      aria-label="Sayfalama"
      className={`flex flex-wrap items-center justify-between gap-3 px-5 py-3 border-t border-line text-micro text-content-muted ${className}`}
    >
      <div>
        {totalItems !== undefined && (
          <span>
            Toplam <strong className="font-semibold text-content-primary">{totalItems}</strong> kayıt
          </span>
        )}
      </div>
      <div className="flex items-center gap-2">
        <span className="text-content-secondary">
          Sayfa <strong className="font-semibold text-content-primary">{page}</strong> / {totalPages || 1}
        </span>
        <div className="flex items-center gap-1">
          <Button
            size="sm"
            variant="ghost"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
            aria-label="Önceki Sayfa"
          >
            Önceki
          </Button>
          <Button
            size="sm"
            variant="ghost"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
            aria-label="Sonraki Sayfa"
          >
            Sonraki
          </Button>
        </div>
      </div>
    </div>
  );
}

export default Pagination;
