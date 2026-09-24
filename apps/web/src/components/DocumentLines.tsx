import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import * as Popover from '@radix-ui/react-popover';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { SearchSelect, type SelectOption } from '@/components/SearchSelect';
import { chargeAdds, chargeLedgers, type LedgerSummary } from '@/lib/ledgers';
import type { ProductSummary } from '@/lib/products';
import type { StockValuationRow } from '@/lib/stock';
import { moneyAlways, useMoney } from '@/lib/money';
import { chargesFor, type ChargeDocument } from '@/lib/charges';
import { useSettings } from '@/stores/settings';

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
      product.retailRate > 0 ? `@ ${moneyAlways(product.retailRate)}` : '',
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

  /*
    On a popover rather than a div that is shown and hidden.

    It had no way out but the button that opened it: no Escape, nothing on a click
    elsewhere. Toggling a column changes the width of the table underneath, which
    moves the toolbar the button is in — so the one exit could be somewhere other
    than where it was pressed, and the panel stayed up over the lines it was there
    to arrange. A popover closes on Escape, on a click outside and on the button
    again, and Radix keeps it above the dialog this usually opens inside.
  */
  const picker = (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger
        className={clsx(
          'rounded-lg border px-2.5 py-1 text-xs font-medium whitespace-nowrap transition',
          open
            ? 'border-brand-500/40 bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-200'
            : 'border-line bg-surface text-ink-muted hover:border-line-strong hover:bg-surface-3 hover:text-ink',
        )}
      >
        {t('documents.columns')}
      </Popover.Trigger>

      <Popover.Portal>
        <Popover.Content
          align="end"
          sideOffset={4}
          className="animate-drop z-[70] w-56 rounded-lg border border-line bg-surface p-2 shadow-float"
        >
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

          {/* Said plainly, because the panel is a list of checkboxes and nothing in
              it looks like a way out. */}
          <Popover.Close className="mt-1 w-full rounded px-1.5 py-1 text-xs font-medium text-ink-muted hover:bg-surface-3 hover:text-ink">
            {t('common.close')}
          </Popover.Close>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );

  return { shows, toggle, picker };
}

/**
 * The product's own figures, offered as extra line columns.
 *
 * A buyer keying an order against a supplier's price list has the code on the
 * invoice and the printed price on the carton, and had to open the product to check
 * either — on every line, of every document, on a screen they cannot leave without
 * losing what they have typed. Off by default, because a firm that does not price
 * against an MRP wants five columns and not nine; kept as the same four columns on
 * every entry screen, so a firm turns them on once and recognises them everywhere.
 *
 * Read-only on the line. These belong to the master and the document does not
 * change them: what a line does carry is its own rate, which the picker already
 * fills from the master and which anybody may type over.
 */
export function productDetailColumns(t: (key: string) => string): readonly LineColumn[] {
  return [
    { key: 'productCode', label: t('lineDetails.code'), defaultOn: false },
    { key: 'productUnit', label: t('lineDetails.unit'), defaultOn: false },
    { key: 'productRetail', label: t('lineDetails.retail'), defaultOn: false },
    { key: 'productMrp', label: t('lineDetails.mrp'), defaultOn: false },
  ];
}

/**
 * The cells behind {@link productDetailColumns}.
 *
 * Rendered as a fragment so a caller drops them into its row at whatever point its
 * own columns reach, and skipped one at a time by the same `shows` the rest of the
 * row is gated on.
 */
export function ProductDetailCells({
  product,
  shows,
  labels,
}: {
  readonly product: ProductSummary | undefined;
  readonly shows: (key: string) => boolean;
  readonly labels: (key: string) => string;
}): React.JSX.Element {
  const figure = (value: number | undefined): string =>
    value === undefined || value <= 0 ? '—' : moneyAlways(value);

  return (
    <>
      {shows('productCode') && (
        <td
          data-label={labels('lineDetails.code')}
          className="px-2 py-1 font-mono text-xs text-ink-muted"
        >
          {product?.code ?? '—'}
        </td>
      )}

      {shows('productUnit') && (
        <td
          data-label={labels('lineDetails.unit')}
          className="px-2 py-1 text-xs text-ink-muted"
        >
          {product?.stockUnitCode ?? '—'}
        </td>
      )}

      {shows('productRetail') && (
        <td
          data-label={labels('lineDetails.retail')}
          className="px-2 py-1 text-end font-mono tabular-nums text-ink-muted"
        >
          {figure(product?.retailRate)}
        </td>
      )}

      {shows('productMrp') && (
        <td
          data-label={labels('lineDetails.mrp')}
          className="px-2 py-1 text-end font-mono tabular-nums text-ink-muted"
        >
          {figure(product?.maximumRetailPrice)}
        </td>
      )}
    </>
  );
}

