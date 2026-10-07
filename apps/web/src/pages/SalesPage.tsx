import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { DataGrid, GridAction, type GridColumn } from '@/components/DataGrid';
import { Modal, ModalButton } from '@/components/Modal';
import { ReportFrame } from '@/components/ReportFrame';
import { DateField, Field as FormField, SelectField, TextField } from '@/components/Form';
import { SearchSelect } from '@/components/SearchSelect';
import { StatusBadge, type StatusTone } from '@/components/StatusBadge';
import { IconBarcode, IconClose, IconPlus } from '@/components/icons';
import {
  DocumentTotals,
  productOption,
  stockByProduct,
} from '@/components/DocumentLines';
import { listLedgers, type LedgerSummary } from '@/lib/ledgers';
import { fetchStockValuation, type StockValuationReport } from '@/lib/stock';
import { collect, numeric, required, useValidation } from '@/lib/validation';
import { useSettings } from '@/stores/settings';
import type { ApiError } from '@/lib/api';
import { listCustomers, type CustomerSummary } from '@/lib/customers';
import { listMaster, type WarehouseSummary } from '@/lib/inventory';
import { listProducts, type ProductSummary } from '@/lib/products';
import {
  fetchProductBatches,
  fetchProductSerials,
  type BatchStockRow,
  type SerialNumberView,
} from '@/lib/stock';
import {
  cancelSalesInvoice,
  createSalesInvoice,
  getSalesInvoice,
  isReturn,
  listSalesInvoices,
  postSalesInvoice,
  SalesDocumentKind,
  SalesInvoiceStatus,
  type PagedResult,
  type SalesInvoiceDetail,
  type SalesInvoiceSummary,
  type SalesLineInput,
  type SalesChargeInput,
} from '@/lib/sales';
import { moneyAlways } from '@/lib/money';

const PAGE_SIZE = 25;

/** A line as the screen holds it, before it is worth sending. */
interface DraftLine {
  productId: string;
  quantity: string;
  freeQuantity?: string;
  rate: string;
  inclusiveRate?: string;
  taxPercentage: string;
  discount: string;
  /** The batch sold, where the product is tracked in batches. */
  batchNumber: string;
  expiresOn?: string;
  /** The units sold, where the product is tracked by serial number. */
  serialNumbers: readonly string[];
}

const emptyLine: DraftLine = {
  productId: '',
  quantity: '1',
  freeQuantity: '0',
  rate: '',
  inclusiveRate: '',
  taxPercentage: '0',
  discount: '0',
  batchNumber: '',
  expiresOn: '',
  serialNumbers: [],
};

/**
 * Sales invoices and credit notes.
 *
 * One screen for both, because they are one kind of document: an invoice and a return
 * differ in which way the goods and the money move and not at all in shape, so two
 * screens would be two copies of the same grid and the second would drift. The kind is
 * chosen when a document is entered and shown as a badge in the list.
 *
 * Entering and posting are separate here as they are on the server. A draft has moved
 * nothing and can be abandoned; posting issues the stock, raises or credits the debt and
 * writes the books in one transaction, and what it produced is reported back rather than
 * left to be looked up.
 */
