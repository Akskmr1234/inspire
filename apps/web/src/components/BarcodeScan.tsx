import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { IconBarcode } from '@/components/icons';
import { listProducts, type ProductSummary } from '@/lib/products';

/**
 * The product a scanned code names, most exact first.
 *
 * Its own code; then the server's search, which also matches every barcode on the
 * product - taken only when it finds exactly one, so a scan never adds whichever of
 * several products happened to come first.
 */
export async function findScanned(
  scanned: string,
  products: readonly ProductSummary[],
): Promise<ProductSummary | null> {
  const code = scanned.trim();

  if (!code) return null;

  const byCode = products.find(
    (product) => product.code.toLowerCase() === code.toLowerCase(),
  );

  if (byCode) return byCode;

  try {
    const matches = await listProducts(code, '', false);
    const only = matches.length === 1 ? matches[0] : undefined;

    return only ? (products.find((product) => product.id === only.id) ?? only) : null;
  } catch {
    return null;
  }
}

/**
 * One more of a scanned product: on the line that already has it, else on the first
 * empty line, else on a new one.
 *
 * @returns The lines, and the quantity the product now stands at.
 */
export function addScanned<
  TLine extends { readonly productId: string; readonly quantity: string },
>(
  lines: readonly TLine[],
  product: ProductSummary,
  fresh: (product: ProductSummary) => TLine,
): { readonly lines: readonly TLine[]; readonly quantity: number } {
  const existing = lines.findIndex((line) => line.productId === product.id);

  if (existing >= 0) {
    const quantity = (Number(lines[existing]?.quantity) || 0) + 1;

    return {
      lines: lines.map((line, at) =>
        at === existing ? { ...line, quantity: String(quantity) } : line,
      ),
      quantity,
    };
  }

  const line = { ...fresh(product), quantity: '1' };
  const empty = lines.findIndex((candidate) => candidate.productId === '');

  return {
    lines:
      empty >= 0
        ? lines.map((candidate, at) => (at === empty ? line : candidate))
        : [...lines, line],
    quantity: 1,
  };
}

/**
 * A box a barcode scanner types into.
 *
 * A scanner is a keyboard that types the code and presses Enter, so Enter is the scan.
 * What it finds is handed to `onScan`, which puts it on the document and says what
 * quantity it now stands at; the box empties and keeps the focus for the next one.
 */
export function BarcodeScan({
  products,
  onScan,
  disabled = false,
}: {
  readonly products: readonly ProductSummary[];
  /**
   * Adds the product, and returns the quantity of it the document now holds - or,
   * where this document cannot take it, the reason why.
   */
  readonly onScan: (product: ProductSummary) => number | string;
  readonly disabled?: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [code, setCode] = useState('');
  const [notice, setNotice] = useState<{
    readonly text: string;
    readonly found: boolean;
  } | null>(null);
  const timer = useRef<number | undefined>(undefined);
  // The lookup can wait on the server, so the document it lands on is the one on
  // screen by then, not the one there was when the code was scanned.
  const latest = useRef(onScan);
  latest.current = onScan;

  const say = (text: string, found: boolean): void => {
    window.clearTimeout(timer.current);
    setNotice({ text, found });
    timer.current = window.setTimeout(() => setNotice(null), 3000);
  };

  const scan = async (): Promise<void> => {
    const scanned = code.trim();

    if (!scanned) return;

    setCode('');
    const product = await findScanned(scanned, products);

    if (product) {
      const added = latest.current(product);

      if (typeof added === 'string') {
        say(added, false);
      } else {
        say(t('stock.barcodeAdded', { code: product.code, qty: added }), true);
      }
    } else {
      say(t('stock.barcodeNotFound', { barcode: scanned }), false);
    }
  };

  return (
    <div className="flex flex-wrap items-center gap-2 rounded-xl border border-line bg-surface-2/60 p-2">
      <IconBarcode className="size-5 shrink-0 text-ink-muted" />
      <input
        type="text"
        value={code}
        disabled={disabled}
        onChange={(event) => setCode(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.preventDefault();
            void scan();
          }
        }}
        aria-label={t('stock.barcodeScanner')}
        placeholder={t('stock.barcodeScanPlaceholder')}
        className="field-input min-w-40 flex-1"
      />
      <button
        type="button"
        disabled={disabled || code.trim() === ''}
        onClick={() => void scan()}
        className="btn-secondary"
      >
        {t('stock.scan')}
      </button>
      {notice && (
        <span
          role="status"
          className={
            notice.found
              ? 'text-xs font-medium text-brand-700 dark:text-brand-300'
              : 'text-xs font-medium text-red-700 dark:text-red-300'
          }
        >
          {notice.text}
        </span>
      )}
    </div>
  );
}
