import { useId, useMemo, useState } from 'react';
import clsx from 'clsx';
import { Field } from '@/components/Form';

/** One thing a suggest field can offer. */
export interface Suggestion {
  readonly value: string;
  readonly label: string;
  readonly detail?: string;
}

/**
 * The list under a field that suggests as it is typed in.
 *
 * Items are picked on mouse-down rather than click, so the field keeps its focus and
 * its blur does not close the list before the pick lands.
 */
export function SuggestList({
  id,
  items,
  active,
  onPick,
  empty,
}: {
  readonly id: string;
  readonly items: readonly Suggestion[];
  readonly active: number;
  readonly onPick: (item: Suggestion) => void;
  /** Shown when there is nothing to offer; nothing at all when absent. */
  readonly empty?: string | undefined;
}): React.JSX.Element | null {
  if (items.length === 0 && !empty) return null;

  return (
    <ul
      id={id}
      role="listbox"
      className="absolute inset-x-0 top-full z-30 mt-1 max-h-64 overflow-auto rounded-xl border border-line bg-surface py-1 shadow-card"
    >
      {items.length === 0 ? (
        <li className="px-3 py-2 text-xs text-ink-muted">{empty}</li>
      ) : (
        items.map((item, index) => (
          <li
            key={item.value}
            role="option"
            aria-selected={index === active}
            onMouseDown={(event) => {
              event.preventDefault();
              onPick(item);
            }}
            className={clsx(
              'cursor-pointer px-3 py-1.5 text-sm',
              index === active
                ? 'bg-brand-50 dark:bg-brand-950/40'
                : 'hover:bg-surface-3',
            )}
          >
            <span className="font-medium">{item.label}</span>
            {item.detail && (
              <span className="block truncate text-xs text-ink-muted">{item.detail}</span>
            )}
          </li>
        ))
      )}
    </ul>
  );
}

/**
 * Arrow keys, Enter and Escape for a field with a suggestion list under it.
 * Answers true when it handled the key.
 */
export function suggestKeys(
  event: React.KeyboardEvent,
  count: number,
  active: number,
  setActive: (index: number) => void,
  pick: () => void,
  close: () => void,
): boolean {
  if (event.key === 'ArrowDown' && count > 0) {
    setActive((active + 1) % count);
  } else if (event.key === 'ArrowUp' && count > 0) {
    setActive((active - 1 + count) % count);
  } else if (event.key === 'Enter' && count > 0 && active >= 0) {
    pick();
  } else if (event.key === 'Escape') {
    close();
  } else {
    return false;
  }

  event.preventDefault();
  return true;
}

/**
 * A text field that offers matching entries from a list as it is typed in, and still
 * takes whatever is typed: a walk-in customer, or a device the product list does not
 * carry.
 */
export function SuggestInput({
  label,
  value,
  onChange,
  options,
  onPick,
  required = false,
  disabled = false,
  placeholder,
  className,
  size = 'default',
  limit = 8,
}: {
  readonly label: string;
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly options: readonly Suggestion[];
  readonly onPick: (option: Suggestion) => void;
  readonly required?: boolean;
  readonly disabled?: boolean;
  readonly placeholder?: string;
  readonly className?: string;
  readonly size?: 'default' | 'sm';
  /** How many matches to offer at once. */
  readonly limit?: number;
}): React.JSX.Element {
  const id = useId();
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);

  const matches = useMemo(() => {
    const typed = value.trim().toLowerCase();
    if (typed === '') return [];

    const found: Suggestion[] = [];
    for (const option of options) {
      if (
        option.label.toLowerCase().includes(typed) ||
        (option.detail?.toLowerCase().includes(typed) ?? false)
      ) {
        found.push(option);
        if (found.length >= limit) break;
      }
    }
    return found;
  }, [value, options, limit]);

  const pick = (option: Suggestion): void => {
    onPick(option);
    setOpen(false);
  };

  return (
    <Field
      label={label}
      required={required}
      htmlFor={id}
      {...(className === undefined ? {} : { className })}
    >
      <div className="relative">
        <input
          id={id}
          value={value}
          disabled={disabled}
          placeholder={placeholder ?? ''}
          autoComplete="off"
          role="combobox"
          aria-expanded={open && matches.length > 0}
          aria-controls={`${id}-list`}
          aria-autocomplete="list"
          onChange={(event) => {
            onChange(event.target.value);
            setActive(0);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => setOpen(false)}
          onKeyDown={(event) => {
            if (!open) return;
            suggestKeys(
              event,
              matches.length,
              active,
              setActive,
              () => {
                const chosen = matches[active];
                if (chosen) pick(chosen);
              },
              () => setOpen(false),
            );
          }}
          className={size === 'sm' ? 'field-input-sm' : 'field-input'}
        />
        {open && (
          <SuggestList id={`${id}-list`} items={matches} active={active} onPick={pick} />
        )}
      </div>
    </Field>
  );
}