/** The headings for {@link ProductDetailCells}, in the same order. */
export function ProductDetailHeaders({
  shows,
  labels,
}: {
  readonly shows: (key: string) => boolean;
  readonly labels: (key: string) => string;
}): React.JSX.Element {
  return (
    <>
      {shows('productCode') && (
        <th className="px-2 py-1 text-start">{labels('lineDetails.code')}</th>
      )}
      {shows('productUnit') && (
        <th className="px-2 py-1 text-start">{labels('lineDetails.unit')}</th>
      )}
      {shows('productRetail') && (
        <th className="px-2 py-1 text-end">{labels('lineDetails.retail')}</th>
      )}
      {shows('productMrp') && (
        <th className="px-2 py-1 text-end">{labels('lineDetails.mrp')}</th>
      )}
    </>
  );
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
 * Puts the firm's standing charges onto a document as it opens.
 *
 * Applied once, when the chart of accounts arrives rather than when the screen
 * mounts: the heads are picked from the chart, so seeding before it has loaded would
 * drop every row that refers to it. Once only, because a second run would put the
 * freight row back after somebody deliberately took it off.
 *
 * A head that has since left the chart is dropped rather than seeded empty — see
 * {@link chargesFor}.
 */
export function useDefaultCharges(
  document: ChargeDocument,
  ledgers: readonly LedgerSummary[],
  apply: (charges: readonly DraftCharge[]) => void,
): void {
  const defaults = useSettings((state) => state.defaultCharges);
  const seeded = useRef(false);

  useEffect(() => {
    if (seeded.current || ledgers.length === 0) {
      return;
    }

    seeded.current = true;

    const rows = chargesFor(document, defaults, ledgers);

    if (rows.length > 0) {
      apply(
        rows.map((row) => ({
          key: crypto.randomUUID(),
          ledgerId: row.ledgerId,
          amount: row.amount,
        })),
      );
    }
  }, [document, defaults, ledgers, apply]);
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
          {moneyAlways(Math.abs(total))}
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
  gross,
  discount,
  tax,
  charges,
  rounding = 0,
  currency,
}: {
  /** The goods at what they were priced at, before any discount came off. */
  readonly gross: number;
  /** What came off the lines. Positive: it is shown as a deduction. */
  readonly discount: number;
  readonly tax: number;
  readonly charges: number;
  /** What the server moved the total by to reach a whole unit of the currency. */
  readonly rounding?: number;
  readonly currency?: string;
}): React.JSX.Element {
  const { t } = useTranslation();
  const { moneyAlways } = useMoney();

  const net = gross - discount;
  const total = net + tax + charges + rounding;

  return (
    <dl className="ms-auto grid w-full max-w-xs grid-cols-[1fr_auto] gap-x-6 gap-y-1 text-sm">
      <Line label={t('documents.gross')} value={moneyAlways(gross)} />

      {/*
        Shown only when there is one, and shown as a deduction: a discount line
        reading `100.00` between a gross of 1,000 and a net of 900 makes the reader
        do the subtraction to find out which way it went.

        The sign is left to `Intl` rather than written in front of the figure. A
        minus character of our own is a neutral run of its own to the bidirectional
        algorithm, so in an Arabic document it was laid out on the far side of the
        digits and read as a trailing sign. Handed a negative number, `Intl` formats
        the whole thing as one run and it stays in front of the figure in both
        directions.
      */}
      {discount !== 0 && (
        <Line label={t('documents.discount')} value={moneyAlways(-Math.abs(discount))} />
      )}

      {/*
        Ruled off only when something came off above it. The rule means "the lines
        above add up to this", and drawn under a gross with no deduction beneath it
        it would be a line ruled under a single figure to announce the same figure
        again — which reads as a mistake rather than as arithmetic.

        Shown either way, though. It is the line the tax is worked out on and the
        line an auditor asks about: the same figure the API calls `taxable` and a
        tax return calls the taxable value, which on a bill is the net.
      */}
      <Line label={t('documents.net')} value={moneyAlways(net)} ruled={discount !== 0} />

      <Line label={t('documents.tax')} value={moneyAlways(tax)} />

      {charges !== 0 && (
        <Line label={t('documents.chargesShort')} value={moneyAlways(charges)} />
      )}

      {rounding !== 0 && (
        <Line label={t('documents.rounding')} value={moneyAlways(rounding)} />
      )}

      <Line
        label={t('documents.total')}
        value={`${moneyAlways(total)}${currency ? ` ${currency}` : ''}`}
        strong
      />
    </dl>
  );
}

/** One row of the ladder: a word on the left, a figure on the right. */
function Line({
  label,
  value,
  ruled = false,
  strong = false,
}: {
  readonly label: string;
  readonly value: string;
  /** A hairline above, for a subtotal the lines above it add up to. */
  readonly ruled?: boolean;
  readonly strong?: boolean;
}): React.JSX.Element {
  return (
    <>
      <dt
        className={clsx(
          ruled && 'mt-1 border-t border-line pt-1',
          strong
            ? 'mt-1 border-t border-line pt-1 font-semibold text-ink'
            : 'text-ink-muted',
        )}
      >
        {label}
      </dt>
      <dd
        className={clsx(
          'text-end font-mono tabular-nums',
          ruled && 'mt-1 border-t border-line pt-1',
          strong && 'mt-1 border-t border-line pt-1 font-semibold text-ink',
        )}
      >
        {value}
      </dd>
    </>
  );
}