export function SalesPage({
  kind = SalesDocumentKind.invoice,
}: {
  readonly kind?: number;
} = {}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  const isSalesReturn = isReturn(kind);
  const today = new Date().toISOString().slice(0, 10);
  const monthStart = `${today.slice(0, 7)}-01`;

  const [from, setFrom] = useState(monthStart);
  const [to, setTo] = useState(today);
  const kindFilter = isSalesReturn ? SalesDocumentKind.return : SalesDocumentKind.invoice;
  const [statusFilter, setStatusFilter] = useState<number | ''>('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const [entering, setEntering] = useState(false);
  const [viewing, setViewing] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const filter = { from, to, kind: kindFilter, status: statusFilter, search };

  const query = useQuery<PagedResult<SalesInvoiceSummary>, ApiError>({
    queryKey: ['sales-invoices', from, to, kindFilter, statusFilter, search, page],
    queryFn: () => listSalesInvoices(filter, page, PAGE_SIZE),
  });

  const nextInvoiceNo = useMemo(() => {
    const items = query.data?.items ?? [];
    const prefix = isSalesReturn ? 'RET-' : 'INV-';
    if (items.length === 0) return `${prefix}1001`;
    const nums = items
      .map((item) => {
        const match = item.number.match(/\d+/);
        return match ? parseInt(match[0], 10) : 0;
      })
      .filter((n) => !isNaN(n));
    const maxNum = nums.length > 0 ? Math.max(...nums) : 1000;
    return `${prefix}${String(maxNum + 1).padStart(4, '0')}`;
  }, [query.data?.items, isSalesReturn]);

  const mutation = useMutation<string | null, ApiError, () => Promise<string | null>>({
    mutationFn: (action) => action(),
    onSuccess: async (message) => {
      setError(null);
      setNotice(message);
      window.setTimeout(() => setNotice(null), 5000);

      await queryClient.invalidateQueries({ queryKey: ['sales-invoices'] });
      await queryClient.invalidateQueries({ queryKey: ['sales-invoice'] });
    },
    // The server owns the rules — short stock, a missing tax account, a bill somebody
    // has paid against — so its own message is shown rather than one guessed at here.
    onError: (failure) => setError(failure.detail || failure.code),
  });

  const run = (action: () => Promise<string | null>): void => mutation.mutate(action);

  // Any change of filter goes back to the first page: staying on page four of a list
  // that now has two would show an empty screen and look broken.
  const narrow =
    <T,>(set: (value: T) => void) =>
    (value: T): void => {
      set(value);
      setPage(1);
    };

  const columns: readonly GridColumn<SalesInvoiceSummary>[] = [
    { key: 'number', header: t('sales.number'), value: (row) => row.number },
    {
      key: 'reference',
      header: t('sales.referenceNo'),
      value: (row) => row.referenceNumber ?? '—',
    },
    { key: 'date', header: t('sales.date'), value: (row) => row.date },
    { key: 'customer', header: t('sales.customer'), value: (row) => row.customerName },
    {
      key: 'lines',
      header: t('sales.lines'),
      value: (row) => row.lineCount,
      numeric: true,
    },
    {
      key: 'taxable',
      header: t('sales.taxable'),
      value: (row) => row.taxable,
      numeric: true,
    },
    { key: 'tax', header: t('sales.tax'), value: (row) => row.tax, numeric: true },
    { key: 'total', header: t('sales.total'), value: (row) => row.total, numeric: true },
    {
      key: 'status',
      header: t('sales.status'),
      value: (row) => statusLabel(row.status, t),
      render: (row) => (
        <StatusBadge
          tone={statusTone(row.status)}
          label={statusLabel(row.status, t)}
          struck={row.status === SalesInvoiceStatus.cancelled}
        />
      ),
    },
    {
      key: 'actions',
      header: '',
      value: () => '',
      render: (row) => (
        <button
          type="button"
          onClick={() => setViewing(row.salesInvoiceId)}
          className="row-action row-action-neutral"
        >
          {t('sales.open')}
        </button>
      ),
    },
  ];

  const controls = (
    <div className="filter-grid flex flex-wrap items-end gap-3 w-full">
      <TextField
        label={t('sales.search')}
        type="search"
        value={search}
        onChange={(value) => narrow(setSearch)(value)}
        placeholder={t('sales.searchHint')}
        size="sm"
        className="min-w-48 flex-1"
      />

      <SelectField<number | ''>
        label={t('sales.status')}
        value={statusFilter}
        onChange={narrow<number | ''>(setStatusFilter)}
        size="sm"
        options={[
          { value: '', label: t('sales.allStatuses') },
          { value: SalesInvoiceStatus.draft, label: t('sales.draft') },
          { value: SalesInvoiceStatus.posted, label: t('sales.posted') },
          { value: SalesInvoiceStatus.cancelled, label: t('sales.cancelled') },
        ]}
      />

      <div className="ms-auto flex items-end gap-2">
        <DateField
          label={t('sales.from')}
          value={from}
          onChange={narrow(setFrom)}
          size="sm"
        />
        <DateField label={t('sales.to')} value={to} onChange={narrow(setTo)} size="sm" />
      </div>
    </div>
  );

  return (
    <>
      <ReportFrame
        title={isSalesReturn ? (t('sales.salesReturns') || 'Sales Returns') : (t('sales.salesInvoices') || 'Sales Invoices')}
        controls={controls}
        query={query}
      >
        {(result) => (
          <div className="space-y-3">
            {error && <p className="alert-error">{error}</p>}

            {notice && <p className="alert-success">{notice}</p>}

            <DataGrid
              gridKey={isSalesReturn ? 'sales-returns' : 'sales-invoices'}
              rows={result.items}
              columns={columns}
              rowKey={(row) => row.salesInvoiceId}
              emptyMessage={isSalesReturn ? (t('sales.noReturns') || 'No sales returns in this range.') : t('sales.none')}
              actions={
                <GridAction
                  label={
                    isSalesReturn
                      ? (t('sales.newSalesReturn') || 'New return')
                      : t('sales.newInvoice')
                  }
                  onClick={() => setEntering(true)}
                />
              }
              paging={{
                page: result.page,
                pageSize: result.pageSize,
                totalCount: result.totalCount,
                totalPages: result.totalPages,
                onPageChange: setPage,
              }}
            />
          </div>
        )}
      </ReportFrame>

      {/* Outside the frame on purpose: it replaces its children with a skeleton
          whenever the list goes back to pending — a page change or a filter — and an
          invoice half entered should not go with them. */}
      {entering && (
        <EntryDialog
          initialKind={kindFilter}
          nextInvoiceNo={nextInvoiceNo}
          onClose={() => setEntering(false)}
          onSaved={(message) => {
            setEntering(false);
            setNotice(message);
            void queryClient.invalidateQueries({ queryKey: ['sales-invoices'] });
          }}
          onError={setError}
        />
      )}

      {viewing && (
        <DocumentDialog
          id={viewing}
          busy={mutation.isPending}
          onClose={() => setViewing(null)}
          onPost={(id) =>
            run(async () => {
              const posted = await postSalesInvoice(id);

              return t('sales.postedNotice', {
                number: posted.number,
                stock: posted.stockDocumentNumber,
                total: moneyAlways(posted.total),
              });
            })
          }
          onCancel={(id, reason) =>
            run(async () => {
              await cancelSalesInvoice(id, reason);
              setViewing(null);

              return t('sales.cancelledNotice');
            })
          }
        />
      )}
    </>
  );
}

interface AdditionalLedgerItem {
  readonly id: string;
  readonly ledgerId: string;
  readonly isAddition: boolean;
  readonly amount: string;
}

function AdditionalLedgersSection({
  rows,
  ledgers,
  onChange,
}: {
  readonly rows: readonly AdditionalLedgerItem[];
  readonly ledgers: readonly LedgerSummary[];
  readonly onChange: (rows: readonly AdditionalLedgerItem[]) => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const addRow = (): void => {
    onChange([
      ...rows,
      {
        id: Math.random().toString(36).slice(2, 9),
        ledgerId: '',
        isAddition: true,
        amount: '0.00',
      },
    ]);
  };

  const removeRow = (id: string): void => {
    onChange(rows.filter((r) => r.id !== id));
  };

  const updateRow = (
    id: string,
    patch: Partial<AdditionalLedgerItem>,
  ): void => {
    onChange(rows.map((r) => (r.id === id ? { ...r, ...patch } : r)));
  };

  return (
    <div className="flex flex-col gap-2 rounded-xl border border-line bg-surface p-3">
      <div className="flex items-center justify-between border-b border-line pb-1.5">
        <span className="text-xs font-semibold uppercase tracking-wider text-ink-muted">
          {t('sales.additionalLedgers')}
        </span>
        <button
          type="button"
          onClick={addRow}
          className="btn-secondary btn-xs flex items-center gap-1 text-xs"
        >
          <IconPlus className="size-3" />
          {t('sales.addLedger')}
        </button>
      </div>

      {rows.length === 0 ? (
        <p className="py-2 text-center text-xs text-ink-muted">
          {t('sales.none')}
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-xs">
            <thead>
              <tr className="text-ink-muted">
                <th className="pb-1 text-start">{t('sales.ledger')}</th>
                <th className="w-24 pb-1 text-center">{t('sales.taxTreatment')}</th>
                <th className="w-24 pb-1 text-end">{t('sales.amount')}</th>
                <th className="w-7" />
              </tr>
            </thead>
            <tbody className="divide-y divide-line/60">
              {rows.map((row) => (
                <tr key={row.id}>
                  <td className="pe-2 py-1">
                    <SearchSelect
                      value={row.ledgerId}
                      onChange={(ledgerId) => updateRow(row.id, { ledgerId })}
                      options={ledgers.map((l) => ({
                        value: l.ledgerId,
                        label: `${l.code} — ${l.name}`,
                      }))}
                      size="sm"
                      label={t('sales.chooseLedger')}
                      placeholder={t('sales.chooseLedger')}
                    />
                  </td>
                  <td className="px-1 py-1 text-center">
                    <select
                      value={row.isAddition ? 'add' : 'deduct'}
                      onChange={(e) =>
                        updateRow(row.id, { isAddition: e.target.value === 'add' })
                      }
                      className="field-input-sm w-full py-0.5 text-xs font-medium"
                    >
                      <option value="add">{t('sales.addition')}</option>
                      <option value="deduct">{t('sales.deduction')}</option>
                    </select>
                  </td>
                  <td className="px-1 py-1 text-end">
                    <input
                      type="number"
                      inputMode="decimal"
                      value={row.amount}
                      onChange={(e) => updateRow(row.id, { amount: e.target.value })}
                      className="field-input-sm w-20 text-end font-mono tabular-nums"
                    />
                  </td>
                  <td className="ps-1 py-1 text-end">
                    <button
                      type="button"
                      onClick={() => removeRow(row.id)}
                      className="rounded p-1 text-ink-muted transition hover:text-red-600"
                      aria-label={t('sales.removeLine')}
                    >
                      <IconClose className="size-3.5" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/** Enters a draft: the header, then the lines. */
function EntryDialog({
  initialKind = SalesDocumentKind.invoice,
  nextInvoiceNo,
  onClose,
  onSaved,
  onError,
}: {
  readonly initialKind?: number;
  readonly nextInvoiceNo: string;
  readonly onClose: () => void;
  readonly onSaved: (message: string) => void;
  readonly onError: (message: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const settings = useSettings();
  const preferredWarehouseId = settings.preferredWarehouseId;

  const today = new Date().toISOString().slice(0, 10);

  const [kind] = useState<number>(initialKind);
  const [date, setDate] = useState(today);
  const [customerId, setCustomerId] = useState(settings.defaultCustomerId || '');
  const [warehouseId, setWarehouseId] = useState('');
  const [reference, setReference] = useState('');
  const [returnsInvoiceId, setReturnsInvoiceId] = useState('');
  const [paymentMode, setPaymentMode] = useState<string>(settings.defaultPaymentMode || 'Cash');
  const [salesman, setSalesman] = useState<string>(settings.defaultSalesman || 'Primary');
  const [billingman, setBillingman] = useState<string>(settings.defaultBillingman || 'Primary');
  const [taxMode, setTaxMode] = useState<'NT' | 'TAX' | 'GST'>(
    (settings.defaultTaxMode as 'NT' | 'TAX' | 'GST') || 'TAX',
  );
  const [rateType, setRateType] = useState<'retail' | 'wholesale' | 'mrp'>(
    settings.defaultSalesRateType || 'retail',
  );
  const [reverseCalc, setReverseCalc] = useState<boolean>(settings.enableReverseCalculation ?? false);
  const [autoBatch, setAutoBatch] = useState<boolean>(settings.enableAutoBatch ?? true);
  const [enableFreeQty, setEnableFreeQty] = useState<boolean>(settings.enableFreeQuantity ?? true);
  const [enableDiscount, setEnableDiscount] = useState<boolean>(settings.enableItemDiscount ?? true);

  const [barcodeInput, setBarcodeInput] = useState('');
  const [barcodeNotice, setBarcodeNotice] = useState<string | null>(null);

  const [narration, setNarration] = useState('');
  const [paidAmount, setPaidAmount] = useState<string>('');
  const [cashGiven, setCashGiven] = useState<string>('');
  const [additionalLedgers, setAdditionalLedgers] = useState<readonly AdditionalLedgerItem[]>(
    () => {
      const targetType = isReturn(initialKind) ? 'SalesReturn' : 'Sales';
      return (settings.defaultAdditionalLedgers ?? [])
        .filter((l) => l.transactionType === targetType && l.isDefaultActive)
        .map((l) => ({
          id: Math.random().toString(36).slice(2, 9),
          ledgerId: l.ledgerId,
          isAddition: l.isAddition,
          amount: l.defaultAmount || '0.00',
        }));
    },
  );
  const [activeBottomTab, setActiveBottomTab] = useState<'general' | 'shipping'>('general');
  const [lines, setLines] = useState<readonly DraftLine[]>([{ ...emptyLine }]);
  const [busy, setBusy] = useState(false);

  const customers = useQuery<readonly CustomerSummary[], ApiError>({
    queryKey: ['customers', 'picker'],
    queryFn: () => listCustomers('', true),
  });

  const selectedCustomer = useMemo(
    () => customers.data?.find((c) => c.customerId === customerId),
    [customers.data, customerId],
  );

  const prevBalance = selectedCustomer?.openingBalance ?? 0;

  useEffect(() => {
    if (!customerId && settings.defaultCustomerId) {
      setCustomerId(settings.defaultCustomerId);
    }
  }, [customerId, settings.defaultCustomerId]);

  const warehouses = useQuery<readonly WarehouseSummary[], ApiError>({
    queryKey: ['warehouses', false],
    queryFn: () => listMaster<WarehouseSummary>('warehouses', false),
  });

  const products = useQuery<readonly ProductSummary[], ApiError>({
    queryKey: ['products', 'picker'],
    queryFn: () => listProducts('', '', false),
  });

  const ledgers = useQuery<readonly LedgerSummary[], ApiError>({
    queryKey: ['ledgers'],
    queryFn: () => listLedgers(true),
    staleTime: 5 * 60 * 1000,
  });

  const valuation = useQuery<StockValuationReport, ApiError>({
    queryKey: ['stock-valuation', 'picker', warehouseId],
    queryFn: () => fetchStockValuation(warehouseId, '', true),
    staleTime: 60 * 1000,
  });

  const onHand = useMemo(() => stockByProduct(valuation.data?.rows), [valuation.data]);

  const productOptions = useMemo(
    () =>
      (products.data ?? []).map((product) =>
        productOption(product, onHand.get(product.id) ?? 0),
      ),
    [products.data, onHand],
  );

  useEffect(() => {
    if (warehouseId !== '' || !warehouses.data) {
      return;
    }

    const chosen =
      warehouses.data.find((warehouse) => warehouse.id === preferredWarehouseId) ??
      warehouses.data.find((warehouse) => warehouse.isDefault) ??
      (warehouses.data.length === 1 ? warehouses.data[0] : undefined);

    if (chosen) {
      setWarehouseId(chosen.id);
    }
  }, [warehouses.data, preferredWarehouseId, warehouseId]);

  const returnable = useQuery<PagedResult<SalesInvoiceSummary>, ApiError>({
    queryKey: ['sales-invoices', 'returnable', customerId],
    queryFn: () =>
      listSalesInvoices(
        {
          kind: SalesDocumentKind.invoice,
          status: SalesInvoiceStatus.posted,
          customerLedgerId: customerId,
        },
        1,
        50,
      ),
    enabled: isReturn(kind) && customerId !== '',
  });

  const additionalLedgersTotal = useMemo(() => {
    return additionalLedgers.reduce((acc, row) => {
      const amt = Number(row.amount);
      if (!Number.isFinite(amt) || amt <= 0) return acc;
      return row.isAddition ? acc + amt : acc - amt;
    }, 0);
  }, [additionalLedgers]);

  const totals = useMemo(() => {
    let gross = 0;
    let discount = 0;
    let tax = 0;

    for (const line of lines) {
      const lineQty = Number(line.quantity || 0);
      const lineRate = Number(line.rate || 0);
      const lineDisc = enableDiscount ? Number(line.discount || 0) : 0;
      const lineTaxPct = Number(line.taxPercentage || 0);

      const net = lineQty * lineRate - lineDisc;

      if (Number.isFinite(net) && net > 0) {
        gross += lineQty * lineRate;
        discount += lineDisc;
        tax += (net * lineTaxPct) / 100;
      }
    }

    const safeGross = Number.isFinite(gross) ? gross : 0;
    const safeDiscount = Number.isFinite(discount) ? discount : 0;
    const taxable = safeGross - safeDiscount;

    return {
      gross: safeGross,
      discount: safeDiscount,
      tax,
      taxable,
      total: taxable + tax,
    };
  }, [lines, enableDiscount]);

  const grandTotal = useMemo(() => {
    const raw = totals.total + additionalLedgersTotal;
    return Number.isFinite(raw) ? Math.max(0, raw) : 0;
  }, [totals.total, additionalLedgersTotal]);

  const effectivePaid = useMemo(() => {
    if (paidAmount !== '') {
      const parsed = Number(paidAmount);
      return Number.isFinite(parsed) ? parsed : 0;
    }
    return paymentMode === 'Credit' ? 0 : grandTotal;
  }, [paidAmount, paymentMode, grandTotal]);

  const balanceDue = useMemo(() => {
    return Math.max(0, grandTotal - effectivePaid);
  }, [grandTotal, effectivePaid]);

  const changeBack = useMemo(() => {
    const cash = Number(cashGiven);
    if (!Number.isFinite(cash) || cash <= 0) return 0;
    return Math.max(0, cash - effectivePaid);
  }, [cashGiven, effectivePaid]);

  const change = (index: number, patch: Partial<DraftLine>): void =>
    setLines((previous) =>
      previous.map((line, at) => (at === index ? { ...line, ...patch } : line)),
    );

  const handleBarcodeScan = (scannedCode: string): void => {
    const code = scannedCode.trim();
    if (!code) return;

    const found = products.data?.find(
      (p) =>
        p.code.toLowerCase() === code.toLowerCase() ||
        p.id.toLowerCase() === code.toLowerCase() ||
        p.description.toLowerCase().includes(code.toLowerCase()),
    );

    if (!found) {
      setBarcodeNotice(t('sales.scanProductNotFound'));
      window.setTimeout(() => setBarcodeNotice(null), 3000);
      return;
    }

    const defaultTax = taxMode === 'NT' ? 0 : taxMode === 'TAX' ? 5 : 18;
    const baseRate =
      rateType === 'wholesale'
        ? found.wholesaleRate || found.retailRate
        : rateType === 'mrp'
        ? found.maximumRetailPrice || found.retailRate
        : found.retailRate;
    const incRate = (baseRate * (1 + defaultTax / 100)).toFixed(2);

    const existingIndex = lines.findIndex((l) => l.productId === found.id);
    const existing = existingIndex >= 0 ? lines[existingIndex] : undefined;
    if (existingIndex >= 0 && existing) {
      const nextQty = String(Number(existing.quantity || '0') + 1);
      change(existingIndex, { quantity: nextQty });
      setBarcodeNotice(`${found.code} (Qty: ${nextQty})`);
    } else {
      const emptyIndex = lines.findIndex((l) => !l.productId);
      const newLine: DraftLine = {
        productId: found.id,
        quantity: '1',
        freeQuantity: '0',
        rate: String(baseRate),
        inclusiveRate: incRate,
        taxPercentage: String(defaultTax),
        discount: '0',
        batchNumber: '',
        expiresOn: '',
        serialNumbers: [],
      };
      if (emptyIndex >= 0) {
        setLines((prev) => prev.map((l, idx) => (idx === emptyIndex ? newLine : l)));
      } else {
        setLines((prev) => [...prev, newLine]);
      }
      setBarcodeNotice(`${t('sales.scanProductSuccess')}: ${found.code}`);
    }

    setBarcodeInput('');
    window.setTimeout(() => setBarcodeNotice(null), 3000);
  };

  const handleTaxModeChange = (newMode: 'NT' | 'TAX' | 'GST'): void => {
    setTaxMode(newMode);
    const newTaxPct = newMode === 'NT' ? 0 : newMode === 'TAX' ? 5 : 18;
    setLines((prev) =>
      prev.map((l) => {
        const updated = { ...l, taxPercentage: String(newTaxPct) };
        if (reverseCalc && l.inclusiveRate) {
          const inc = Number(l.inclusiveRate);
          if (Number.isFinite(inc) && inc > 0) {
            updated.rate = (inc / (1 + newTaxPct / 100)).toFixed(4);
          }
        } else if (!reverseCalc && l.rate) {
          const r = Number(l.rate);
          if (Number.isFinite(r) && r > 0) {
            updated.inclusiveRate = (r * (1 + newTaxPct / 100)).toFixed(2);
          }
        }
        return updated;
      }),
    );
  };

  const handleRateTypeChange = (newRateType: 'retail' | 'wholesale' | 'mrp'): void => {
    setRateType(newRateType);
    setLines((prev) =>
      prev.map((l) => {
        if (!l.productId) return l;
        const prod = products.data?.find((p) => p.id === l.productId);
        if (!prod) return l;
        const baseRate =
          newRateType === 'wholesale'
            ? prod.wholesaleRate || prod.retailRate
            : newRateType === 'mrp'
            ? prod.maximumRetailPrice || prod.retailRate
            : prod.retailRate;
        const taxPct = Number(l.taxPercentage || 0);
        return {
          ...l,
          rate: String(baseRate),
          inclusiveRate: (baseRate * (1 + taxPct / 100)).toFixed(2),
        };
      }),
    );
  };

  const { errors, submit } = useValidation<{
    date: string;
    customerId: string;
    warehouseId: string;
  }>((values) =>
    collect({
      date: required(values.date, t('sales.dateRequired')),
      customerId: required(values.customerId, t('sales.customerRequired')),
      warehouseId: required(values.warehouseId, t('sales.warehouseRequired')),
      lines: lines.some((line) => line.productId && Number(line.quantity) > 0)
        ? null
        : t('sales.linesRequired'),
    }),
  );

  const lineErrors = useMemo(
    () =>
      lines.map((line) =>
        collect({
          quantity: line.productId
            ? numeric(line.quantity, t('sales.quantityInvalid'), { min: 0.000001 })
            : null,
          rate: line.productId
            ? numeric(line.rate, t('sales.rateInvalid'), { min: 0 })
            : null,
          taxPercentage: numeric(line.taxPercentage, t('sales.taxInvalid'), {
            min: 0,
            max: 100,
          }),
        }),
      ),
    [lines, t],
  );

  const save = async (): Promise<void> => {
    if (
      !submit({ date, customerId, warehouseId }) ||
      lineErrors.some((line) => Object.keys(line).length > 0)
    ) {
      return;
    }

    setBusy(true);

    try {
      const payload: readonly SalesLineInput[] = lines
        .filter((line) => line.productId && Number(line.quantity) > 0)
        .map((line) => ({
          productId: line.productId,
          quantity: Number(line.quantity),
          rate: Number(line.rate || 0),
          taxPercentage: Number(line.taxPercentage || 0),
          discount: enableDiscount ? Number(line.discount || 0) : 0,
          batchNumber: line.batchNumber || null,
          serialNumbers: line.serialNumbers,
        }));

      // Requirement 3: if referenceNo is blank then update with Invoice No
      const finalReference = reference.trim() || nextInvoiceNo;

      // Requirement 7: addition ledgers mapped into charges
      const chargesPayload: readonly SalesChargeInput[] = additionalLedgers
        .filter((row) => row.ledgerId !== '' && Number(row.amount) > 0)
        .map((row) => ({
          ledgerId: row.ledgerId,
          amount: Number(row.amount),
        }));

      const header = await createSalesInvoice({
        date,
        customerLedgerId: customerId,
        warehouseId,
        lines: payload,
        kind,
        returnsInvoiceId: isReturn(kind) && returnsInvoiceId ? returnsInvoiceId : null,
        referenceNumber: finalReference,
        narration: narration.trim() || null,
        charges: chargesPayload,
      });

      onSaved(t('sales.savedNotice', { number: header.number }));
    } catch (failure) {
      const api = failure as ApiError;
      onError(api.detail || api.code);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={isReturn(kind) ? t('sales.newReturn') : t('sales.newInvoice')}
      onClose={onClose}
      size="full"
    >
      <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 gap-3 bg-surface-2/40 p-4 rounded-2xl border border-line">
        {/* Document Numbers */}
        <div className="space-y-2">
          <TextField
            label={isReturn(kind) ? t('sales.returnNo') || 'Return No' : t('sales.invoiceNo')}
            value={nextInvoiceNo}
            disabled
            onChange={() => {}}
            className="bg-surface-3/50 font-mono font-semibold"
            size="sm"
          />
          <TextField
            label={t('sales.referenceNo')}
            value={reference}
            onChange={setReference}
            placeholder={nextInvoiceNo}
            size="sm"
          />
        </div>

        {/* Column 2: Customer & Payment Mode */}
        <div className="space-y-2">
          <FormField label={t('sales.customer')} required error={errors['customerId']}>
            <SearchSelect
              value={customerId}
              onChange={setCustomerId}
              options={(customers.data ?? []).map((customer) => ({
                value: customer.customerId,
                label: `${customer.code} — ${customer.name}`,
                ...(customer.contact.mobileNumber
                  ? { detail: customer.contact.mobileNumber }
                  : {}),
              }))}
              invalid={errors['customerId'] !== undefined}
              label={t('sales.customer')}
              placeholder={t('sales.chooseCustomer')}
              size="sm"
            />
          </FormField>

          <SelectField<string>
            label={t('sales.paymentMode')}
            value={paymentMode}
            onChange={(val) => setPaymentMode(String(val))}
            options={[
              { value: 'Cash', label: 'Cash' },
              { value: 'Credit', label: 'Credit' },
              { value: 'Card', label: 'Card' },
              { value: 'Bank', label: 'Bank' },
            ]}
            size="sm"
          />
        </div>

        {/* Column 3: Sales Staff & Barcode Scanner */}
        <div className="space-y-2">
          <div className="grid grid-cols-2 gap-2">
            <TextField
              label={t('sales.salesman')}
              value={salesman}
              onChange={setSalesman}
              size="sm"
            />
            <TextField
              label={t('sales.billingman')}
              value={billingman}
              onChange={setBillingman}
              size="sm"
            />
          </div>

          <div>
            <label className="block text-xs text-ink-muted mb-1 font-medium">
              {t('sales.barcode')}
            </label>
            <div className="relative flex items-center">
              <input
                type="text"
                value={barcodeInput}
                onChange={(e) => setBarcodeInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault();
                    handleBarcodeScan(barcodeInput);
                  }
                }}
                placeholder={t('sales.barcodePlaceholder')}
                className="field-input-sm w-full pe-8 font-mono text-xs"
              />
              <button
                type="button"
                onClick={() => handleBarcodeScan(barcodeInput)}
                className="absolute end-1.5 p-1 text-ink-muted hover:text-brand-600 transition"
                title="Scan / Read Barcode"
              >
                <IconBarcode className="size-4" />
              </button>
            </div>
            {barcodeNotice && (
              <span className="block mt-1 text-[11px] font-medium text-brand-600 dark:text-brand-300 truncate">
                {barcodeNotice}
              </span>
            )}
          </div>
        </div>

        {/* Column 4: Date, Mode, Rate & Warehouse */}
        <div className="space-y-2">
          <div className="grid grid-cols-2 gap-2">
            <DateField
              label={t('sales.date')}
              required
              value={date}
              onChange={setDate}
              error={errors['date']}
              size="sm"
            />
            <SelectField<'NT' | 'TAX' | 'GST'>
              label={t('sales.taxMode')}
              value={taxMode}
              onChange={(val) => handleTaxModeChange(val as 'NT' | 'TAX' | 'GST')}
              options={[
                { value: 'NT', label: 'NT (0%)' },
                { value: 'TAX', label: 'TAX (5%)' },
                { value: 'GST', label: 'GST (18%)' },
              ]}
              size="sm"
            />
          </div>

          <div className="grid grid-cols-2 gap-2">
            <SelectField<'retail' | 'wholesale' | 'mrp'>
              label={t('sales.salesRateType')}
              value={rateType}
              onChange={(val) => handleRateTypeChange(val as 'retail' | 'wholesale' | 'mrp')}
              options={[
                { value: 'retail', label: 'Retail' },
                { value: 'wholesale', label: 'Wholesale' },
                { value: 'mrp', label: 'MRP' },
              ]}
              size="sm"
            />
            <FormField label={t('sales.warehouse')} required error={errors['warehouseId']}>
              <SearchSelect
                value={warehouseId}
                onChange={setWarehouseId}
                options={(warehouses.data ?? []).map((warehouse) => ({
                  value: warehouse.id,
                  label: `${warehouse.code} — ${warehouse.name}`,
                  ...(warehouse.isDefault ? { meta: t('settings.masterDefault') } : {}),
                }))}
                invalid={errors['warehouseId'] !== undefined}
                label={t('sales.warehouse')}
                placeholder={t('sales.chooseWarehouse')}
                size="sm"
              />
            </FormField>
          </div>
        </div>
      </div>

      {/* Feature Quick Toggles Bar */}
      <div className="flex flex-wrap items-center justify-between gap-3 px-1 py-0.5 text-xs text-ink-muted">
        <div className="flex flex-wrap items-center gap-4">
          <label className="flex items-center gap-1.5 cursor-pointer font-medium select-none hover:text-ink">
            <input
              type="checkbox"
              checked={reverseCalc}
              onChange={(e) => setReverseCalc(e.target.checked)}
              className="rounded border-line text-brand-600 focus:ring-brand-500"
            />
            {t('sales.reverseCalculation')}
          </label>
          <label className="flex items-center gap-1.5 cursor-pointer font-medium select-none hover:text-ink">
            <input
              type="checkbox"
              checked={autoBatch}
              onChange={(e) => setAutoBatch(e.target.checked)}
              className="rounded border-line text-brand-600 focus:ring-brand-500"
            />
            {t('sales.autoBatch')}
          </label>
          <label className="flex items-center gap-1.5 cursor-pointer font-medium select-none hover:text-ink">
            <input
              type="checkbox"
              checked={enableFreeQty}
              onChange={(e) => setEnableFreeQty(e.target.checked)}
              className="rounded border-line text-brand-600 focus:ring-brand-500"
            />
            {t('sales.freeQuantity')}
          </label>
          <label className="flex items-center gap-1.5 cursor-pointer font-medium select-none hover:text-ink">
            <input
              type="checkbox"
              checked={enableDiscount}
              onChange={(e) => setEnableDiscount(e.target.checked)}
              className="rounded border-line text-brand-600 focus:ring-brand-500"
            />
            {t('sales.itemDiscount')}
          </label>
        </div>

        {isReturn(kind) && (
          <div className="w-72">
            <SearchSelect
              value={returnsInvoiceId}
              onChange={setReturnsInvoiceId}
              clearable
              size="sm"
              label={t('sales.againstInvoice')}
              placeholder={t('sales.noInvoice')}
              options={(returnable.data?.items ?? []).map((inv) => ({
                value: inv.salesInvoiceId,
                label: inv.number,
                detail: inv.date,
                meta: moneyAlways(inv.total),
              }))}
            />
          </div>
        )}
      </div>

      {isReturn(kind) && (
        <p className="alert-warn text-xs">{t('sales.returnHint')}</p>
      )}

      {/* Line Items Table */}
      <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
        <table className="line-table sm:min-w-[46rem]">
          <thead className="text-start text-xs text-ink-muted">
            <tr>
              <th className="px-2 py-1 text-start">{t('sales.product')}</th>
              <th className="px-2 py-1 text-end">{t('sales.quantity')}</th>
              {enableFreeQty && (
                <th className="px-2 py-1 text-end">{t('sales.freeQuantity')}</th>
              )}
              <th className="px-2 py-1 text-end">
                {t('sales.rate')}
                {reverseCalc ? ` (${t('sales.inclusiveRate')})` : ''}
              </th>
              {enableDiscount && (
                <th className="px-2 py-1 text-end">{t('sales.itemDiscount')}</th>
              )}
              <th className="px-2 py-1 text-end">{t('sales.taxPercent')}</th>
              <th className="px-2 py-1 text-end">{t('sales.net')}</th>
              <th className="line-action-column" />
            </tr>
          </thead>

          <tbody>
            {lines.map((line, index) => (
              <LineRow
                key={index}
                line={line}
                products={products.data ?? []}
                productOptions={productOptions}
                warehouseId={warehouseId}
                rateType={rateType}
                taxMode={taxMode}
                reverseCalc={reverseCalc}
                autoBatch={autoBatch}
                enableFreeQty={enableFreeQty}
                enableDiscount={enableDiscount}
                errors={lineErrors[index] ?? {}}
                removable={lines.length > 1}
                onChange={(patch) => change(index, patch)}
                onRemove={() =>
                  setLines((previous) => previous.filter((_, at) => at !== index))
                }
              />
            ))}
          </tbody>
        </table>
      </div>

      <div className="flex flex-wrap items-center gap-4">
        <button
          type="button"
          onClick={() => setLines((previous) => [...previous, { ...emptyLine }])}
          className="btn-secondary btn-sm"
        >
          {t('sales.addLine')}
        </button>
      </div>

      {/* Bottom Master Section: Customer Details + Additional Ledgers + Partial Payment & Totals */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-4 pt-2">
        {/* Panel 1: Customer Details & Narration */}
        <div className="lg:col-span-4 flex flex-col gap-2 rounded-xl border border-line bg-surface p-3">
          <div className="flex items-center gap-2 border-b border-line pb-1.5">
            <button
              type="button"
              onClick={() => setActiveBottomTab('general')}
              className={clsx(
                'text-xs font-semibold uppercase tracking-wider transition px-1 py-0.5 border-b-2',
                activeBottomTab === 'general'
                  ? 'border-brand-600 text-brand-700 dark:text-brand-300'
                  : 'border-transparent text-ink-muted hover:text-ink',
              )}
            >
              {t('sales.generalTab')}
            </button>
            <button
              type="button"
              onClick={() => setActiveBottomTab('shipping')}
              className={clsx(
                'text-xs font-semibold uppercase tracking-wider transition px-1 py-0.5 border-b-2',
                activeBottomTab === 'shipping'
                  ? 'border-brand-600 text-brand-700 dark:text-brand-300'
                  : 'border-transparent text-ink-muted hover:text-ink',
              )}
            >
              {t('sales.shippingTab')}
            </button>
          </div>

          {activeBottomTab === 'general' ? (
            <div className="space-y-2 text-xs">
              <div className="grid grid-cols-2 gap-2 text-ink-muted">
                <div>
                  <span className="block text-[10px] uppercase font-bold text-ink-muted/80">
                    {t('customers.address')}
                  </span>
                  <p className="truncate text-ink font-medium">
                    {selectedCustomer?.contact.addressLine1 ||
                      selectedCustomer?.contact.addressLine2 ||
                      '—'}
                  </p>
                </div>
                <div>
                  <span className="block text-[10px] uppercase font-bold text-ink-muted/80">
                    {t('customers.mobile')}
                  </span>
                  <p className="truncate font-mono text-ink">
                    {selectedCustomer?.contact.mobileNumber ||
                      selectedCustomer?.contact.phone ||
                      '—'}
                  </p>
                </div>
              </div>

              <div>
                <label className="block text-[10px] uppercase font-bold text-ink-muted/80 mb-1">
                  {t('sales.narration')}
                </label>
                <textarea
                  rows={2}
                  value={narration}
                  onChange={(e) => setNarration(e.target.value)}
                  placeholder={t('sales.narration')}
                  className="w-full rounded-lg border border-line bg-surface p-2 text-xs text-ink focus:border-brand-500 focus:outline-none resize-none"
                />
              </div>
            </div>
          ) : (
            <div className="space-y-2 text-xs">
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <span className="block text-[10px] uppercase font-bold text-ink-muted/80">
                    {t('sales.salesman')}
                  </span>
                  <p className="text-ink font-medium">{salesman}</p>
                </div>
                <div>
                  <span className="block text-[10px] uppercase font-bold text-ink-muted/80">
                    {t('sales.billingman')}
                  </span>
                  <p className="text-ink font-medium">{billingman}</p>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Panel 2: Additional Ledgers */}
        <div className="lg:col-span-4">
          <AdditionalLedgersSection
            rows={additionalLedgers}
            ledgers={ledgers.data ?? []}
            onChange={setAdditionalLedgers}
          />
        </div>

        {/* Panel 3: Partial Payment & Grand Totals */}
        <div className="lg:col-span-4 rounded-xl border border-line bg-surface-2/60 p-3 flex flex-col justify-between gap-3">
          <div className="space-y-1.5 text-xs">
            <div className="flex justify-between text-ink-muted">
              <span>{t('sales.prevBalance')}</span>
              <span className="font-mono tabular-nums">{moneyAlways(prevBalance)}</span>
            </div>
            <div className="flex justify-between text-ink-muted">
              <span>{t('sales.taxable')}</span>
              <span className="font-mono tabular-nums">{moneyAlways(totals.taxable)}</span>
            </div>
            {totals.tax > 0 && (
              <div className="flex justify-between text-ink-muted">
                <span>{t('sales.tax')} ({taxMode})</span>
                <span className="font-mono tabular-nums">{moneyAlways(totals.tax)}</span>
              </div>
            )}
            {additionalLedgersTotal !== 0 && (
              <div className="flex justify-between text-ink-muted">
                <span>{t('sales.additionalLedgersTotal')}</span>
                <span className="font-mono tabular-nums">
                  {additionalLedgersTotal >= 0 ? '+' : ''}
                  {moneyAlways(additionalLedgersTotal)}
                </span>
              </div>
            )}
            <div className="flex justify-between text-sm font-bold text-ink pt-1 border-t border-line">
              <span>{t('sales.grandTotal')}</span>
              <span className="font-mono text-base text-brand-700 dark:text-brand-300 tabular-nums">
                {moneyAlways(grandTotal)}
              </span>
            </div>
          </div>

          {/* Partial Payment inputs */}
          <div className="grid grid-cols-2 gap-2 pt-2 border-t border-line/70">
            <div>
              <label className="block text-[10px] uppercase font-bold text-ink-muted mb-1">
                {t('sales.paidAmount')}
              </label>
              <input
                type="number"
                inputMode="decimal"
                value={paidAmount}
                placeholder={moneyAlways(effectivePaid)}
                onChange={(e) => setPaidAmount(e.target.value)}
                className="field-input-sm w-full text-end font-mono tabular-nums font-semibold"
              />
            </div>
            <div>
              <label className="block text-[10px] uppercase font-bold text-ink-muted mb-1">
                {t('sales.balanceDue')}
              </label>
              <div className="h-8 flex items-center justify-end font-mono text-sm font-bold text-red-600 dark:text-red-400 tabular-nums">
                {moneyAlways(balanceDue)}
              </div>
            </div>
            {paymentMode === 'Cash' && (
              <>
                <div>
                  <label className="block text-[10px] uppercase font-bold text-ink-muted mb-1">
                    {t('sales.cashGiven')}
                  </label>
                  <input
                    type="number"
                    inputMode="decimal"
                    value={cashGiven}
                    placeholder="0.00"
                    onChange={(e) => setCashGiven(e.target.value)}
                    className="field-input-sm w-full text-end font-mono tabular-nums"
                  />
                </div>
                <div>
                  <label className="block text-[10px] uppercase font-bold text-ink-muted mb-1">
                    {t('sales.changeBack')}
                  </label>
                  <div className="h-8 flex items-center justify-end font-mono text-sm font-semibold text-emerald-600 dark:text-emerald-400 tabular-nums">
                    {moneyAlways(changeBack)}
                  </div>
                </div>
              </>
            )}
          </div>
        </div>
      </div>

      <div className="flex justify-end gap-2 pt-2 border-t border-line/60">
        <ModalButton onClick={onClose}>{t('sales.close')}</ModalButton>
        <ModalButton primary disabled={busy} onClick={() => void save()}>
          {t('sales.saveDraft')}
        </ModalButton>
      </div>
    </Modal>
  );
}

/**
 * One line, and whatever the product it names needs beyond a quantity and a rate.
 */
function LineRow({
  line,
  products,
  productOptions,
  warehouseId,
  rateType,
  taxMode,
  reverseCalc,
  autoBatch,
  enableFreeQty,
  enableDiscount,
  errors,
  removable,
  onChange,
  onRemove,
}: {
  readonly line: DraftLine;
  readonly products: readonly ProductSummary[];
  readonly productOptions: ReturnType<typeof productOption>[];
  readonly warehouseId: string;
  readonly rateType: 'retail' | 'wholesale' | 'mrp';
  readonly taxMode: 'NT' | 'TAX' | 'GST';
  readonly reverseCalc: boolean;
  readonly autoBatch: boolean;
  readonly enableFreeQty: boolean;
  readonly enableDiscount: boolean;
  readonly errors: Readonly<Record<string, string>>;
  readonly removable: boolean;
  readonly onChange: (patch: Partial<DraftLine>) => void;
  readonly onRemove: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();

  const product = products.find((candidate) => candidate.id === line.productId);
  const net = Number(line.quantity) * Number(line.rate) - (enableDiscount ? Number(line.discount || 0) : 0);

  const wanted = Number(line.quantity);
  const needsBatch = product?.tracksBatches === true;
  const needsSerials = product?.tracksSerialNumbers === true;

  const batches = useQuery<readonly BatchStockRow[], ApiError>({
    queryKey: ['sales-line-batches', line.productId, warehouseId],
    queryFn: () => fetchProductBatches(line.productId, warehouseId),
    enabled: needsBatch && line.productId !== '' && warehouseId !== '',
  });

  const serials = useQuery<readonly SerialNumberView[], ApiError>({
    queryKey: ['sales-line-serials', line.productId, warehouseId],
    queryFn: () => fetchProductSerials(line.productId, warehouseId),
    enabled: needsSerials && line.productId !== '' && warehouseId !== '',
  });

  // Auto Batch FIFO
  useEffect(() => {
    if (
      autoBatch &&
      needsBatch &&
      (!line.batchNumber || line.batchNumber === '') &&
      batches.data &&
      batches.data.length > 0
    ) {
      const available = batches.data.find((b) => b.quantity > 0) ?? batches.data[0];
      if (available) {
        onChange({
          batchNumber: available.batchNumber,
          expiresOn: available.expiresOn ?? '',
        });
      }
    }
  }, [autoBatch, needsBatch, line.batchNumber, batches.data]);

  const toggleSerial = (number: string): void => {
    const chosen = new Set(line.serialNumbers);

    if (chosen.has(number)) {
      chosen.delete(number);
    } else {
      chosen.add(number);
    }

    onChange({ serialNumbers: [...chosen] });
  };

  const handleProductSelect = (value: string): void => {
    const chosen = products.find((candidate) => candidate.id === value);
    if (chosen) {
      const baseRate =
        rateType === 'wholesale'
          ? chosen.wholesaleRate || chosen.retailRate
          : rateType === 'mrp'
          ? chosen.maximumRetailPrice || chosen.retailRate
          : chosen.retailRate;
      const defaultTax = Number(
        line.taxPercentage || (taxMode === 'NT' ? 0 : taxMode === 'TAX' ? 5 : 18),
      );
      const incRate = (baseRate * (1 + defaultTax / 100)).toFixed(2);
      onChange({
        productId: value,
        rate: String(baseRate),
        inclusiveRate: incRate,
        batchNumber: '',
        expiresOn: '',
        serialNumbers: [],
      });
    } else {
      onChange({
        productId: value,
        batchNumber: '',
        expiresOn: '',
        serialNumbers: [],
      });
    }
  };

  const isExpired = line.expiresOn ? new Date(line.expiresOn) < new Date() : false;
  const isNearExpiry = line.expiresOn
    ? !isExpired &&
      (new Date(line.expiresOn).getTime() - Date.now()) / (1000 * 3600 * 24) <= 30
    : false;

  return (
    <>
      <tr className="border-t border-line">
        <td data-label={t('sales.product')} className="min-w-64 px-2 py-1">
          <SearchSelect
            value={line.productId}
            onChange={handleProductSelect}
            options={productOptions}
            size="sm"
            label={t('sales.product')}
            placeholder={t('sales.chooseProduct')}
          />
        </td>
        <NumberCell
          label={t('sales.quantity')}
          value={line.quantity}
          error={errors['quantity']}
          onChange={(value) => onChange({ quantity: value })}
        />
        {enableFreeQty && (
          <NumberCell
            label={t('sales.freeQuantity')}
            value={line.freeQuantity ?? '0'}
            onChange={(value) => onChange({ freeQuantity: value })}
          />
        )}
        {reverseCalc ? (
          <NumberCell
            label={`${t('sales.rate')} (${t('sales.inclusiveRate')})`}
            value={line.inclusiveRate ?? ''}
            error={errors['rate']}
            subtitle={line.rate ? `Excl: ${moneyAlways(Number(line.rate))}` : undefined}
            onChange={(val) => {
              const inc = Number(val);
              const taxPct = Number(line.taxPercentage || 0);
              const exc =
                Number.isFinite(inc) && inc > 0 ? (inc / (1 + taxPct / 100)).toFixed(4) : '';
              onChange({ inclusiveRate: val, rate: exc });
            }}
          />
        ) : (
          <NumberCell
            label={t('sales.rate')}
            value={line.rate}
            error={errors['rate']}
            subtitle={
              line.inclusiveRate ? `Incl: ${moneyAlways(Number(line.inclusiveRate))}` : undefined
            }
            onChange={(val) => {
              const r = Number(val);
              const taxPct = Number(line.taxPercentage || 0);
              const inc =
                Number.isFinite(r) && r > 0 ? (r * (1 + taxPct / 100)).toFixed(2) : '';
              onChange({ rate: val, inclusiveRate: inc });
            }}
          />
        )}
        {enableDiscount && (
          <NumberCell
            label={t('sales.itemDiscount')}
            value={line.discount}
            onChange={(value) => onChange({ discount: value })}
          />
        )}
        <NumberCell
          label={t('sales.taxPercent')}
          value={line.taxPercentage}
          error={errors['taxPercentage']}
          onChange={(value) => onChange({ taxPercentage: value })}
        />
        <td data-label={t('sales.net')} className="px-2 py-1 text-end font-mono">
          {Number.isFinite(net) ? moneyAlways(net) : '—'}
        </td>
        <td className="px-2 py-1 text-end">
          {removable && (
            <button
              type="button"
              onClick={onRemove}
              className="rounded px-1.5 py-0.5 text-xs font-medium text-red-600 transition hover:bg-red-50 dark:text-red-400 dark:hover:bg-red-500/10"
            >
              {t('sales.removeLine')}
            </button>
          )}
        </td>
      </tr>

      {(needsBatch || needsSerials) && (
        <tr className="border-t border-dashed border-line bg-surface-2/30">
          <td colSpan={8} className="px-3 py-2">
            <div className="flex flex-wrap items-center gap-4">
              {needsBatch && (
                <div className="flex flex-wrap items-center gap-2">
                  <Field label={t('sales.batch')}>
                    <Select
                      label={t('sales.batch')}
                      value={line.batchNumber}
                      onChange={(value) => {
                        const chosen = (batches.data ?? []).find(
                          (b) => b.batchNumber === String(value),
                        );
                        onChange({
                          batchNumber: String(value),
                          expiresOn: chosen?.expiresOn ?? '',
                        });
                      }}
                      options={[
                        { value: '', label: t('sales.chooseBatch') },
                        ...(batches.data ?? []).map((batch) => ({
                          value: batch.batchNumber,
                          label: `${batch.batchNumber} (Qty: ${batch.quantity}${batch.expiresOn ? ` · Exp: ${batch.expiresOn}` : ''})`,
                        })),
                      ]}
                    />
                  </Field>

                  {line.expiresOn && (
                    <div className="mt-4">
                      <span
                        className={clsx(
                          'inline-flex items-center gap-1 rounded px-2 py-0.5 text-xs font-mono font-medium',
                          isExpired
                            ? 'bg-red-50 text-red-700 dark:bg-red-500/20 dark:text-red-300 ring-1 ring-red-500/30'
                            : isNearExpiry
                            ? 'bg-amber-50 text-amber-700 dark:bg-amber-500/20 dark:text-amber-300 ring-1 ring-amber-500/30'
                            : 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300',
                        )}
                      >
                        {isExpired
                          ? t('sales.batchExpired')
                          : t('sales.batchExpiresOn')}
                        : {line.expiresOn}
                      </span>
                    </div>
                  )}

                  {autoBatch && (
                    <div className="mt-4">
                      <span className="rounded bg-brand-50 px-2 py-0.5 text-[10px] font-semibold text-brand-700 dark:bg-brand-500/15 dark:text-brand-300">
                        {t('sales.autoBatch')} (FIFO)
                      </span>
                    </div>
                  )}
                </div>
              )}

              {needsSerials && (
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-ink-muted">
                    {t('sales.serialsChosen', {
                      chosen: line.serialNumbers.length,
                      wanted: Number.isFinite(wanted) ? wanted : 0,
                    })}
                  </span>

                  <div className="flex max-h-24 flex-wrap gap-2 overflow-auto">
                    {(serials.data ?? []).map((unit) => (
                      <label
                        key={unit.serialNumberId}
                        className="flex cursor-pointer items-center gap-1.5 rounded-md border border-line bg-surface px-2 py-0.5 text-xs transition hover:border-line-strong hover:bg-surface-3"
                      >
                        <input
                          type="checkbox"
                          checked={line.serialNumbers.includes(unit.number)}
                          onChange={() => toggleSerial(unit.number)}
                        />
                        {unit.number}
                      </label>
                    ))}

                    {(serials.data ?? []).length === 0 && (
                      <span className="text-xs text-ink-muted">
                        {t('sales.noSerials')}
                      </span>
                    )}
                  </div>
                </div>
              )}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

/** Reads a document back, and offers what may still be done to it. */
function DocumentDialog({
  id,
  busy,
  onClose,
  onPost,
  onCancel,
}: {
  readonly id: string;
  readonly busy: boolean;
  readonly onClose: () => void;
  readonly onPost: (id: string) => void;
  readonly onCancel: (id: string, reason: string) => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');

  const query = useQuery<SalesInvoiceDetail, ApiError>({
    queryKey: ['sales-invoice', id],
    queryFn: () => getSalesInvoice(id),
  });

  const document = query.data;

  return (
    <Modal title={document?.header.number ?? t('sales.document')} onClose={onClose}>
      {query.isLoading && (
        <div className="space-y-3" aria-busy="true">
          <div className="grid gap-2 sm:grid-cols-3">
            {Array.from({ length: 6 }, (_, index) => (
              <div key={index}>
                <span className="skeleton block h-2 w-16 rounded" />
                <span className="skeleton mt-1.5 block h-4 w-24 rounded" />
              </div>
            ))}
          </div>
          <span className="skeleton block h-24 w-full rounded-lg" />
        </div>
      )}

      {document && (
        <>
          <div className="grid gap-2 text-sm sm:grid-cols-3">
            <Detail label={t('sales.date')} value={document.date} />
            <div>
              <span className="text-xs text-ink-muted">{t('sales.status')}</span>
              <div className="mt-0.5">
                <StatusBadge
                  tone={statusTone(document.header.status)}
                  label={statusLabel(document.header.status, t)}
                  struck={document.header.status === SalesInvoiceStatus.cancelled}
                />
              </div>
            </div>
            <Detail label={t('sales.currency')} value={document.currency} />
          </div>

          <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
            <table className="w-full min-w-[32rem] text-sm">
              <thead className="text-xs text-ink-muted">
                <tr>
                  <th className="px-2 py-1 text-start">#</th>
                  <th className="px-2 py-1 text-end">{t('sales.quantity')}</th>
                  <th className="px-2 py-1 text-end">{t('sales.rate')}</th>
                  <th className="px-2 py-1 text-end">{t('sales.taxable')}</th>
                  <th className="px-2 py-1 text-end">{t('sales.tax')}</th>
                </tr>
              </thead>
              <tbody>
                {document.lines.map((line) => (
                  <tr key={line.lineNumber} className="border-t border-line">
                    <td className="px-2 py-1">{line.lineNumber}</td>
                    <td className="px-2 py-1 text-end font-mono">{line.quantity}</td>
                    <td className="px-2 py-1 text-end font-mono">
                      {moneyAlways(line.rate)}
                    </td>
                    <td className="px-2 py-1 text-end font-mono">
                      {moneyAlways(line.taxable)}
                    </td>
                    <td className="px-2 py-1 text-end font-mono">
                      {moneyAlways(line.tax)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/*
            The same ladder the draft was entered against. Gross and discount are
            added up from the lines because the header carries neither — it keeps the
            taxable value, which is what is left after the discount.
          */}
          <div className="line-totals flex flex-wrap items-end justify-end gap-6">
            <DocumentTotals
              gross={document.lines.reduce(
                (running, line) => running + line.quantity * line.rate,
                0,
              )}
              discount={document.lines.reduce(
                (running, line) => running + line.discount,
                0,
              )}
              tax={document.header.tax}
              charges={document.header.chargeTotal}
              rounding={document.header.roundingDifference}
              currency={document.currency}
            />
          </div>

          {/* What posting produced. A sale leaves two documents on purpose, so the issue
              is named rather than left for somebody to find in the stock ledger. */}
          {document.stockDocumentId && (
            <p className="text-xs text-ink-muted">{t('sales.postedProduced')}</p>
          )}

          {document.header.status === SalesInvoiceStatus.draft && (
            <div className="flex justify-end">
              <ModalButton primary disabled={busy} onClick={() => onPost(id)}>
                {t('sales.post')}
              </ModalButton>
            </div>
          )}

          {document.header.status === SalesInvoiceStatus.posted && (
            <div className="flex flex-wrap items-end justify-end gap-2">
              <Field label={t('sales.cancelReason')}>
                <input
                  value={reason}
                  onChange={(event) => setReason(event.target.value)}
                  className="field-input-sm"
                />
              </Field>
              <ModalButton
                disabled={busy || reason.trim() === ''}
                onClick={() => onCancel(id, reason.trim())}
              >
                {t('sales.cancel')}
              </ModalButton>
            </div>
          )}
        </>
      )}

      <div className="flex justify-end">
        <ModalButton onClick={onClose}>{t('sales.close')}</ModalButton>
      </div>
    </Modal>
  );
}

function statusLabel(status: number, t: (key: string) => string): string {
  if (status === SalesInvoiceStatus.posted) return t('sales.posted');
  if (status === SalesInvoiceStatus.cancelled) return t('sales.cancelled');

  return t('sales.draft');
}

/** Posted is done, cancelled did not happen, and a draft is still somebody's to finish. */
function statusTone(status: number): StatusTone {
  if (status === SalesInvoiceStatus.posted) return 'success';
  if (status === SalesInvoiceStatus.cancelled) return 'danger';

  return 'warn';
}

function Detail({
  label,
  value,
}: {
  readonly label: string;
  readonly value: string;
}): React.JSX.Element {
  return (
    <div>
      <span className="text-xs text-ink-muted">{label}</span>
      <div className="font-mono tabular-nums text-ink">{value}</div>
    </div>
  );
}

function Field({
  label,
  children,
}: {
  readonly label: string;
  readonly children: React.ReactNode;
}): React.JSX.Element {
  return (
    <label className="flex min-w-0 flex-col gap-1 text-xs text-ink-muted">
      {label}
      {children}
    </label>
  );
}

function NumberCell({
  value,
  onChange,
  label,
  error,
  subtitle,
  placeholder,
}: {
  readonly value: string;
  readonly onChange: (value: string) => void;
  readonly label: string;
  readonly error?: string | undefined;
  readonly subtitle?: string | undefined;
  readonly placeholder?: string | undefined;
}): React.JSX.Element {
  return (
    <td data-label={label} className="px-2 py-1 text-end">
      <input
        type="number"
        inputMode="decimal"
        aria-label={label}
        value={value}
        placeholder={placeholder}
        aria-invalid={error ? true : undefined}
        onChange={(event) => onChange(event.target.value)}
        className={clsx(
          'field-input-sm ms-auto w-24 text-end font-mono tabular-nums',
          error && 'field-invalid',
        )}
      />
      {subtitle && (
        <span className="mt-0.5 block font-mono text-[10px] tabular-nums text-ink-muted text-end">
          {subtitle}
        </span>
      )}
    </td>
  );
}

function Select<TValue extends string | number>({
  value,
  onChange,
  options,
  label,
}: {
  readonly value: TValue;
  readonly onChange: (value: TValue) => void;
  readonly options: readonly { readonly value: TValue; readonly label: string }[];
  readonly label?: string | undefined;
}): React.JSX.Element {
  return (
    <SearchSelect
      value={String(value)}
      onChange={(next) =>
        onChange((typeof value === 'number' ? Number(next) : next) as TValue)
      }
      size="sm"
      label={label}
      options={options.map((option) => ({
        value: String(option.value),
        label: option.label,
      }))}
    />
  );
}

/** Sales returns screen rendering the returns list and entry. */
export function SalesReturnsPage(): React.JSX.Element {
  return <SalesPage kind={SalesDocumentKind.return} />;
}

