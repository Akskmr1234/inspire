import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ConfirmPrompt } from '@/components/ConfirmPrompt';
import { Modal } from '@/components/Modal';
import type { SelectOption } from '@/components/SearchSelect';

/** What makes two lines the same item: the product, and its batch and expiry where it has them. */
export interface ItemLine {
  readonly productId: string;
  readonly batchNumber?: string | undefined;
  readonly expiresOn?: string | undefined;
}

/** The fields a change has to touch before two lines can become the same item. */
const IDENTITY: readonly string[] = ['productId', 'batchNumber', 'batchId', 'expiresOn'];

/**
 * Whether two lines carry the same item: the same product, in the same batch, expiring
 * the same day. A different batch or expiry is a different item, and may have a line
 * of its own.
 */
export function isSameItem(a: ItemLine, b: ItemLine): boolean {
  const batch = (value: string | undefined): string => (value ?? '').trim().toUpperCase();

  return (
    a.productId !== '' &&
    a.productId === b.productId &&
    batch(a.batchNumber) === batch(b.batchNumber) &&
    (a.expiresOn ?? '') === (b.expiresOn ?? '')
  );
}

interface Duplicate<T> {
  /** The line that was just made the same as another. */
  readonly index: number;
  /** The line it repeats. */
  readonly existing: number;
  /** The line as it was before the change. */
  readonly before: T;
  /** The line as the change left it. */
  readonly after: T;
}

/**
 * The rules a document's lines are entered under, the same on every transaction screen:
 *
 * - one item, one line. Choosing again an item that is already on the document - the
 *   same product, batch and expiry - asks "This item has already been added. Do you
 *   want to update the quantity?". Yes leaves the one line and puts the cursor in its
 *   quantity; No leaves the line that was there as it was, and the new choice is undone.
 * - one empty line at a time. Add item adds a line only when every line names a
 *   product; while one does not, it opens a list of products to choose that line's
 *   from instead.
 *
 * The screen keeps its own lines and its own way of choosing a product - the rate, the
 * tax and the unit it starts a line at - and hands both in.
 */
export function useLineGuards<T extends ItemLine>({
  lines,
  setLines,
  blank,
  attend,
  choose,
  productOptions,
  triggers = IDENTITY,
}: {
  readonly lines: readonly T[];
  readonly setLines: (update: (previous: readonly T[]) => readonly T[]) => void;
  /** A new, empty line. */
  readonly blank: () => T;
  /** Brings a line into view; with `quantity`, puts the cursor in its quantity. */
  readonly attend: (
    line: number | 'last',
    options?: { readonly focus?: boolean | 'quantity' },
  ) => void;
  /** Chooses a product for a line exactly as its own picker would. */
  readonly choose: (index: number, productId: string) => void;
  /** The products the screen's pickers offer. */
  readonly productOptions: readonly SelectOption[];
  /**
   * The fields whose change is checked. A batch number typed a letter at a time is
   * left out where a screen has one - "B1" is not yet the "B12" being typed - and the
   * line is checked when its product, chosen batch or expiry is.
   */
  readonly triggers?: readonly string[];
}): {
  /** Changes a line, asking first when the change repeats another line's item. */
  readonly change: (index: number, patch: Partial<T>) => void;
  /** Asks, as `change` does, about a change the screen has made to a line itself. */
  readonly review: (index: number, patch: Partial<T>) => void;
  /** Add item: a new line, or the empty one's product to choose. */
  readonly add: (addLine: () => void) => void;
  /** The question and the product list, to render anywhere on the screen. */
  readonly dialogs: React.ReactNode;
} {
  const { t } = useTranslation();
  const [duplicate, setDuplicate] = useState<Duplicate<T> | null>(null);
  const [picking, setPicking] = useState<number | null>(null);

  const review = (index: number, patch: Partial<T>): void => {
    const before = lines[index];
    if (!before) return;

    const after = { ...before, ...patch };
    if (!after.productId || !Object.keys(patch).some((key) => triggers.includes(key))) return;

    const existing = lines.findIndex((other, at) => at !== index && isSameItem(after, other));
    if (existing >= 0) setDuplicate({ index, existing, before, after });
  };

  const change = (index: number, patch: Partial<T>): void => {
    setLines((previous) =>
      previous.map((line, at) => (at === index ? { ...line, ...patch } : line)),
    );
    review(index, patch);
  };

  const add = (addLine: () => void): void => {
    const empty = lines.findIndex((line) => !line.productId);

    if (empty < 0) {
      addLine();
      return;
    }

    // A line still waiting for its product: that one's product first.
    attend(empty);
    setPicking(empty);
  };

  /** Yes: one line for the item, and the cursor in its quantity. */
  const keepExisting = (found: Duplicate<T>): void => {
    setLines((previous) => previous.filter((_, at) => at !== found.index));
    attend(found.existing > found.index ? found.existing - 1 : found.existing, {
      focus: 'quantity',
    });
  };

  /**
   * No: the line that was there stays as it was, and the new choice is undone. A line
   * that held another product goes back to it; one that was new - or whose batch was
   * filled in for it - is empty again, or goes where another line is already empty.
   */
  const undo = (found: Duplicate<T>): void => {
    const restore =
      found.before.productId !== '' &&
      (found.before.productId !== found.after.productId ||
        (found.before.batchNumber ?? '') !== '');

    setLines((previous) => {
      if (restore) {
        return previous.map((line, at) => (at === found.index ? found.before : line));
      }

      const anotherEmpty = previous.some((line, at) => at !== found.index && !line.productId);
      return anotherEmpty
        ? previous.filter((_, at) => at !== found.index)
        : previous.map((line, at) => (at === found.index ? blank() : line));
    });
  };

  const dialogs = (
    <>
      {duplicate && (
        <ConfirmPrompt
          title={t('invoice.duplicateTitle')}
          body={t('invoice.duplicateBody')}
          onConfirm={() => {
            setDuplicate(null);
            keepExisting(duplicate);
          }}
          onCancel={() => {
            setDuplicate(null);
            undo(duplicate);
          }}
        />
      )}
      {picking !== null && (
        <ProductPickerDialog
          options={productOptions}
          onPick={(productId) => {
            const index = picking;
            setPicking(null);
            choose(index, productId);
            attend(index, { focus: 'quantity' });
          }}
          onClose={() => setPicking(null)}
        />
      )}
    </>
  );

  return { change, review, add, dialogs };
}

