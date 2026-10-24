import { useEffect, useId, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Field } from '@/components/Form';
import { IconSearch } from '@/components/icons';
import { SuggestList, suggestKeys, type Suggestion } from '@/components/SuggestInput';
import type { ApiError } from '@/lib/api';
import { findJobCardCustomers, type JobCardCustomerMatch } from '@/lib/jobCards';

/**
 * The mobile number, which is how the counter finds a returning customer: three digits
 * in, it offers the people who gave a number like it before - on an earlier job, or on
 * their account. The search button looks the number up at once, and fills the details
 * straight in when only one customer has it.
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
  const id = useId();
  const [typed, setTyped] = useState('');
  const [open, setOpen] = useState(false);
  const [asked, setAsked] = useState(false);
  const [active, setActive] = useState(0);

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
  const items: Suggestion[] = found.map((match, index) => ({
    value: String(index),
    label: match.customer.name,
    detail: [match.customer.mobile, match.customer.addressLine1]
      .filter(Boolean)
      .join(' · '),
  }));

  const pick = (match: JobCardCustomerMatch | undefined): void => {
    if (!match) return;
    onPick(match);
    setOpen(false);
    setAsked(false);
  };

  /** Looks the number up now, filling the details in when only one customer has it. */
  const search = async (): Promise<void> => {
    const number = value.trim();
    if (number.length < 3) return;

    setTyped(number);
    setOpen(true);
    setAsked(true);

    try {
      const result = await findJobCardCustomers(number);
      if (result.length === 1) pick(result[0]);
    } catch {
      // The list under the field says what the lookup found, failure included.
    }
  };

  return (
    <Field label={label ?? t('service.mobile')} htmlFor={id}>
      <div className="relative">
        <input
          id={id}
          type="tel"
          value={value}
          autoComplete="off"
          role="combobox"
          aria-expanded={open && items.length > 0}
          aria-controls={`${id}-list`}
          onChange={(event) => {
            onChange(event.target.value);
            setActive(0);
            setAsked(false);
            setOpen(true);
          }}
          onBlur={() => setOpen(false)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && !(open && items.length > 0)) {
              event.preventDefault();
              void search();
              return;
            }
            if (!open) return;
            suggestKeys(
              event,
              items.length,
              active,
              setActive,
              () => pick(found[active]),
              () => setOpen(false),
            );
          }}
          className={size === 'sm' ? 'field-input-sm pe-9' : 'field-input pe-10'}
        />
        <button
          type="button"
          aria-label={t('service.findMobile')}
          title={t('service.findMobile')}
          onMouseDown={(event) => event.preventDefault()}
          onClick={() => void search()}
          className="absolute inset-y-0 end-0 flex w-9 items-center justify-center rounded-e-lg text-ink-subtle hover:text-brand-600"
        >
          <IconSearch className="size-4" />
        </button>
        {open && (
          <SuggestList
            id={`${id}-list`}
            items={items}
            active={active}
            onPick={(item) => pick(found[Number(item.value)])}
            empty={
              asked && !matches.isFetching && typed.length >= 3
                ? t('service.noPreviousEntry')
                : undefined
            }
          />
        )}
      </div>
    </Field>
  );
}
