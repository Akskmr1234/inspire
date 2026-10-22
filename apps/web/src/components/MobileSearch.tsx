import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { TextField } from '@/components/Form';
import type { ApiError } from '@/lib/api';
import { findJobCardCustomers, type JobCardCustomerMatch } from '@/lib/jobCards';

/**
 * The mobile number, which is how the counter finds a returning customer: three digits
 * in, it offers the people who gave a number like it before.
 */
export function MobileSearch({
  value,
  onChange,
  onPick,
  label,
  size,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly onPick: (match: JobCardCustomerMatch) => void;
  /** The field's label, where the screen calls it something else - "Mobile 1". */
  readonly label?: string;
  readonly size?: 'default' | 'sm';
}): React.JSX.Element {
  const { t } = useTranslation();
  const [typed, setTyped] = useState('');
  const [open, setOpen] = useState(false);

  // Waits for the typing to pause rather than asking on every key.
  useEffect(() => {
    const timer = window.setTimeout(() => setTyped(value.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [value]);

  const matches = useQuery<readonly JobCardCustomerMatch[], ApiError>({
    queryKey: ['job-card-customers', typed],
    queryFn: () => findJobCardCustomers(typed),
    enabled: open && typed.length >= 3,
  });

  const found = matches.data ?? [];

  return (
    <div className="relative">
      <TextField
        label={label ?? t('service.mobile')}
        type="tel"
        value={value}
        onChange={(next) => {
          onChange(next);
          setOpen(true);
        }}
        hint={t('service.mobileHint')}
        {...(size ? { size } : {})}
      />
      {open && found.length > 0 && (
        <ul className="absolute inset-x-0 top-full z-20 mt-1 max-h-64 overflow-auto rounded-xl border border-line bg-surface shadow-card">
          {found.map((match) => (
            <li key={`${match.customerLedgerId ?? ''}-${match.customer.mobile ?? ''}`}>
              <button
                type="button"
                onClick={() => {
                  onPick(match);
                  setOpen(false);
                }}
                className="block w-full px-3 py-2 text-start text-sm hover:bg-surface-3"
              >
                <span className="font-medium">{match.customer.name}</span>{' '}
                <span className="font-mono text-xs text-ink-muted">
                  {match.customer.mobile}
                </span>
                {match.customer.addressLine1 && (
                  <span className="block text-xs text-ink-muted">
                    {match.customer.addressLine1}
                  </span>
                )}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
