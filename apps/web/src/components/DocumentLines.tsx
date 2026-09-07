import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { SearchSelect, type SelectOption } from '@/components/SearchSelect';
import { chargeAdds, chargeLedgers, type LedgerSummary } from '@/lib/ledgers';
import type { ProductSummary } from '@/lib/products';
import type { StockValuationRow } from '@/lib/stock';

/**
 * The pieces the document entry screens share: the product picker, the
 * customisable line columns, and the charges a document carries beside its goods.
 *
 * Written once because a purchase, a purchase return and a purchase order ask for
 * the same things in the same shapes, and the three copies that would otherwise
 * exist would disagree about which of them offers a discount column.
 */

/** A product as a picker row: what it is, and what is on the shelf. */
export function productOption(
  product: ProductSummary,
  onHand: number | undefined,
): SelectOption {
  return {
    value: product.id,
    // The code leads because that is what somebody reading a supplier's invoice
    // has in front of them, and the description follows on its own line rather
    // than being crammed into the same one and truncated.
    label: `${product.code} — ${product.description}`,
    detail: [
      product.categoryName,
      product.brandName,
      product.stockUnitCode,
      product.retailRate > 0 ? `@ ${product.retailRate.toFixed(2)}` : '',
    ]
      .filter(Boolean)
      .join(' · '),
    // What is actually in stock, which is the question the picker was silently
    // refusing to answer: a buyer choosing what to reorder and a storekeeper
    // choosing what to send back both need it, and both were opening the stock
    // report in another tab to get it.
    ...(onHand === undefined
      ? {}
      : { meta: `${onHand.toLocaleString(undefined, { maximumFractionDigits: 3 })}` }),
    keywords: product.descriptionArabic ?? '',
  };
}

/** Totals stock by product, so a picker can say what is on the shelf. */
export function stockByProduct(
  rows: readonly StockValuationRow[] | undefined,
): ReadonlyMap<string, number> {
  const totals = new Map<string, number>();

  for (const row of rows ?? []) {
    totals.set(row.productId, (totals.get(row.productId) ?? 0) + row.quantity);
  }

  return totals;
}

/** One column a line grid can be told to show or hide. */
export interface LineColumn {
  readonly key: string;
  readonly label: string;
  /** Shown unless the user turns it off. */
  readonly defaultOn: boolean;
  /** Always drawn: the product and the quantity are what a line is. */
  readonly fixed?: boolean;
}

/**
 * Which line columns a document entry shows.
 *
 * A purchase line can carry a discount, a tax rate, the tax that rate comes to, and
 * what is on the shelf — and a firm that buys at a flat rate with no discounts is
 * being asked to look past two empty boxes on every line of every purchase they
 * enter. The list grid has had a column picker since it was written; the entry grid
 * had none, which is the asymmetry this closes.
 *
 * Remembered per document kind in this browser, which is the right scope: it is a
 * preference about how somebody works, not a fact about the firm.
 */
export function useLineColumns(
  storageKey: string,
  columns: readonly LineColumn[],
): {
  readonly shows: (key: string) => boolean;
  readonly toggle: (key: string) => void;
  readonly picker: React.ReactNode;
} {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  const [hidden, setHidden] = useState<ReadonlySet<string>>(() => {
    const fromDefaults = new Set(
      columns.filter((column) => !column.defaultOn).map((column) => column.key),
    );

    try {
      const stored = localStorage.getItem(`erp.lines.${storageKey}`);

      return stored ? new Set(JSON.parse(stored) as string[]) : fromDefaults;
    } catch {
      return fromDefaults;
    }
  });

  const toggle = useCallback(
    (key: string): void =>
      setHidden((previous) => {
        const next = new Set(previous);

        if (next.has(key)) {
          next.delete(key);
        } else {
          next.add(key);
        }

        try {
          localStorage.setItem(`erp.lines.${storageKey}`, JSON.stringify([...next]));
        } catch {
          // A browser with storage disabled still gets the choice for this session.
        }

        return next;
      }),
    [storageKey],
  );

  const shows = useCallback(
    (key: string): boolean => {
      const column = columns.find((candidate) => candidate.key === key);

      return column?.fixed === true || !hidden.has(key);
    },
    [columns, hidden],
  );

  const picker = (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
        className={clsx(
          'rounded-lg border px-2.5 py-1 text-xs font-medium whitespace-nowrap transition',
          open
            ? 'border-brand-500/40 bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-200'
            : 'border-line bg-surface text-ink-muted hover:border-line-strong hover:bg-surface-3 hover:text-ink',
        )}
      >
        {t('documents.columns')}
      </button>

      {open && (
        <div className="animate-drop absolute end-0 z-40 mt-1 w-56 rounded-lg border border-line bg-surface p-2 shadow-float">
          {columns.map((column) => (
            <label
              key={column.key}
              className={clsx(
                'field-check w-full rounded px-1.5 py-1 text-xs',
                column.fixed === true && 'opacity-50',
              )}
            >
              <input
                type="checkbox"
                checked={shows(column.key)}
                disabled={column.fixed === true}
                onChange={() => toggle(column.key)}
              />
              {column.label}
            </label>
          ))}
        </div>
      )}
    </div>
  );

  return { shows, toggle, picker };
}

/** One charge on a document being entered. */
export interface DraftCharge {
  readonly key: string;
  ledgerId: string;
  amount: string;
}

