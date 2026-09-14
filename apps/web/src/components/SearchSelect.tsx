import { useEffect, useId, useMemo, useRef, useState } from 'react';
import * as Popover from '@radix-ui/react-popover';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';

/**
 * One choice in a {@link SearchSelect}.
 *
 * `label` is what the box shows once the choice is made and the first thing the
 * search matches. `detail` and `meta` are the two lines a picker needs to be useful
 * without opening the record behind it — for a product, its description and what is
 * on the shelf — and both are searched too, because somebody typing a barcode is
 * typing something that appears in neither the label nor the description.
 */
export interface SelectOption {
  readonly value: string;
  readonly label: string;
  /** A second line under the label: a description, a group, an address. */
  readonly detail?: string;
  /** A figure held at the end of the row: stock on hand, a balance, a rate. */
  readonly meta?: string;
  /** Extra text the search should match but nothing needs to show. */
  readonly keywords?: string;
  /** Offered but refused, with the reason in `detail`. */
  readonly disabled?: boolean;
}

/** How many matches the list draws at once. */
const VISIBLE_LIMIT = 60;

function matches(option: SelectOption, needle: string): boolean {
  if (needle === '') {
    return true;
  }

  const haystack =
    `${option.label} ${option.detail ?? ''} ${option.meta ?? ''} ${option.keywords ?? ''}`.toLowerCase();

  // Every word has to appear somewhere, in any order. "blue 500" finds "500ml blue
  // paint", which a substring match on the whole phrase would not.
  return needle
    .toLowerCase()
    .split(/\s+/)
    .filter((word) => word !== '')
    .every((word) => haystack.includes(word));
}

/**
 * A dropdown you can type into.
 *
 * The native `<select>` this replaces has no search at all: on a chart of accounts
 * or a product master it is a list of several thousand rows that can only be walked,
 * and the browser's own type-ahead matches the first characters of an option — which
 * is the code, not the name somebody is looking up by. Every picker in the
 * application that reaches a master of any size uses this instead.
 *
 * The popup is positioned `fixed` and measured from the input rather than laid out
 * beneath it. A line-item table scrolls sideways inside `overflow-x-auto`, and an
 * absolutely positioned list inside that container is clipped at its edge — which is
 * exactly where the product picker lives.
 */