/** How many products the list draws at once; the search narrows the rest. */
const SHOWN = 80;

/**
 * A list of products to choose a line's from: Add item pressed while a line still has
 * none. Typing narrows it - by code, name or barcode, as the line's own picker does -
 * and Enter or a click chooses.
 */
export function ProductPickerDialog({
  options,
  onPick,
  onClose,
}: {
  readonly options: readonly SelectOption[];
  readonly onPick: (productId: string) => void;
  readonly onClose: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [active, setActive] = useState(0);

  const found = useMemo(() => {
    const words = search.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return options
      .filter((option) => !option.disabled)
      .filter((option) => {
        const text = `${option.label} ${option.detail ?? ''} ${option.keywords ?? ''}`.toLowerCase();
        return words.every((word) => text.includes(word));
      })
      .slice(0, SHOWN);
  }, [options, search]);

  const pick = (index: number): void => {
    const option = found[index];
    if (option) onPick(option.value);
  };

  return (
    <Modal title={t('invoice.chooseProduct')} subtitle={t('invoice.chooseProductHint')} size="form" onClose={onClose}>
      <div className="space-y-3">
        <input
          type="search"
          autoFocus
          value={search}
          aria-label={t('invoice.searchProducts')}
          placeholder={t('invoice.searchProducts')}
          onChange={(event) => {
            setSearch(event.target.value);
            setActive(0);
          }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown') {
              event.preventDefault();
              setActive((current) => Math.min(current + 1, found.length - 1));
            } else if (event.key === 'ArrowUp') {
              event.preventDefault();
              setActive((current) => Math.max(current - 1, 0));
            } else if (event.key === 'Enter') {
              event.preventDefault();
              pick(active);
            }
          }}
          className="field-input w-full"
        />
        <ul role="listbox" aria-label={t('invoice.chooseProduct')} className="max-h-96 overflow-y-auto rounded-xl border border-line">
          {found.length === 0 ? (
            <li className="px-3 py-4 text-center text-sm text-ink-muted">{t('invoice.noProducts')}</li>
          ) : (
            found.map((option, index) => (
              <li
                key={option.value}
                role="option"
                aria-selected={index === active}
                onMouseEnter={() => setActive(index)}
                onClick={() => pick(index)}
                className={`flex cursor-pointer items-center justify-between gap-3 border-b border-line px-3 py-2 text-sm last:border-b-0 ${
                  index === active ? 'bg-brand-50 dark:bg-brand-500/15' : ''
                }`}
              >
                <span className="min-w-0">
                  <span className="block truncate font-medium text-ink">{option.label}</span>
                  {option.detail && (
                    <span className="block truncate text-xs text-ink-muted">{option.detail}</span>
                  )}
                </span>
                {option.meta && (
                  <span className="shrink-0 text-xs tabular-nums text-ink-muted">{option.meta}</span>
                )}
              </li>
            ))
          )}
        </ul>
      </div>
    </Modal>
  );
}