/** A fresh, empty charge row. */
export function emptyCharge(): DraftCharge {
  return { key: crypto.randomUUID(), ledgerId: '', amount: '' };
}

/**
 * What the charges come to, as the screen adds them up.
 *
 * The sign follows the firm's charge matrix, which lives on the server: freight
 * adds, a discount deducts. This reads it off the ledger's code because the API
 * exposes no endpoint for the matrix — so it is the screen's running total and never
 * the figure that reaches the books, which the server computes from the matrix
 * itself.
 */
export function chargeTotal(
  charges: readonly DraftCharge[],
  ledgers: readonly LedgerSummary[],
): number {
  let total = 0;

  for (const charge of charges) {
    const ledger = ledgers.find((candidate) => candidate.ledgerId === charge.ledgerId);
    const amount = Number(charge.amount);

    if (!ledger || !Number.isFinite(amount)) {
      continue;
    }

    total += chargeAdds(ledger) ? amount : -amount;
  }

  return total;
}

/**
 * The charges a document carries beside its goods.
 *
 * Freight, packing, delivery, a discount off the whole document — every one of them
 * already supported by the API and by none of the screens, so a purchase with
 * carriage on it could not be entered as the supplier billed it. The accounts come
 * from the firm's own chart: whichever it has classified as additional charges, plus
 * the codes the standard chart seeds.
 */
export function ChargesPanel({
  charges,
  ledgers,
  onChange,
  currency,
}: {
  readonly charges: readonly DraftCharge[];
  readonly ledgers: readonly LedgerSummary[];
  readonly onChange: (charges: readonly DraftCharge[]) => void;
  readonly currency?: string;
}): React.JSX.Element {
  const { t } = useTranslation();

  const available = useMemo(() => chargeLedgers(ledgers), [ledgers]);

  const options: readonly SelectOption[] = available.map((ledger) => ({
    value: ledger.ledgerId,
    label: `${ledger.code} — ${ledger.name}`,
    detail: chargeAdds(ledger) ? t('documents.chargeAdds') : t('documents.chargeDeducts'),
  }));

  const total = chargeTotal(charges, ledgers);

  return (
    <section className="panel space-y-3">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-sm font-semibold text-ink">{t('documents.charges')}</h3>
          <p className="mt-0.5 text-xs text-ink-muted">{t('documents.chargesHint')}</p>
        </div>

        <span className="font-mono text-sm tabular-nums text-ink">
          {total >= 0 ? '+' : '−'}
          {Math.abs(total).toFixed(2)}
          {currency ? ` ${currency}` : ''}
        </span>
      </header>

      {available.length === 0 ? (
        <p className="text-xs text-ink-muted">{t('documents.noChargeAccounts')}</p>
      ) : (
        <>
          {charges.map((charge, index) => (
            <div key={charge.key} className="flex flex-wrap items-end gap-2">
              <div className="min-w-48 flex-1">
                <SearchSelect
                  value={charge.ledgerId}
                  onChange={(ledgerId) =>
                    onChange(
                      charges.map((candidate, at) =>
                        at === index ? { ...candidate, ledgerId } : candidate,
                      ),
                    )
                  }
                  options={options}
                  size="sm"
                  label={t('documents.chargeAccount')}
                  placeholder={t('documents.chooseCharge')}
                />
              </div>

              <input
                type="number"
                step="0.01"
                inputMode="decimal"
                aria-label={t('documents.chargeAmount')}
                value={charge.amount}
                onChange={(event) =>
                  onChange(
                    charges.map((candidate, at) =>
                      at === index
                        ? { ...candidate, amount: event.target.value }
                        : candidate,
                    ),
                  )
                }
                className="field-input-sm w-32 text-end font-mono tabular-nums"
              />

              <button
                type="button"
                onClick={() => onChange(charges.filter((_, at) => at !== index))}
                className="row-action row-action-danger"
              >
                {t('documents.removeCharge')}
              </button>
            </div>
          ))}

          <button
            type="button"
            onClick={() => onChange([...charges, emptyCharge()])}
            className="btn-secondary btn-sm"
          >
            {t('documents.addCharge')}
          </button>
        </>
      )}
    </section>
  );
}

/** The figures a document adds up to, shown the way an invoice states them. */
export function DocumentTotals({
  taxable,
  tax,
  charges,
  currency,
}: {
  readonly taxable: number;
  readonly tax: number;
  readonly charges: number;
  readonly currency?: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const total = taxable + tax + charges;

  return (
    <dl className="ms-auto grid w-full max-w-xs grid-cols-[1fr_auto] gap-x-6 gap-y-1 text-sm">
      <dt className="text-ink-muted">{t('documents.taxable')}</dt>
      <dd className="text-end font-mono tabular-nums">{taxable.toFixed(2)}</dd>

      <dt className="text-ink-muted">{t('documents.tax')}</dt>
      <dd className="text-end font-mono tabular-nums">{tax.toFixed(2)}</dd>

      {charges !== 0 && (
        <>
          <dt className="text-ink-muted">{t('documents.chargesShort')}</dt>
          <dd className="text-end font-mono tabular-nums">{charges.toFixed(2)}</dd>
        </>
      )}

      <dt className="border-t border-line pt-1 font-semibold text-ink">
        {t('documents.total')}
      </dt>
      <dd className="border-t border-line pt-1 text-end font-mono font-semibold tabular-nums text-ink">
        {total.toFixed(2)}
        {currency ? ` ${currency}` : ''}
      </dd>
    </dl>
  );
}
