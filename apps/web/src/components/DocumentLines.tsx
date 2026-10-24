import { useCallback, useEffect, useRef, useState } from 'react';
import * as Popover from '@radix-ui/react-popover';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { type SelectOption } from '@/components/SearchSelect';
import { AdditionalLedgerList } from '@/components/AdditionalLedgerList';
import { chargeAdds, type LedgerSummary } from '@/lib/ledgers';
import type { ProductSummary } from '@/lib/products';
import type { StockValuationRow } from '@/lib/stock';
import { moneyAlways, useMoney } from '@/lib/money';
import { setupName, type ChargeDocument, type ChargeSetup } from '@/lib/charges';
import { useSettings, useTaxOn } from '@/stores/settings';

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

      // A column the screen did not offer - tax, where the firm charges none - is not
      // shown, whatever this browser remembers about it.
      return column !== undefined && (column.fixed === true || !hidden.has(key));
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
          className="animate-drop z-[70] w-56 rounded-xl border border-line bg-surface p-2 shadow-float ring-1 ring-black/5 dark:ring-white/10"
        >
          {columns.map((column) => (
            <label
              key={column.key}
              className={clsx(
                'field-check w-full rounded-md px-2 py-1.5 text-xs transition-colors hover:bg-surface-3/70',
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
          <Popover.Close className="mt-2 w-full rounded-lg border border-line bg-surface-2 px-2 py-1.5 text-center text-xs font-semibold text-ink-muted transition hover:bg-surface-3 hover:text-ink">
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
 * Puts the default amounts Settings gives a document's additional ledgers onto it as it
 * opens.
 *
 * Applied once, when both the chart and the document's charge setup have arrived: a
 * default for a ledger no longer set up for the document would be refused by the
 * server, so it is left out rather than sent. A return starts with none - its sale's
 * or purchase's freight is not charged again when the goods come back.
 */
export function useDefaultCharges(
  document: ChargeDocument,
  ledgers: readonly LedgerSummary[],
  apply: (charges: readonly DraftCharge[]) => void,
  setup?: readonly ChargeSetup[],
): void {
  const defaults = useSettings((state) => state.defaultAdditionalLedgers);
  const seeded = useRef(false);

  useEffect(() => {
    if (seeded.current || ledgers.length === 0 || setup === undefined) {
      return;
    }

    seeded.current = true;

    if (document === 'salesReturn' || document === 'purchaseReturn') {
      return;
    }

    const configured = new Set(setup.map((charge) => charge.ledgerId));
    const rows = (defaults ?? [])
      .filter(
        (row) =>
          row.transactionType === setupName(document) &&
          row.isDefaultActive &&
          configured.has(row.ledgerId) &&
          Number(row.defaultAmount) > 0,
      )
      .map((row) => ({
        key: crypto.randomUUID(),
        ledgerId: row.ledgerId,
        amount: row.defaultAmount,
      }));

    if (rows.length > 0) {
      apply(rows);
    }
  }, [document, defaults, ledgers, setup, apply]);
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
  setup?: readonly ChargeSetup[],
): number {
  let total = 0;

  for (const charge of charges) {
    const ledger = ledgers.find((candidate) => candidate.ledgerId === charge.ledgerId);
    const amount = Number(charge.amount);

    if (!ledger || !Number.isFinite(amount)) {
      continue;
    }

    // The firm's own setup where it has arrived; the code's guess until then.
    const set = setup?.find((candidate) => candidate.ledgerId === charge.ledgerId);
    const adds = set ? set.isAddition : chargeAdds(ledger);

    total += adds ? amount : -amount;
  }

  return total;
}

/**
 * The additional ledgers a document carries beside its goods.
 *
 * One row for every ledger Settings set up for this kind of document, each waiting for
 * its amount: nobody picks freight from a list on every purchase, and a ledger that
 * is not set up cannot be chosen here and refused by the server afterwards. A blank
 * amount is simply not charged.
 */
export function ChargesPanel({
  charges,
  ledgers,
  onChange,
  currency,
  setup,
  nonTax = false,
}: {
  readonly charges: readonly DraftCharge[];
  readonly ledgers: readonly LedgerSummary[];
  readonly onChange: (charges: readonly DraftCharge[]) => void;
  readonly currency?: string;
  /** The firm's charge setup for the document. */
  readonly setup?: readonly ChargeSetup[] | undefined;
  readonly nonTax?: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();

  const setAmount = (ledgerId: string, amount: string): void =>
    onChange(
      charges.some((charge) => charge.ledgerId === ledgerId)
        ? charges.map((charge) =>
            charge.ledgerId === ledgerId ? { ...charge, amount } : charge,
          )
        : [...charges, { key: crypto.randomUUID(), ledgerId, amount }],
    );

  const total = chargeTotal(charges, ledgers, setup);

  return (
    <section className="panel space-y-3">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold text-ink">{t('documents.charges')}</h3>

        <span className="font-mono text-sm tabular-nums text-ink">
          {total >= 0 ? '+' : '−'}
          {moneyAlways(Math.abs(total))}
          {currency ? ` ${currency}` : ''}
        </span>
      </header>

      <AdditionalLedgerList
        carried={charges}
        setup={setup}
        nonTax={nonTax}
        ledgers={ledgers}
        onAmount={setAmount}
        onRemove={(ledgerId) =>
          onChange(charges.filter((charge) => charge.ledgerId !== ledgerId))
        }
      />
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
  const taxOn = useTaxOn();

  const net = gross - discount;
  const total = net + tax + charges + rounding;

  return (
    <dl className="ms-auto grid w-full max-w-sm grid-cols-[1fr_auto] gap-x-6 gap-y-1.5 rounded-xl border border-line bg-surface-2/60 p-4 shadow-card text-sm">
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

      {(taxOn || tax !== 0) && (
        <Line label={t('documents.tax')} value={moneyAlways(tax)} />
      )}

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
          ruled && 'mt-1.5 border-t border-line/80 pt-1.5',
          strong
            ? 'mt-2 border-t-2 border-line-strong pt-2 text-base font-bold text-ink'
            : 'text-ink-muted',
        )}
      >
        {label}
      </dt>
      <dd
        className={clsx(
          'text-end font-mono tabular-nums',
          ruled && 'mt-1.5 border-t border-line/80 pt-1.5',
          strong
            ? 'mt-2 border-t-2 border-line-strong pt-2 text-base font-bold text-brand-600 dark:text-brand-300'
            : 'text-ink',
        )}
      >
        {value}
      </dd>
    </>
  );
}