export function SearchSelect({
  value,
  onChange,
  options,
  placeholder,
  disabled = false,
  invalid = false,
  clearable = false,
  size = 'default',
  id,
  label,
  className,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly options: readonly SelectOption[];
  /** What the empty box says. Also the label of the "choose nothing" row. */
  readonly placeholder?: string;
  readonly disabled?: boolean;
  /** Draws the box in the error colour, for a mandatory field left empty. */
  readonly invalid?: boolean;
  /** Offers a row that puts the field back to empty. */
  readonly clearable?: boolean;
  readonly size?: 'default' | 'sm';
  readonly id?: string;
  /** Spoken name, where no visible label is associated with the box. */
  readonly label?: string;
  readonly className?: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const listId = `${inputId}-list`;

  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);

  const input = useRef<HTMLInputElement>(null);

  const selected = options.find((option) => option.value === value);

  const shown = useMemo(() => {
    const needle = query.trim();
    const found = options.filter((option) => matches(option, needle));

    return found.slice(0, VISIBLE_LIMIT);
  }, [options, query]);

  const total = useMemo(() => {
    const needle = query.trim();
    return needle === ''
      ? options.length
      : options.filter((option) => matches(option, needle)).length;
  }, [options, query]);

  const rows: readonly SelectOption[] = clearable
    ? [{ value: '', label: placeholder ?? t('common.none') }, ...shown]
    : shown;

  const pick = (option: SelectOption): void => {
    if (option.disabled) {
      return;
    }

    onChange(option.value);
    setQuery('');
    setOpen(false);
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLInputElement>): void => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();

      if (!open) {
        setOpen(true);
        setActive(0);
        return;
      }

      setActive((current) => {
        const next = event.key === 'ArrowDown' ? current + 1 : current - 1;
        return Math.max(0, Math.min(next, rows.length - 1));
      });

      return;
    }

    if (event.key === 'Enter' && open) {
      const option = rows[active];

      if (option) {
        event.preventDefault();
        pick(option);
      }

      return;
    }

    if (event.key === 'Escape' && open) {
      // Swallowed, so a picker inside a dialog closes the list rather than the
      // dialog it is sitting in.
      event.preventDefault();
      event.stopPropagation();
      setOpen(false);
      setQuery('');
      return;
    }

    if (event.key === 'Tab' && open) {
      setOpen(false);
      setQuery('');
    }
  };

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Anchor asChild>
        <div className={clsx('relative', className)}>
          <input
            ref={input}
            id={inputId}
            type="text"
            role="combobox"
            autoComplete="off"
            aria-expanded={open}
            aria-controls={open ? listId : undefined}
            aria-autocomplete="list"
            {...(label === undefined ? {} : { 'aria-label': label })}
            disabled={disabled}
            value={open ? query : (selected?.label ?? '')}
            placeholder={selected ? selected.label : (placeholder ?? t('common.choose'))}
            onFocus={() => {
              setOpen(true);
              setActive(0);
            }}
            // Focus alone is not enough. Escape closes the list and leaves the caret
            // where it is, so the next click on the field fires no focus event and the
            // list stayed shut — the box looked live, took typing, and showed nothing.
            // Clicking always opens and never closes: this is a box to type in, and a
            // click to put the caret between two words should not take the list away.
            onClick={() => {
              setOpen(true);
              setActive(0);
            }}
            onChange={(event) => {
              setQuery(event.target.value);
              setOpen(true);
              setActive(0);
            }}
            onKeyDown={onKeyDown}
            className={clsx(
              size === 'sm' ? 'field-input-sm' : 'field-input',
              'pe-7',
              invalid && 'field-invalid',
            )}
          />

          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={1.8}
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
            className="pointer-events-none absolute inset-y-0 end-2 my-auto size-4 text-ink-subtle"
          >
            <path d="m6 9 6 6 6-6" />
          </svg>
        </div>
      </Popover.Anchor>

      {/*
        Portalled, as the hand-rolled list was. Radix positions with Floating UI, so
        it would land in the right place either way — but a list left inline is still
        clipped by whatever `overflow` it sits inside, and the product picker lives
        in a line-item table that scrolls sideways. Out at the body there is nothing
        to clip it, and Radix keeps it inside the dialog's own layer, so a picker
        opened from a form stays reachable while everything behind the form is inert.
      */}
      <Popover.Portal>
        <Popover.Content
          id={listId}
          role="listbox"
          align="start"
          sideOffset={4}
          /*
          Focus stays in the box. A popover takes focus by default, which for a list
          hanging off a field somebody is typing into would move the caret out of it
          on the first keystroke.
        */
          onOpenAutoFocus={(event) => event.preventDefault()}
          /*
          And the box keeps it when the list closes. Radix would otherwise return
          focus to the anchor — which is this field's own wrapper, not the input —
          and the caret would land nowhere.
        */
          onCloseAutoFocus={(event) => event.preventDefault()}
          /*
          Typing is the point of this control, so the keys go to the input rather
          than to the list: Radix's own typeahead would otherwise swallow them.
        */
          onKeyDown={(event) => event.stopPropagation()}
          className="animate-drop z-[70] max-h-64 w-[var(--radix-popover-trigger-width)] min-w-60 overflow-y-auto overscroll-contain rounded-lg border border-line bg-surface py-1 shadow-float"
        >
          {rows.length === 0 && (
            <p className="px-3 py-2 text-xs text-ink-muted">{t('common.noMatches')}</p>
          )}

          {rows.map((option, index) => (
            <button
              key={option.value === '' ? '__none' : option.value}
              type="button"
              role="option"
              aria-selected={option.value === value}
              disabled={option.disabled === true}
              // Focus must not leave the input, or the box would empty itself
              // mid-click and the choice would be made against a stale list.
              onMouseDown={(event) => event.preventDefault()}
              onMouseEnter={() => setActive(index)}
              onClick={() => pick(option)}
              className={clsx(
                'flex w-full items-start gap-3 px-3 py-1.5 text-start text-sm transition-colors',
                option.disabled === true && 'cursor-not-allowed opacity-50',
                index === active ? 'bg-brand-50 dark:bg-brand-500/15' : 'bg-transparent',
                option.value === value ? 'font-semibold text-ink' : 'text-ink',
              )}
            >
              <span className="min-w-0 flex-1">
                <span className="block truncate">{option.label}</span>
                {option.detail && (
                  <span className="block truncate text-xs text-ink-muted">
                    {option.detail}
                  </span>
                )}
              </span>

              {option.meta && (
                <span className="shrink-0 font-mono text-xs tabular-nums text-ink-muted">
                  {option.meta}
                </span>
              )}
            </button>
          ))}

          {total > shown.length && (
            <p className="border-t border-line px-3 py-1.5 text-xs text-ink-subtle">
              {t('common.moreMatches', { count: total - shown.length })}
            </p>
          )}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}

/**
 * Chooses for somebody when there is nothing to choose between.
 *
 * A required picker that opens empty asks a question it already knows the answer
 * to: a firm with one warehouse, one unit of measure or one tax head makes every
 * new record wait on a dropdown with a single row in it. Where the master offers
 * exactly one usable option, or names one as its default, the field starts there
 * and the person filling the form is left to the fields that genuinely need them.
 *
 * Only ever fills a field that is empty. A choice already made — by the user, or
 * by a record being edited — is never overwritten.
 */
export function useDefaultChoice(
  value: string,
  options: readonly SelectOption[],
  onChange: (value: string) => void,
  /** A value the master marks as its default, preferred over the only-one rule. */
  preferred?: string,
): void {
  // Reduced to a string before it reaches the effect. The options are mapped fresh
  // on every render, so depending on the array itself would re-run this constantly;
  // depending on what it comes to does not.
  const fallback = useMemo(() => {
    const usable = options.filter(
      (option) => option.disabled !== true && option.value !== '',
    );

    if (preferred !== undefined && usable.some((option) => option.value === preferred)) {
      return preferred;
    }

    return usable.length === 1 ? (usable[0]?.value ?? '') : '';
  }, [options, preferred]);

  useEffect(() => {
    if (value === '' && fallback !== '') {
      onChange(fallback);
    }
  }, [value, fallback, onChange]);
}
