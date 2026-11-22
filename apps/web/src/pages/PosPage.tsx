import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import clsx from 'clsx';
import { ApiError } from '@/lib/api';
import { BarcodeScan } from '@/components/BarcodeScan';
import { ConfirmPrompt } from '@/components/ConfirmPrompt';
import { Modal } from '@/components/Modal';
import { PageHeading } from '@/components/PageHeading';
import { PrintCenter } from '@/components/print/PrintCenter';
import { TouchProductGrid } from '@/components/product/TouchProductGrid';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import { discardHeldSale, holdSale, takeHeldSale, useHeldSales } from '@/lib/heldSales';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { LedgerKind, listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { moneyAlways } from '@/lib/money';
import { fetchProductThumbnails } from '@/lib/photos';
import {
  closePosDay,
  closePosShift,
  fetchPosDay,
  fetchPosStatus,
  openPosShift,
  type PosDay,
  type PosShift,
  type PosStatus,
  type PosTakings,
} from '@/lib/pos';
import { printableSale } from '@/lib/printing';
import { listProducts, type ProductSummary } from '@/lib/products';
import {
  createSalesInvoice,
  getSalesInvoice,
  listSalesInvoices,
  SalesDocumentKind,
  SalesPaymentMode,
  type SalesInvoiceDetail,
  type SalesInvoiceSummary,
} from '@/lib/sales';
import { useSession } from '@/stores/session';
import { useSettings, useTaxOn } from '@/stores/settings';

/** A line on the bill at the counter. */
interface PosLine {
  readonly productId: string;
  readonly quantity: number;
  readonly rate: number;
  readonly discount: number;
  readonly taxPercentage: number;
}

/** A bill as it is held: the lines and who it is for. */
interface PosDraft {
  readonly returning: boolean;
  readonly lines: readonly PosLine[];
  readonly customerName: string;
  readonly customerMobile: string;
}

type Payment = 'cash' | 'card' | 'upi' | 'online';

/** Held POS bills are kept apart from the sales window's: their drafts differ. */
const HELD_SALE = 101;
const HELD_RETURN = 102;

/**
 * The point of sale: a cashier's counter.
 *
 * Only what a cashier uses: scan or tap products, take payment, print. The shift wraps
 * it - opened with the float, closed with the count - and the day is closed once every
 * shift is. Bills are ordinary sales invoices and returns, posted as they are paid.
 *
 * Keys: F2 new bill, F4 scan, F7 hold, F8 held bills, F9 reprint the last bill,
 * F10 pay.
 */
export function PosPage(): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const settings = useSettings();
  const taxOn = useTaxOn();
  const can = useSession((state) => state.can);
  const who = useSession((state) => state.displayName);

  const status = useQuery<PosStatus, ApiError>({
    queryKey: ['pos-status'],
    queryFn: fetchPosStatus,
  });
  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', 'pos'],
    queryFn: () => listProducts('', '', false),
    staleTime: 60_000,
  });
  const customers = useQuery<readonly CustomerSummary[], ApiError>({
    queryKey: ['customers', 'picker'],
    queryFn: () => listCustomers('', true),
  });
  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });
  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers', 'active'],
    queryFn: () => listLedgers(true),
  });
  const thumbnails = useQuery<Readonly<Record<string, string>>, ApiError>({
    queryKey: ['product-thumbnails'],
    queryFn: fetchProductThumbnails,
    enabled: settings.posShowCatalog,
    staleTime: 5 * 60_000,
  });

  const [returning, setReturning] = useState(false);
  const [lines, setLines] = useState<readonly PosLine[]>([]);
  const [customerName, setCustomerName] = useState('');
  const [customerMobile, setCustomerMobile] = useState('');
  const [payment, setPayment] = useState<Payment>(settings.posDefaultPayment);
  const [onlineMethod, setOnlineMethod] = useState('');
  const [tendered, setTendered] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [dialog, setDialog] = useState<
    'history' | 'held' | 'closeShift' | 'dayClose' | null
  >(null);
  const [printing, setPrinting] = useState<SalesInvoiceDetail | null>(null);
  const [asking, setAsking] = useState(false);
  const lastBill = useRef<string | null>(null);
  const scanBox = useRef<HTMLDivElement>(null);

  const productsById = useMemo(
    () => new Map((products.data ?? []).map((product) => [product.id, product])),
    [products.data],
  );
  const sellable = useMemo(
    () => (products.data ?? []).filter((product) => !product.hideInPos),
    [products.data],
  );

  const walkIn = useMemo(
    () =>
      (customers.data ?? []).find(
        (customer) =>
          (settings.walkInCustomerId &&
            customer.customerId === settings.walkInCustomerId) ||
          customer.isPrimary === true,
      ),
    [customers.data, settings.walkInCustomerId],
  );
  const warehouseId =
    settings.preferredWarehouseId ||
    (warehouses.data?.find((warehouse) => warehouse.isDefault) ?? warehouses.data?.[0])
      ?.id ||
    '';

  const shift = status.data?.shift ?? null;
  const today = status.data?.today ?? new Date().toISOString().slice(0, 10);

  // The bill's figures, worked out as the sales window does: rate less discount, then tax.
  const totals = useMemo(() => {
    let items = 0;
    let taxable = 0;
    let tax = 0;
    for (const line of lines) {
      const net = Math.max(0, line.quantity * line.rate - line.discount);
      items += line.quantity;
      taxable += net;
      tax += (net * line.taxPercentage) / 100;
    }
    return { items, taxable, tax, total: Math.round((taxable + tax) * 100) / 100 };
  }, [lines]);

  const tenderedAmount = Number(tendered) || 0;
  const change =
    payment === 'cash' && tenderedAmount > 0 ? tenderedAmount - totals.total : 0;

  const clear = useCallback((): void => {
    setLines([]);
    setCustomerName('');
    setCustomerMobile('');
    setTendered('');
    setOnlineMethod('');
    setPayment(settings.posDefaultPayment);
    setError(null);
  }, [settings.posDefaultPayment]);

  /** Adds one of a product, or one more of it. Returns what the bill now holds. */
  const add = useCallback(
    (product: ProductSummary): number => {
      let held = 1;
      setLines((current) => {
        const existing = current.find((line) => line.productId === product.id);
        if (existing) {
          held = existing.quantity + 1;
          return current.map((line) =>
            line.productId === product.id
              ? { ...line, quantity: line.quantity + 1 }
              : line,
          );
        }
        return [
          ...current,
          {
            productId: product.id,
            quantity: 1,
            rate: product.retailRate,
            discount: 0,
            taxPercentage: taxOn ? (product.taxPercentage ?? settings.defaultTaxRate) : 0,
          },
        ];
      });
      setError(null);
      return held;
    },
    [settings.defaultTaxRate, taxOn],
  );

  const updateLine = (productId: string, patch: Partial<PosLine>): void =>
    setLines((current) =>
      current.map((line) =>
        line.productId === productId ? { ...line, ...patch } : line,
      ),
    );

  const hold = useCallback((): void => {
    if (lines.length === 0) return;
    holdSale<PosDraft>({
      kind: returning ? HELD_RETURN : HELD_SALE,
      party: customerName.trim() || walkIn?.name || t('pos.walkIn'),
      items: lines.length,
      total: totals.total,
      draft: { returning, lines, customerName, customerMobile },
    });
    clear();
    setNotice(t('pos.held'));
  }, [clear, customerMobile, customerName, lines, returning, t, totals.total, walkIn]);

  const retrieve = (id: string): void => {
    const held = takeHeldSale<PosDraft>(id);
    if (!held) return;
    if (lines.length > 0) hold();
    setReturning(held.draft.returning);
    setLines(held.draft.lines);
    setCustomerName(held.draft.customerName);
    setCustomerMobile(held.draft.customerMobile);
    setDialog(null);
  };

  /** The account the money lands in, for the way it was paid. */
  const receivedInto = (mode: Payment): string | null => {
    const all = ledgers.data ?? [];
    const first = (kind: number): string | undefined =>
      all.find((ledger) => ledger.kind === kind)?.ledgerId;
    if (mode === 'cash')
      return settings.defaultCashLedgerId || first(LedgerKind.cash) || null;
    if (mode === 'card')
      return settings.defaultBankLedgerId || first(LedgerKind.bank) || null;
    const methodName = mode === 'upi' ? settings.posUpiMethod : onlineMethod;
    const type = settings.onlinePaymentTypes.find((entry) => entry.name === methodName);
    return (
      type?.ledgerId || settings.defaultBankLedgerId || first(LedgerKind.bank) || null
    );
  };

  const pay = useMutation<SalesInvoiceDetail, ApiError>({
    mutationFn: async () => {
      if (!walkIn) throw new ApiError(422, 'Pos.NoCustomer', t('pos.noWalkIn'));
      const mode =
        payment === 'cash'
          ? SalesPaymentMode.cash
          : payment === 'card'
            ? SalesPaymentMode.card
            : SalesPaymentMode.online;
      const method =
        payment === 'upi'
          ? settings.posUpiMethod
          : payment === 'online'
            ? onlineMethod
            : '';

      const header = await createSalesInvoice({
        date: today,
        customerLedgerId: walkIn.customerId,
        warehouseId,
        kind: returning ? SalesDocumentKind.return : SalesDocumentKind.invoice,
        lines: lines.map((line) => ({
          productId: line.productId,
          quantity: line.quantity,
          rate: line.rate,
          discount: line.discount,
          taxPercentage: line.taxPercentage,
        })),
        paymentMode: mode,
        onlinePaymentType: method || null,
        billingName: customerName.trim() || null,
        billingMobile: customerMobile.trim() || null,
        ...(returning
          ? { amountReceived: 0, paidInFull: false, receivedIntoLedgerId: null }
          : {
              amountReceived: 0,
              paidInFull: true,
              receivedIntoLedgerId: receivedInto(payment),
            }),
        post: true,
      });
      return getSalesInvoice(header.salesInvoiceId);
    },
    onSuccess: async (bill) => {
      lastBill.current = bill.header.salesInvoiceId;
      const total = moneyAlways(bill.header.total);
      clear();
      setNotice(
        payment === 'cash' && change > 0
          ? t('pos.paidWithChange', {
              number: bill.header.number,
              total,
              change: moneyAlways(change),
            })
          : t('pos.paid', { number: bill.header.number, total }),
      );
      if (settings.posAutoPrint) setPrinting(bill);
      await queryClient.invalidateQueries({ queryKey: ['pos-status'] });
      await queryClient.invalidateQueries({ queryKey: ['pos-history'] });
    },
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const canPay =
    shift !== null &&
    !status.data?.dayClosed &&
    lines.length > 0 &&
    warehouseId !== '' &&
    !pay.isPending &&
    (payment !== 'online' ||
      onlineMethod !== '' ||
      settings.onlinePaymentTypes.length === 0) &&
    (payment !== 'cash' ||
      returning ||
      tenderedAmount === 0 ||
      tenderedAmount >= totals.total);

  const requestPay = useCallback((): void => {
    if (!canPay) return;
    if (settings.posAskBeforeSave) setAsking(true);
    else pay.mutate();
  }, [canPay, pay, settings.posAskBeforeSave]);

  const reprintLast = useCallback(async (): Promise<void> => {
    if (!lastBill.current) return;
    setPrinting(await getSalesInvoice(lastBill.current));
  }, []);

  // The counter's keys. Function keys only, so typing in a box is never taken.
  useEffect(() => {
    const keys = (event: KeyboardEvent): void => {
      if (dialog || printing || asking) return;
      const handled: Record<string, () => void> = {
        F2: clear,
        F4: () => scanBox.current?.querySelector<HTMLInputElement>('input')?.focus(),
        F7: hold,
        F8: () => setDialog('held'),
        F9: () => void reprintLast(),
        F10: requestPay,
      };
      const run = handled[event.key];
      if (run) {
        event.preventDefault();
        run();
      }
    };
    window.addEventListener('keydown', keys);
    return () => window.removeEventListener('keydown', keys);
  }, [asking, clear, dialog, hold, printing, reprintLast, requestPay]);

  const heldSales = useHeldSales<PosDraft>(HELD_SALE);
  const heldReturns = useHeldSales<PosDraft>(HELD_RETURN);
  const heldCount = heldSales.length + heldReturns.length;
  const onlineChoices = settings.onlinePaymentTypes.filter(
    (type) => type.active && type.name.trim(),
  );

  if (status.isPending) {
    return (
      <section className="page">
        <PageHeading title={t('pos.title')} />
        <p className="text-sm text-ink-muted">{t('common.loading')}</p>
      </section>
    );
  }

  return (
    <section className="page gap-3">
      <PageHeading
        title={t('pos.title')}
        subtitle={
          shift
            ? t('pos.shiftSubtitle', {
                who: who ?? '',
                time: new Date(shift.openedAtUtc).toLocaleTimeString([], {
                  hour: '2-digit',
                  minute: '2-digit',
                }),
              })
            : undefined
        }
      />

      {/* The counter's own bar: everything a cashier reaches for, one tap away. */}
      <div className="flex flex-wrap items-center gap-1.5 rounded-xl border border-line bg-surface p-2">
        <ToolbarButton label={t('pos.history')} onClick={() => setDialog('history')} />
        <ToolbarButton
          label={`${t('pos.heldBills')}${heldCount ? ` (${heldCount})` : ''}`}
          keyName="F8"
          onClick={() => setDialog('held')}
        />
        <ToolbarButton
          label={t('pos.reprint')}
          keyName="F9"
          disabled={!lastBill.current}
          onClick={() => void reprintLast()}
        />
        {shift && (
          <ToolbarButton
            label={t('pos.closeShift')}
            onClick={() => setDialog('closeShift')}
          />
        )}
        {can('sales:pos:approve') && (
          <ToolbarButton
            label={t('pos.dayClose')}
            onClick={() => setDialog('dayClose')}
          />
        )}
        <Link
          to="/settings?tab=pos"
          className="btn-ghost btn-sm text-ink-muted"
          title={t('pos.settings')}
        >
          {t('pos.settings')}
        </Link>
      </div>

      {status.data?.dayClosed && !shift && (
        <p className="alert-warn text-sm">{t('pos.dayClosedNotice')}</p>
      )}

      {!shift && !status.data?.dayClosed && (
        <OpenShiftPanel
          onOpened={async () => {
            await queryClient.invalidateQueries({ queryKey: ['pos-status'] });
          }}
        />
      )}

      {shift && (
        <div className="grid min-h-0 gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(22rem,26rem)]">
          {/* Left: what is being sold - scan, or tap. */}
          <div className="flex min-w-0 flex-col gap-3">
            <div className="flex flex-wrap items-center gap-3 rounded-xl border border-line bg-surface p-3">
              <label className="flex items-center gap-2 text-sm font-medium text-ink">
                <input
                  type="checkbox"
                  checked={!returning}
                  onChange={() => setReturning(false)}
                  className="size-4 accent-brand-600"
                />
                {t('pos.modeSales')}
              </label>
              <label
                className={clsx(
                  'flex items-center gap-2 text-sm font-medium',
                  settings.posAllowReturns ? 'text-ink' : 'text-ink-subtle',
                )}
              >
                <input
                  type="checkbox"
                  checked={returning}
                  disabled={!settings.posAllowReturns}
                  onChange={() => setReturning(true)}
                  className="size-4 accent-red-600"
                />
                {t('pos.modeReturn')}
              </label>
              {returning && (
                <span className="rounded-full bg-red-500/10 px-2 py-0.5 text-xs font-semibold text-red-700 dark:text-red-300">
                  {t('pos.returnBanner')}
                </span>
              )}
            </div>

            <div ref={scanBox}>
              <BarcodeScan
                products={products.data ?? []}
                onScan={(product) => add(product)}
                asField
              />
            </div>

            {settings.posShowCatalog && (
              <TouchProductGrid
                products={sellable}
                onSelectProduct={(product) => add(product)}
                quantitiesInCart={
                  new Map(lines.map((line) => [line.productId, line.quantity]))
                }
                thumbnails={thumbnails.data ?? {}}
                className="min-h-[20rem] flex-1"
              />
            )}
          </div>

          {/* Right: the bill and the payment. */}
          <div className="flex min-w-0 flex-col gap-3 rounded-2xl border border-line bg-surface p-3 shadow-card">
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-bold tracking-tight text-ink">
                {returning ? t('pos.returnBill') : t('pos.bill')}
              </h2>
              <span className="text-xs text-ink-muted">
                {t('pos.itemsCount', { count: totals.items })}
              </span>
            </div>

            <div className="grid grid-cols-2 gap-2">
              <input
                value={customerName}
                onChange={(event) => setCustomerName(event.target.value)}
                placeholder={t('sales.customerName')}
                aria-label={t('sales.customerName')}
                maxLength={120}
                className="field-input-sm"
              />
              <input
                value={customerMobile}
                onChange={(event) => setCustomerMobile(event.target.value)}
                placeholder={t('sales.customerMobile')}
                aria-label={t('sales.customerMobile')}
                inputMode="tel"
                maxLength={20}
                className="field-input-sm"
              />
            </div>

            <ul className="max-h-[40vh] min-h-32 divide-y divide-line overflow-y-auto rounded-xl border border-line">
              {lines.length === 0 && (
                <li className="p-6 text-center text-sm text-ink-muted">
                  {t('pos.emptyBill')}
                </li>
              )}
              {lines.map((line) => {
                const product = productsById.get(line.productId);
                const lineTotal =
                  Math.max(0, line.quantity * line.rate - line.discount) *
                  (1 + line.taxPercentage / 100);
                return (
                  <li
                    key={line.productId}
                    className="flex items-center gap-2 px-2.5 py-2"
                  >
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-medium text-ink">
                        {product?.description ?? '—'}
                      </p>
                      <div className="flex items-center gap-2 text-xs text-ink-muted">
                        {settings.posAllowRateChange ? (
                          <input
                            type="number"
                            min="0"
                            step="any"
                            value={line.rate}
                            aria-label={t('sales.rate')}
                            onChange={(event) =>
                              updateLine(line.productId, {
                                rate: Number(event.target.value) || 0,
                              })
                            }
                            className="field-input-sm h-6 w-20 text-end"
                          />
                        ) : (
                          <span className="font-mono">{moneyAlways(line.rate)}</span>
                        )}
                        {settings.posAllowDiscount && (
                          <input
                            type="number"
                            min="0"
                            step="any"
                            value={line.discount || ''}
                            placeholder={t('pos.discount')}
                            aria-label={t('pos.discount')}
                            onChange={(event) =>
                              updateLine(line.productId, {
                                discount: Number(event.target.value) || 0,
                              })
                            }
                            className="field-input-sm h-6 w-20 text-end"
                          />
                        )}
                      </div>
                    </div>
                    <div className="flex items-center gap-1">
                      <button
                        type="button"
                        aria-label={t('pos.less')}
                        onClick={() =>
                          line.quantity <= 1
                            ? setLines((current) =>
                                current.filter(
                                  (entry) => entry.productId !== line.productId,
                                ),
                              )
                            : updateLine(line.productId, { quantity: line.quantity - 1 })
                        }
                        className="btn-secondary size-7 p-0"
                      >
                        −
                      </button>
                      <input
                        type="number"
                        min="0"
                        step="any"
                        value={line.quantity}
                        aria-label={t('sales.quantity')}
                        onChange={(event) =>
                          updateLine(line.productId, {
                            quantity: Number(event.target.value) || 0,
                          })
                        }
                        className="field-input-sm h-7 w-14 text-center font-mono"
                      />
                      <button
                        type="button"
                        aria-label={t('pos.more')}
                        onClick={() =>
                          updateLine(line.productId, { quantity: line.quantity + 1 })
                        }
                        className="btn-secondary size-7 p-0"
                      >
                        +
                      </button>
                    </div>
                    <span className="w-20 text-end font-mono text-sm font-semibold text-ink tabular-nums">
                      {moneyAlways(lineTotal)}
                    </span>
                  </li>
                );
              })}
            </ul>

            <dl className="space-y-1 text-sm">
              <div className="flex justify-between text-ink-muted">
                <dt>{t('pos.subtotal')}</dt>
                <dd className="font-mono tabular-nums">{moneyAlways(totals.taxable)}</dd>
              </div>
              {totals.tax > 0 && (
                <div className="flex justify-between text-ink-muted">
                  <dt>{t('pos.tax')}</dt>
                  <dd className="font-mono tabular-nums">{moneyAlways(totals.tax)}</dd>
                </div>
              )}
              <div className="flex items-baseline justify-between border-t border-line pt-1.5">
                <dt className="font-semibold text-ink">
                  {returning ? t('pos.refund') : t('pos.total')}
                </dt>
                <dd className="font-mono text-2xl font-bold text-brand-600 tabular-nums dark:text-brand-300">
                  {moneyAlways(totals.total)}
                </dd>
              </div>
            </dl>

            <div
              className="grid grid-cols-4 gap-1.5"
              role="radiogroup"
              aria-label={t('pos.payment')}
            >
              {(['cash', 'card', 'upi', 'online'] as const).map((mode) => (
                <button
                  key={mode}
                  type="button"
                  role="radio"
                  aria-checked={payment === mode}
                  onClick={() => setPayment(mode)}
                  className={clsx(
                    'rounded-lg border px-2 py-2 text-sm font-semibold transition',
                    payment === mode
                      ? 'border-brand-600 bg-brand-500/10 text-brand-700 ring-1 ring-brand-600 dark:text-brand-300'
                      : 'border-line bg-surface text-ink hover:bg-surface-2',
                  )}
                >
                  {t(`pos.${mode}`)}
                </button>
              ))}
            </div>

            {payment === 'online' && onlineChoices.length > 0 && (
              <select
                value={onlineMethod}
                onChange={(event) => setOnlineMethod(event.target.value)}
                aria-label={t('vouchers.onlineMethod')}
                className="field-input-sm"
              >
                <option value="">{t('sales.chooseOnlineType')}</option>
                {onlineChoices.map((type) => (
                  <option key={type.id} value={type.name}>
                    {type.name}
                  </option>
                ))}
              </select>
            )}

            {payment === 'cash' && !returning && (
              <div className="grid grid-cols-2 items-end gap-2">
                <label className="block">
                  <span className="field-label">{t('pos.tendered')}</span>
                  <input
                    type="number"
                    min="0"
                    step="any"
                    value={tendered}
                    onChange={(event) => setTendered(event.target.value)}
                    className="field-input text-end font-mono"
                  />
                </label>
                <div className="rounded-lg bg-surface-2 px-3 py-2 text-end">
                  <span className="block text-xs text-ink-muted">{t('pos.change')}</span>
                  <span
                    className={clsx(
                      'font-mono text-lg font-bold tabular-nums',
                      change < 0 ? 'text-red-600' : 'text-emerald-600',
                    )}
                  >
                    {moneyAlways(Math.max(change, 0))}
                  </span>
                </div>
              </div>
            )}

            {error && (
              <p role="alert" className="alert-error text-sm">
                {error}
              </p>
            )}
            {notice && !error && (
              <p role="status" className="alert-success text-sm">
                {notice}
              </p>
            )}

            <div className="grid grid-cols-3 gap-2">
              <button type="button" onClick={clear} className="btn-secondary">
                {t('pos.newBill')}{' '}
                <kbd className="ms-1 text-[10px] text-ink-muted">F2</kbd>
              </button>
              <button
                type="button"
                onClick={hold}
                disabled={lines.length === 0}
                className="btn-secondary"
              >
                {t('pos.hold')} <kbd className="ms-1 text-[10px] text-ink-muted">F7</kbd>
              </button>
              <button
                type="button"
                onClick={requestPay}
                disabled={!canPay}
                className={clsx(
                  returning ? 'btn-danger' : 'btn-primary',
                  'py-3 text-base',
                )}
              >
                {pay.isPending
                  ? t('common.saving')
                  : returning
                    ? t('pos.refundNow')
                    : t('pos.pay')}
                <kbd className="ms-1 text-[10px] opacity-80">F10</kbd>
              </button>
            </div>
          </div>
        </div>
      )}

      {asking && (
        <ConfirmPrompt
          title={t('common.confirmSave')}
          body={`${returning ? t('pos.refund') : t('pos.total')}: ${moneyAlways(totals.total)}`}
          onConfirm={() => {
            setAsking(false);
            pay.mutate();
          }}
          onCancel={() => setAsking(false)}
        />
      )}

      {dialog === 'held' && (
        <Modal title={t('pos.heldBills')} size="form" onClose={() => setDialog(null)}>
          {heldCount === 0 ? (
            <p className="text-sm text-ink-muted">{t('pos.noneHeld')}</p>
          ) : (
            <ul className="divide-y divide-line rounded-xl border border-line">
              {[...heldSales, ...heldReturns].map((held) => (
                <li key={held.id} className="flex items-center gap-3 px-3 py-2 text-sm">
                  <span className="min-w-0 flex-1">
                    <span className="font-medium text-ink">{held.party}</span>
                    <span className="block text-xs text-ink-muted">
                      {new Date(held.heldAt).toLocaleTimeString()} ·{' '}
                      {t('pos.itemsCount', { count: held.items })}
                      {held.kind === HELD_RETURN ? ` · ${t('pos.modeReturn')}` : ''}
                    </span>
                  </span>
                  <span className="font-mono font-semibold">
                    {moneyAlways(held.total)}
                  </span>
                  <button
                    type="button"
                    onClick={() => retrieve(held.id)}
                    className="btn-primary btn-sm"
                  >
                    {t('pos.retrieve')}
                  </button>
                  <button
                    type="button"
                    onClick={() => discardHeldSale(held.id)}
                    className="btn-ghost btn-sm text-red-600"
                  >
                    {t('common.remove')}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Modal>
      )}

      {dialog === 'history' && (
        <HistoryDialog
          date={today}
          who={who}
          onReprint={async (id) => {
            setDialog(null);
            setPrinting(await getSalesInvoice(id));
          }}
          onClose={() => setDialog(null)}
        />
      )}

      {dialog === 'closeShift' && shift && (
        <CloseShiftDialog
          shift={shift}
          onClosed={async () => {
            setDialog(null);
            clear();
            await queryClient.invalidateQueries({ queryKey: ['pos-status'] });
          }}
          onCancel={() => setDialog(null)}
        />
      )}

      {dialog === 'dayClose' && (
        <DayCloseDialog
          date={today}
          onClosed={async () => {
            await queryClient.invalidateQueries({ queryKey: ['pos-status'] });
          }}
          onCancel={() => setDialog(null)}
        />
      )}

      {printing && (
        <Modal title={t('pos.printBill')} onClose={() => setPrinting(null)}>
          <PrintCenter
            document={printableSale(
              printing,
              {
                customers: customers.data ?? [],
                products: products.data ?? [],
                warehouses: warehouses.data ?? [],
              },
              {
                taxable: t('printing.taxable'),
                tax: t('printing.taxTotal'),
                charges: t('printing.charges'),
                roundOff: t('printing.roundOff'),
              },
            )}
            onDone={() => setPrinting(null)}
          />
        </Modal>
      )}
    </section>
  );
}

function ToolbarButton({
  label,
  keyName,
  onClick,
  disabled = false,
}: {
  readonly label: string;
  readonly keyName?: string;
  readonly onClick: () => void;
  readonly disabled?: boolean;
}): React.JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="btn-secondary btn-sm"
    >
      {label}
      {keyName && <kbd className="ms-1 text-[10px] text-ink-muted">{keyName}</kbd>}
    </button>
  );
}

/** Before the first bill: the float the drawer opens with. */
function OpenShiftPanel({
  onOpened,
}: {
  readonly onOpened: () => Promise<void>;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [float, setFloat] = useState('');
  const open = useMutation<PosShift, ApiError>({
    mutationFn: () => openPosShift(Number(float) || 0),
    onSuccess: () => onOpened(),
  });

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        open.mutate();
      }}
      className="card card-body mx-auto w-full max-w-md space-y-4"
    >
      <div>
        <h2 className="text-lg font-bold text-ink">{t('pos.openShiftTitle')}</h2>
        <p className="text-sm text-ink-muted">{t('pos.openShiftHint')}</p>
      </div>
      <label className="block">
        <span className="field-label">{t('pos.openingCash')}</span>
        <input
          type="number"
          min="0"
          step="any"
          value={float}
          autoFocus
          onChange={(event) => setFloat(event.target.value)}
          className="field-input text-end font-mono text-lg"
        />
      </label>
      {open.error && (
        <p role="alert" className="alert-error text-sm">
          {open.error.detail || open.error.code}
        </p>
      )}
      <button
        type="submit"
        disabled={open.isPending}
        className="btn-primary w-full py-2.5"
      >
        {t('pos.openShift')}
      </button>
    </form>
  );
}

/** What a shift or a day took, by how it was paid. */
function TakingsTable({ takings }: { readonly takings: PosTakings }): React.JSX.Element {
  const { t } = useTranslation();
  const rows: readonly (readonly [string, number])[] = [
    [t('pos.sales', { count: takings.salesCount }), takings.salesTotal],
    [t('pos.returns', { count: takings.returnsCount }), -takings.returnsTotal],
    [t('pos.cash'), takings.cashTotal],
    [t('pos.card'), takings.cardTotal],
    [t('pos.online'), takings.onlineTotal],
    [t('pos.credit'), takings.creditTotal],
  ];
  return (
    <dl className="divide-y divide-line rounded-xl border border-line text-sm">
      {rows.map(([label, value]) => (
        <div key={label} className="flex justify-between px-3 py-1.5">
          <dt className="text-ink-muted">{label}</dt>
          <dd className="font-mono tabular-nums">{moneyAlways(value)}</dd>
        </div>
      ))}
      <div className="flex justify-between px-3 py-2 font-semibold">
        <dt>{t('pos.net')}</dt>
        <dd className="font-mono tabular-nums">{moneyAlways(takings.netTotal)}</dd>
      </div>
    </dl>
  );
}

function CloseShiftDialog({
  shift,
  onClosed,
  onCancel,
}: {
  readonly shift: PosShift;
  readonly onClosed: () => Promise<void>;
  readonly onCancel: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [counted, setCounted] = useState('');
  const [notes, setNotes] = useState('');
  const [closed, setClosed] = useState<PosShift | null>(null);
  const close = useMutation<PosShift, ApiError>({
    mutationFn: () =>
      closePosShift(shift.shiftId, Number(counted) || 0, notes.trim() || null),
    onSuccess: (result) => setClosed(result),
  });
  const view = closed ?? shift;
  const difference = (Number(counted) || 0) - view.expectedCash;

  return (
    <Modal
      title={t('pos.closeShift')}
      size="form"
      onClose={() => (closed ? void onClosed() : onCancel())}
    >
      <TakingsTable takings={view.takings} />
      <dl className="grid grid-cols-2 gap-2 text-sm">
        <div className="rounded-lg bg-surface-2 p-3">
          <dt className="text-xs text-ink-muted">{t('pos.openingCash')}</dt>
          <dd className="font-mono font-semibold">{moneyAlways(view.openingCash)}</dd>
        </div>
        <div className="rounded-lg bg-surface-2 p-3">
          <dt className="text-xs text-ink-muted">{t('pos.expectedCash')}</dt>
          <dd className="font-mono font-semibold">{moneyAlways(view.expectedCash)}</dd>
        </div>
      </dl>
      {closed ? (
        <div className="space-y-3">
          <p className="alert-success text-sm">
            {t('pos.shiftClosed', {
              counted: moneyAlways(closed.countedCash ?? 0),
              difference: moneyAlways((closed.countedCash ?? 0) - closed.expectedCash),
            })}
          </p>
          <div className="form-actions">
            <button type="button" onClick={() => void onClosed()} className="btn-primary">
              {t('common.close')}
            </button>
          </div>
        </div>
      ) : (
        <form
          data-no-confirm=""
          onSubmit={(event) => {
            event.preventDefault();
            close.mutate();
          }}
          className="space-y-3"
        >
          <label className="block">
            <span className="field-label">{t('pos.countedCash')}</span>
            <input
              type="number"
              min="0"
              step="any"
              required
              value={counted}
              autoFocus
              onChange={(event) => setCounted(event.target.value)}
              className="field-input text-end font-mono text-lg"
            />
          </label>
          {counted !== '' && (
            <p
              className={clsx(
                'text-sm',
                difference === 0 ? 'text-emerald-600' : 'text-amber-700',
              )}
            >
              {t('pos.difference', { amount: moneyAlways(difference) })}
            </p>
          )}
          <label className="block">
            <span className="field-label">{t('pos.notes')}</span>
            <textarea
              rows={2}
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
              className="field-input h-auto"
            />
          </label>
          {close.error && (
            <p role="alert" className="alert-error text-sm">
              {close.error.detail || close.error.code}
            </p>
          )}
          <div className="form-actions">
            <button type="submit" disabled={close.isPending} className="btn-primary">
              {t('pos.closeShift')}
            </button>
          </div>
        </form>
      )}
    </Modal>
  );
}

function DayCloseDialog({
  date,
  onClosed,
  onCancel,
}: {
  readonly date: string;
  readonly onClosed: () => Promise<void>;
  readonly onCancel: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [notes, setNotes] = useState('');
  const day = useQuery<PosDay, ApiError>({
    queryKey: ['pos-day', date],
    queryFn: () => fetchPosDay(date),
  });
  const close = useMutation<PosDay, ApiError>({
    mutationFn: () => closePosDay(date, notes.trim() || null),
    onSuccess: async (result) => {
      queryClient.setQueryData(['pos-day', date], result);
      await onClosed();
    },
  });

  return (
    <Modal title={t('pos.dayCloseTitle', { date })} size="form" onClose={onCancel}>
      {day.data && <TakingsTable takings={day.data.takings} />}
      {day.data?.isClosed ? (
        <p className="alert-success text-sm">{t('pos.dayIsClosed')}</p>
      ) : (
        <form
          data-no-confirm=""
          onSubmit={(event) => {
            event.preventDefault();
            close.mutate();
          }}
          className="space-y-3"
        >
          {day.data && day.data.openShifts > 0 && (
            <p className="alert-warn text-sm">
              {t('pos.shiftsStillOpen', { count: day.data.openShifts })}
            </p>
          )}
          <label className="block">
            <span className="field-label">{t('pos.notes')}</span>
            <textarea
              rows={2}
              value={notes}
              onChange={(event) => setNotes(event.target.value)}
              className="field-input h-auto"
            />
          </label>
          {close.error && (
            <p role="alert" className="alert-error text-sm">
              {close.error.detail || close.error.code}
            </p>
          )}
          <div className="form-actions">
            <button
              type="submit"
              disabled={close.isPending || (day.data?.openShifts ?? 0) > 0}
              className="btn-danger"
            >
              {t('pos.dayClose')}
            </button>
          </div>
        </form>
      )}
    </Modal>
  );
}

/** Today's bills by the signed-in cashier, newest first, each one reprintable. */
function HistoryDialog({
  date,
  who,
  onReprint,
  onClose,
}: {
  readonly date: string;
  readonly who: string | null;
  readonly onReprint: (id: string) => Promise<void>;
  readonly onClose: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const bills = useQuery({
    queryKey: ['pos-history', date],
    queryFn: () => listSalesInvoices({ from: date, to: date }, 1, 200),
  });
  const mine: readonly SalesInvoiceSummary[] = (bills.data?.items ?? []).filter(
    (bill) => !who || !bill.enteredByName || bill.enteredByName === who,
  );
  const total = mine.reduce(
    (sum, bill) =>
      sum + (bill.kind === SalesDocumentKind.return ? -bill.total : bill.total),
    0,
  );

  return (
    <Modal title={t('pos.historyTitle')} subtitle={date} onClose={onClose}>
      {bills.isPending && <p className="text-sm text-ink-muted">{t('common.loading')}</p>}
      {bills.data && mine.length === 0 && (
        <p className="text-sm text-ink-muted">{t('pos.noBillsYet')}</p>
      )}
      {mine.length > 0 && (
        <>
          <table className="table">
            <thead>
              <tr>
                <th className="text-start">{t('pos.billNo')}</th>
                <th className="text-start">{t('pos.time')}</th>
                <th className="text-start">{t('pos.customer')}</th>
                <th className="text-end">{t('pos.total')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {mine.map((bill) => (
                <tr key={bill.salesInvoiceId}>
                  <td className="font-mono">
                    {bill.number}
                    {bill.kind === SalesDocumentKind.return && (
                      <span className="ms-1 text-xs text-red-600">
                        {t('pos.modeReturn')}
                      </span>
                    )}
                  </td>
                  <td>{new Date(bill.createdAtUtc).toLocaleTimeString()}</td>
                  <td>{bill.billingName || bill.customerName}</td>
                  <td className="text-end font-mono">{moneyAlways(bill.total)}</td>
                  <td className="text-end">
                    <button
                      type="button"
                      onClick={() => void onReprint(bill.salesInvoiceId)}
                      className="btn-secondary btn-sm"
                    >
                      {t('pos.reprint')}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <p className="report-footer">
            <span>{t('pos.billsCount', { count: mine.length })}</span>
            <span className="ms-auto font-semibold text-ink">
              {t('pos.net')}: {moneyAlways(total)}
            </span>
          </p>
        </>
      )}
    </Modal>
  );
}
